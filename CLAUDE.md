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
    Controllers/                   Auth, Me, FavoriteLeagues, Password, Matches, Cache, Users, Roles, Groups,
                                   Permissions, AppSettings, SampleItems
    Filter/                        BearerSecurityOperationFilter (Swagger)
    Options/                       Ai, Auth, Forecast, Jwt, OddsProvider, RateLimit, Redis, Smtp
    Services/
      AI/                          Claude/, Gemini/, Ollama/ clients + PoissonMath
      ApiFootball/                 IOddsProvider, ApiFootballOddsProvider (+ unused Mock/Public providers)
      AppSetting/                  DatabaseSettings (DB-backed configuration source)
      Email/                       SMTP sender, EmailErrorNotifier, StartupNotificationService
      Forecast/                    ForecastService, ForecastStore (IForecastStore), Prompt, Catalog, ProjectionBuilder, PredictionSelector,
                                   ForecastPrewarmQueue (+ ForecastPrewarmWorker in the same file)
      Guest/                       GuestAccess, GuestUserBootstrapper (namespace: Services.Security.Guest)
      Health/                      DatabaseHealthCheck, RedisHealthCheck, HealthResponseWriter
      Redis/                       RedisCacheService, InMemoryRedisCacheService (tests)
      Security/                    AuthService, JwtTokenService, GoogleIdTokenValidator, PasswordGenerator,
                                   AdminUserBootstrapper, TokenCleanUpWorker,
                                   PermissionAuthorization ([RequirePermission], PermissionPolicy, PermissionPolicyProvider),
                                   PermissionResolver (effective permissions), AdminGuard (+ ClaimsPrincipal.GetUserId())
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

**Next task:** Roadmap step 8 — **Admin panel, phase 8.5: Roles tab** (React first, then Angular). Shell (8.3) and
Users tab (8.4) are done; the backend for the panel (8.0–8.2) is complete but not yet built/tested with `dotnet`.
The admin panel is for **users, roles, permissions and groups only** (not for matches/settings); see section 2.

**⚠ Before running the API after pulling the 8.1 changes:** generate the migration (it was NOT written by hand this
time, and EF Core refuses `MigrateAsync` while the model has pending changes):
```bash
cd backend
dotnet ef migrations add AddPermissions --project MatchForecast.Data --startup-project MatchForecast.Api
```
Expected content: tables `permissions` (unique `key`) and `role_permissions` (PK `role_id, permission_id`, cascade
FKs), column `roles.is_system` (bool, default false). Seeding then adds the 8 system permissions and marks
`Admin`/`User`/`Guest` as system roles on the next start.
Steps 7 (persist forecasts) and 8.0 (admin backend prerequisites) are done but not yet built/tested (see below).

**Changes delivered as files but NOT yet verified with `dotnet build` / `dotnet test`** (the session environment had
no NuGet access; the changed C# files only passed a Roslyn compile against the framework reference assemblies, which
catches syntax/typing errors but not EF/Moq/xUnit usage. React passed `tsc -b`, Angular passed `ngc` type-check):
1. **CI fix — `LocalizationTests`:** the theory created its client with `factory.CreateClient()` and skipped
   `WithTestJwt()`, so the host started with the real `appsettings.json` (`Smtp:Enabled=true`, empty password) and
   `ValidateOnStart` failed on the CI agent (`Smtp:Password ... boş olamaz`, 5 failures). Fix: line 25 →
   `factory.WithTestJwt().CreateClient()`. **Apply if not yet done.** Rule: every `WebApplicationFactory` client in
   integration tests must go through `WithTestJwt()` / `CreateAuthorizedClient()`.
2. **Cache clear is Admin-only** (see "Cache clear" below).
3. **Favorite leagues** (see "Favorite leagues" below), including the hand-written migration `AddUserFavoriteLeagues`.
4. **Roadmap step 7 — Persist forecasts** (2026-11-04), including the hand-written migration `AddForecasts`.
5. **Roadmap step 8.0 — Admin backend prerequisites** (2026-11-04): `GET /api/roles`, `GET /api/groups`,
   self-lockout / last-admin guards in `UsersController`.
