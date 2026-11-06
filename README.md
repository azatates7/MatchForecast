# MatchForecast (Maç Analiz)

Günün futbol maçlarını API-Football'dan çeker, her maç için yapay zekadan istatistik **beklentilerini** (gol, ilk yarı
golü, korner, kart) ister, bunları Poisson modeliyle olasılığa çevirir ve gerçekleşme olasılığı en yüksek
**10 tahmini** bir istatistik tablosuyla birlikte listeler (ör. "MS 1", "2.5 Alt", "KG Var", "İY 0.5 Üst").
Yapay zeka bahis seçmez; olasılıklar ve tahmin listesi kodda hesaplanır. Hiçbir şey girişsiz görünmez
(kullanıcı adı/şifre, Google veya şifresiz misafir girişi).

| Katman | Teknoloji |
|---|---|
| Backend | .NET 10 Web API (controller'lar): `backend/MatchForecast.Api`, `.Models`, `.Data`, `.Logger`, `.Tests` |
| Frontend | React 19 + TypeScript + Vite (`frontend`) **veya** Angular 22, signals + standalone (`frontend-angular`); arayüz birebir aynı |
| Veritabanı | Supabase (PostgreSQL), EF Core + Npgsql, tablolar `matchforecast` şemasında |
| Önbellek | Redis (`StackExchange.Redis`); kapalıyken API çalışmaya devam eder, yalnızca önbellek devre dışı kalır |
| Veri | API-Football (api-sports.io v3), yalnızca `/fixtures` |
| Yapay zeka | `Ai:Provider` ile seçilir: Gemini, Claude veya Ollama (yerel ya da Ollama Cloud) |
| Kimlik / yetki | JWT + dönen refresh token; veritabanındaki roller, gruplar ve yetkiler; yönetim paneli |
| Altyapı | Docker (çok aşamalı imajlar), `docker-compose.yml` / `docker-compose.prod.yml` (Cloudflare Tunnel), Azure Pipelines |

---

## ⚠ Durum: doğrulanmayı bekleyen değişiklikler

Son değişiklikler (favori ligler, tahminlerin kaydı, yetki modeli, yönetim paneli) NuGet erişimi olmayan bir ortamda
yazıldı. C# dosyaları yalnızca framework referanslarına karşı derlendi; `dotnet build` / `dotnet test` ile henüz
çalıştırılmadı. Frontend'ler derlendi ve tarayıcıda **sahte bir API'ye** karşı test edildi. Sırayla:

1. **Migration `AddPermissions`'ı üretin.** Elle yazılmadı; üretilmeden API açılmaz (EF Core, bekleyen model
   değişikliği varken `MigrateAsync` yapmaz):
   ```bash
   cd backend
   dotnet ef migrations add AddPermissions --project MatchForecast.Data --startup-project MatchForecast.Api
   ```
   Beklenen içerik: `permissions` (benzersiz `key`) ve `role_permissions` (PK `role_id, permission_id`, iki yönde
   cascade) tabloları, `roles.is_system` kolonu (bool, varsayılan false). İlk açılışta seed 8 sistem yetkisini ekler ve
   `Admin`/`User`/`Guest`'i sistem rolü olarak işaretler.
2. **`LocalizationTests` düzeltmesi** uygulanmadıysa: 25. satır `factory.WithTestJwt().CreateClient()` olmalı
   (aksi halde host gerçek `appsettings.json` ile açılıp `Smtp:Password` doğrulamasında düşer).
3. `dotnet build` ve `dotnet test backend/MatchForecast.Tests/MatchForecast.Tests.csproj`.
4. **Migration kontrolü:** `dotnet ef migrations add Check --project MatchForecast.Data --startup-project MatchForecast.Api`
   **boş** bir migration üretmeli (elle yazılan `AddUserFavoriteLeagues` ve `AddForecasts` snapshot ile uyumlu demektir),
   sonra `dotnet ef migrations remove ...`. Boş değilse bu iki migration'ı ve snapshot değişikliklerini silip
   `dotnet ef` ile yeniden üretin.
5. Gerçek API ile elle kontrol: yıldızla favori lig ekleme / "Favori ligler" seçeneği; "Önbelleği Sil" yalnızca
   yetkisi olana; bir tahmin açılınca `matchforecast.forecasts`'ta satır oluşması; yönetim panelinde bir rol oluşturup
   yetki verme, bir kullanıcıyı bu rolün olduğu gruba ekleme ve o kullanıcıyla girince yalnızca ilgili sekmenin
   görünmesi.
