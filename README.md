# MatchForecast

Günün futbol maçlarını ve her maçın **tüm bahis marketlerini** API'den çeker, bunları bir prompt ile
yapay zekaya gönderir ve gerçekleşme olasılığı en yüksek **5 seçeneği** listeler
(ör. "İY 0.5 Alt — ilk yarı gol olmaz", "MS 2", "KG Var").

- **Backend:** .NET 10 Minimal API (`backend/MatchForecast.Api`) — DB yok, `IMemoryCache` ile önbellek
- **Frontend:** React 19 + Vite + TypeScript (`frontend`)
- **Veri:** API-Football (api-sports.io v3) — `/fixtures`, `/odds`
- **Yapay zeka:** Anthropic Claude (Messages API)

## Hızlı başlangıç (örnek veriyle, anahtar gerekmeden maç listesi)

```bash
# 1) Backend
cd backend/MatchForecast.Api
dotnet user-secrets set "Ai:ApiKey" "sk-ant-..."     # analiz için gerekli
dotnet run                                          # http://localhost:5080

# 2) Frontend (ayrı terminal)
cd frontend
npm install
npm run dev                                         # http://localhost:5173
```

`OddsProvider:UseMock = true` iken 5 örnek maç ve API-Football formatında marketler döner.

## Gerçek veriye geçiş

```bash
cd backend/MatchForecast.Api
dotnet user-secrets set "OddsProvider:ApiKey" "<api-sports anahtarı>"
dotnet user-secrets set "OddsProvider:UseMock" "false"
```

İsteğe bağlı: `OddsProvider:BookmakerId` (boşsa en çok market sunan bahisçi seçilir).
Not: API-Football ücretsiz planı günlük istek sınırlıdır ve plan kapsamı (sezon/oran erişimi) değişebilir;
hesabınızın güncel sezon oranlarına erişimini kontrol edin. Sonuçlar 10 dk önbelleğe alınır.

## Endpointler

| Yöntem | Yol | Açıklama |
|---|---|---|
| GET | `/api/matches?date=2026-09-23` | O günün maçları |
| GET | `/api/matches/{id}/odds` | Maçın tüm marketleri (ham) |
| GET | `/api/matches/{id}/forecast?refresh=true` | AI analizi, 5 seçenek (30 dk önbellek) |

OpenAPI dokümanı (Development): `/openapi/v1.json`

## Nasıl çalışır

1. `ForecastPrompt` her seçeneğe kısa bir kimlik verir (`5.3` = market 5'in 3. seçeneği) ve oranları,
   ima edilen olasılıkla (`100 / oran`) birlikte listeler.
2. Model yalnızca bu kimliklerle JSON döner; `ForecastService` listede olmayan kimlikleri eler,
   tekrarları atar, güvene göre sıralar ve ilk 5'i alır. Böylece model olmayan bir bahis uyduramaz.
3. Arayüzde yeşil çubuk yapay zekanın güvenini, dikey çizgi oranın ima ettiği olasılığı gösterir.

## Genişletme noktaları

- Başka bir veri kaynağı: `IOddsProvider` uygula, `Program.cs`'te kaydet.
- Başka bir LLM (OpenAI vb.): `IForecastAiClient` uygula.
- Prompt ayarı: `Services/ForecastPrompt.cs`.
