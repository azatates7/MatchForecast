# CLAUDE.md — MatchForecast Project Guide & Roadmap

MatchForecast fetches the day's football fixtures from API-Football, asks an LLM for the **expected values** of match
statistics (goals, first-half goals, corners, cards), turns those into probabilities with a Poisson model, and lists
the **10 most likely predictions** with a stats table. The LLM never picks bets; all probabilities and the prediction
list are computed in code.

- **Backend:** .NET 10 Web API with controllers (`backend/MatchForecast.Api`), Models class library
  (`backend/MatchForecast.Models`), Logger & middleware library (`backend/MatchForecast.Logger`), xUnit tests
  (`backend/MatchForecast.Tests`)
- **Frontends (identical UI, pick either):**
  - React 19 + TypeScript + Vite (`frontend`)
  - Angular 22 + signals, standalone components (`frontend-angular`)
- **Cache:** Redis (`StackExchange.Redis`)
- **External data:** API-Football (api-sports.io v3), fixtures only (odds are no longer used)
- **AI engine:** pluggable via `Ai:Provider`: Gemini, Claude or Ollama (local)
- **Infrastructure:** Docker (multi-stage images) + `docker-compose.yml`

---

## 1. Steps Completed So Far

### Backend core
- **Architecture & DI:** `Program.cs` with options pattern; every critical option is validated at startup with
  `ValidateOnStart` (`JwtOptions`, `SmtpOptions`, `RedisOptions`, `ForecastOptions`), so misconfiguration fails at boot,
  not on the first request.
- **Controllers:** `[ApiController]` classes under `Controllers/` are discovered automatically.
  - `AuthController`: `GET /api/auth/token`
  - `MatchesController`: `GET /api/matches`, `GET /api/matches/popular`, `GET /api/matches/popular/forecasts`,
    `GET /api/matches/{id}/forecast`
  - `CacheController`: `DELETE /api/cache`
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
- **JWT authentication:** HS256 tokens from `JwtTokenService` (unique `jti`), 30 s clock skew. An authorization
  **fallback policy** protects every endpoint without `[AllowAnonymous]`. The token endpoint issues tokens without
  credentials.
- **Logging (`MatchForecast.Logger`, NLog):** daily rolling files + console. Log folder comes from
  `MATCHFORECAST_LOG_DIR`, falling back to `{basedir}/logs`.
  - `LoggingMiddleware`: method, path, status, duration, request/response bodies with sensitive fields masked.
  - `ExceptionMiddleware`: `ForecastException` → its own status code; others → 500; RFC 7807 `ProblemDetails`.
- **E-mail notifications (SMTP / Gmail App Password):** `EmailErrorNotifier` queues 5xx errors and sends them in the
  background with per-error throttling (`Smtp:ThrottleMinutes`); `StartupNotificationService` sends an
  "application started" mail. `Smtp:Enabled=false` turns both off.

### ✅ Roadmap step 1 — Redis
- `IRedisCacheService` / `RedisCacheService` on `StackExchange.Redis` (singleton `IConnectionMultiplexer`) replaced
  `IMemoryCache`. Values are stored as JSON under the `Redis:InstanceName` prefix (`MatchForecast:`).
- **Fail-open:** `AbortOnConnectFail=false` + `BacklogPolicy.FailFast`; read/write errors count as cache misses, so the API
  keeps answering from the source when Redis is down.
- Cached keys: `fixtures:{date}` (1 min if a match is live, otherwise `OddsProvider:CacheMinutes`), `fixture:{id}`,
  `forecast:v2:{id}` (`Ai:CacheMinutes`). `?refresh=true` bypasses the forecast cache.
- `DELETE /api/cache` clears only this app's keys using `SCAN` (not `KEYS`, no `allowAdmin`); returns 503 if no Redis
  primary is connected. Startup validation prevents an empty `InstanceName` (which would match `*`).
- Tests use `InMemoryRedisCacheService`; integration tests never resolve `IConnectionMultiplexer`.

### ✅ Roadmap step 2 — Rate Limiting
- Built-in ASP.NET Core rate limiter (no extra package), configured by `RateLimitOptions` (`RateLimiting` section).
  Policies are attached with `[EnableRateLimiting]`; endpoints without it are not limited.
  - `TokenPolicy` on `GET /api/auth/token`: fixed window **per client IP**, default 10 requests / 60 s.
  - `CacheClearPolicy` on `DELETE /api/cache`: **global** sliding window, default 2 requests / 60 min.
- Rejections return `429` as `ProblemDetails` (the frontends show `detail`); `Retry-After` is sent for the token policy.
- `UseRateLimiter()` runs **after** authorization, so unauthenticated cache-clear calls get 401 without consuming quota.
- Behind a proxy, `ASPNETCORE_FORWARDEDHEADERS_ENABLED=true` is required for real client IPs (set in docker-compose).
- Covered by `RateLimitingIntegrationTests`.

### ✅ Roadmap step 3 — Docker Containerization
- `backend/Dockerfile`: multi-stage (`sdk:10.0` → `aspnet:10.0`), listens on `8080`, sets
  `MATCHFORECAST_LOG_DIR=/app/logs` and `TZ=Europe/Istanbul` (server-side "today" depends on local time).
- `frontend/Dockerfile` and `frontend-angular/Dockerfile`: `node:22-alpine` build → `nginx:alpine`.
  `nginx.conf` serves the SPA and reverse-proxies `/api/` to `http://api:8080` (300 s read timeout for slow AI calls),
  so no CORS is needed in containers.
- `docker-compose.yml`: `redis` (redis:7-alpine), `api` (secrets from `.env`, Redis via service name, Ollama via
  `host.docker.internal`, `./logs` volume), `web` (React, published on host port `8080`).

