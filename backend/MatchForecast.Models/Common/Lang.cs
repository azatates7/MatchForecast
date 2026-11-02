using System.Globalization;

namespace MatchForecast.Models.Common;

/*
 * İstek diline göre metin seçimi. API'de RequestLocalization, Accept-Language başlığından CurrentUICulture'ı ayarlar
 * (desteklenen: tr, en; varsayılan tr). Kullanıcıya dönen hata mesajları T("Türkçe", "English") ile yazılır.
 * İstek dışındaki işlerde Program.cs'teki DefaultThreadCurrentUICulture (tr) geçerlidir.
 */
public static class Lang
{
    public static bool IsEnglish =>
        string.Equals(CultureInfo.CurrentUICulture.TwoLetterISOLanguageName, "en", StringComparison.OrdinalIgnoreCase);

    public static string T(string tr, string en) => IsEnglish ? en : tr;
}

// DataAnnotations mesajları için (ErrorMessageResourceType + ErrorMessageResourceName); her okunuşta istek diline göre döner.
public static class ValidationMessages
{
    public static string UserNameFormat => Lang.T(
        "Kullanıcı adı 3-50 karakter olmalı; yalnızca harf, rakam, '.', '_' ve '-' içerebilir.",
        "User name must be 3-50 characters and may contain only letters, digits, '.', '_' and '-'.");
}