6. İsteğe bağlı: `MSB3277` uyarısı (EF Core Relational 10.0.0 / 10.0.13) için `MatchForecast.Tests.csproj`'daki EF
   paket sürümlerini Api/Data projeleriyle (10.0.13) eşitleyin.

---

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
dotnet user-secrets set "Ai:Gemini:ApiKey" "<anahtar>"                            # veya Ai:Claude:ApiKey / Ai:Ollama:*
dotnet user-secrets set "Jwt:SecretKey" "<en az 32 rastgele karakter>"
dotnet user-secrets set "Smtp:Password" "<Gmail uygulama şifresi>"                 # gerekmiyorsa Smtp:Enabled=false
dotnet user-secrets set "Admin:UserName" "admin"                                  # ilk Admin, bir kez oluşturulur
dotnet user-secrets set "Admin:Email" "<e-posta>"
dotnet user-secrets set "Admin:Password" "<şifre>"
dotnet run                                                                        # http://localhost:5080, Swagger: /swagger

# 2) Frontend (ayrı terminal) — biri yeterli
cd frontend && npm install && npm run dev                                         # http://localhost:5173
cd frontend-angular && npm install && npm start                                   # http://localhost:4200
```

İsteğe bağlı ayarlar:
- `Auth:GoogleClientId` — boşsa Google düğmesi gösterilmez.
- `Auth:PasswordResetUrl` — sıfırlama bağlantısının açılacağı frontend adresi (varsayılan `http://localhost:4200/`;
  React geliştirme sunucusu için `http://localhost:5173/`).
- `Guest:UserName`, `Guest:Email` (+ isteğe bağlı `Guest:Password`) — paylaşılan misafir hesabı. Yalnızca
  env/user-secrets'tan okunur, `app_settings`'e kopyalanmaz. Sahte bir e-posta kullanın.

Redis yoksa API çalışır ama hiçbir şey önbelleğe alınmaz ve önbellek temizleme 503 döner.
`ConnectionStrings:Supabase` yoksa API açılır ama giriş/kayıt çalışmaz.

### Ayarlar veritabanında (`app_settings`)

Açılışta migration'lar otomatik uygulanır ve ayarlar `app_settings` tablosundan okunur. İlk çalıştırmada
appsettings.json / user-secrets / ortam değişkenlerindeki değerler tabloya kopyalanır; sonrasında **veritabanındaki
değer geçerlidir** (en son yapılandırma kaynağı).
- Ortamda kalması gereken yalnızca iki değer vardır: `ConnectionStrings:Supabase` ve `AppSettings:EncryptionKey`
  (32 byte base64; kaybolursa şifreli satırlar okunamaz).
