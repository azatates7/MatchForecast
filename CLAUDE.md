# CLAUDE.md — MatchForecast Project Guide & Roadmap

MatchForecast fetches the day's football fixtures from API-Football, asks an LLM for the **expected values** of match
statistics (goals, first-half goals, corners, cards), turns those into probabilities with a Poisson model, and lists
the **10 most likely predictions** with a stats table. The LLM never picks bets; all probabilities and the prediction
list are computed in code. Users sign in (username/password, Google, or password-less guest) before they can see anything.

- **Backend:** .NET 10 Web API with controllers (`backend/MatchForecast.Api`), Models class library
  (`backend/MatchForecast.Models`), Data library with EF Core (`backend/MatchForecast.Data`), Logger & middleware
  library (`backend/MatchForecast.Logger`), xUnit tests (`backend/MatchForecast.Tests`)
- **Frontends (identical UI, pick either):**
  - React 19 + TypeScript + Vite (`frontend`)
  - Angular 22 + signals, standalone components (`frontend-angular`)
- **Database:** Supabase (PostgreSQL) via EF Core + Npgsql, tables in the `matchforecast` schema
- **Cache:** Redis (`StackExchange.Redis`)
- **External data:** API-Football (api-sports.io v3), fixtures only (odds are no longer used)
- **AI engine:** pluggable via `Ai:Provider`: Gemini, Claude or Ollama (local or Ollama Cloud)
- **Infrastructure:** Docker (multi-stage images), `docker-compose.yml` (local) and `docker-compose.prod.yml`
  (production behind Cloudflare Tunnel), Azure Pipelines CI/CD (`azure-pipelines.yml`)

### Repository layout
```
MatchForecast.slnx                 solution (Api, Models, Data, Logger, Tests)
azure-pipelines.yml                CI/CD: test → deploy on push to main (self-hosted agent on the VPS)
docker-compose.yml / .prod.yml     local stack / production stack (+ cloudflared)
backend/
  Dockerfile
  MatchForecast.Api/
    Controllers/                   Auth, Me, FavoriteLeagues, Password, Matches, Cache, Users, AppSettings, SampleItems
    Filter/                        BearerSecurityOperationFilter (Swagger)
    Options/                       Ai, Auth, Forecast, Jwt, OddsProvider, RateLimit, Redis, Smtp
    Services/
      AI/                          Claude/, Gemini/, Ollama/ clients + PoissonMath
      ApiFootball/                 IOddsProvider, ApiFootballOddsProvider (+ unused Mock/Public providers)
      AppSetting/                  DatabaseSettings (DB-backed configuration source)
      Email/                       SMTP sender, EmailErrorNotifier, StartupNotificationService
      Forecast/                    ForecastService, Prompt, Catalog, ProjectionBuilder, PredictionSelector,
                                   ForecastPrewarmQueue (+ ForecastPrewarmWorker in the same file)
      Guest/                       GuestAccess, GuestUserBootstrapper (namespace: Services.Security.Guest)
      Health/                      DatabaseHealthCheck, RedisHealthCheck, HealthResponseWriter
      Redis/                       RedisCacheService, InMemoryRedisCacheService (tests)
      Security/                    AuthService, JwtTokenService, GoogleIdTokenValidator, PasswordGenerator,
                                   AdminUserBootstrapper, TokenCleanUpWorker
  MatchForecast.Models/            Common/, Request/, Response/ DTOs (records)
  MatchForecast.Data/              Configuration/AppDbContext, Entity/, Migrations/, Seed/, Security/AppSettingsCrypto
  MatchForecast.Logger/            NLog config, Middleware/ (Logging, Exception), Notifications/IErrorNotifier
  MatchForecast.Tests/             xUnit unit + WebApplicationFactory integration tests (TestAuth helper)
frontend/                          React 19 + Vite (src/App.tsx, api.ts, types.ts, i18n/, components/)
frontend-angular/                  Angular 22 (src/app/app.ts, core/ incl. core/i18n/, components/)
```
Code comments are in **Turkish**; keep new ones consistent. UI texts are TR/EN (see "Internationalization" below):
never hard-code a user-facing string, add it to the dictionaries / `Lang.T(...)`.

---

## 0. Current Status / Handoff (last updated 2026-11-04)

Read this first when resuming work.

**Next task:** Roadmap step 8 — **Admin panel** (detailed plan in section 2). Start with phase 8.0 (backend prerequisites).

**Changes from the last session, delivered as files but NOT yet verified with a build** (the session environment had
no NuGet access, so the backend was neither built nor tested; React passed `tsc -b`, Angular passed `ngc` type-check):
1. **CI fix — `LocalizationTests`:** the theory created its client with `factory.CreateClient()` and skipped
   `WithTestJwt()`, so the host started with the real `appsettings.json` (`Smtp:Enabled=true`, empty password) and
   `ValidateOnStart` failed on the CI agent (`Smtp:Password ... boş olamaz`, 5 failures). Fix: line 25 →
   `factory.WithTestJwt().CreateClient()`. **Apply if not yet done.** Rule: every `WebApplicationFactory` client in
   integration tests must go through `WithTestJwt()` / `CreateAuthorizedClient()`.
