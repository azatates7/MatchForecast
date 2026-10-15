# MatchForecast

Günün futbol maçlarını API-Football'dan çeker. Seçilen maç için yapay zekadan gol, ilk yarı golü, korner ve kart
**beklenen değerlerini** ister, bunları Poisson modeliyle olasılığa çevirir ve gerçekleşme olasılığı en yüksek
**10 tahmini** (ör. "1.5 Üst", "KG Var", "İY 0.5 Üst") istatistik tablosuyla birlikte listeler.

Model bahis seçmez, sadece sayı tahmin eder. Olasılıklar ve tahmin listesi kodda hesaplanır, bu yüzden aynı model
çıktısı her zaman aynı listeyi verir.

| Katman | Teknoloji |
|---|---|
| Backend | .NET 10 Web API (`backend/MatchForecast.Api`) + Models, Logger ve Tests projeleri |
| Frontend | React 19 + Vite (`frontend`) **veya** Angular 22 (`frontend-angular`). İkisinin arayüzü aynı. |
| Önbellek | Redis |
| Veri | API-Football (api-sports.io v3), sadece fikstür |
| Yapay zeka | Gemini, Claude veya Ollama (yerel); `Ai:Provider` ile seçilir |
| Altyapı | Docker + docker-compose |

## Docker ile çalıştırma (önerilen)

Repo kökünde bir `.env` dosyası oluştur. Ayar adlarındaki `:` yerine `__` yazılır:

```env
OddsProvider__ApiKey=<api-sports anahtarı>
Ai__Provider=Gemini
Ai__Gemini__ApiKey=<anahtar>
Jwt__SecretKey=<en az 32 karakterlik rastgele değer>
Smtp__Password=<Gmail uygulama şifresi>
```

```bash
docker compose up --build      # arayüz: http://localhost:8080
```

| Servis | İçerik |
|---|---|
| `redis` | redis:7-alpine |
| `api` | .NET API (container içinde 8080). Loglar `./logs` klasörüne yazılır. |
| `web` | React build + nginx. `/api` isteklerini `api` servisine yönlendirir, CORS gerekmez. |

Ollama kullanıyorsan model bilgisayarında çalışır, API ona `host.docker.internal:11434` üzerinden ulaşır.

Angular arayüzünü Docker'da çalıştırmak için `docker-compose.yml`'a şu servisi ekle:

```yaml
  web-angular:
    build: ./frontend-angular
    ports: ["8081:80"]
    depends_on: [api]
```

## Yerel geliştirme

**Gereksinimler:** .NET 10 SDK, Node.js 22.22.3+ veya 24+ (Angular CLI bunu ister), Redis (`localhost:6379`).

```bash
# Redis
docker run -d --name mf-redis -p 6379:6379 redis:7-alpine

# Backend (http://localhost:5080, Swagger: /swagger)
cd backend/MatchForecast.Api
dotnet user-secrets set "OddsProvider:ApiKey" "<api-sports anahtarı>"
dotnet user-secrets set "Ai:Provider" "Gemini"           # Gemini | Claude | Ollama
dotnet user-secrets set "Ai:Gemini:ApiKey" "<anahtar>"
dotnet user-secrets set "Jwt:SecretKey" "<en az 32 karakter>"
dotnet run

# Frontend: biri yeterli
cd frontend && npm install && npm run dev                 # http://localhost:5173
cd frontend-angular && npm install && npm start           # http://localhost:4200
```

Her iki frontend de `/api` isteklerini geliştirme sunucusu üzerinden `localhost:5080`'e yönlendirir. React'te bunu
`vite.config.ts`, Angular'da `proxy.conf.json` yapar.

Redis kapalıyken API çalışmaya devam eder ama hiçbir şey önbelleğe alınmaz ve "Cache Temizle" 503 döner.

Testler:

```bash
dotnet test backend/MatchForecast.Tests/MatchForecast.Tests.csproj
cd frontend-angular && npm test
```

## Endpointler

`/api/auth/token` dışındaki tüm endpointler `Authorization: Bearer {token}` ister. Frontend'ler token'ı kendisi alır
ve süresi dolunca yeniler.