- API anahtarları, şifreler ve JWT anahtarı AES-256-GCM ile şifreli saklanır, API yanıtlarında maskelenir.
- Değişiklik `/api/app-settings` (`app-settings.manage`) ile yapılır ve **yeniden başlatınca** geçerli olur.
  `DELETE` = varsayılana dön (bir sonraki açılışta dosya/secrets/env'den yeniden kopyalanır).
- Bağlantı dizesi yoksa (CI testleri) bu adım atlanır.

---

## Proje yapısı

```
MatchForecast/
├── .gitignore
├── azure-pipelines.yml              CI/CD: main'e push → test → VPS'te deploy (self-hosted agent)
├── docker-compose.yml               yerel: redis + api + web (React)
├── docker-compose.prod.yml          canlı: aynı servisler + cloudflared, açık port yok
├── MatchForecast.slnx               çözüm: Api, Models, Data, Logger, Tests
├── README.md
├── backend/
│   ├── Dockerfile
│   ├── MatchForecast.Api/
│   │   ├── Controllers/             Auth, Me, FavoriteLeagues, Password, Matches, Cache,
│   │   │                            Users, Roles, Groups, Permissions, AppSettings, SampleItems
│   │   ├── Filter/                  BearerSecurityOperationFilter (Swagger)
│   │   ├── Options/                 Ai, Auth, Forecast, Jwt, OddsProvider, RateLimit, Redis, Smtp
│   │   ├── Services/
│   │   │   ├── AI/                  Claude/, Gemini/, Ollama/ istemcileri + PoissonMath
│   │   │   ├── ApiFootball/         IOddsProvider, ApiFootballOddsProvider
│   │   │   ├── AppSetting/          DatabaseSettings (veritabanı yapılandırma kaynağı)
│   │   │   ├── Email/               SMTP, EmailErrorNotifier, StartupNotificationService
│   │   │   ├── Forecast/            ForecastService, ForecastStore, ForecastPrompt, ForecastCatalog,
│   │   │   │                        ProjectionBuilder, PredictionSelector, ForecastPrewarmQueue (+ Worker)
│   │   │   ├── Guest/               GuestAccess, GuestUserBootstrapper
│   │   │   ├── Health/              DatabaseHealthCheck, RedisHealthCheck, HealthResponseWriter
│   │   │   ├── Redis/               RedisCacheService, InMemoryRedisCacheService (testler)
│   │   │   └── Security/            AuthService, JwtTokenService, GoogleIdTokenValidator, PasswordGenerator,
│   │   │                            AdminUserBootstrapper, TokenCleanUpWorker, PermissionAuthorization,
│   │   │                            PermissionResolver, AdminGuard
│   │   ├── Program.cs
│   │   └── appsettings.json
│   ├── MatchForecast.Models/        Common/ (Lang, ...), Request/, Response/ (record DTO'lar)
│   ├── MatchForecast.Data/          Configuration/AppDbContext, Entity/, Migrations/, Seed/
│   │                                (RoleSeed, PermissionSeed, ...), Security/AppSettingsCrypto
│   ├── MatchForecast.Logger/        NLog, Middleware/ (Logging, Exception), Notifications/IErrorNotifier
│   └── MatchForecast.Tests/         xUnit birim + WebApplicationFactory entegrasyon testleri (TestAuth)
├── frontend/                        React 19 + Vite
│   ├── Dockerfile, nginx.conf, index.html, vite.config.ts, tsconfig.json, package.json
│   └── src/
│       ├── App.tsx, main.tsx, api.ts, types.ts, styles.css
│       ├── AdminView.ts             yönetim görünümü: sekmeler, #admin adresi, yetkiye göre görünürlük
│       ├── i18n/                    i18n.ts, messages.ts, useI18n.ts
│       └── components/
│           ├── AdminPanel.tsx, AuthPanel.tsx, ForecastPanel.tsx, Google.ts, LanguageToggle.tsx,
│           │   LeagueFilter.tsx, MatchList.tsx, MatchStatus.tsx, StatsTable.tsx, ThemeToggle.tsx
│           └── admin/
│               ├── AdminUI.tsx      ortak parçalar: Chips, useAction, ActionStatus
│               ├── UsersTab.tsx, UserDialog.tsx
│               ├── RolesTab.tsx, RoleDialog.tsx
│               ├── GroupsTab.tsx, GroupDialog.tsx
│               └── PermissionsTab.tsx, PermissionDialog.tsx
└── frontend-angular/                Angular 22 (zoneless, signals)
    ├── Dockerfile, nginx.conf, angular.json, proxy.conf.json, package.json, README.md
    ├── tsconfig.json, tsconfig.app.json, tsconfig.spec.json
    ├── .dockerignore, .editorconfig, .gitignore, .prettierrc
    └── src/
        ├── index.html, main.ts, styles.css
        └── app/
            ├── app.ts, app.html, app.css, app.config.ts, app.routes.ts, app.spec.ts
            ├── core/
            │   ├── api.ts, types.ts, admin-view.ts, google.ts, match-status.ts
            │   └── i18n/            i18n.ts, i18n.service.ts, messages.ts
            └── components/
                ├── auth-panel, forecast-panel, league-filter, match-list, stats-table   (.ts + .html)
                ├── language-toggle.ts, theme-toggle.ts
                ├── admin-panel.ts/.html, admin-panel.spec.ts, admin-action.ts
                ├── admin-users-tab, admin-user-dialog                                     (.ts + .html)
                ├── admin-roles-tab, admin-role-dialog                                     (.ts + .html)
                ├── admin-groups-tab, admin-group-dialog                                   (.ts + .html)
                └── admin-permissions-tab, admin-permission-dialog                         (.ts + .html)
```

**Adlandırma:** React bileşenleri PascalCase (`components/admin/` altında), Angular bileşenleri `components/` altında
düz ve kebab-case (`admin-` önekiyle). Kod yorumları **Türkçe**dir; yenileri de öyle yazılmalı.

**Birbirinin kopyası olan dosyalar** (biri değişince diğeri de güncellenmeli):
| React (`frontend/src`) | Angular (`frontend-angular/src/app`) |
|---|---|
| `api.ts`, `types.ts` | `core/api.ts`, `core/types.ts` |
| `AdminView.ts` | `core/admin-view.ts` |
| `i18n/messages.ts`, `i18n/i18n.ts` | `core/i18n/messages.ts`, `core/i18n/i18n.ts` |
| `components/MatchStatus.tsx`, `components/Google.ts` | `core/match-status.ts`, `core/google.ts` |
| `styles.css` | `src/styles.css` (birebir aynı) |

---

## Nasıl çalışır

1. `ApiFootballOddsProvider` günün maçlarını çeker, bitmiş ve eskimiş başlamamış maçları eler, başlama saatine göre
   sıralar. Popüler maçlarda Süper Lig, UCL, Premier League, La Liga, Serie A, Bundesliga, Ligue 1, UEL, UECL öndedir.
2. `ForecastPrompt` modelden yalnızca her istatistiğin beklenen değerini (λ) JSON olarak ister (Türkçe ve İngilizce
   açıklamalar aynı çağrıda).
3. `ProjectionBuilder` değerleri doğrular/sınırlar (`Forecast:Max*`) ve `PoissonMath` ile skor dağılımı, 1X2, KG ve
   alt/üst olasılıklarını hesaplar.
4. `PredictionSelector`, `ForecastCatalog`'daki seçeneklerden `Forecast:MinPickProbability`–`MaxPickProbability`
   bandındakileri tercih ederek, her gruptan en fazla bir tane olmak üzere `Forecast:PredictionCount` (10) tahmin seçer.

**Önbellek (Redis, `MatchForecast:` önekiyle):** `fixtures:{date}` (canlı maç varsa 1 dk, yoksa
`OddsProvider:CacheMinutes`), `fixture:{id}`, `forecast:v3:{id}` (`Ai:CacheMinutes`). Redis hatası önbellek ıskası
sayılır; API kaynaktan cevap vermeye devam eder. Aynı maç için eşzamanlı istekler tek bir AI çağrısını bekler.

**Ön ısıtma:** Her token verilişinde (giriş, kayıt, Google, misafir, yenileme) arka planda o günün Süper Lig tahminleri
sırayla üretilir (Redis kilidi ile tek seferde bir kez). `Forecast:PrewarmOnLogin=false` ile kapanır.

**Tahminlerin kaydı:** Üretilen her tahmin (önbellekte yoksa veya `refresh=true`) `forecasts` tablosuna da yazılır;
maç başına tek satır, yeniden üretilirse güncellenir (`result_json` jsonb = API yanıtıyla aynı biçim, sağlayıcı ve
model adı). **Okuma hâlâ Redis'ten yapılır**; tablo sonradan analiz içindir. Kayıt hatası tahmini engellemez.

---

## Kimlik doğrulama, roller ve yetkiler

- **Giriş:** kullanıcı adı + şifre (PBKDF2), kayıt (`User` rolüyle doğrudan giriş), Google ID token, şifresiz misafir.
  Şifre sıfırlama bağlantısı e-postayla gelir (`Auth:PasswordResetUrl` + `?resetToken=`); başarılı sıfırlama tüm
  oturumları kapatır.
- **Token'lar:** HS256 access token (`Jwt:ExpiryMinutes`), 64 byte refresh token (SHA-256 ile saklanır, her yenilemede
  değişir; iptal edilmiş bir token tekrar kullanılırsa kullanıcının tüm token'ları iptal edilir). Frontend'ler access
  token'ı bellekte, refresh token'ı `localStorage`'da (`matchforecast.refreshToken`) tutar.
- **Roller:** `Admin`, `User`, `Guest` sistem rolleridir (adı değişmez, silinemez); panelden yeni roller eklenebilir.
  Kullanıcı rolleri doğrudan veya **gruplar** üzerinden alır.
- **Yetkiler:** veritabanındaki satırlardır (`area.action` anahtarı, TR/EN ad). Kullanıcının etkin yetkileri = doğrudan
  ve gruptan gelen tüm rollerin yetkileri; **`Admin` sonradan eklenenler dahil tüm yetkilere sahiptir.** Yetkiler JWT'ye
  `perm` claim'i olarak yazılır; değişiklikler kullanıcıya **token yenilenince** yansır (en geç `Jwt:ExpiryMinutes`).
  `GET /api/me` rolleri ve yetkileri döner; frontend'ler düğme/sekme göstermek için kullanır, asıl kontrol API'dedir.
- **Uygulama:** `[RequirePermission(PermissionKeys.X)]`. Birden fazla anahtar verilirse **biri yeterlidir**. Endpoint
  dışında kontrol için `PermissionPolicy.HasPermission(User, key)`.
- **Kilitlenme koruması:** kimse kendi hesabını pasife alamaz, silemez veya kendi Admin'liğini (doğrudan ya da grup
  üzerinden) kaldıramaz; son aktif Admin pasife alınamaz, silinemez, Admin'liği kaldırılamaz (`AdminGuard`).