6. **Roadmap step 8.1 — Permission model** (2026-11-04): permissions in DB + JWT, `[RequirePermission]`, role checks
   replaced; migration `AddPermissions` still has to be generated (see above).
7. **Roadmap step 8.2 — Management API** (2026-11-04): permissions / roles / groups CRUD, group-side Admin guards,
   effective permissions in `UserResponse`. No schema change, no migration.
8. **Roadmap step 8.3 — Admin panel shell** (2026-11-04): `#admin` view, "Yönetim" button, permission-filtered tabs,
   admin API client in both frontends. Verified in the browser (React dev server + Angular dev build) with mocked
   API responses.
9. **Roadmap step 8.4 — Users tab** (2026-11-04): list/search, create/edit dialog, roles/groups, password, delete,
   read-only mode. Same 17-step browser test passed on React and Angular against a stateful mock API (not against the
   real backend).

**To verify before starting step 8:**
- [ ] `dotnet build` + `dotnet test backend/MatchForecast.Tests/MatchForecast.Tests.csproj` (new:
      `FavoriteLeaguesIntegrationTests`, `RateLimitingIntegrationTests.ClearCache_NonAdmin_ReturnsForbidden`,
      `ForecastStoreTests`, two new `ForecastServiceTests`, `AdminUsersIntegrationTests`,
      `PermissionAuthorizationTests`, `PermissionSeedTests`, `AdminManagementIntegrationTests`).
- [ ] Migration check: `dotnet ef migrations add Check --project MatchForecast.Data --startup-project MatchForecast.Api`
      must produce an **empty** migration (proves the hand-written snapshot matches the model); then
      `dotnet ef migrations remove ...`. If it is not empty, delete the hand-written migrations
      (`20261103120000_AddUserFavoriteLeagues*`, `20261104120000_AddForecasts*`) and the snapshot changes, then
      regenerate with `dotnet ef migrations add AddUserFavoriteLeagues` and `dotnet ef migrations add AddForecasts`.
- [ ] After deploying: open one forecast and check that a row appears in `matchforecast.forecasts`.
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
  - `CacheController` (`cache.clear`): `DELETE /api/cache`
  - `FavoriteLeaguesController` (signed-in, not guest): `GET|PUT|DELETE /api/me/favorite-leagues`
  - `PermissionsController`: `GET /api/permissions[/{id}]` (`permissions.manage` or `roles.manage`),
    `POST`, `PUT /{id}`, `DELETE /{id}` (`permissions.manage`)
  - `RolesController`: `GET /api/roles[/{id}]` (`users.view` or `roles.manage`), `POST`, `PUT /{id}`,
    `DELETE /{id}?force=`, `PUT /{id}/permissions` (`roles.manage`)
  - `GroupsController`: `GET /api/groups[/{id}]` (`users.view` or `groups.manage`), `POST`, `PUT /{id}`,
    `DELETE /{id}`, `PUT /{id}/roles`, `PUT /{id}/users` (`groups.manage`)
  - `UsersController` (`users.view`; changes also need `users.manage`): CRUD on `/api/users`, plus
    `PUT /api/users/{id}/password|roles|groups`
  - `AppSettingsController` (`app-settings.manage`): `GET /api/app-settings`, `GET|PUT|DELETE /api/app-settings/{id}`
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
  `UserFavoriteLeague`, `ForecastRecord` (table `forecasts`), `Permission`, `RolePermission`.
- Migrations in `MatchForecast.Data/Migrations` (`InitialCreate`, `AddUserManagement`, `AddRefreshTokenReset`,
  `AddAppSettings`, `AddUserFavoriteLeagues`, `AddForecasts` — the last two were written by hand, see section 0). They are applied **automatically at startup** (`DatabaseSettings.LoadAsync` →
  `MigrateAsync`); `EF.IsDesignTime` skips this during `dotnet ef`.
