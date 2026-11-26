namespace MatchForecast.Models.Response;

/// <summary>Maça çıkamayacak ya da çıkması şüpheli oyuncu (API-Football /injuries).</summary>
/// <param name="Team">Takım adı; fikstürdeki ev/deplasman adlarıyla aynı kaynaktan gelir.</param>
/// <param name="Type">"Missing Fixture" (oynamayacak) veya "Questionable" (şüpheli).</param>
/// <param name="Reason">Sağlayıcının verdiği neden, ör. "Suspended", "Knee Injury" (İngilizce).</param>
public sealed record MissingPlayer(string Team, string Player, string Type, string Reason);