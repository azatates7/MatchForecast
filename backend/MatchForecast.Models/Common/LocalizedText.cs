namespace MatchForecast.Models.Common;

/*
 * İki dilli metin (JSON: { "tr": "...", "en": "..." }). Tahmin sonuçlarında kullanılır: cache'te tek kayıt tutulur,
 * frontend seçili dile göre alanı seçer; dil değişince API'ye yeniden istek gerekmez.
 */
public sealed record LocalizedText(string Tr, string En)
{
    public static readonly LocalizedText Empty = new("", "");

    // İngilizce metin boşsa Türkçesi kullanılır (model İngilizce alanı atlarsa ekran boş kalmasın).
    public static LocalizedText Of(string? tr, string? en)
    {
        var t = tr?.Trim() ?? "";
        var e = en?.Trim() ?? "";
        return new LocalizedText(t.Length > 0 ? t : e, e.Length > 0 ? e : t);
    }
}
