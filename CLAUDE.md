# CLAUDE.md — MatchForecast Project Guide & Roadmap

MatchForecast fetches the day's football fixtures from API-Football, asks an LLM for the **expected values** of match
statistics (goals, first-half goals, corners, cards), turns those into probabilities with a Poisson model, and lists
the **10 most likely predictions** with a stats table. The LLM never picks bets; all probabilities and the prediction
list are computed in code. Users sign in (username/password or Google) before they can see anything.

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
  (production behind Cloudflare Tunnel)

---

## 1. Steps Completed So Far

### Backend core
- **Architecture & DI:** `Program.cs` with options pattern; critical options are validated at startup with
  `ValidateOnStart` (`JwtOptions`, `SmtpOptions`, `RedisOptions`, `ForecastOptions`, `AuthOptions`), so
  misconfiguration fails at boot, not on the first request.
- **Controllers:** `[ApiController]` classes under `Controllers/` are discovered automatically.
  - `AuthController` (anonymous): `GET /api/auth/config`, `POST /api/auth/token`, `POST /api/auth/refresh`,
    `POST /api/auth/register`, `POST /api/auth/google`, `POST /api/auth/forgot-password`,
    `POST /api/auth/reset-password`, `POST /api/auth/revoke`
  - `PasswordController` (anonymous): `GET /api/password?length=16` (random password generator, 8–128, nothing stored)
  - `MatchesController`: `GET /api/matches`, `GET /api/matches/popular`, `GET /api/matches/popular/forecasts`,
    `GET /api/matches/{id}/forecast`
  - `CacheController`: `DELETE /api/cache`
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
  `forecast:v2:{id}` (`Ai:CacheMinutes`), lock `prewarm:superlig:{date}` (10 min, `TryLockAsync` = `SET NX`).
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
    (JWT `sub`, falls back to IP), default 30 requests / 10 min. Cached answers count too.
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
  `Ai__Provider=Gemini`, `RateLimiting__TrustCloudflareHeader=true`, no published ports, plus a `cloudflared` service
  (`CLOUDFLARE_TUNNEL_TOKEN`) that exposes `http://web:80` through a Cloudflare Tunnel.

### ✅ Roadmap step 4 — Database (Supabase / PostgreSQL)
- Replaced the planned MSSQL step with **Supabase PostgreSQL**. Connection string: `ConnectionStrings:Supabase`
  (user-secrets / env only, never in the repo; prod adds `Maximum Pool Size=10`).
- Tables live in the `matchforecast` schema (not `public`, which Supabase exposes through its REST Data API);
  the migrations history table is in the same schema.
- `DateTime` values are stored as UTC (`timestamptz`) and converted to/from Europe/Istanbul by a value converter.
- `DatabaseHealthCheck` is part of `/healthz/ready`.

### ✅ Roadmap step 5 — EF Core
- `AppDbContext` (`MatchForecast.Data/Configuration`) with entities: `User`, `Role`, `Group`, `UserRole`, `UserGroup`,
  `GroupRole`, `UserLogin` (external logins), `RefreshToken`, `PasswordResetToken`, `AppSetting`, `SampleItem`.