**Sistem yetkileri.** Okuma uçları "herhangi biri" kuralıyla açılır (her sekme ihtiyaç duyduğu adları okuyabilsin
diye); her değişiklik ilgili `*.manage` yetkisini ister.

| Anahtar | Panel sekmesi | Verdiği erişim |
|---|---|---|
| `users.view` | Kullanıcılar (salt okunur) | `GET /api/users[/{id}]`, `GET /api/roles`, `GET /api/groups` |
| `users.manage` | Kullanıcılar (düzenleme) | kullanıcı oluşturma/güncelleme/silme, şifre, kullanıcının rolleri/grupları |
| `roles.manage` | Roller | rol CRUD, rol ↔ yetki; ayrıca `GET /api/roles`, `GET /api/permissions` |
| `groups.manage` | Gruplar | grup CRUD, grup rolleri/üyeleri; ayrıca `GET /api/groups`, `GET /api/roles`, `GET /api/users` |
| `permissions.manage` | Yetkiler | yetki CRUD (yalnızca özel yetkiler silinir); ayrıca `GET /api/permissions`, `GET /api/roles` |
| `cache.clear` | — | `DELETE /api/cache` ("Önbelleği Sil" düğmesi) |
| `forecast.refresh` | — | tahmin uçlarında `refresh=true` |
| `app-settings.manage` | — | `/api/app-settings` (yalnızca API, panelde yok) |