2. **Cache clear is Admin-only** (see "Cache clear" below).
3. **Favorite leagues** (see "Favorite leagues" below), including the hand-written migration `AddUserFavoriteLeagues`.

**To verify before starting step 8:**
- [ ] `dotnet build` + `dotnet test backend/MatchForecast.Tests/MatchForecast.Tests.csproj` (new:
      `FavoriteLeaguesIntegrationTests`, `RateLimitingIntegrationTests.ClearCache_NonAdmin_ReturnsForbidden`).
- [ ] Migration check: `dotnet ef migrations add Check --project MatchForecast.Data --startup-project MatchForecast.Api`
      must produce an **empty** migration (proves the hand-written snapshot matches the model); then
      `dotnet ef migrations remove ...`. If it is not empty, delete `20261103120000_AddUserFavoriteLeagues*` and the
      snapshot change and regenerate with `dotnet ef migrations add AddUserFavoriteLeagues`.
- [ ] Manual UI check in both frontends: star toggle, "Favori ligler" option, cache button only for Admin.
- [ ] Optional: build warning `MSB3277` (EF Core Relational 10.0.0 vs 10.0.13) — align the EF package versions in
      `MatchForecast.Tests.csproj` with the Api/Data projects (10.0.13).

**Open UX follow-ups for favorite leagues (agreed as small fixes, not done yet):**
- [ ] Clicking a star re-sorts the list immediately (favorites go to the top), so the clicked row jumps away and a
      different league ends up under the cursor. Fix: compute the favorites-first order only when the panel opens and
      keep it stable while the panel is open.
- [ ] Hovering an empty star turns it yellow, which looks like an existing favorite. Fix: on hover only darken
      (`color: var(--ink)`); keep yellow (`var(--card)`) for `aria-pressed="true"` only.
- [ ] Optional: pre-select favorite leagues on page load (today the default is still "Tümü").

---

## 1. Steps Completed So Far

### Backend core
- **Architecture & DI:** `Program.cs` with options pattern; critical options are validated at startup with
  `ValidateOnStart` (`JwtOptions`, `SmtpOptions`, `RedisOptions`, `ForecastOptions`, `AuthOptions`), so
  misconfiguration fails at boot, not on the first request.
- **Controllers:** `[ApiController]` classes under `Controllers/` are discovered automatically.
  - `AuthController` (anonymous): `GET /api/auth/config`, `POST /api/auth/token`, `POST /api/auth/refresh`,
    `POST /api/auth/register`, `POST /api/auth/google`, `POST /api/auth/guest`, `POST /api/auth/forgot-password`,
    `POST /api/auth/reset-password`, `POST /api/auth/revoke`
  - `MeController` (any signed-in user, guest included): `GET /api/me` → user name, display name (from DB), roles
  - `PasswordController` (anonymous): `GET /api/password?length=16` (random password generator, 8–128, nothing stored)
  - `MatchesController`: `GET /api/matches`, `GET /api/matches/popular`, `GET /api/matches/popular/forecasts`,
    `GET /api/matches/{id}/forecast`
  - `CacheController` (Admin): `DELETE /api/cache`
  - `FavoriteLeaguesController` (signed-in, not guest): `GET|PUT|DELETE /api/me/favorite-leagues`
  - `UsersController` (Admin): CRUD on `/api/users`, plus `PUT /api/users/{id}/password|roles|groups`
  - `AppSettingsController` (Admin): `GET /api/app-settings`, `GET|PUT|DELETE /api/app-settings/{id}`
  - `SampleItemsController`: demo CRUD on `/api/sample-items` (seeded table, kept as an EF Core example)
- **Swagger / OpenAPI** (Development only): `/swagger`, `/openapi/v1.json`, with an `Authorize` button
  (`Bearer {token}`) and `BearerSecurityOperationFilter` marking protected endpoints.
- **Fixtures (`IOddsProvider` → `ApiFootballOddsProvider`):** fetches fixtures via `x-apisports-key`, filters out finished
  and stale not-started matches, orders by kickoff. Popular matches prioritize Süper Lig, UCL, Premier League, La Liga,
  Serie A, Bundesliga, Ligue 1, UEL, UECL. API-Football errors returned with HTTP 200 in the `errors` field are surfaced
  as `ForecastException`.
- **Forecast engine:**
  - `ForecastPrompt`: asks the model only for expected values (λ) of each metric as JSON.
  - `IForecastAiClient` implementations: `GeminiForecastClient`, `ClaudeForecastClient`, `OllamaForecastClient`
    (selected by `Ai:Provider`; changing it requires a restart). API keys are masked in logs.
  - `ProjectionBuilder` validates/clamps model values (`Forecast:Max*`) and builds Poisson ranges/line probabilities
    (`PoissonMath`).
  - `ForecastCatalog` is the single list of selectable markets; adding a market is one line.
  - `PredictionSelector` picks `Forecast:PredictionCount` options, preferring the
    `MinPickProbability`–`MaxPickProbability` band, at most one per group, deterministically.
  - `count` on the popular endpoints is clamped to 1–10. `refresh=true` (skip forecast cache) is honored **only for
    Admin**; for other users it is ignored.
