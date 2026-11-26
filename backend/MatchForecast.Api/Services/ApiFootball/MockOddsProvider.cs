using MatchForecast.Models.Common;
using MatchForecast.Models.Response;

namespace MatchForecast.Api.Services.ApiFootball;

// API anahtarı olmadan uçtan uca denemek için örnek fikstür verisi.
public sealed class MockOddsProvider : IOddsProvider
{
    // (id, lig, ülke, ev, deplasman, saat)
    private static readonly (int Id, string League, string Country, string Home, string Away, int Hour)[] Seed =
    [
        (900001, "Süper Lig", "Turkey", "Galatasaray", "Fenerbahçe", 19),
        (900002, "Süper Lig", "Turkey", "Beşiktaş", "Trabzonspor", 20),
        (900003, "Premier League", "England", "Manchester City", "Liverpool", 18),
        (900004, "Premier League", "England", "Arsenal", "Chelsea", 21),
        (900005, "La Liga", "Spain", "Real Madrid", "Barcelona", 22),
        (900006, "La Liga", "Spain", "Atletico Madrid", "Sevilla", 19),
        (900007, "Serie A", "Italy", "Inter", "Juventus", 21),
        (900008, "Serie A", "Italy", "Milan", "Roma", 19),
        (900009, "Bundesliga", "Germany", "Bayern München", "Borussia Dortmund", 18),
        (900010, "Ligue 1", "France", "PSG", "Marseille", 22),
    ];

    public Task<IReadOnlyList<MatchSummary>> GetMatchesAsync(DateOnly date, CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<MatchSummary>>(Upcoming(date).ToList());

    public Task<IReadOnlyList<MatchSummary>> GetPopularMatchesAsync(DateOnly date, int count, CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<MatchSummary>>(Upcoming(date).Take(count).ToList());

    // Bitmiş maçlar elenir; gerçek provider ile aynı davranış.
    private static IEnumerable<MatchSummary> Upcoming(DateOnly date) =>
        Seed.Select(s => ToSummary(s, date))
            .Where(m => !MatchStatus.IsFinished(m.Status))
            .OrderBy(m => m.Kickoff);

    public Task<MatchSummary?> GetMatchAsync(int fixtureId, CancellationToken ct)
    {
        var s = Seed.FirstOrDefault(x => x.Id == fixtureId);
        return Task.FromResult<MatchSummary?>(s.Id == 0 ? null : ToSummary(s, DateOnly.FromDateTime(DateTime.Today)));
    }

    private static MatchSummary ToSummary(
        (int Id, string League, string Country, string Home, string Away, int Hour) s, DateOnly date)
    {
        var kickoff = new DateTimeOffset(date.ToDateTime(new TimeOnly(s.Hour, 0)), TimeSpan.FromHours(3));
        var (status, elapsed, extra) = SimulateStatus(kickoff, DateTimeOffset.Now);
        return new(s.Id, kickoff, s.League, s.Country, s.Home, s.Away, status, elapsed, extra);
    }

    /*
     * Başlama saatine göre maç durumunu taklit eder: 45 dk 1. yarı + 2 dk uzatma, 15 dk devre arası,
     * 45 dk 2. yarı + 4 dk uzatma. Toplam ~111 dk sonra maç biter (FT).
     */
    private static (string Status, int? Elapsed, int? Extra) SimulateStatus(DateTimeOffset kickoff, DateTimeOffset now)
    {
        var m = (int)Math.Floor((now - kickoff).TotalMinutes);
        return m switch
        {
            < 0 => ("NS", null, null),
            < 45 => ("1H", m + 1, null),
            < 47 => ("1H", 45, m - 44),
            < 62 => ("HT", 45, null),
            < 107 => ("2H", m - 16, null),
            < 111 => ("2H", 90, m - 106),
            _ => ("FT", 90, null)
        };
    }
}