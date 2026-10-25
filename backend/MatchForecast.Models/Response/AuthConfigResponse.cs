namespace MatchForecast.Models.Response;

// Frontend'lerin derleme zamanı ayarı gerektirmeden Google ve misafir butonlarını göstermesi için. Client ID gizli değildir.
public sealed record AuthConfigResponse(string? GoogleClientId, bool GuestLoginEnabled = false);