- **Logging (`MatchForecast.Logger`, NLog):** daily rolling files + console. Log folder comes from
  `MATCHFORECAST_LOG_DIR`, falling back to `{basedir}/logs`.
  - `LoggingMiddleware`: method, path, status, duration, request/response bodies with sensitive fields
    (`password`, `refreshToken`, ...) masked.
  - `ExceptionMiddleware`: `ForecastException` → its own status code; others → 500; RFC 7807 `ProblemDetails`.
    Client-aborted requests (`OperationCanceledException` with `RequestAborted` cancelled) return 499, logged at Debug, no e-mail.
- **E-mail (SMTP / Gmail App Password):** `EmailErrorNotifier` queues 5xx errors and sends them in the background with
  per-error throttling (`Smtp:ThrottleMinutes`); `StartupNotificationService` sends an "application started" mail;
  `AuthService` sends password-reset links. `Smtp:Enabled=false` turns sending off.

### ✅ Roadmap step 1 — Redis
- `IRedisCacheService` / `RedisCacheService` on `StackExchange.Redis` (singleton `IConnectionMultiplexer`) replaced
  `IMemoryCache`. Values are stored as JSON under the `Redis:InstanceName` prefix (`MatchForecast:`).
- **Fail-open:** `AbortOnConnectFail=false` + `BacklogPolicy.FailFast`; read/write errors count as cache misses, so the API
  keeps answering from the source when Redis is down.
- Cached keys: `fixtures:{date}` (1 min if a match is live, otherwise `OddsProvider:CacheMinutes`), `fixture:{id}`,
  `forecast:v3:{id}` (`Ai:CacheMinutes`), lock `prewarm:superlig:{date}` (10 min, `TryLockAsync` = `SET NX`).
- **Forecast prewarm:** every token issue in `AuthService` (login, register, Google, guest, refresh) calls
  `ForecastPrewarmQueue.Request()` (capacity-1 channel, never blocks). `ForecastPrewarmWorker` (hosted service) takes the
  Redis lock and generates today's Süper Lig forecasts one by one; cached ones are skipped. Off with
  `Forecast:PrewarmOnLogin=false` (also off in tests via `TestAuth`). If Redis is down the prewarm is skipped.
- `ForecastService` serializes cache misses per fixture (64 striped `SemaphoreSlim`s, single instance), so a user click
  during prewarm waits for the same result instead of calling the AI twice.
- `DELETE /api/cache` clears only this app's keys using `SCAN` (not `KEYS`, no `allowAdmin`); returns 503 if no Redis
  primary is connected. Startup validation prevents an empty `InstanceName` (which would match `*`).
- Tests use `InMemoryRedisCacheService`; integration tests never resolve `IConnectionMultiplexer`.

### ✅ Roadmap step 2 — Rate Limiting
- Built-in ASP.NET Core rate limiter (no extra package), configured by `RateLimitOptions` (`RateLimiting` section).
  Policies are attached with `[EnableRateLimiting]`; endpoints without it are not limited. Counters are in-memory,
  per instance.
  - `TokenPolicy` on `token`, `refresh`, `register`, `google`, `forgot-password`, `reset-password`: fixed window
    **per client IP**, default 10 requests / 60 s (also slows down password guessing).
  - `AiPolicy` on `GET /api/matches/{id}/forecast` and `GET /api/matches/popular/forecasts`: fixed window **per user**
    (JWT `sub`; guests and missing `sub` fall back to IP), default 30 requests / 10 min. Cached answers count too.
  - `CacheClearPolicy` on `DELETE /api/cache`: **global** sliding window, default 2 requests / 60 min.
- Client IP: `RateLimiting:TrustCloudflareHeader=true` reads `CF-Connecting-IP` (only safe when the API is reachable
  exclusively through Cloudflare, e.g. the prod tunnel); otherwise the connection address is used.
- Rejections return `429` as `ProblemDetails` (the frontends show `detail`); `Retry-After` is sent for fixed-window
  policies, not for the sliding cache-clear window.
- `UseRateLimiter()` runs **after** authorization, so unauthenticated calls get 401 without consuming quota.
- Covered by `RateLimitingIntegrationTests`.

### ✅ Roadmap step 3 — Docker Containerization
- `backend/Dockerfile`: multi-stage (`sdk:10.0` → `aspnet:10.0`), listens on `8080`, sets
  `MATCHFORECAST_LOG_DIR=/app/logs` and `TZ=Europe/Istanbul` (server-side "today" depends on local time).
- `frontend/Dockerfile` and `frontend-angular/Dockerfile`: `node:22-alpine` build → `nginx:alpine`.
  `nginx.conf` serves the SPA and reverse-proxies `/api/` to `http://api:8080` (300 s read timeout for slow AI calls),
  so no CORS is needed in containers.
- `docker-compose.yml` (local): `redis`, `api` (secrets from `.env`, Ollama via `host.docker.internal`, `./logs`
  volume), `web` (React, host port `8080`).