### Frontend (React 19 + Vite) — `frontend`
- Vite dev server on `5173`, proxies `/api` → `http://localhost:5080`.
- `api.ts`: in-memory token (never `localStorage`), shared in-flight token promise, one automatic refresh+retry on `401`,
  error text from `ProblemDetails.detail`.
- `App.tsx`: date navigator, team/league search, league filter, cache-clear button, live-match auto refresh
  (5 min) with a local minute ticker, request sequencing (`requestSeq`) against stale responses.
- Components: `MatchList`, `MatchStatus` (live minute labels), `ForecastPanel` (1X2 / first-half bars, BTTS,
  10 predictions), `StatsTable`.

### Frontend (Angular 22) — `frontend-angular`
- One-to-one port of the React UI; same `styles.css`, same behavior.
- `src/app/core/`: `api.ts` (same fetch/token logic as React), `types.ts`, `match-status.ts`. These are **copies**
  of the React files; keep both in sync when backend DTOs change.
- `src/app/components/`: `match-list`, `forecast-panel`, `stats-table` (signal inputs/outputs, built-in control flow).
- `app.ts`: state in signals, side effects in `effect()` with `onCleanup`. Zoneless (Angular default since v21).
- Dev server on `4200`, `proxy.conf.json` (wired in `angular.json` → `serve.options.proxyConfig`) proxies `/api` → `5080`.
- Unit tests: Vitest via `ng test` (`app.spec.ts` stubs `fetch`).

---

## 2. Next Steps & Feature Roadmap

```
[1. Redis ✅] ──► [2. Rate Limiting ✅] ──► [3. Docker ✅]
                                                  │
                                                  ▼
[6. OAuth 2.0] ◄── [5. EF Core] ◄── [4. MSSQL Connection]
```

#### 4. MSSQL Connection
- [ ] Add SQL Server connection string (`ConnectionStrings:MatchForecastDb`), kept in user-secrets / environment variables.
- [ ] Add `mssql` service to `docker-compose.yml` (with a named volume).
- [ ] Add SQL Server health check to `/healthz`.

#### 5. EF Core
- [ ] Add `Microsoft.EntityFrameworkCore.SqlServer` and create `MatchForecastDbContext`.
- [ ] Define entities and configurations, create initial migrations.
- [ ] Persist forecast results (and user data needed for OAuth) in the database.

#### 6. OAuth 2.0 Integration
- [ ] Implement OAuth 2.0 / OpenID Connect authentication (Google & GitHub login).
- [ ] Create OAuth endpoints (`/api/auth/oauth/{provider}`, `/api/auth/oauth/callback`).
- [ ] Support social account linking and JWT generation for external users (via `JwtTokenService`).
- [ ] Require credentials for `/api/auth/token` (today it issues tokens to anyone).

### Follow-ups left over from steps 1–3 (not blocking)
- [ ] `/healthz` endpoint with Redis health check; `healthcheck` entries in docker-compose.
- [ ] Rate-limit the AI endpoints (`/api/matches/{id}/forecast`, `/api/matches/popular/forecasts`) and cap `count`
      (currently unbounded; `refresh=true` with a large `count` triggers that many sequential AI calls).
- [ ] Optional: distributed (Redis-backed) rate limiting if the API is scaled to multiple instances
      (current limiter state is in-memory, per instance).
- [ ] docker-compose: add `web-angular` service (e.g. `profiles: [angular]`, port `8081`), Redis volume, `.env.example`.

### Known issues
- `appsettings.json` contains real secrets (`Smtp:Password`, `Jwt:SecretKey`). Move them to user-secrets / `.env`,
  blank them in `appsettings.json`, and rotate the Gmail App Password.
- `OddsProvider:UseMock` is not read anywhere; `MockOddsProvider` and `PublicOddsProvider` are not registered
  (`PublicOddsProvider` also depends on an unregistered `IMemoryCache`). Remove or wire them up.
- `frontend/src/types.ts` and `frontend/src/components/Types.tsx` are duplicates that have drifted; keep one.
- Client-aborted requests (`TaskCanceledException`) are logged as 500 and e-mailed; `ExceptionMiddleware` should ignore
  them when `HttpContext.RequestAborted` is cancelled.

---

## 3. Project Commands

### Backend (.NET 10 API)
```bash
cd backend/MatchForecast.Api
dotnet run                                   # http://localhost:5080, Swagger: /swagger

dotnet test backend/MatchForecast.Tests/MatchForecast.Tests.csproj   # from repo root

# Local secrets (user-secrets id: matchforecast-api)
dotnet user-secrets set "OddsProvider:ApiKey" "<api-sports key>"
dotnet user-secrets set "Ai:Provider" "Gemini"            # Gemini | Claude | Ollama
dotnet user-secrets set "Ai:Gemini:ApiKey" "<key>"        # or Ai:Claude:ApiKey
dotnet user-secrets set "Jwt:SecretKey" "<at least 32 random characters>"
dotnet user-secrets set "Smtp:Password" "<gmail app password>"
```
A local Redis is expected at `localhost:6379` (e.g. `docker run -d -p 6379:6379 redis:7-alpine`); without it the API
still works but nothing is cached and cache-clear returns 503.

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
# repo root; .env holds secrets as env vars (Section__Key), e.g.
#   OddsProvider__ApiKey=...  Ai__Provider=Gemini  Ai__Gemini__ApiKey=...
#   Jwt__SecretKey=...  Smtp__Password=...
docker compose up --build        # UI: http://localhost:8080
docker compose logs -f api
docker compose down
```