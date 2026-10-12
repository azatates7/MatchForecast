namespace MatchForecast.Models.Response;

/*
 * Maç sonucu olasılıkları (yüzde). Home + Draw + Away her zaman 100'dür.
 * İlk yarı alanları, model ilk yarı gol beklentisi vermediyse null olur.
 */
public sealed record OutcomeProbabilities(
    int Home,
    int Draw,
    int Away,
    int BttsYes,
    int? FirstHalfHome,
    int? FirstHalfDraw,
    int? FirstHalfAway);