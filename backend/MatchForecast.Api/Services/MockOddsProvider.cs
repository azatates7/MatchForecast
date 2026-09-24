using MatchForecast.Api.Models;

namespace MatchForecast.Api.Services;

/// <summary>
/// API anahtarı olmadan uçtan uca denemek için örnek veri.
/// Market ve seçenek isimleri API-Football formatıyla aynıdır.
/// </summary>
public sealed class MockOddsProvider : IOddsProvider
{
    // (id, lig, ülke, ev, deplasman, saat, ev gücü 0..1)
    private static readonly (int Id, string League, string Country, string Home, string Away, int Hour, double HomeStrength)[] Seed =
    [
        (900001, "Süper Lig", "Turkey", "Galatasaray", "Kasımpaşa", 19, 0.78),
        (900002, "Süper Lig", "Turkey", "Samsunspor", "Fenerbahçe", 20, 0.35),
        (900003, "Premier League", "England", "Arsenal", "Brentford", 21, 0.72),
        (900004, "La Liga", "Spain", "Getafe", "Osasuna", 22, 0.50),
        (900005, "Serie A", "Italy", "Inter", "Napoli", 21, 0.55),
    ];

    public Task<IReadOnlyList<MatchSummary>> GetMatchesAsync(DateOnly date, CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<MatchSummary>>(Seed.Select(s => ToSummary(s, date)).ToList());

    public Task<MatchSummary?> GetMatchAsync(int fixtureId, CancellationToken ct)
    {
        var s = Seed.FirstOrDefault(x => x.Id == fixtureId);
        return Task.FromResult<MatchSummary?>(s.Id == 0 ? null : ToSummary(s, DateOnly.FromDateTime(DateTime.Today)));
    }

    public Task<MatchOdds?> GetOddsAsync(int fixtureId, CancellationToken ct)
    {
        var s = Seed.FirstOrDefault(x => x.Id == fixtureId);
        if (s.Id == 0) return Task.FromResult<MatchOdds?>(null);

        var h = s.HomeStrength;
        var draw = 0.27;
        var home = (1 - draw) * h;
        var away = 1 - draw - home;
        var goals = s.Id % 2 == 0 ? 0.48 : 0.58; // Üst 2.5 olasılığı

        Market M(int id, string name, params (string Value, double P)[] opts) =>
            new(id, name, opts.Select(o => new OddOption(o.Value, Odd(o.P))).ToList());

        var markets = new List<Market>
        {
            M(1, "Match Winner", ("Home", home), ("Draw", draw), ("Away", away)),
            M(12, "Double Chance", ("Home/Draw", home + draw), ("Home/Away", home + away), ("Draw/Away", draw + away)),
            M(5, "Goals Over/Under",
                ("Over 1.5", goals + 0.22), ("Under 1.5", 0.78 - goals),
                ("Over 2.5", goals), ("Under 2.5", 1 - goals),
                ("Over 3.5", goals - 0.25), ("Under 3.5", 1.25 - goals)),
            M(8, "Both Teams Score", ("Yes", goals - 0.02), ("No", 1.02 - goals)),
            M(13, "First Half Winner", ("Home", home * 0.8), ("Draw", 0.42), ("Away", away * 0.75)),
            M(6, "Goals Over/Under First Half",
                ("Over 0.5", goals + 0.15), ("Under 0.5", 0.85 - goals),
                ("Over 1.5", goals - 0.18), ("Under 1.5", 1.18 - goals)),
            M(3, "Second Half Winner", ("Home", home * 0.85), ("Draw", 0.36), ("Away", away * 0.8)),
        };

        return Task.FromResult<MatchOdds?>(new MatchOdds(fixtureId, "Mock Bookmaker", markets));
    }

    // %6 marj ekleyip oranı 2 haneye yuvarla
    private static decimal Odd(double p) =>
        Math.Round((decimal)(1 / Math.Clamp(p * 1.06, 0.03, 0.97)), 2);

    private static MatchSummary ToSummary(
        (int Id, string League, string Country, string Home, string Away, int Hour, double HomeStrength) s, DateOnly date) =>
        new(s.Id,
            new DateTimeOffset(date.ToDateTime(new TimeOnly(s.Hour, 0)), TimeSpan.FromHours(3)),
            s.League, s.Country, s.Home, s.Away, "NS");
}
