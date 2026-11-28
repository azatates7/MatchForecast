using MatchForecast.Api.Services.Kafka;
using MatchForecast.Api.Services.Security;
using MatchForecast.Data.Seed;
using MatchForecast.Models.Response;
using Microsoft.AspNetCore.Mvc;

namespace MatchForecast.Api.Controllers;

/*
 * Kafka DLQ yönetimi (yönetim paneli, kafka.manage yetkisi; Admin örtük olarak sahip).
 * - GET: DLQ'daki son kayıtlar (en yeni önce) ve hata nedenleri. Okuma hiçbir offset'i değiştirmez.
 * - POST {partition}/{offset}/replay: tek kaydı ana topic'e geri gönderir (yeniden işlenir).
 * - POST replay-all: listedeki, henüz yeniden işlenmemiş tüm kayıtları geri gönderir.
 * Kafka kapalıysa GET Enabled=false döner, POST uçları 409.
 */
[ApiController]
[Route("api/admin/kafka/dlq")]
[RequirePermission(PermissionKeys.KafkaManage)]
public sealed class KafkaDlqController(KafkaDlqService dlq) : ControllerBase
{
    [HttpGet]
    public Task<KafkaDlqReportResponse> Get(CancellationToken ct) => dlq.GetReportAsync(ct);

    [HttpPost("{partition:int}/{offset:long}/replay")]
    public Task<KafkaDlqReplayResponse> Replay(int partition, long offset, CancellationToken ct) => dlq.ReplayAsync(partition, offset, ct);

    [HttpPost("replay-all")]
    public Task<KafkaDlqReplayResponse> ReplayAll(CancellationToken ct) => dlq.ReplayAllAsync(ct);
}