# CLAUDE.md — MatchForecast Project Guide & Roadmap

MatchForecast is a full-stack application that fetches daily football fixtures and all betting markets from an API, analyzes them using an LLM (Anthropic Claude), and presents the top 5 highest-probability options with confidence scores and reasoning.

- **Backend:** .NET 10 Minimal API (`backend/MatchForecast.Api`), Class Library Models (`backend/MatchForecast.Models`), Logger & Middleware Library (`backend/MatchForecast.Logger`), xUnit Tests (`backend/MatchForecast.Tests`)
- **Frontend:** React 19 + TypeScript + Vite (`frontend`)
- **External Data Provider:** API-Football (api-sports.io v3)
- **AI Engine:** Anthropic Claude (Messages API)

---

## 1. Steps Completed So Far

### Backend (.NET 10 Minimal API)
- **Architecture & DI Setup:**
  - Configured Minimal API pipeline in `Program.cs` using `.NET 10`.
  - Configured Options Pattern (`OddsProviderOptions`, `AiOptions`, `JwtOptions`).
  - Configured CORS policy (`Cors:AllowedOrigins`) targeting frontend server (`http://localhost:5173`).
  - Added OpenAPI & Swagger UI support (`AddOpenApi`, `AddEndpointsApiExplorer`, `AddSwaggerGen`, `UseSwagger`, `UseSwaggerUI` available at `/swagger`).
- **Odds & Fixtures Integration:**
  - Defined `IOddsProvider` interface for decoupled data abstraction.
  - Implemented `GetPopularMatchesAsync` to extract the **top 10 most popular matches** of the daily betting bulletin (prioritizing Süper Lig, Champions League, Premier League, La Liga, Serie A, Bundesliga, Ligue 1).
  - Updated `MockOddsProvider` with 10 top popular bulletin fixtures for keyless development.
  - Implemented `ApiFootballOddsProvider` connecting to API-Football (`v3.football.api-sports.io`) via `HttpClient` using `x-apisports-key`.
- **AI Forecast Engine:**
  - Defined `IForecastAiClient` interface and implemented `ClaudeForecastClient` connecting to Anthropic Claude (`claude-sonnet-5`).
  - Implemented `ForecastPrompt` utility for building structured prompts with unique option identifiers (e.g. `5.3`), implied odds calculation (`100 / odd`), and system instructions restricting AI outputs strictly to valid market options.
  - Implemented `ForecastService` supporting single fixture analysis (`GetForecastAsync`) and daily top 10 popular bulletin batch forecasting (`GetPopularForecastsAsync`).
- **Shared Models Library (`backend/MatchForecast.Models`):**
  - Extracted domain models into a standalone `.NET 10` class library (`Request`, `Response`, `Common` namespaces).
- **Daily Logging & Exception Audit (`NLog.Web.AspNetCore`):**
  - Configured NLog in `Program.cs` (`builder.Host.UseNLog()`).
  - Added `nlog.config` with daily rolling log files (`logs/matchforecast-${shortdate}.log`) and console logging.
  - Added structured pre-exception log messages (`logger.LogError` / `logger.LogWarning`) across `ForecastService`, `ClaudeForecastClient`, `ApiFootballOddsProvider`, and centralized exception middleware before any `ForecastException` or unhandled error is thrown.
- **Middleware (`backend/MatchForecast.Logger`):**
  - `LoggingMiddleware` (`app.UseMatchForecastLogging()`): logs HTTP method, path, status code, duration (ms), and request/response bodies. Sensitive JSON fields (`password`, `token`, `accessToken`, `refreshToken`, `otp`, `tckn`, etc.) are masked as `***` at any nesting depth; multipart requests are skipped.
  - `ExceptionMiddleware` (`app.UseMatchForecastExceptionHandling()`): converts `ForecastException` to its own status code and message, and any other exception to a generic 500; responses are RFC 7807 `ProblemDetails`. 5xx errors are logged as `Error`, others as `Warning`.
- **JWT Authentication & Token:**
  - Added JWT Bearer authentication (`Microsoft.AspNetCore.Authentication.JwtBearer`) with `JwtOptions` (`SecretKey`, `Issuer`, `Audience`, `ExpiryMinutes`); `SecretKey` must be at least 32 bytes (HS256) and is validated at startup (`ValidateOnStart`).
  - Implemented `JwtTokenService` generating HS256-signed tokens (unique `jti` per token) via `JsonWebTokenHandler`.
  - Added `AuthController` with `GET /api/auth/token` (`GetToken`), which returns a token directly without credentials (`[AllowAnonymous]`, `no-store` caching).
  - Configured an authorization **fallback policy**: every endpoint without `[AllowAnonymous]` requires a valid Bearer token (new controllers are protected by default). Token lifetime is validated with a 30-second `ClockSkew`.
  - Swagger UI: added an `Authorize` button (ApiKey scheme on the `Authorization` header, value entered as `Bearer {accessToken}`); `BearerSecurityOperationFilter` adds the lock icon and `401` response only to protected endpoints.
- **Test Suite (`backend/MatchForecast.Tests`):**
  - Created xUnit test project referencing `MatchForecast.Api` with `Microsoft.AspNetCore.Mvc.Testing` and `Moq`.
  - Added integration tests (`MatchesApiIntegrationTests.cs`) covering `/api/matches`, `/api/matches/{id}/odds`, and `/api/matches/{id}/forecast`, using authorized clients via the `TestAuth` helper.
  - Added auth integration tests (`AuthApiIntegrationTests.cs`) covering token generation, `401` without token or with an invalid token, and end-to-end access with a token from `GetToken`.
  - Added unit tests (`ForecastServiceTests.cs`) for prompt building, JSON parsing error handling, confidence clamping, option filtering, and memory caching.

