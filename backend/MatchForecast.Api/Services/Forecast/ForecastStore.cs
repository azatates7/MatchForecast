using System.Text.Json;
using MatchForecast.Api.Options;
using MatchForecast.Data.Configuration;
using MatchForecast.Data.Entity;
using MatchForecast.Models.Response;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace MatchForecast.Api.Services.Forecast;

// Üretilen tahminleri veritabanında saklar (forecasts tablosu). Okuma tarafı hâlâ Redis'tir.
public interface IForecastStore
{
    Task SaveAsync(ForecastResult result, CancellationToken ct);
}

public sealed class ForecastStore(AppDbContext db, IOptions<AiOptions> aiOptions) : IForecastStore
{
    private static readonly TimeZoneInfo Istanbul = TimeZoneInfo.FindSystemTimeZoneById("Europe/Istanbul");

    // API yanıtıyla aynı biçim (camelCase, enum adları); frontend tipleriyle birebir okunabilir.
    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web);

    public async Task SaveAsync(ForecastResult result, CancellationToken ct)
    {
        var match = result.Match;
        var record = await db.Forecasts.FirstOrDefaultAsync(x => x.FixtureId == match.Id, ct);
        if (record is null)
        {
            record = new ForecastRecord { FixtureId = match.Id };
            db.Forecasts.Add(record);
        }
        else
        {
            record.UpdatedAt = DateTime.Now;
        }

        // Entity'deki DateTime alanları İstanbul saatidir (AppDbContext UTC'ye çevirerek yazar).
        record.Kickoff = TimeZoneInfo.ConvertTime(match.Kickoff, Istanbul).DateTime;
        record.Country = match.Country;
        record.League = match.League;
        record.HomeTeam = match.HomeTeam;
        record.AwayTeam = match.AwayTeam;
        (record.AiProvider, record.AiModel) = CurrentModel(aiOptions.Value);
        record.ResultJson = JsonSerializer.Serialize(result, JsonOpts);
        record.GeneratedAt = TimeZoneInfo.ConvertTime(result.GeneratedAt, Istanbul).DateTime;

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch
        {
            // Aynı scope'ta (ör. ön ısıtma döngüsü) sonraki kayıtlar bu başarısız değişikliği tekrar denemesin.
            db.ChangeTracker.Clear();
            throw;
        }
    }

    // Program.cs ile aynı kural: Ai:Provider boşsa Gemini.
    private static (string Provider, string Model) CurrentModel(AiOptions ai)
    {
        var provider = string.IsNullOrWhiteSpace(ai.Provider) ? "Gemini" : ai.Provider.Trim();
        var model = provider.ToLowerInvariant() switch
        {
            "gemini" => ai.Gemini.Model,
            "claude" => ai.Claude.Model,
            "ollama" => ai.Ollama.Model,
            _ => string.Empty
        };
        return (provider, model);
    }
}