using System.Net;
using System.Text;
using System.Text.Json;
using MatchForecast.Api.Options;
using MatchForecast.Api.Services.AI.Ollama;
using MatchForecast.Models.Common;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace MatchForecast.Tests;

public class OllamaForecastClientTests
{
    private static OllamaForecastClient CreateClient(Func<HttpRequestMessage, HttpResponseMessage> responder, Action<string>? captureBody = null)
    {
        var http = new HttpClient(new StubHandler(responder, captureBody)) { BaseAddress = new Uri("http://localhost:11434/") };
        var options = Options.Create(new AiOptions { MaxTokens = 1234, Ollama = new OllamaOptions { Model = "test-model", ContextLength = 8192 } });
        return new OllamaForecastClient(http, options, NullLogger<OllamaForecastClient>.Instance);
    }

    private static HttpResponseMessage Json(HttpStatusCode status, string json) =>
        new(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    [Fact]
    public async Task CompleteAsync_ReturnsMessageContent_AndSendsExpectedRequest()
    {
        string? sentBody = null;
        var client = CreateClient(
            _ => Json(HttpStatusCode.OK, """{"message":{"role":"assistant","content":"{\"summary\":\"ok\"}"},"done":true,"done_reason":"stop"}"""),
            b => sentBody = b);

        var text = await client.CompleteAsync("sys", "user", CancellationToken.None);

        Assert.Equal("""{"summary":"ok"}""", text);
        using var doc = JsonDocument.Parse(sentBody!);
        var root = doc.RootElement;
        Assert.Equal("test-model", root.GetProperty("model").GetString());
        Assert.False(root.GetProperty("stream").GetBoolean());
        Assert.Equal("json", root.GetProperty("format").GetString());
        Assert.Equal(1234, root.GetProperty("options").GetProperty("num_predict").GetInt32());
        Assert.Equal(8192, root.GetProperty("options").GetProperty("num_ctx").GetInt32());
        Assert.Equal("system", root.GetProperty("messages")[0].GetProperty("role").GetString());
    }

    [Fact]
    public async Task CompleteAsync_ThrowsModelNotFound_On404()
    {
        var client = CreateClient(_ => Json(HttpStatusCode.NotFound, """{"error":"model 'test-model' not found"}"""));

        var ex = await Assert.ThrowsAsync<ForecastException>(() => client.CompleteAsync("s", "u", CancellationToken.None));
        Assert.Contains("ollama pull test-model", ex.Message);
    }

    [Fact]
    public async Task CompleteAsync_Throws503_WhenServerUnreachable()
    {
        var client = CreateClient(_ => throw new HttpRequestException("Connection refused"));

        var ex = await Assert.ThrowsAsync<ForecastException>(() => client.CompleteAsync("s", "u", CancellationToken.None));
        Assert.Equal(503, ex.StatusCode);
    }

    [Fact]
    public async Task CompleteAsync_ThrowsTokenLimit_WhenDoneReasonIsLength()
    {
        var client = CreateClient(_ => Json(HttpStatusCode.OK, """{"message":{"role":"assistant","content":""},"done":true,"done_reason":"length"}"""));

        var ex = await Assert.ThrowsAsync<ForecastException>(() => client.CompleteAsync("s", "u", CancellationToken.None));
        Assert.Contains("Ai:MaxTokens", ex.Message);
    }

    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> responder, Action<string>? captureBody) : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            if (captureBody is not null && request.Content is not null)
                captureBody(await request.Content.ReadAsStringAsync(ct));
            return responder(request);
        }
    }
}