- `docker-compose.prod.yml`: same services with `restart: unless-stopped`, `ASPNETCORE_ENVIRONMENT=Production`,
  `Ai__Provider=Ollama` (Ollama Cloud via `Ai__Ollama__*`; like all env values it only seeds `app_settings` on the first
  run), `RateLimiting__TrustCloudflareHeader=true`, no published ports, plus a `cloudflared` service
  (`CLOUDFLARE_TUNNEL_TOKEN`) that exposes `http://web:80` through a Cloudflare Tunnel.
- `azure-pipelines.yml`: on push to `main` (PR builds disabled, the repo is public) a self-hosted agent
  (`matchforecast-vps` pool) runs `dotnet test` (Release), then in `/opt/matchforecast` does `git pull --ff-only` and
  `docker compose -f docker-compose.prod.yml up -d --build`.

### ✅ Roadmap step 4 — Database (Supabase / PostgreSQL)
- Replaced the planned MSSQL step with **Supabase PostgreSQL**. Connection string: `ConnectionStrings:Supabase`
  (user-secrets / env only, never in the repo; prod adds `Maximum Pool Size=10`).
- Tables live in the `matchforecast` schema (not `public`, which Supabase exposes through its REST Data API);
  the migrations history table is in the same schema.
- `DateTime` values are stored as UTC (`timestamptz`) and converted to/from Europe/Istanbul by a value converter.
- `DatabaseHealthCheck` is part of `/healthz/ready`.

### ✅ Roadmap step 5 — EF Core
- `AppDbContext` (`MatchForecast.Data/Configuration`) with entities: `User`, `Role`, `Group`, `UserRole`, `UserGroup`,
  `GroupRole`, `UserLogin` (external logins), `RefreshToken`, `PasswordResetToken`, `AppSetting`, `SampleItem`,
  `UserFavoriteLeague`.
- Migrations in `MatchForecast.Data/Migrations` (`InitialCreate`, `AddUserManagement`, `AddRefreshTokenReset`,
  `AddAppSettings`, `AddUserFavoriteLeagues` — the last one was written by hand, see section 0). They are applied **automatically at startup** (`DatabaseSettings.LoadAsync` →
  `MigrateAsync`); `EF.IsDesignTime` skips this during `dotnet ef`.
- Seeding via `UseSeeding`/`UseAsyncSeeding`: roles (`Admin`, `User` — `RoleNames`) and sample items; only missing
  rows are added.
- `AdminUserBootstrapper`: if no user has the Admin role and `Admin:UserName/Email/Password` are set, the first admin
  is created at startup (or an existing user with that name gets the role). Changing the setting later does not change
  the password.
- Tests replace the DbContext with `UseInMemoryDatabase`; the bootstrapper only runs on a relational provider.

### ✅ Roadmap step 6 — Authentication & user management
- **Credentials required:** `POST /api/auth/token` takes username + password in the body (not the query string).
  Passwords are hashed with ASP.NET Core `PasswordHasher<User>` (PBKDF2); unknown users still run a dummy verify
  (timing), and hashes are upgraded on login when the algorithm changes.
- **JWT + refresh tokens:** HS256 access tokens from `JwtTokenService` (unique `jti`, `sub`, `role` claims,
  `MapInboundClaims=false`), `Jwt:ExpiryMinutes`, 30 s clock skew. Refresh tokens are random 64 bytes, stored as SHA-256
  hashes, valid `Jwt:RefreshTokenExpiryDays`, **rotated** on every refresh; reusing a revoked token revokes all of the
  user's tokens. `POST /api/auth/revoke` = logout.
- **Register:** `POST /api/auth/register` creates a user with the `User` role and logs in directly.
- **Google login:** the frontend uses Google Identity Services; `POST /api/auth/google` validates the ID token
  (`GoogleIdTokenValidator`, audience = `Auth:GoogleClientId`), then finds the user by `UserLogin(Google, sub)`, links
  by verified e-mail, or creates a new user. `GET /api/auth/config` returns the client id; empty = Google button hidden.
  (Redirect-based OAuth/GitHub from the original plan was not needed.)
- **Password reset:** `forgot-password` always returns 204 (no account enumeration) and mails a link built from
  `Auth:PasswordResetUrl` (never from the request `Origin`) with `?resetToken=...`; token stored hashed, valid
  `Auth:PasswordResetTokenMinutes`. A successful reset revokes all refresh tokens.
- **Authorization:** a **fallback policy** protects every endpoint without `[AllowAnonymous]`. `UsersController`,
  `AppSettingsController` and `CacheController` require `Admin`. Roles (`RoleNames`): `Admin`, `User`, `Guest`.

### ✅ Guest access
- **Guest user:** `GuestUserBootstrapper` creates one shared account with only the `Guest` role at startup when
  `Guest:UserName` and `Guest:Email` are set (env/user-secrets only; the `Guest` section is **not** copied into
  `app_settings`). `Guest:Password` is optional; empty = only the button login works. Use a fake e-mail.
- **Login:** `POST /api/auth/guest` issues tokens for the first active `Guest` user without a password.
  `GET /api/auth/config` returns `guestLoginEnabled`; the frontends show "continue as guest" only then.