- Seeding via `UseSeeding`/`UseAsyncSeeding`: sample items, system roles (`RoleSeed`: `Admin`, `User`, `Guest`,
  marked `IsSystem`) and system permissions (`PermissionSeed`); only missing rows are added, existing rows only get
  their `IsSystem` flag fixed (names/descriptions edited in the panel are kept; role ↔ permission assignments are
  never touched).
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
- **Authorization:** a **fallback policy** protects every endpoint without `[AllowAnonymous]`. Feature access is
  **permission-based** since step 8.1 (`[RequirePermission(...)]`, see "Permissions" below); `Admin` has every
  permission. Guest restrictions stay role-based (`GuestAccess`, `NotGuest` policy). Roles (`RoleNames`): `Admin`, `User`, `Guest`.

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

### ✅ Cache clear: Admin only (2026-11-03) — superseded by 8.1: now the `cache.clear` permission (Admin still has it)
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

### ✅ Roadmap step 7 — Persist forecasts (2026-11-04)
- Every forecast that is **generated** (AI call, i.e. Redis miss or Admin `refresh=true`, prewarm included) is also
  written to the DB. **Reads still come only from Redis**, so freshness (`Ai:CacheMinutes`) is unchanged; the DB is a
  permanent record for later analysis (hit rate, admin views), not a second cache layer.
- Entity `ForecastRecord`, table `forecasts`: `fixture_id` (unique — **one row per match**, a regenerated forecast
  updates the row and sets `updated_at`), `kickoff` (indexed, for date queries), `country`, `league`, `home_team`,
  `away_team`, `ai_provider` / `ai_model` (from `Ai:Provider` and `Ai:{Provider}:Model`, empty provider = Gemini like
  `Program.cs`), `result_json` (**jsonb**, the full `ForecastResult` serialized with `JsonSerializerDefaults.Web` =
  the same shape the API returns), `generated_at`, `created_at`, `updated_at`. DateTime columns follow the global
  Istanbul ↔ UTC converter.
- `IForecastStore` / `ForecastStore` (scoped, `Services/Forecast/ForecastStore.cs`), registered in `Program.cs`.
  `ForecastService.GenerateAsync` calls it after `cache.SetAsync`:
  - **fail-open:** any exception is logged as a warning and the forecast is still returned;
  - saves with `CancellationToken.None`, so a forecast that was already paid for is kept even if the client
    disconnected;
  - on a failed `SaveChanges` the store clears the change tracker, so later saves in the same scope (prewarm loop)
    don't retry the broken entity.
- Not stored: final scores / hit-rate fields. They belong to the follow-up below and need their own migration.
- Tests: `ForecastStoreTests` (insert then update = single row, Istanbul times, JSON round-trip, provider/model),
  `ForecastServiceTests` (saved once, not for cache hits; store failure still returns the forecast). Test DI setups
  that build `ForecastService` manually (`ForecastPrewarmTests`) register `Mock.Of<IForecastStore>()`.

### ✅ Roadmap step 8.0 — Admin backend prerequisites (2026-11-04) — DTOs, permissions and guards were extended in 8.1/8.2
- `RolesController` → `GET /api/roles` → `RoleResponse(Id, Name, Description)`; `GroupsController` →
  `GET /api/groups` → `GroupResponse(Id, Name, Description)`. Admin only, read-only, ordered by name.
  Needed because `PUT /api/users/{id}/roles|groups` take ids.
- `UsersController` lock-out guards (current user = JWT `sub`), all → 400 `ProblemDetails` with `Lang.T` texts:
  - an Admin cannot deactivate (`PUT /api/users/{self}` with `isActive=false`), delete or remove the Admin role from
    **their own** account;
  - the **last active Admin** cannot be deactivated, deleted or lose the Admin role, whoever asks (covers a token whose
    user no longer exists).
  Guards only trigger on an actual change (deactivating an active user, removing an existing Admin role).
- Tests: `AdminUsersIntegrationTests` (seeds roles/users itself — `RoleSeed` does not run on the in-memory DB).

### ✅ Roadmap step 8.1 — Permissions (2026-11-04)
- **Model:** `Permission` (`permissions`: `key` unique, max 100, lowercase `area.action`; `name` TR, `name_en`,
  `description`, `is_system`), `RolePermission` (`role_permissions`, PK `(role_id, permission_id)`, cascade from both
  sides), `Role.IsSystem`. Migration `AddPermissions` — generate it with `dotnet ef` (section 0).