Seed yalnızca eksik yetkileri ekler; panelde yapılan rol ↔ yetki atamalarına dokunmaz. Varsayılan olarak `User` ve
`Guest`'in hiçbir yetkisi yoktur.

**Kodda yeni yetki kontrolü eklemek:** `PermissionKeys`'e sabit, `PermissionSeed.CreatePermissions`'a satır (TR/EN ad,
açıklama) ekleyin ve endpoint'e `[RequirePermission(PermissionKeys.X)]` koyun. Migration ve `Program.cs` değişikliği
gerekmez; seed satırı bir sonraki açılışta ekler. Panelden eklenen **özel** yetkiler, kod anahtarlarını kontrol edene
kadar hiçbir şeyi açmaz.

**Misafir:** yalnızca **bugünün** (İstanbul saati) **Türkiye / Süper Lig** maçları; `date` yok sayılır. Toplu tahmin,
favori ligler ve örnek uçlar misafire kapalıdır (`NotGuest` politikası). Paylaşılan hesap olduğu için AI kotası IP
başına sayılır.

---

## Yönetim paneli

Yalnızca **kullanıcı, rol, grup ve yetki** yönetimi içindir (maçlar ve uygulama ayarları panelde yok). Üst çubuktaki
"Yönetim" düğmesi en az bir sekmeyi görebilen kullanıcıya çıkar. Adres tabanlıdır: `#admin` ilk görünen sekmeyi,
`#admin/users|roles|groups|permissions` ilgili sekmeyi açar; tarayıcının geri/ileri düğmeleri çalışır. Yetkisi
olmayan biri `#admin` açarsa maç ekranında kalır.

- **Kullanıcılar** (`users.view`, düzenleme `users.manage`): arama; oluşturma (şifre üretici); profil, roller,
  gruplar, şifre belirleme (tüm oturumları kapatır), silme (kullanıcı adı yazılarak); etkin yetkiler salt okunur.
  `users.manage` yoksa her şey salt okunurdur.
- **Roller** (`roles.manage`): oluşturma, ad/açıklama (sistem rolünde yalnızca açıklama), alana göre gruplanmış yetki
  listesi; Admin "tüm yetkiler"dir, düzenlenmez. Atanmış rol, uyarıyla ve rol adı yazılarak silinir.
- **Gruplar** (`groups.manage`): oluşturma, roller (Admin seçilirse uyarı), aranabilir üye listesi, silme (üyeler
  silinmez).