- **Restrictions (enforced in the API, `GuestAccess`; the frontend only mirrors them):** only **today's** (Istanbul
  time) **Turkey / Süper Lig** matches; the `date` parameter is ignored; single forecasts only for allowed matches of
  today. The `NotGuest` policy (`GuestAccess.NotGuestPolicy`) blocks `GET /api/matches/popular/forecasts`,
  `/api/sample-items` and `/api/me/favorite-leagues`. `DELETE /api/cache` is Admin-only (so guests get 403 as well).
- **Shared-account handling:** `AiPolicy` counts guests per client IP (not per `sub`); reusing a revoked guest refresh
  token rejects only that request instead of revoking every guest session.
- Covered by `GuestAccessIntegrationTests`.

### ✅ Health checks
- `GET /healthz` — liveness, checks nothing (a DB outage must not restart the container).
- `GET /healthz/ready` — `database` (5 s timeout; Unhealthy → 503) and `redis` (3 s; unreachable = Degraded → 200),
  JSON body from `HealthResponseWriter`. Both endpoints are anonymous. Covered by `HealthCheckIntegrationTests`.

### ✅ Settings in the database (`app_settings`)
- At startup, **before** services are registered, `DatabaseSettings.LoadAsync` migrates the DB, copies any keys of the
  sections `OddsProvider, Ai, Smtp, Jwt, Admin, Redis, Forecast, RateLimiting, Cors, Auth` that are missing in the DB
  from the current configuration (appsettings.json + user-secrets + env), then adds all DB rows for the current
  environment + `TargetProject=MatchForecast.Api` as the **last** configuration source (DB wins).
- Only two values must stay outside the DB: `ConnectionStrings:Supabase` and `AppSettings:EncryptionKey`
  (32-byte base64; losing it makes encrypted rows unreadable). Logging/AllowedHosts stay in the file.
- Secret keys (`OddsProvider:ApiKey`, `Ai:*:ApiKey`, `Smtp:Password`, `Jwt:SecretKey`, `Admin:Password`) are stored
  AES-256-GCM encrypted (`AesGcmAppSettingsCrypto`) and masked in API responses (`?reveal=true` for Admin).
- `PUT` changes take effect **after a restart**. `DELETE` means "reset to default": the key is re-seeded from
  file/secrets/env on the next start. To add a new key: put it in user-secrets/env and restart.
- Without a connection string (CI tests) the step is skipped and only file/secrets/env are used.

### ✅ Cache clear: Admin only (2026-11-03)
- `CacheController.Clear` uses `[Authorize(Roles = RoleNames.Admin)]` (was `NotGuest`). Authorization still runs
  before the rate limiter, so 403 responses do not consume the global 2/hour quota.
- Both frontends show the button only when `me.roles` contains `Admin` (`isAdmin`); while `/api/me` is loading it
  stays hidden. Label: TR **"Önbelleği Sil"** (was "Cache Temizle"), EN "Clear cache"; `cache.clearing` is still
  "Temizleniyor…".
- Tests: `RateLimitingIntegrationTests` now use Admin tokens; new `ClearCache_NonAdmin_ReturnsForbidden`;
  `GuestAccessIntegrationTests.ClearCache_Guest_ReturnsForbidden` unchanged.

### ✅ Favorite leagues (2026-11-03)
- Per-user, stored in the DB: entity `UserFavoriteLeague` (`UserId`, `League`, `CreatedAt`), table
  `user_favorite_leagues`, composite PK `(user_id, league)`, `league` max 200, cascade delete with the user,
  navigation `User.FavoriteLeagues`.
- The league key is the frontend's filter key **`"{Country} / {League}"`** (e.g. `Turkey / Süper Lig`); API-Football
  league ids are not used. If the provider renames a league, old favorites simply stop matching.
- API `FavoriteLeaguesController` (`NotGuest` policy): `GET /api/me/favorite-leagues` → `string[]`;
  `PUT ?league=...` adds, `DELETE ?league=...` removes; both are idempotent and return the updated list. Empty or
  >200 characters → 400 (`Lang.T`). No body, so the frontends' `request()` helper is reused unchanged.
- Frontends (`api.ts`: `getFavoriteLeagues`, `addFavoriteLeague`, `removeFavoriteLeague`):
  - `favorites` state is loaded once `/api/me` is known and the user is not a guest; cleared on logout.
  - League options are sorted favorites first, then alphabetically.
  - `LeagueFilter` / `app-league-filter`: star button on every row (`aria-pressed`, label "Favorilere ekle" /
    "Favorilerden çıkar"; `preventDefault` so it does not toggle the row checkbox). Under "Tümü" a "★ Favori ligler"
    row (shown when at least one favorite is in the list) selects exactly the favorite leagues; clicking it again
    returns to "Tümü". While active the toggle button reads "Favori ligler (n)".
  - Star clicks are optimistic and roll back on API error.
  - i18n keys: `league.favorites`, `league.favoriteLeagues`, `league.addFavorite`, `league.removeFavorite`.
  - CSS: `.league-multi-star`, `.league-multi-fav-icon` (yellow = `var(--card)`).
- Tests: `FavoriteLeaguesIntegrationTests` (idempotency, per-user isolation, 400 on empty, 403 for guest).