- **System permissions** (`PermissionKeys` in `MatchForecast.Data/Seed/PermissionSeed.cs`, seeded by `PermissionSeed`):
  `users.view`, `users.manage`, `roles.manage`, `permissions.manage`, `groups.manage`, `cache.clear`,
  `forecast.refresh`, `app-settings.manage`. Panel-created permissions (8.2) are `IsSystem=false`. A panel-created
  permission with the same key as a later system permission is adopted (marked system) by the seed.
- **Default access:** only `Admin` (implicitly **all** permissions, including ones added later). `User` and `Guest`
  have none until roles get permissions in the panel. So after deploy, behavior equals the previous Admin-only setup.
- **Token:** `AuthService.IssueTokensAsync` resolves effective roles (direct + via groups) and their permissions
  (Admin → every key in the table) and `JwtTokenService.CreateAccessToken(..., permissions)` writes one `perm` claim per
  key. Changes reach a user on the next token refresh (≤ `Jwt:ExpiryMinutes`). `GET /api/me` returns `permissions`.
- **Enforcement:** `[RequirePermission(PermissionKeys.X)]` (`Services/Security/PermissionAuthorization.cs`) sets the
  policy name `perm:{key}`; with several keys (`perm:a|b`) **any one** is enough. `PermissionPolicyProvider`
  (registered in `Program.cs` as `IAuthorizationPolicyProvider`) builds these policies on demand, other names go to
  the default provider. Check rule (`PermissionPolicy.HasPermission`): role `Admin` **or** a `perm` claim with the key.
  Code that branches on a permission uses `PermissionPolicy.HasPermission(User, key)` (e.g.
  `MatchesController.CanRefresh` → `forecast.refresh`).
- **Where:** `UsersController` class `users.view` + `users.manage` on every change; `GET /api/roles` (`users.view` |
  `roles.manage`); `GET /api/groups` (`users.view` | `groups.manage`); `AppSettingsController` `app-settings.manage`;
  `DELETE /api/cache` `cache.clear`.
- **Admin lock-out guards** now treat "Admin" as direct role **or** membership in a group that grants `Admin`
  (`UsersController.IsAdmin` expression): self-removal via `PUT .../roles` or `PUT .../groups`, self deactivate/delete,
  and last-active-Admin protection all use it. Group-side changes (group deleted / loses Admin) come with 8.2.
- **Frontends:** `MeResponse.permissions` + `hasPermission(me, key)` in `types.ts` (Admin → true, mirrors the API);
  the "Önbelleği Sil" button uses `hasPermission(me, 'cache.clear')`.
- **Adding a permission check in code:** add a constant to `PermissionKeys` and a row (TR/EN name, description) to
  `PermissionSeed.CreatePermissions`, then put `[RequirePermission(PermissionKeys.X)]` on the endpoint. No
  `Program.cs` change, no migration (seed adds the row on next start).
- Tests: `PermissionAuthorizationTests` (permission vs role, view vs manage, any-of, `/api/me`, effective permissions in
  the token via direct and group roles, Admin gets all), `PermissionSeedTests` (idempotent, adopts existing rows, keeps
  assignments), `AdminUsersIntegrationTests.AdminViaGroup_CountsAsAdmin_ForLockOutGuards`.
  `TestAuth.CreateClientFor(userId, roles, permissions...)` creates a client with a custom token.

### ✅ Roadmap step 8.2 — Management API (2026-11-04)
- **Shared services** (`Services/Security`, scoped, registered in `Program.cs`):
  - `PermissionResolver.ForUsersAsync(userIds)` → effective permission keys per user (direct roles + group roles via
    joins; Admin → every key in the table; ordinal-sorted). Used by `AuthService` (token) and `UsersController`.
  - `AdminGuard`: `IsAdmin` expression (Admin role directly or via a group), `AdminsOnlyViaGroupAsync(groupId,
    among)` (users whose Admin access comes **only** from that group), `CheckLossAsync(losing, currentUserId)` →
    `null` or a TR/EN message: refuses when the caller is in `losing` or when no active Admin would remain.
    `UsersController` and `GroupsController` both use it. `ClaimsPrincipal.GetUserId()` reads JWT `sub`.