| Yöntem | Yol | Açıklama |
|---|---|---|
| GET | `/api/auth/token` | JWT üretir (IP başına dakikada 10) |
| GET | `/api/matches?date=2026-10-15` | O günün başlamamış ve canlı maçları |
| GET | `/api/matches/popular?date=&count=10` | Büyük liglere öncelik veren popüler maçlar |
| GET | `/api/matches/popular/forecasts?date=&count=10` | Popüler maçlar için toplu tahmin |
| GET | `/api/matches/{id}/forecast?refresh=true` | Maç tahmini (30 dk önbellek, `refresh` önbelleği atlar) |
| DELETE | `/api/cache` | Uygulamanın Redis kayıtlarını siler (tüm istemciler için 60 dk'da 2 kez) |

Limit aşılınca `429` döner. Hatalar `ProblemDetails` formatındadır ve arayüz `detail` alanını gösterir.

## Nasıl çalışır

1. `ApiFootballOddsProvider` fikstürü çeker ve Redis'e yazar. Canlı maç varken liste 1 dk, yoksa 30 dk önbellekte kalır.
2. `ForecastPrompt` modelden yalnızca metriklerin beklenen değerlerini JSON olarak ister.
3. `ProjectionBuilder` değerleri doğrular ve `Forecast:Max*` sınırlarıyla kırpar. Ardından Poisson ile 1/X/2, KG,
   aralık ve Alt/Üst olasılıklarını hesaplar.
4. `PredictionSelector`, `ForecastCatalog`'daki seçeneklerden önce `%55–%92` bandındakileri seçer. Her gruptan en
   fazla birini alır ve listeyi 10'a tamamlar.
5. Sonuç `forecast:v2:{id}` anahtarıyla Redis'te 30 dk tutulur.

5xx hatalarında yöneticiye e-posta gider. Aynı hata 10 dk içinde tekrar ederse yeniden mail atılmaz. Uygulama her
başladığında da bilgilendirme maili gönderilir. İkisi de `Smtp:Enabled` ile kapatılabilir.

## Yapılandırma

| Bölüm | Önemli ayarlar |
|---|---|
| `Ai` | `Provider`, `CacheMinutes`, `Gemini/Claude/Ollama: BaseUrl, ApiKey, Model`, `Ollama:TimeoutSeconds, ContextLength` |
| `OddsProvider` | `ApiKey`, `Timezone`, `CacheMinutes` |
| `Redis` | `ConnectionString`, `InstanceName` (anahtar öneki, boş olamaz), `CacheMinutes` |
| `RateLimiting` | `TokenPermitLimit/TokenWindowSeconds`, `CacheClearPermitLimit/CacheClearWindowMinutes` |
| `Forecast` | `PredictionCount`, `MinPickProbability/MaxPickProbability`, `Max*` |
| `Jwt` | `SecretKey` (en az 32 byte), `Issuer`, `Audience`, `ExpiryMinutes` |
| `Smtp` | `Enabled`, `Username`, `Password` (Gmail uygulama şifresi), `AdminEmails`, `ThrottleMinutes` |

Gizli değerler (`*ApiKey`, `Jwt:SecretKey`, `Smtp:Password`) `appsettings.json`'a yazılmamalı. Yerelde
user-secrets, Docker'da `.env` kullanılmalı.

## Genişletme noktaları

- **Yeni veri kaynağı:** `IOddsProvider` arayüzünü uygula ve `Program.cs`'te kaydet.
- **Yeni LLM sağlayıcısı:** `IForecastAiClient` arayüzünü uygula ve `Ai:Provider` seçimine ekle.
- **Yeni market:** `ForecastCatalog`'a bir satır ekle.
- **Prompt:** `Services/Forecast/ForecastPrompt.cs`.
- **Backend DTO değişikliği:** iki frontend'deki tip dosyalarını birlikte güncelle
  (`frontend/src/components/Types.tsx`, `frontend-angular/src/app/core/types.ts`).

Yol haritası ve sonraki adımlar (MSSQL, EF Core, OAuth) için `CLAUDE.md` dosyasına bak.