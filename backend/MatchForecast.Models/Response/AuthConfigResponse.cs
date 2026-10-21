namespace MatchForecast.Models.Response;

// Frontend'lerin derleme zamanı ayarı gerektirmeden Google butonunu göstermesi için. Client ID gizli değildir.
public sealed record AuthConfigResponse(string? GoogleClientId);