- **Permissions API** (`PermissionsController`, DTOs `PermissionResponse(Id, Key, Name, NameEn, Description,
  IsSystem, RoleIds)`, `CreatePermissionRequest`, `UpdatePermissionRequest`): key format
  `PermissionKeyPattern` (`area.action`, ≥ 2 dot-separated parts of letters/digits/`-`), stored lowercase, unique
  (409). System permissions: key change → 400, delete → 400; name/description editable. `Key: null` on PUT keeps it.
- **Roles API** (`RolesController`, `RoleResponse(Id, Name, Description, IsSystem, HasAllPermissions,
  PermissionIds, UserCount, GroupCount)`, `RoleRequest`, `RolePermissionsRequest`): names unique case-insensitive
  (409); system roles cannot be renamed or deleted (400, description is editable); a role assigned to users/groups is
  deleted only with `?force=true` (409 otherwise, assignments removed); `PUT /{id}/permissions` replaces the list
  (diff applied), unknown id → 400, `Admin` → 400 (`HasAllPermissions`, list stays empty). Other system roles
  (`User`, `Guest`) **can** get permissions.
- **Groups API** (`GroupsController`, `GroupResponse(Id, Name, Description, RoleIds, MemberIds)`, `GroupRequest`,
  `GroupRolesRequest`, `GroupUsersRequest`): names unique case-insensitive (409); `PUT /{id}/roles` and
  `PUT /{id}/users` replace the lists (diff applied, unknown ids → 400). Admin guards: deleting a group, removing
  `Admin` from it, or removing members → `AdminGuard` on the users whose Admin access comes only from that group.
- **Users:** `UserResponse` gained `Permissions` (effective, read-only); `Roles`/`Groups` stay the direct assignments.
- Deletes load dependent rows (`Include`) so the in-memory test DB removes them too (Postgres has FK cascade anyway).
- Name comparisons use `ToLowerInvariant()` for the parameter and `ToLower()` (SQL `lower()`) for the column.
- Tests: `AdminManagementIntegrationTests` (custom permission lifecycle, system permission protection, roles.manage
  can read but not change permissions, role lifecycle, system role rules, delete with force, group lifecycle, Admin
  group guards incl. self, groups.manage needed for changes, effective permissions in `/api/users`). Seeds system
  data with the real `RoleSeed` / `PermissionSeed`.

### ✅ Roadmap step 8.3 — Admin panel shell (2026-11-04)
- **Navigation:** hash-based, no router. `#admin` opens the panel on the first visible tab, `#admin/<tab>` (`users`,
  `roles`, `groups`, `permissions`) a specific one. Tabs are `<a href="#admin/...">` links, so browser back/forward
  works. The app listens to `hashchange` (React `useEffect`, Angular `host: '(window:hashchange)'`).
  "Maçlara dön" and logout remove the hash with `history.pushState` (no stray `#` left in the URL).
- **Shared logic:** `frontend/src/adminView.ts` = `frontend-angular/src/app/core/admin-view.ts` (**copies**, keep in
  sync): `ADMIN_TABS` (tab → permission → i18n label: users → `users.view`, roles → `roles.manage`, groups →
  `groups.manage`, permissions → `permissions.manage`), `parseAdminHash`, `adminHash`, `visibleAdminTabs(me)`,
  `canOpenAdmin(me)`, `clearHash()`.
- **Header:** "Yönetim" / "Admin" button when the user can see at least one tab (`canOpenAdmin`); in the admin view it
  becomes "Maçlara dön" / "Back to matches", the date navigator and "Önbelleği Sil" are hidden. A user without any
  admin permission who opens `#admin` stays on the match screen. Header buttons no longer wrap their text
  (`.cache-btn { white-space: nowrap }`) and the action row wraps on narrow screens (`.cache-actions { flex-wrap }`).