### Frontend (React 19 + Vite) — `frontend`
- Vite dev server on `5173`, proxies `/api` → `http://localhost:5080`.
- `api.ts`: access token in memory; **refresh token in `localStorage`** (`matchforecast.refreshToken`) so a page reload
  keeps the session (trade-off: readable on XSS). One shared in-flight refresh promise (refresh tokens are single use),
  automatic refresh + one retry on `401`, error text from `ProblemDetails.detail`.
- `Auth-Panel.tsx`: login, register (with password generator via `/api/password`), forgot password, reset password
  (`?resetToken=` is removed from the address bar), Google button (`Google.ts`).
- `App.tsx`: date navigator, team/league search, league filter (with favorites), cache-clear button (Admin only), logout, live-match auto refresh
  (5 min) with a local minute ticker, request sequencing (`requestSeq`) against stale responses. Loads `GET /api/me`;
  for guests it pins the date to today, shows a guest notice, hides admin-only actions and auto-selects the match when
  there is exactly one.
- Components: `MatchList`, `MatchStatus` (live minute labels), `LeagueFilter`, `ForecastPanel` (1X2 / first-half bars,
  BTTS, 10 predictions), `StatsTable`, `ThemeToggle` (light/dark via `<html data-theme>`, saved in `localStorage`
  `matchforecast.theme`; an inline script in `index.html` applies it before first paint, default = system preference).

### Frontend (Angular 22) — `frontend-angular`
- One-to-one port of the React UI; same `styles.css`, same behavior, including `auth-panel`.
- `src/app/core/`: `api.ts` (same fetch/token logic as React), `types.ts`, `match-status.ts`, `google.ts`. These are
  **copies** of the React files; keep both in sync when backend DTOs or auth flows change.
- `src/app/components/`: `auth-panel`, `match-list`, `league-filter`, `forecast-panel`, `stats-table`, `theme-toggle`
  (signal inputs/outputs, built-in control flow).
- `app.ts`: state in signals, side effects in `effect()` with `onCleanup`. Zoneless (Angular default since v21).
- Dev server on `4200`, `proxy.conf.json` (wired in `angular.json` → `serve.options.proxyConfig`) proxies `/api` → `5080`.
  `Auth:PasswordResetUrl` defaults to `http://localhost:4200/`; change it when using the React dev server.
- Unit tests: Vitest via `ng test` (`app.spec.ts` stubs `fetch`).

### ✅ Internationalization (TR / EN)
- **Site name:** TR → **"Maç Analiz"**, EN → **"Match Forecast"** (brand, `<title>`, password-reset e-mail).
- **Language choice:** `TR | EN` toggle in the top bar and on the login card (`LanguageToggle` /
  `app-language-toggle`). An inline script in `index.html` sets `<html lang>` and the title before the first paint:
  `?lang=` (removed from the URL) > `localStorage` `matchforecast.lang` > browser language (`tr*` → tr, else en).
- **Frontend dictionaries:** `frontend/src/i18n/{messages,i18n}.ts` = `frontend-angular/src/app/core/i18n/{messages,i18n}.ts`
  (**copies**, keep in sync). Add a text: put the key in `tr`, and `en: Dict` then fails to compile until it has the
  key too. `{name}` placeholders: `t('key', { name })`. Helpers: `pct()` (`%55` vs `55%`), `loc()` (picks from
  `{ tr, en }`), `locale()` (`tr-TR` / `en-GB`). React: `useI18n()` (`useSyncExternalStore`); Angular: `I18nService`
  (signal; templates call `i18n.t('key')`). Non-component modules (`api.ts`, `MatchStatus`, `Google.ts`) use `t()` directly.
- **API requests** send `Accept-Language: tr|en`. The backend uses `UseRequestLocalization` (before the exception
  middleware; supported `tr`, `en`; default/unsupported → `tr`; only the UI culture changes). User-facing messages
  are written as `Lang.T("Türkçe", "English")` (`MatchForecast.Models/Common/Lang.cs`), including `ProblemDetails`,
  `ForecastException`, 429 texts and DataAnnotations (`ValidationMessages`). Work outside a request runs in `tr`
  (`CultureInfo.DefaultThreadCurrentUICulture`).
- **Forecasts are language-neutral:** `ForecastResult.Summary`, `StatProjection.Label/Reasoning` and
  `Prediction.Label/Reasoning` are `LocalizedText` (`{ "tr": ..., "en": ... }`). The prompt asks the model for
  `summary`+`summaryEn` and `reasoning`+`reasoningEn` in a **single call**; a missing English text falls back to
  Turkish. Market labels are bilingual in `ForecastCatalog` (`MS 1` / `FT 1`, `KG Var` / `BTTS Yes`, `İY 1.5 Üst` /
  `HT Over 1.5`). Cache key is `forecast:v3:{id}`; switching language needs no new request.
- `Prediction.Market` / `Selection` stay as API-Football English names in both languages.
- Covered by `LocalizationTests` (backend) and `app.spec.ts` (Angular).

---

## 2. Next Steps & Feature Roadmap

