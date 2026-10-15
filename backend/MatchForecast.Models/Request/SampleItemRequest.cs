using System.ComponentModel.DataAnnotations;

namespace MatchForecast.Models.Request;

public sealed record SampleItemRequest(
    [Required, MaxLength(100)] string Name,
    [MaxLength(500)] string? Description);