- **Component:** React `components/AdminPanel.tsx`, Angular `components/admin-panel.ts/.html` (`app-admin-panel`):
  title, `role="tablist"` tabs filtered by permission, a `role="tabpanel"` that currently shows the placeholder text
  `admin.comingSoon` (replaced by the real tabs in 8.4–8.7). The match screen state is kept while the panel is open.
- **API client** (`api.ts`, both frontends): `request<T>(url, method, body?)` sends JSON bodies; 204 responses
  return `undefined`. Functions for every admin endpoint: `getUsers`, `createUser`, `updateUser`, `setUserPassword`,
  `deleteUser`, `setUserRoles`, `setUserGroups`, `getRoles`, `createRole`, `updateRole`, `deleteRole(id, force)`,
  `setRolePermissions`, `getGroups`, `createGroup`, `updateGroup`, `deleteGroup`, `setGroupRoles`, `setGroupUsers`,
  `getPermissions`, `createPermission`, `updatePermission`, `deletePermission`.
- **Types** (`types.ts`): `UserResponse`, `CreateUserRequest`, `UpdateUserRequest`, `RoleResponse`, `RoleRequest`,
  `GroupResponse`, `GroupRequest`, `PermissionResponse`, `CreatePermissionRequest`, `UpdatePermissionRequest`, and
  `PERMISSIONS` (system permission keys, mirrors backend `PermissionKeys`).
- **i18n:** `admin.open`, `admin.back`, `admin.title`, `admin.tabs`, `admin.tab.users|roles|groups|permissions`,
  `admin.comingSoon`. **CSS:** `.admin`, `.admin-title`, `.admin-tabs`, `.admin-tab` (selected = `aria-selected`),
  `.admin-tabpanel`, `.admin-placeholder`.
- Angular production build (`ng build`) inlines Google Fonts and needs internet access; offline use
  `ng build --configuration development`.

### ✅ Roadmap step 8.4 — Users tab (2026-11-04)
- **Files:** React `components/admin/UsersTab.tsx` + `components/admin/UserDialog.tsx`; Angular
  `components/admin/users-tab.ts/.html` + `components/admin/user-dialog.ts/.html`. `AdminPanel` / `app-admin-panel`
  renders the tab for `users`; the other tabs still show `admin.comingSoon`.
- **List:** loads `getUsers()`, `getRoles()`, `getGroups()` in parallel (roles/groups are needed to map the names in
  `UserResponse` to ids for the assignment endpoints). Search over user name, display name and e-mail (`tr` lower
  case). Columns: user (+ display name), e-mail, roles, groups (chips), status badge, last login (`toLocaleString` with
  the UI locale), "Düzenle" (with `users.manage`) or "Görüntüle" (view only).
- **Dialog:** native `<dialog>` opened with `showModal()` (Esc / "Kapat" close it). React re-creates it with `key`,
  Angular with a one-element `@for (...; track key)`.
  - Create: user name, e-mail, display name, password (+ generator via `/api/password`). After `createUser` the list
    reloads and the same dialog switches to edit mode for the new user, so roles/groups can be assigned right away.
  - Edit: separate sections, each with its own save button and status line (`admin-status`): profile (e-mail,
    display name, active), roles (checkboxes, "Sistem" badge), groups, effective permissions (read-only chips + hint
    that changes apply on the next token refresh), set password (signs the user out everywhere), delete (button enabled
    only after typing the exact user name).
  - Without `users.manage`: everything is inside `<fieldset disabled>` and no save/password/delete controls render.
  - Own account: note shown, "active" checkbox disabled, no delete section. Guest account: note that it is recreated on
    restart. All other rules (last Admin, self via roles/groups, duplicates) come from the API `ProblemDetails` text.
- **i18n:** `admin.loading`, `admin.edit`, `admin.view`, `admin.close`, `admin.save`, `admin.saved`, `admin.system`,
  `admin.users.*`, `admin.user.*`; reuses `auth.userName`, `auth.email`, `auth.displayName`, `auth.password`,
  `auth.newPassword`, `auth.generate`, `auth.generating`, `auth.show`, `auth.hide`.