- Migrations in `MatchForecast.Data/Migrations` (`InitialCreate`, `AddUserManagement`, `AddRefreshTokenReset`,
  `AddAppSettings`). They are applied **automatically at startup** (`DatabaseSettings.LoadAsync` →
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
- **Authorization:** a **fallback policy** protects every endpoint without `[AllowAnonymous]`. `UsersController` and
  `AppSettingsController` require `Admin`.

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

### Frontend (React 19 + Vite) — `frontend`
- Vite dev server on `5173`, proxies `/api` → `http://localhost:5080`.
- `api.ts`: access token in memory; **refresh token in `localStorage`** (`matchforecast.refreshToken`) so a page reload
  keeps the session (trade-off: readable on XSS). One shared in-flight refresh promise (refresh tokens are single use),
  automatic refresh + one retry on `401`, error text from `ProblemDetails.detail`.
- `Auth-Panel.tsx`: login, register (with password generator via `/api/password`), forgot password, reset password
  (`?resetToken=` is removed from the address bar), Google button (`Google.ts`).
- `App.tsx`: date navigator, team/league search, league filter, cache-clear button, logout, live-match auto refresh
  (5 min) with a local minute ticker, request sequencing (`requestSeq`) against stale responses.
- Components: `MatchList`, `MatchStatus` (live minute labels), `ForecastPanel` (1X2 / first-half bars, BTTS,
  10 predictions), `StatsTable`.

### Frontend (Angular 22) — `frontend-angular`
- One-to-one port of the React UI; same `styles.css`, same behavior, including `auth-panel`.
- `src/app/core/`: `api.ts` (same fetch/token logic as React), `types.ts`, `match-status.ts`, `google.ts`. These are
  **copies** of the React files; keep both in sync when backend DTOs or auth flows change.
- `src/app/components/`: `auth-panel`, `match-list`, `forecast-panel`, `stats-table` (signal inputs/outputs, built-in
  control flow).
- `app.ts`: state in signals, side effects in `effect()` with `onCleanup`. Zoneless (Angular default since v21).
- Dev server on `4200`, `proxy.conf.json` (wired in `angular.json` → `serve.options.proxyConfig`) proxies `/api` → `5080`.
  `Auth:PasswordResetUrl` defaults to `http://localhost:4200/`; change it when using the React dev server.
- Unit tests: Vitest via `ng test` (`app.spec.ts` stubs `fetch`).

---

## 2. Next Steps & Feature Roadmap

```
[1. Redis ✅] ──► [2. Rate Limiting ✅] ──► [3. Docker ✅]
                                                  │
                                                  ▼
[6. Auth & Users ✅] ◄── [5. EF Core ✅] ◄── [4. Supabase ✅]
        │
        ▼
[7. Persist forecasts] ──► [8. Admin UI]
```

#### 7. Persist forecasts (next)
- [ ] Store generated forecasts in the DB (today they exist only in Redis for `Ai:CacheMinutes`).
- [ ] Later: compare stored predictions with final results to measure hit rate per market.

#### 8. Admin UI
- [ ] Frontend screens for `/api/users` (roles, groups, activate/deactivate) and `/api/app-settings`
      (both are API-only today; use Swagger).

### Follow-ups (not blocking)
- [ ] docker-compose: `healthcheck` entries using `/healthz` and `/healthz/ready`, Redis volume, `.env.example`,
      optional `web-angular` service (e.g. `profiles: [angular]`, port `8081`).
- [ ] Optional: distributed (Redis-backed) rate limiting if the API is scaled to multiple instances.
- [ ] Clean up expired/revoked refresh tokens and used password-reset tokens (rows accumulate).
- [ ] Optional: trigger a settings reload instead of requiring a restart after `PUT /api/app-settings/{id}`.

### Known issues
- `backend/MatchForecast.Tests/AuthFlowsIntegrationTests` has **no `.cs` extension**, so it is not compiled and its
  tests never run. Rename it to `AuthFlowsIntegrationTests.cs`.
- Real secrets (`Smtp:Password`, `Jwt:SecretKey`) are blank in `appsettings.json` now, but they are still in **git
  history** (earlier commits). Rotate the Gmail App Password and the JWT key if not already done.
- `appsettings.json` has `Ai:Provider=Ollama` with an empty `Ai:Ollama:BaseUrl`/`Model`; a fresh clone without
  user-secrets/DB settings fails on the first forecast. Set the provider explicitly in user-secrets.
- `OddsProvider:UseMock` is not read anywhere; `MockOddsProvider` and `PublicOddsProvider` are not registered
  (`PublicOddsProvider` also depends on an unregistered `IMemoryCache`). Remove or wire them up.
- `frontend/src/types.ts` and `frontend/src/components/Types.tsx` are duplicates that have drifted and are both
  imported; keep one (`types.ts`, which has the auth types).
- File name with a space: `Services/Redis/InMemoryRedisCacheService .cs`.
- Swagger description still says "LLM (Claude)" although the provider is configurable.

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