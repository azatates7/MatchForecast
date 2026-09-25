namespace MatchForecast.Models.Common;

public sealed record Market(int Id, string Name, IReadOnlyList<OddOption> Options);
