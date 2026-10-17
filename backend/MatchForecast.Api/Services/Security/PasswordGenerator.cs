using System.Security.Cryptography;

namespace MatchForecast.Api.Services.Security;

// Kriptografik olarak güvenli rastgele şifre üretir (RandomNumberGenerator; System.Random tahmin edilebilir olduğu için kullanılmaz).
// Her şifrede en az bir büyük harf, küçük harf, rakam ve sembol bulunur. Karışabilen karakterler (0/O, 1/l/I) dışarıda bırakıldı.
public static class PasswordGenerator
{
    public const int MinLength = 8;
    public const int MaxLength = 128;
    public const int DefaultLength = 16;

    private const string Upper = "ABCDEFGHJKLMNPQRSTUVWXYZ";
    private const string Lower = "abcdefghijkmnopqrstuvwxyz";
    private const string Digits = "23456789";
    private const string Symbols = "!@#$%^&*-_=+?";
    private const string All = Upper + Lower + Digits + Symbols;

    public static string Generate(int length = DefaultLength)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(length, MinLength);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(length, MaxLength);

        var chars = new char[length];
        // Her gruptan bir karakter garanti; kalanlar tüm kümeden. Sonra karıştırılır ki garanti karakterler hep başta olmasın.
        chars[0] = Pick(Upper);
        chars[1] = Pick(Lower);
        chars[2] = Pick(Digits);
        chars[3] = Pick(Symbols);
        for (var i = 4; i < length; i++) chars[i] = Pick(All);

        RandomNumberGenerator.Shuffle(chars.AsSpan());
        return new string(chars);
    }

    private static char Pick(string set) => set[RandomNumberGenerator.GetInt32(set.Length)];
}