- **Yetkiler** (`permissions.manage`): anahtar, TR/EN ad, açıklama, "Sistem"/"Özel" etiketi ve yetkinin hangi
  rollerde olduğu. Özel yetki ekleme, düzenleme, silme (anahtar yazılarak); sistem yetkisinin anahtarı değişmez,
  silinemez. Rollere atama Roller sekmesinden yapılır.

API'nin koruma mesajları (son Admin, kendi Admin'liği, tekrarlanan ad) ilgili bölümde gösterilir.

---

## Endpointler

`[AllowAnonymous]` olmayan her endpoint `Authorization: Bearer {accessToken}` ister.

| Yöntem | Yol | Açıklama |
|---|---|---|
| GET | `/api/auth/config` | Giriş ekranı ayarları (Google Client ID, misafir girişi açık mı) — anonim |
| POST | `/api/auth/token` | Kullanıcı adı + şifre ile access + refresh token — anonim |
| POST | `/api/auth/refresh` | Refresh token ile yeni token çifti (eskisi geçersiz olur) — anonim |
| POST | `/api/auth/register` | Yeni hesap (`User` rolü) ve doğrudan giriş — anonim |
| POST | `/api/auth/google` | Google ID token ile giriş — anonim |
| POST | `/api/auth/guest` | Şifresiz misafir girişi — anonim |
| POST | `/api/auth/forgot-password` | Şifre sıfırlama e-postası (her durumda 204) — anonim |
| POST | `/api/auth/reset-password` | E-postadaki token ile yeni şifre — anonim |
| POST | `/api/auth/revoke` | Çıkış (refresh token iptali) |
| GET | `/api/password?length=16` | Rastgele güvenli şifre (8–128, saklanmaz) — anonim |
| GET | `/api/me` | Kullanıcı adı, görünen ad, roller, etkin yetkiler |
| GET, PUT, DELETE | `/api/me/favorite-leagues[?league=]` | Kullanıcının favori ligleri (`"Ülke / Lig"`) — misafir hariç |
| GET | `/api/matches?date=2026-10-23` | O günün maçları |
| GET | `/api/matches/popular?date=&count=` | Popüler liglerden maçlar (en fazla 10) |
| GET | `/api/matches/popular/forecasts?date=&count=` | Popüler maçlar için toplu tahmin — misafir hariç |
| GET | `/api/matches/{id}/forecast` | Maçın tahmini (önbellekli; `refresh=true` yalnızca `forecast.refresh`) |
| DELETE | `/api/cache` | Uygulamanın Redis önbelleğini temizler — `cache.clear` |
| * | `/api/users` (+ `/{id}/password`, `/roles`, `/groups`) | Kullanıcı yönetimi |
| * | `/api/roles` (+ `/{id}/permissions`, `DELETE ?force=`) | Rol yönetimi |
| * | `/api/groups` (+ `/{id}/roles`, `/{id}/users`) | Grup yönetimi |
| * | `/api/permissions` | Yetki yönetimi |
| * | `/api/app-settings[/{id}]` | Veritabanındaki ayarlar — `app-settings.manage` |
| GET | `/healthz` | Liveness (hiçbir şeyi kontrol etmez) |
| GET | `/healthz/ready` | Veritabanı ve Redis (DB yoksa 503, Redis yoksa 200 + Degraded) |

Yönetim uçlarının yetkileri için yukarıdaki tabloya bakın. Swagger UI ve OpenAPI dokümanı (yalnızca Development):
`/swagger`, `/openapi/v1.json` (`Authorize` düğmesiyle).

**Rate limiting** (yerleşik ASP.NET Core rate limiter, sayaçlar örnek başına bellekte; aşılırsa `429`):
- Giriş/kayıt/şifre uçları: **IP başına** dakikada 10.
- AI tahmin uçları: **kullanıcı başına** 10 dakikada 30 (önbellekten dönenler de sayılır).
- Önbellek temizleme: **tüm istemciler için** 60 dakikada 2.
- `RateLimiting:TrustCloudflareHeader=true` istemci IP'sini `CF-Connecting-IP`'den okur (yalnızca API'ye sadece
  Cloudflare üzerinden erişiliyorsa güvenli). Yetkilendirme rate limiter'dan önce çalışır; 401/403 kota harcamaz.

---

## Çoklu dil (TR / EN)

- Site adı: TR **"Maç Analiz"**, EN **"Match Forecast"**. Dil seçimi üst çubukta ve giriş kartında; öncelik
  `?lang=` > `localStorage` (`matchforecast.lang`) > tarayıcı dili.
