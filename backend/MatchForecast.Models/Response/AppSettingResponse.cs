namespace MatchForecast.Models.Response;

// Şifreli ayarların değeri hiçbir zaman döndürülmez ("********").
public sealed record AppSettingResponse(
    Guid Id,
    string Key,
    string Value,
    bool IsEncrypted,
    string? Description,
    string TargetProject,
    string Environment,
    DateTime CreatedAt,
    DateTime? UpdatedAt,
    string? UpdatedBy);