- **CSS** (both `styles.css`, identical): `.sr-only`, `.admin-toolbar`, `.admin-search`, `.admin-table(-wrap)`,
  `.admin-sub`, `.admin-muted`, `.admin-nowrap`, `.chips` / `.chip`, `.badge` / `.badge-ok` / `.badge-off`,
  `.admin-dialog(-head|-body)`, `.admin-section`, `.admin-fieldset`, `.admin-form-grid`, `.admin-check(-list)`,
  `.admin-actions`, `.admin-status`, `.admin-hint`, `.admin-note`, `.admin-danger`, `.danger-btn`.

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
[7. Persist forecasts ✅] ──► [8. Admin panel (users/roles/permissions/groups): 8.0 ✅, 8.1 ✅, 8.2 ✅, 8.3 ✅, 8.4 ✅, 8.5 ◄ next]
```

#### 7. Persist forecasts ✅ (2026-11-04, see section 1)
- [x] Store generated forecasts in the DB (`forecasts` table, one row per match).
- [ ] Follow-up: fetch final scores for past `forecasts` rows (API-Football fixture by id, status FT), store them
      (new columns or a `forecast_results` table) and compute hit rate per market. Could be a daily hosted service
      like `TokenCleanUpWorker`, plus an Admin report in the admin panel.

#### 8. Admin panel — users, roles, permissions, groups (revised 2026-11-04)
Goal: an Admin UI to manage **who can do what**: users, roles, permissions and groups. The panel is **not** for
matches, forecasts or app settings. It must stay extensible: new roles and new permissions can be added later
without code changes to the panel.

**Decisions (confirmed by the owner, 2026-11-04):**
- **Permissions are not a fixed list.** Permissions are DB rows; Admins can add new ones from the panel. Permissions
  that the code enforces are seeded as **system permissions** (key cannot be changed or deleted, because code checks
  that key); panel-created ones are **custom permissions**. A custom permission has no effect until code (API policy or
  frontend `me.permissions` check) uses its key — the panel shows this with a "Sistem" / "Özel" badge and a hint.
- **Admins can make other users Admin** (assign the `Admin` role directly or through a group). `Admin` implicitly has
  **every** permission, including ones added later.
- **Changes apply when the access token is refreshed** (at most `Jwt:ExpiryMinutes`): roles and permissions are
  written into the JWT at token issue. No per-request DB lookup.
- **Groups are managed in the panel** (create/edit/delete, assign roles to a group, add/remove users). The token
  already includes roles inherited through groups (`AuthService.IssueTokensAsync`).
- Navigation: hash-based view switch (`#admin`), no new routing dependency. The "Önbelleği Sil" button stays on the
  match screen but is gated by the `cache.clear` permission instead of the `Admin` role.

**Model (target)**
- `Permission`: `Id`, `Key` (unique, e.g. `users.manage`; lowercase `area.action`), `Name` (TR) / `NameEn`,
  `Description`, `IsSystem`, `CreatedAt`, `UpdatedAt`. Table `permissions`.
- `RolePermission`: `(RoleId, PermissionId)` PK, cascade on both sides. Table `role_permissions`.
- `Role` gets `IsSystem` (`Admin`, `User`, `Guest` = true: cannot be deleted or renamed; their meaning is used in code).
  New roles created in the panel are `IsSystem=false`.
- Effective permissions of a user = union of permissions of all effective roles (direct + via groups); `Admin` = all.
- JWT: one `perm` claim per effective permission key (next to the existing `role` claims). `GET /api/me` returns
  `permissions` too, so the frontends can show/hide features by permission.

**System permissions to seed (initial list, extend in code when a new check is added):**
| Key | Used by |
|---|---|
| `users.view` | `GET /api/users`, `GET /api/users/{id}` |
| `users.manage` | create/update/delete users, set password, set roles/groups of a user |
| `roles.manage` | role CRUD, role ↔ permission assignment |
| `permissions.manage` | permission CRUD (custom permissions) |
| `groups.manage` | group CRUD, group roles, group members |
| `cache.clear` | `DELETE /api/cache` |
| `forecast.refresh` | `refresh=true` on forecast endpoints (today: `User.IsInRole(Admin)` in `MatchesController`) |
| `app-settings.manage` | `/api/app-settings` (API only, not in the panel) |

