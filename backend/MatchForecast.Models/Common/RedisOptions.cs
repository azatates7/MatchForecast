namespace MatchForecast.Models.Common;

public class RedisOptions
{
    public string ConnectionString { get; set; } = string.Empty;

    public string InstanceName { get; set; } = "MatchForecast:";
}