- Arayüz metinleri sözlüklerdedir (`messages.ts`, iki frontend'de aynı). Metin elle yazılmaz: anahtar önce `tr`'ye
  eklenir, `en` eksikse derleme hata verir.
- İstekler `Accept-Language: tr|en` gönderir; backend mesajları `Lang.T("Türkçe", "English")` ile yazılır
  (`ProblemDetails`, doğrulama mesajları, 429 metinleri dahil). Varsayılan `tr`.
- Tahminler iki dilli üretilir (`{ "tr": ..., "en": ... }`); dil değiştirmek yeni istek gerektirmez.

---

## Testler

```bash
dotnet test backend/MatchForecast.Tests/MatchForecast.Tests.csproj   # kök dizinden; DB/Redis gerekmez

cd frontend && npm run build                     # tsc -b + vite build (tip kontrolü)
cd frontend-angular && npm test -- --watch=false # Vitest: app.spec.ts, components/admin-panel.spec.ts
```

- Entegrasyon testleri `WebApplicationFactory` ile çalışır, veritabanı yerine bellek içi EF Core, Redis yerine
  `InMemoryRedisCacheService` kullanır. **Her test istemcisi `WithTestJwt()` / `CreateAuthorizedClient()` üzerinden
  oluşturulmalı**; aksi halde host gerçek `appsettings.json` ile açılır. Özel token için
  `TestAuth.CreateClientFor(userId, roles, permissions...)`.
- Bellek içi veritabanında seed çalışmaz; testler rolleri/yetkileri kendisi ekler (gerekirse gerçek `RoleSeed` /
  `PermissionSeed` ile).
- `npm test`, `--watch=false` olmadan izleme modunda açık kalır ve dosya değiştikçe yeniden çalışır.

---

## Docker ve CI/CD

```bash
# Yerel (kök dizin): .env dosyasına Section__Key biçiminde yazın, en az:
#   ConnectionStrings__Supabase=...  AppSettings__EncryptionKey=...  OddsProvider__ApiKey=...
#   Ai__Provider=Gemini  Ai__Gemini__ApiKey=...  Jwt__SecretKey=...  Smtp__Password=...
docker compose up --build                                   # http://localhost:8080
docker compose logs -f api
docker compose down

# Canlı ortam (Cloudflare Tunnel arkasında, açık port yok); .env'de ayrıca CLOUDFLARE_TUNNEL_TOKEN
docker compose -f docker-compose.prod.yml up -d --build
```

- `backend/Dockerfile`: `sdk:10.0` → `aspnet:10.0`, port `8080`, `TZ=Europe/Istanbul` (sunucudaki "bugün" buna bağlı),
  loglar `/app/logs`.
- `frontend/Dockerfile`, `frontend-angular/Dockerfile`: `node:22-alpine` ile build → `nginx:alpine`. `nginx.conf` SPA'yı
  sunar ve `/api/`'yi `http://api:8080`'e yönlendirir (yavaş AI çağrıları için 300 sn), bu yüzden CORS gerekmez.
- `azure-pipelines.yml`: `main`'e push edilince self-hosted agent (`matchforecast-vps`) `dotnet test` çalıştırır, sonra
  `/opt/matchforecast`'ta `git pull --ff-only` ve `docker compose -f docker-compose.prod.yml up -d --build` yapar.
  **Sunucu kodu git'ten aldığı için diskteki değil git'teki dosya adları geçerlidir** (aşağıya bakın).
- Angular production build'i Google Fonts'u dosyaya gömer; `fonts.googleapis.com`'a erişim yoksa
  `Inlining of fonts failed ... 403` ile düşer. Bu durumda `npm run build -- --configuration development` kullanın.

---

## Geliştirme notları

- **Dosya adlarında büyük/küçük harf (Windows):** Windows ve macOS harf farkını önemsemez, Docker/Linux önemser. Git de
  Windows'ta (`core.ignorecase=true`) yalnızca harfi değişen bir yeniden adlandırmayı kaydetmez; diskte `AdminView.ts`
  görünürken git'te hâlâ `adminView.ts` kalabilir ve build yalnızca sunucuda/Docker'da düşer. Üç şey aynı olmalı:
  diskteki ad, git'teki ad (`git ls-files <klasör>`) ve import (`'./AdminView'`). Harfi değiştirmek için iki adım:
  ```bash
  git mv frontend/src/adminView.ts frontend/src/tmp.ts
  git mv frontend/src/tmp.ts frontend/src/AdminView.ts
  ```
- **Yeni tablo / kolon:** `MatchForecast.Data`'ya entity ekleyin, sonra (backend/ içinden)
  `dotnet ef migrations add <Ad> --project MatchForecast.Data --startup-project MatchForecast.Api`. Migration'lar API
  açılışında otomatik uygulanır.
- **Başka bir veri kaynağı:** `IOddsProvider` uygulayın, `Program.cs`'te kaydedin.
- **Başka bir LLM:** `IForecastAiClient` uygulayın, `Program.cs`'teki `Ai:Provider` seçimine ekleyin.
- **Yeni market:** `Services/Forecast/ForecastCatalog.cs`'e bir satır. Prompt: `Services/Forecast/ForecastPrompt.cs`.
- **Yeni arayüz metni:** iki `messages.ts`'e de (TR ve EN) ekleyin.
- **Angular:** zoneless olduğu için ekran bir sonraki karede güncellenir; DOM'u hemen okuyan testler beklemeli
  (`admin-panel.spec.ts`'teki `eventually()`).

---

## Bilinen sorunlar

- Gerçek gizli değerler (`Smtp:Password`, `Jwt:SecretKey`) artık `appsettings.json`'da boş, ama **git geçmişinde**
  duruyor. Gmail uygulama şifresini ve JWT anahtarını değiştirin. Sızmış anahtarlardan biri hâlâ ayarlıysa açılış
  bilerek hata verir (`JwtOptions.IsLeakedKey`).
- `appsettings.json`'da `Ai:Provider=Ollama` ve `Ai:Ollama:BaseUrl`/`Model` boş; user-secrets'sız yeni bir klonda ilk
  tahmin hata verir. Sağlayıcıyı user-secrets'ta açıkça belirtin.
- `OddsProvider:UseMock` hiçbir yerde okunmuyor; `MockOddsProvider` ve `PublicOddsProvider` kayıtlı değil. Silinmeli
  veya bağlanmalı.
- Dosya adında boşluk: `Services/Redis/InMemoryRedisCacheService .cs`.
- `Services/Guest/*` dosyalarının namespace'i `MatchForecast.Api.Services.Security.Guest` (klasörle uyumsuz).
- `.gitignore`'da olduğu halde git'te duran dosyalar: `backend/MatchForecast.Logger/logs/*`,
  `frontend/tsconfig.tsbuildinfo`. `git rm --cached` ile kaldırılmalı.
- Favori ligler: yıldıza tıklayınca liste hemen yeniden sıralanıyor ve satır imlecin altından kayıyor; boş yıldızın
  üzerine gelince sarıya dönmesi favori gibi görünüyor.

---

## Yol haritası

Tamamlananlar: Redis, rate limiting, Docker, Supabase, EF Core, kimlik doğrulama ve kullanıcı yönetimi, misafir
girişi, sağlık kontrolleri, ayarların veritabanına taşınması, çoklu dil, favori ligler, tahminlerin kaydı, yetki modeli
ve yönetim paneli.

Sıradaki işler (öncelik sırasıyla öneri):
- [ ] Yukarıdaki **doğrulama** adımları.
- [ ] Kullanıcının rolü/grubu değişince refresh token'larını iptal etmek (değişiklik token süresini beklemeden
      yansısın; bedeli: kullanıcı yeniden giriş yapar).
- [ ] Favori ligler: panel açıkken sıralamayı sabit tutmak; boş yıldızda üzerine gelince sarı yerine yalnızca
      koyulaşmak; isteğe bağlı olarak sayfa açılışında favori ligleri seçili getirmek.
- [ ] Geçmiş tahminlerin sonuçlarını (biten maçın skoru) çekip market bazında isabet oranı hesaplamak; günlük arka
      plan servisi + panelde rapor.
- [ ] docker-compose: `/healthz` ve `/healthz/ready` ile `healthcheck`, Redis volume, `.env.example`, isteğe bağlı
      Angular servisi.
- [ ] İsteğe bağlı: ayar değişikliğinde yeniden başlatma yerine yeniden yükleme; ayarlar ve sistem durumu ekranı;
      API birden fazla örnekle çalışacaksa Redis tabanlı rate limiting; liste uçlarında sayfalama; yetki listesi çok
      büyürse token boyutunu izlemek (Admin her anahtarı taşır).