### Frontend (React 19 + TypeScript + Vite)
- **Vite & React Setup:**
  - Configured Vite dev server with proxy settings forwarding `/api` requests to backend at `http://localhost:5080`.
  - Configured TypeScript types (`types.ts`) reflecting backend API DTOs.
- **API Client:**
  - Implemented `api.ts` with error handling that extracts `detail` messages from backend `ProblemDetails`.
  - Added methods `getMatches(date)` and `getForecast(id, refresh)`.
  - Added token handling: fetches a token from `/api/auth/token` on first request, keeps it in memory only, sends it as `Authorization: Bearer {token}`, and on `401` refreshes the token once and retries.
- **User Interface Components:**
  - `App.tsx`: Layout container featuring header date navigator (previous/next buttons & date picker), team/league search filter, and async request sequence tracking (`requestSeq`) to prevent race conditions.
  - `MatchList.tsx`: Interactive match list displaying kickoff times, teams, league names, match status badges, and active match selection.
  - `ForecastPanel.tsx`: Comprehensive forecast viewer displaying match details, summary text, top 5 predictions, AI confidence progress bars, implied probability markers, and refresh/retry trigger buttons.
  - `styles.css`: Dark-themed modern layout with responsive CSS grid/flexbox design.

---

## 2. Next Steps & Feature Roadmap

The application will be enhanced with production infrastructure and features in the following order:

```
[1. Redis] ──► [2. Rate Limiting] ──► [3. Docker Containerization]
                                                  │
                                                  ▼
[6. OAuth 2.0] ◄── [5. EF Core] ◄── [4. MSSQL Connection]
```

### Planned Features Breakdown

#### 1. Redis Integration
- [ ] Integrate Redis via `StackExchange.Redis` and `Microsoft.Extensions.Caching.StackExchangeRedis`.
- [ ] Replace in-memory caching (`IMemoryCache`) in `ForecastService` and `OddsProvider` with `IDistributedCache` backed by Redis.
- [ ] Store rate limiting state in Redis.
- [ ] Add Redis health checks (`/healthz`).

#### 2. Rate Limiting
- [ ] Implement ASP.NET Core Rate Limiting (`Microsoft.AspNetCore.RateLimiting`).
- [ ] Configure rate limit policies:
  - Global IP rate limit (e.g. 100 req/min).
  - Strict AI endpoint rate limit for `/api/matches/{id}/forecast` (e.g. 5 req/min per IP/token).
  - Token endpoint protection for `/api/auth/token` (it issues tokens without credentials).
- [ ] Use Redis as the distributed rate limiting storage partition.

#### 3. Docker Containerization
- [ ] Create multi-stage `Dockerfile` for .NET 10 API (`backend/MatchForecast.Api`).
- [ ] Create multi-stage `Dockerfile` for React 19 Frontend (`frontend`) using Nginx for production build serving and API reverse proxying.
- [ ] Create `docker-compose.yml` to orchestrate:
  - `matchforecast-api` (.NET 10 Web API)
  - `matchforecast-web` (React + Nginx Frontend)
  - `matchforecast-redis` (Redis Server)
  - `matchforecast-mssql` (SQL Server, added with step 4)
- [ ] Define container networks, volume bindings, environment configurations (incl. `Jwt__SecretKey`), and healthcheck commands.

#### 4. MSSQL Connection
- [ ] Add SQL Server connection string (`ConnectionStrings:MatchForecastDb`), kept in user-secrets / environment variables.
- [ ] Add SQL Server health check to `/healthz`.

#### 5. EF Core
- [ ] Add `Microsoft.EntityFrameworkCore.SqlServer` and create `MatchForecastDbContext`.
- [ ] Define entities and configurations, create initial migrations.
- [ ] Persist forecast results (and user data needed for OAuth) in the database.

#### 6. OAuth 2.0 Integration
- [ ] Implement OAuth 2.0 / OpenID Connect authentication (Google & GitHub login).
- [ ] Create OAuth endpoints (`/api/auth/oauth/{provider}`, `/api/auth/oauth/callback`).
- [ ] Support social account linking and automated JWT generation for external users (via `JwtTokenService`).

---

## 3. Project Commands

### Backend (.NET 10 API)
```bash
# Navigate to backend directory
cd backend/MatchForecast.Api

# Restore dependencies
dotnet restore

# Run API (http://localhost:5080)
dotnet run

# Run tests
dotnet test backend/MatchForecast.Tests/MatchForecast.Tests.csproj

# User secrets configuration for local development
dotnet user-secrets set "Ai:ApiKey" "sk-ant-your-claude-key"
dotnet user-secrets set "OddsProvider:ApiKey" "your-api-sports-key"
dotnet user-secrets set "OddsProvider:UseMock" "false"
dotnet user-secrets set "Jwt:SecretKey" "<at-least-32-character-random-value>"
```

### Frontend (React 19 + Vite)
```bash
# Navigate to frontend directory
cd frontend

# Install dependencies
npm install

# Run Vite development server (http://localhost:5173)
npm run dev

# Build for production
npm run build
```