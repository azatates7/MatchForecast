using System.Security.Cryptography;
using System.Text;

namespace MatchForecast.Data.Security;

public interface IAppSettingsCrypto
{
    string Encrypt(string plainText);
    string Decrypt(string cipherText);
}

/*
 * AES-256-GCM. Şifreli metin = base64(nonce[12] + tag[16] + şifreli veri). Her şifrelemede yeni rastgele nonce kullanılır;
 * aynı değer iki kez şifrelense de farklı çıktı üretir. GCM bütünlüğü de doğrular: DB'deki şifreli değer elle bozulursa
 * çözme hata verir, sessizce yanlış değer okunmaz.
 *
 * Anahtar veritabanında DEĞİL, ortam değişkeninde / user-secrets'ta durur (AppSettings:EncryptionKey). Anahtar DB'de
 * olsaydı, DB'yi ele geçiren biri şifreli değerleri de çözebilirdi.
 */
public sealed class AesGcmAppSettingsCrypto : IAppSettingsCrypto
{
    private const int NonceSize = 12;
    private const int TagSize = 16;

    private readonly byte[] _key;

    public AesGcmAppSettingsCrypto(string base64Key)
    {
        try
        {
            _key = Convert.FromBase64String(base64Key);
        }
        catch (FormatException)
        {
            throw new ArgumentException("AppSettings:EncryptionKey geçerli bir base64 değer değil.");
        }

        if (_key.Length != 32)
            throw new ArgumentException("AppSettings:EncryptionKey 32 byte (256 bit) olmalı; base64 hali 44 karakterdir.");
    }

    public string Encrypt(string plainText)
    {
        var plain = Encoding.UTF8.GetBytes(plainText);
        var output = new byte[NonceSize + TagSize + plain.Length];
        var nonce = output.AsSpan(0, NonceSize);
        RandomNumberGenerator.Fill(nonce);

        using var aes = new AesGcm(_key, TagSize);
        aes.Encrypt(nonce, plain, output.AsSpan(NonceSize + TagSize), output.AsSpan(NonceSize, TagSize));
        return Convert.ToBase64String(output);
    }

    public string Decrypt(string cipherText)
    {
        var data = Convert.FromBase64String(cipherText);
        var plain = new byte[data.Length - NonceSize - TagSize];

        using var aes = new AesGcm(_key, TagSize);
        aes.Decrypt(data.AsSpan(0, NonceSize), data.AsSpan(NonceSize + TagSize), data.AsSpan(NonceSize, TagSize), plain);
        return Encoding.UTF8.GetString(plain);
    }
}