Default role mapping when seeding: `Admin` → all (implicit), `User` → none, `Guest` → none. Seeding only adds
missing permissions; it never removes a role ↔ permission assignment made in the panel.

**8.0 Backend prerequisites ✅ (2026-11-04, see section 1)**
- [x] `GET /api/roles`, `GET /api/groups` (read-only) + `RoleResponse`, `GroupResponse` — extended in 8.2.
- [x] Self-lockout and last-active-Admin guards in `UsersController` + `AdminUsersIntegrationTests`.
      To revisit in 8.1: the guard counts only **direct** `Admin` role assignments; Admins via a group must count too.

**8.1 Permission model (backend) ✅ (2026-11-04, see section 1)**
- [x] Entities, seed, `perm` claims, `[RequirePermission]` + policy provider, role checks replaced, `/api/me`
      permissions, frontends' cache button, group-aware Admin guards in `UsersController`, tests.
- [ ] **Generate migration `AddPermissions`** (section 0) — could not be done in the authoring session.
- [ ] Watch token size if the permission list grows a lot (Admin carries every key).

**8.2 Management API (backend) ✅ (2026-11-04, see section 1)**
- [x] Group-side Admin guards (shared `AdminGuard`), permissions / roles / groups CRUD, role ↔ permission and
      group ↔ role / member assignment, effective permissions in `UserResponse`, integration tests.
- [ ] Not done (only if needed): paging/search on the list endpoints.

**8.3 Frontend shell ✅ (2026-11-04, see section 1)**
- [x] `request()` with JSON body, admin API functions and DTO types in both frontends.
- [x] "Yönetim" button, `#admin[/tab]` view, permission-filtered tabs, "Maçlara dön"; users without admin
      permissions stay on the match screen.

**8.4 Users tab ✅ (2026-11-04, see section 1)**
- [x] Table with search, create (password generator), edit, set password, delete with typed confirmation, role and
      group checkboxes, read-only effective permissions, view-only mode, own-account / guest notes.

**8.5 Roles tab** (`roles.manage`) ← next
- [ ] Role list (system badge, user count); create/rename/delete custom roles.
- [ ] Permission checklist per role, grouped by area (prefix before the first `.`); `Admin` shown as "all permissions"
      and not editable.

**8.6 Groups tab** (`groups.manage`)
- [ ] Group list; create/edit/delete; roles of the group; members (add/remove users).

**8.7 Permissions tab** (`permissions.manage`)
- [ ] List with key, TR/EN name, description, "Sistem" / "Özel" badge, roles that have it.
- [ ] Add custom permission (key + names + description); edit; delete custom only. Hint: a custom permission only works
      after code checks its key.

**8.8 Finish**
- [ ] i18n keys (`admin.*`) in both dictionaries; React `tsc -b`, Angular `ng build` + `app.spec.ts` smoke test.
- [ ] Update this file (sections 0, 1 and the permission table).

**Out of the panel's scope (moved to follow-ups):** app settings UI (`/api/app-settings` stays API/Swagger-only,
protected by `app-settings.manage`), health/system view, forecast reports (step 7 follow-up).

### Follow-ups (not blocking)
- [ ] docker-compose: `healthcheck` entries using `/healthz` and `/healthz/ready`, Redis volume, `.env.example`,
      optional `web-angular` service (e.g. `profiles: [angular]`, port `8081`).
- [ ] Optional: distributed (Redis-backed) rate limiting if the API is scaled to multiple instances.
- [x] Token cleanup: `TokenCleanUpWorker` (hosted service, first run 1 min after start) deletes expired refresh tokens and used/expired
      password-reset tokens once a day. Revoked-but-unexpired refresh tokens are kept on purpose (reuse detection).
- [ ] Optional: trigger a settings reload instead of requiring a restart after `PUT /api/app-settings/{id}`.
- [ ] Optional: app settings UI and a system/health view (taken out of the admin panel scope on 2026-11-04).
- [ ] Optional: when a user's roles/groups change, revoke their refresh tokens so the change applies immediately
      instead of after the access token expires (trade-off: the user has to sign in again).

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