```
[1. Redis ✅] ──► [2. Rate Limiting ✅] ──► [3. Docker ✅]
                                                  │
                                                  ▼
[6. Auth & Users ✅] ◄── [5. EF Core ✅] ◄── [4. Supabase ✅]
        │
        ▼
[7. Persist forecasts] ──► [8. Admin panel ◄ next]
```

#### 7. Persist forecasts (postponed; Admin panel comes first)
- [ ] Store generated forecasts in the DB (today they exist only in Redis for `Ai:CacheMinutes`).
- [ ] Later: compare stored predictions with final results to measure hit rate per market.

#### 8. Admin panel (next — detailed plan)
Goal: give Admins a UI for what is API/Swagger-only today (users, roles/groups, app settings) plus the system tools
(cache, health). Keep it small: reuse existing endpoints and styles, no new UI library, no new routing dependency.
Build it in **React first**, then port 1:1 to Angular in the same phase (both frontends must stay identical).

**Decisions to confirm before coding** (defaults in bold):
- Navigation: **hash-based view switch (`#admin`)** read on load + `hashchange`, header button "Yönetim" visible only
  to Admin. (Alternatives: React Router + Angular Router — adds a dependency to React and diverges the two apps.)
- Cache button: **move into the admin panel "Sistem" tab** and remove it from the header, or keep both.
- User removal: **deactivate (`IsActive=false`) as the main action**, hard delete only behind a typed confirmation.
- Groups: **assign existing groups only**; group/role CRUD is out of scope (no group is seeded today).

**8.0 Backend prerequisites**
- [ ] `GET /api/roles` → `[{ id, name, description }]` and `GET /api/groups` → `[{ id, name, description }]` (Admin;
      read-only). Needed because `PUT /api/users/{id}/roles|groups` take ids. New DTOs `RoleResponse`, `GroupResponse`.
- [ ] Self-lockout guards in `UsersController` (current user = JWT `sub`): an Admin cannot remove their own Admin role,
      deactivate themselves or delete themselves → 400 with `Lang.T(...)`. Also refuse removing the Admin role from
      the **last** active Admin.
- [ ] Optional: `GET /api/users?search=&page=&pageSize=` paging — skip unless the user list gets large.
- [ ] Integration tests for all of the above (use `CreateAuthorizedClient(RoleNames.Admin)`; seed users through
      `AppDbContext` in the in-memory DB).

**8.1 Shell & navigation (both frontends)**
- [ ] `isAdmin` already exists in `App.tsx` / `app.ts`; add `view: 'matches' | 'admin'` driven by `location.hash`.
- [ ] Header: "Yönetim" / "Admin" button for Admin; in admin view a "Maçlara dön" button.
- [ ] New components: `AdminPanel` (tabs: Kullanıcılar, Ayarlar, Sistem), `admin/UsersTab`, `admin/SettingsTab`,
      `admin/SystemTab` (Angular: `components/admin/*`). Non-admins hitting `#admin` see the match view.
- [ ] `api.ts` (+ Angular copy): `request()` currently sends no body; add an optional JSON body parameter
      (`request<T>(url, method, body?)`) for POST/PUT. `types.ts`: `UserResponse`, `AppSettingResponse`,
      `RoleResponse`, `GroupResponse`, request types.

**8.2 Users tab** (`/api/users`)
- [ ] Table: user name, display name, e-mail, roles (chips), active, last login; client-side search box.
- [ ] Create user form (user name, e-mail, display name, password with the existing generator `/api/password`).
- [ ] Edit: e-mail, display name, active toggle (`PUT /api/users/{id}`); show 409 messages from `ProblemDetails`.
- [ ] Roles / groups: checkbox lists from 8.0 (`PUT .../roles`, `PUT .../groups`).
- [ ] Set password (`PUT .../password`; note in UI that it signs the user out everywhere).
- [ ] Delete with confirmation (type the user name). The guest account is recreated by `GuestUserBootstrapper` on
      restart — show a hint on the `Guest` row.

**8.3 Settings tab** (`/api/app-settings`)
- [ ] List grouped by section (prefix before the first `:`), with description, updated at/by; encrypted values masked.
- [ ] "Göster" per encrypted row → `GET /api/app-settings/{id}?reveal=true` (never cache revealed values in state
      longer than needed).
- [ ] Edit value / description / `IsEncrypted` (`PUT`; `value: null` keeps the current value).
- [ ] "Varsayılana dön" = `DELETE` with confirmation (re-seeded from file/secrets/env on next start).
- [ ] Persistent banner: changes take effect **after an API restart**.

**8.4 System tab**
- [ ] Health: `GET /healthz/ready` (database / redis status from `HealthResponseWriter`).
- [ ] Cache clear (moved from the header; same rate limit, show the 429 message).

**8.5 Finish**
- [ ] i18n keys for every new text in both dictionaries (`admin.*`).
- [ ] Angular port + `app.spec.ts` smoke test for the admin view; React `tsc -b`, Angular `ng build`.
- [ ] Update this file (section 0 and section 1) when done.

### Follow-ups (not blocking)
- [ ] docker-compose: `healthcheck` entries using `/healthz` and `/healthz/ready`, Redis volume, `.env.example`,
      optional `web-angular` service (e.g. `profiles: [angular]`, port `8081`).
