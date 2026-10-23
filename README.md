# MatchForecast

Günün futbol maçlarını API-Football'dan çeker, her maç için yapay zekadan istatistik **beklentilerini** (gol, ilk yarı
golü, korner, kart) ister, bunları Poisson modeliyle olasılığa çevirir ve gerçekleşme olasılığı en yüksek
**10 tahmini** bir istatistik tablosuyla birlikte listeler (ör. "MS 1", "2.5 Alt", "KG Var", "İY 0.5 Üst").
Yapay zeka bahis seçmez; olasılıklar ve tahmin listesi kodda hesaplanır.

- **Backend:** .NET 10 Web API (controller'lar) — `backend/MatchForecast.Api`, `.Models`, `.Data`, `.Logger`, `.Tests`
- **Frontend:** React 19 + Vite (`frontend`) veya Angular 22 (`frontend-angular`) — ikisi aynı arayüz
- **Veritabanı:** Supabase (PostgreSQL), EF Core + Npgsql, `matchforecast` şeması
- **Önbellek:** Redis (kapalıyken API çalışmaya devam eder, yalnızca önbellek devre dışı kalır)
- **Veri:** API-Football (api-sports.io v3) — yalnızca `/fixtures`
- **Yapay zeka:** `Ai:Provider` ile seçilir — Gemini, Claude veya Ollama
- **Kimlik doğrulama:** kullanıcı adı/şifre veya Google ile giriş, JWT + yenilenen refresh token, Admin/User rolleri

## Hızlı başlangıç

```bash
# 0) Redis (yerel)
docker run -d -p 6379:6379 redis:7-alpine

# 1) Backend
cd backend/MatchForecast.Api
dotnet user-secrets set "ConnectionStrings:Supabase" "Host=...;Port=5432;Database=postgres;Username=...;Password=...;SSL Mode=Require"
dotnet user-secrets set "AppSettings:EncryptionKey" "$(openssl rand -base64 32)"   # kaybetmeyin
dotnet user-secrets set "OddsProvider:ApiKey" "<api-sports anahtarı>"
dotnet user-secrets set "Ai:Provider" "Gemini"                                    # Gemini | Claude | Ollama
dotnet user-secrets set "Ai:Gemini:ApiKey" "<anahtar>"
dotnet user-secrets set "Jwt:SecretKey" "<en az 32 karakter>"
dotnet user-secrets set "Admin:UserName" "admin"
dotnet user-secrets set "Admin:Email" "<e-posta>"
dotnet user-secrets set "Admin:Password" "<şifre>"
dotnet run                                                                        # http://localhost:5080, Swagger: /swagger

# 2) Frontend (ayrı terminal) — biri yeterli
cd frontend && npm install && npm run dev                                         # http://localhost:5173
cd frontend-angular && npm install && npm start                                   # http://localhost:4200
```

İsteğe bağlı: `Smtp:Password` (Gmail Uygulama Şifresi; hata bildirimleri ve şifre sıfırlama e-postaları için,
gerekmiyorsa `Smtp:Enabled=false`), `Auth:GoogleClientId` (boşsa Google butonu gösterilmez),
`Auth:PasswordResetUrl` (sıfırlama bağlantısının açılacağı frontend adresi).

### Ayarlar veritabanında

Açılışta migration'lar otomatik uygulanır ve ayarlar `app_settings` tablosundan okunur. İlk çalıştırmada
appsettings.json / user-secrets / ortam değişkenlerindeki değerler tabloya kopyalanır (API anahtarları, şifreler
AES-256-GCM ile şifreli); sonrasında **veritabanındaki değer geçerlidir**. Ortamda kalması gereken yalnızca iki değer
vardır: `ConnectionStrings:Supabase` ve `AppSettings:EncryptionKey`. Ayar değişiklikleri `/api/app-settings` (Admin)
ile yapılır ve uygulama yeniden başlatılınca geçerli olur.

## Docker

```bash
# Yerel: kök dizindeki .env dosyasına Section__Key biçiminde yazın
#   ConnectionStrings__Supabase=...  AppSettings__EncryptionKey=...  OddsProvider__ApiKey=...
#   Ai__Provider=Gemini  Ai__Gemini__ApiKey=...  Jwt__SecretKey=...  Smtp__Password=...
docker compose up --build                                   # http://localhost:8080

# Canlı ortam (Cloudflare Tunnel arkasında, açık port yok); .env'de ayrıca CLOUDFLARE_TUNNEL_TOKEN
docker compose -f docker-compose.prod.yml up -d --build
```

## Endpointler

`[AllowAnonymous]` olmayan her endpoint `Authorization: Bearer {accessToken}` ister.

| Yöntem | Yol | Açıklama |
|---|---|---|
| GET | `/api/auth/config` | Giriş ekranı ayarları (Google Client ID) — anonim |
| POST | `/api/auth/token` | Kullanıcı adı + şifre ile access + refresh token — anonim |
| POST | `/api/auth/refresh` | Refresh token ile yeni token çifti (eski token geçersiz olur) — anonim |
| POST | `/api/auth/register` | Yeni hesap ("User" rolü) ve doğrudan giriş — anonim |
| POST | `/api/auth/google` | Google ID token ile giriş — anonim |
| POST | `/api/auth/forgot-password` | Şifre sıfırlama e-postası (her durumda 204) — anonim |
| POST | `/api/auth/reset-password` | E-postadaki token ile yeni şifre — anonim |
| POST | `/api/auth/revoke` | Çıkış (refresh token iptali) |
| GET | `/api/password?length=16` | Rastgele güvenli şifre üretir — anonim |
| GET | `/api/matches?date=2026-10-23` | O günün maçları |
| GET | `/api/matches/popular?date=&count=` | Popüler liglerden maçlar (en fazla 10) |
| GET | `/api/matches/popular/forecasts?date=&count=` | Popüler maçlar için toplu tahmin |
| GET | `/api/matches/{id}/forecast` | Maçın AI tahmini (30 dk önbellek; `refresh=true` yalnızca Admin) |
| DELETE | `/api/cache` | Uygulamanın Redis önbelleğini temizler (tüm istemciler için 60 dk'da 2 kez) |
| * | `/api/users` | Kullanıcı, rol ve grup yönetimi — Admin |
| * | `/api/app-settings` | Veritabanındaki ayarlar — Admin |
| GET | `/healthz` | Liveness |
| GET | `/healthz/ready` | Veritabanı ve Redis durumu (DB yoksa 503, Redis yoksa 200 + Degraded) |

Swagger UI ve OpenAPI dokümanı (yalnızca Development): `/swagger`, `/openapi/v1.json`.

Rate limiting: giriş/kayıt/şifre endpoint'leri IP başına dakikada 10, AI tahmin endpoint'leri kullanıcı başına
10 dakikada 30 istek; aşılırsa `429` döner.

## Nasıl çalışır

1. `ApiFootballOddsProvider` günün maçlarını çeker, bitmiş ve eski başlamamış maçları eler; sonuçlar Redis'te tutulur.
2. `ForecastPrompt` modelden yalnızca her istatistiğin beklenen değerini (λ) JSON olarak ister.
3. `ProjectionBuilder` değerleri doğrular/sınırlar ve `PoissonMath` ile skor dağılımı, 1X2, KG ve alt/üst
   olasılıklarını hesaplar.
4. `PredictionSelector`, `ForecastCatalog`'daki seçeneklerden `Forecast:MinPickProbability`–`MaxPickProbability`
   bandındakileri tercih ederek, her gruptan en fazla bir tane olmak üzere 10 tahmin seçer.

## Genişletme noktaları

- Başka bir veri kaynağı: `IOddsProvider` uygula, `Program.cs`'te kaydet.
- Başka bir LLM: `IForecastAiClient` uygula, `Program.cs`'teki `Ai:Provider` seçimine ekle.
- Yeni market: `Services/Forecast/ForecastCatalog.cs`'e bir satır.
- Prompt ayarı: `Services/Forecast/ForecastPrompt.cs`.
- Yeni tablo: `MatchForecast.Data`'ya entity ekle, `dotnet ef migrations add <Ad> --project MatchForecast.Data --startup-project MatchForecast.Api`.