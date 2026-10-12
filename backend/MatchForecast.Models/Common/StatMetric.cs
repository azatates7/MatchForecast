using System.Text.Json.Serialization;

namespace MatchForecast.Models.Common;

// JSON'da sayı yerine ad olarak yazılır ("TotalGoals"); frontend ve Redis'teki kayıt okunabilir kalır.
[JsonConverter(typeof(JsonStringEnumConverter<StatMetric>))]
public enum StatMetric
{
    HomeGoals,
    AwayGoals,
    TotalGoals,
    FirstHalfGoals,
    Corners,
    YellowCards,
    RedCards
}