- [ ] Optional: distributed (Redis-backed) rate limiting if the API is scaled to multiple instances.
- [x] Token cleanup: `TokenCleanUpWorker` (hosted service, first run 1 min after start) deletes expired refresh tokens and used/expired
      password-reset tokens once a day. Revoked-but-unexpired refresh tokens are kept on purpose (reuse detection).
- [ ] Optional: trigger a settings reload instead of requiring a restart after `PUT /api/app-settings/{id}`.

### Known issues
- Real secrets (`Smtp:Password`, `Jwt:SecretKey`) are blank in `appsettings.json` now, but they are still in **git
  history** (earlier commits). Rotate the Gmail App Password and the JWT key if not already done.
  `JwtOptions.IsLeakedKey` (SHA-256 hashes of the leaked keys) makes startup fail if one of them is still configured
  (e.g. still in `app_settings`).
- `appsettings.json` has `Ai:Provider=Ollama` with an empty `Ai:Ollama:BaseUrl`/`Model`; a fresh clone without
  user-secrets/DB settings fails on the first forecast. Set the provider explicitly in user-secrets.
- `OddsProvider:UseMock` is not read anywhere; `MockOddsProvider` and `PublicOddsProvider` are not registered
  (`PublicOddsProvider` also depends on an unregistered `IMemoryCache`). Remove or wire them up.
- File name with a space: `Services/Redis/InMemoryRedisCacheService .cs`.
- `Services/Guest/*` uses namespace `MatchForecast.Api.Services.Security.Guest` (folder and namespace differ).
- Files ignored by `.gitignore` are still tracked: `backend/MatchForecast.Logger/logs/*` and
  `frontend/tsconfig.tsbuildinfo`. Remove with `git rm --cached`.

---

## 3. Project Commands

### Backend (.NET 10 API)
```bash
cd backend/MatchForecast.Api
dotnet run                                   # http://localhost:5080, Swagger: /swagger

dotnet test backend/MatchForecast.Tests/MatchForecast.Tests.csproj   # from repo root (no DB/Redis needed)

# Local secrets (user-secrets id: matchforecast-api)
# Required to start with the database:
dotnet user-secrets set "ConnectionStrings:Supabase" "Host=...;Port=5432;Database=postgres;Username=...;Password=...;SSL Mode=Require"
dotnet user-secrets set "AppSettings:EncryptionKey" "$(openssl rand -base64 32)"
# Copied into app_settings on first start (afterwards the DB values win):
dotnet user-secrets set "OddsProvider:ApiKey" "<api-sports key>"
dotnet user-secrets set "Ai:Provider" "Gemini"            # Gemini | Claude | Ollama
dotnet user-secrets set "Ai:Gemini:ApiKey" "<key>"        # or Ai:Claude:ApiKey / Ai:Ollama:*
dotnet user-secrets set "Jwt:SecretKey" "<at least 32 random characters>"
dotnet user-secrets set "Smtp:Password" "<gmail app password>"
dotnet user-secrets set "Admin:UserName" "admin"          # first admin, created once
dotnet user-secrets set "Admin:Email" "<e-mail>"
dotnet user-secrets set "Admin:Password" "<password>"
dotnet user-secrets set "Auth:GoogleClientId" "<...apps.googleusercontent.com>"   # optional
# Optional guest account (read from env/secrets only, never stored in app_settings):
dotnet user-secrets set "Guest:UserName" "misafir"
dotnet user-secrets set "Guest:Email" "misafir@matchforecast.online"
```
A local Redis is expected at `localhost:6379` (e.g. `docker run -d -p 6379:6379 redis:7-alpine`); without it the API
still works but nothing is cached and cache-clear returns 503. Without `ConnectionStrings:Supabase` the API starts but
login/register (DB-backed) will not work.

### EF Core migrations
```bash
# from backend/
dotnet ef migrations add <Name> --project MatchForecast.Data --startup-project MatchForecast.Api
# applying is automatic at API startup; manual: dotnet ef database update --project MatchForecast.Data --startup-project MatchForecast.Api
```

### Frontend — React
```bash
cd frontend
npm install
npm run dev        # http://localhost:5173
npm run build
```

### Frontend — Angular
```bash
cd frontend-angular
npm install        # if npm fails with "reading 'edgesOut'", use: npm install --legacy-peer-deps
npm start          # ng serve, http://localhost:4200
npm run build      # dist/frontend-angular/browser
npm test           # Vitest
```

### Docker
```bash
# Local (repo root); .env holds env vars as Section__Key, at least:
#   ConnectionStrings__Supabase=...  AppSettings__EncryptionKey=...
#   OddsProvider__ApiKey=...  Ai__Provider=Gemini  Ai__Gemini__ApiKey=...  Jwt__SecretKey=...  Smtp__Password=...
docker compose up --build        # UI: http://localhost:8080
docker compose logs -f api
docker compose down

# Production (server, behind Cloudflare Tunnel); .env additionally needs CLOUDFLARE_TUNNEL_TOKEN
docker compose -f docker-compose.prod.yml up -d --build
```