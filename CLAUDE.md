# CLAUDE.md — MatchForecast Project Guide & Roadmap

MatchForecast is a full-stack application that fetches daily football fixtures and all betting markets from an API, analyzes them using an LLM (Anthropic Claude), and presents the top 5 highest-probability options with confidence scores and reasoning.

- **Backend:** .NET 10 Minimal API (`backend/MatchForecast.Api`), Class Library Models (`backend/MatchForecast.Models`), xUnit Tests (`backend/MatchForecast.Tests`)
- **Frontend:** React 19 + TypeScript + Vite (`frontend`)
- **External Data Provider:** API-Football (api-sports.io v3)
- **AI Engine:** Anthropic Claude (Messages API)

---

## 1. Steps Completed So Far

### Backend (.NET 10 Minimal API)
- **Architecture & DI Setup:**
  - Configured Minimal API pipeline in `Program.cs` using `.NET 10`.
  - Configured Options Pattern (`OddsProviderOptions`, `AiOptions`).
  - Configured CORS policy (`Cors:AllowedOrigins`) targeting frontend server (`http://localhost:5173`).
  - Added OpenAPI & Swagger UI support (`AddOpenApi`, `AddEndpointsApiExplorer`, `AddSwaggerGen`, `UseSwagger`, `UseSwaggerUI` available at `/swagger`).
- **Odds & Fixtures Integration:**
  - Defined `IOddsProvider` interface for decoupled data abstraction.
  - Implemented `MockOddsProvider` providing 5 default sample matches and market odds for local keyless development (`OddsProvider:UseMock = true`).
  - Implemented `ApiFootballOddsProvider` connecting to API-Football (`v3.football.api-sports.io`) via `HttpClient` using `x-apisports-key`.
- **AI Forecast Engine:**
  - Defined `IForecastAiClient` interface and implemented `ClaudeForecastClient` connecting to Anthropic Claude (`claude-sonnet-5`).
  - Implemented `ForecastPrompt` utility for building structured prompts with unique option identifiers (e.g. `5.3`), implied odds calculation (`100 / odd`), and system instructions restricting AI outputs strictly to valid market options.
  - Implemented `ForecastService` to orchestrate match loading, prompt generation, AI response JSON parsing, option filtering/deduplication, confidence clamping (0–100%), and result caching (`IMemoryCache`).
- **Shared Models Library (`backend/MatchForecast.Models`):**
  - Extracted domain models into a standalone `.NET 10` class library.
  - Organized into clean namespaces: `MatchForecast.Models.Request`, `MatchForecast.Models.Response`, and `MatchForecast.Models.Common`.
  - Implemented strongly-typed records & exceptions: `MatchSummary`, `MatchOdds`, `ForecastResult`, `Market`, `OddOption`, `Prediction`, `ForecastQueryRequest`, `ForecastException`.
  - Added project references to `MatchForecast.Models` across `MatchForecast.Api` and `MatchForecast.Tests`.
- **Test Suite (`backend/MatchForecast.Tests`):**
  - Created xUnit test project referencing `MatchForecast.Api` with `Microsoft.AspNetCore.Mvc.Testing` and `Moq`.
  - Added integration tests (`MatchesApiIntegrationTests.cs`) covering `/api/matches`, `/api/matches/{id}/odds`, and `/api/matches/{id}/forecast`.
  - Added unit tests (`ForecastServiceTests.cs`) for prompt building, JSON parsing error handling, confidence clamping, option filtering, and memory caching.

### Frontend (React 19 + TypeScript + Vite)
- **Vite & React Setup:**
  - Configured Vite dev server with proxy settings forwarding `/api` requests to backend at `http://localhost:5080`.
  - Configured TypeScript types (`types.ts`) reflecting backend API DTOs.
- **API Client:**
  - Implemented `api.ts` with error handling that extracts `detail` messages from backend `ProblemDetails`.
  - Added methods `getMatches(date)` and `getForecast(id, refresh)`.
- **User Interface Components:**
  - `App.tsx`: Layout container featuring header date navigator (previous/next buttons & date picker), team/league search filter, and async request sequence tracking (`requestSeq`) to prevent race conditions.
  - `MatchList.tsx`: Interactive match list displaying kickoff times, teams, league names, match status badges, and active match selection.
  - `ForecastPanel.tsx`: Comprehensive forecast viewer displaying match details, summary text, top 5 predictions, AI confidence progress bars, implied probability markers, and refresh/retry trigger buttons.
  - `styles.css`: Dark-themed modern layout with responsive CSS grid/flexbox design.

---

## 2. Next Steps & Feature Roadmap

The application will be enhanced with enterprise-grade features and production infrastructure in the following order:

```
[1. Logger] ──► [2. JWT Auth] ──► [3. Token Management] ──► [4. OAuth 2.0]
                                                                  │
[8. Exception Middleware] ◄── [7. Logging Middleware] ◄── [6. Rate Limiting] ◄── [5. Redis]
           │
           ▼
[9. Docker Containerization]
```

### Planned Features Breakdown

#### 1. Logger (Structured Logging Setup)
- [ ] Integrate **Serilog** (or NLog) into `backend/MatchForecast.Api`.
- [ ] Configure structured JSON console and file log sinks.
- [ ] Enrich logs with Correlation IDs, HTTP request context, user IDs, and environment metadata.

#### 2. JWT Authentication
- [ ] Add JWT Bearer authentication scheme using `Microsoft.AspNetCore.Authentication.JwtBearer`.
- [ ] Define `JwtOptions` (SecretKey, Issuer, Audience, ExpiryMinutes).
- [ ] Create authentication endpoints (`/api/auth/register`, `/api/auth/login`, `/api/auth/me`).
- [ ] Add user identity data storage, password hashing, and role/claims-based authorization policies.

#### 3. Token Management
- [ ] Implement Refresh Token strategy (sliding expiration, secure HTTP-only cookies or bearer payloads).
- [ ] Create `ITokenService` for token generation, validation, and token rotation.
- [ ] Implement token revocation and blacklist mechanism.

#### 4. OAuth 2.0 Integration
- [ ] Implement OAuth 2.0 / OpenID Connect authentication (Google & GitHub login).
- [ ] Create OAuth endpoints (`/api/auth/oauth/{provider}`, `/api/auth/oauth/callback`).
- [ ] Support social account linking and automated JWT generation for external users.

#### 5. Redis Integration
- [ ] Integrate Redis via `StackExchange.Redis` and `Microsoft.Extensions.Caching.StackExchangeRedis`.
- [ ] Replace in-memory caching (`IMemoryCache`) in `ForecastService` and `OddsProvider` with `IDistributedCache` backed by Redis.
- [ ] Store token revocation blacklists and rate limiting state in Redis.
- [ ] Add Redis health checks (`/healthz`).

#### 6. Rate Limiting
- [ ] Implement ASP.NET Core Rate Limiting (`Microsoft.AspNetCore.RateLimiting`).
- [ ] Configure rate limit policies:
  - Global IP rate limit (e.g. 100 req/min).
  - Strict AI endpoint rate limit for `/api/matches/{id}/forecast` (e.g. 5 req/min per IP/user).
  - Authentication endpoint brute-force protection.
- [ ] Use Redis as the distributed rate limiting storage partition.

#### 7. Logging Middleware
- [ ] Implement custom `RequestLoggingMiddleware`.
- [ ] Log incoming request metadata (HTTP Method, Path, Query Parameters, Client IP, Request Headers).
- [ ] Log outgoing response metadata (Status Code, Execution Duration in ms).
- [ ] Generate and trace unique `X-Correlation-ID` headers across request-response lifecycles.

#### 8. Exception Middleware
- [ ] Implement centralized `ExceptionHandlingMiddleware` replacing inline `app.UseExceptionHandler`.
- [ ] Automatically format exceptions (`ForecastException`, `UnauthorizedAccessException`, `KeyNotFoundException`, validation errors) into standardized RFC 7807 `ProblemDetails`.
- [ ] Mask internal exception details in Production environment while logging full exception tracebacks internally.

#### 9. Docker Containerization
- [ ] Create multi-stage `Dockerfile` for .NET 10 API (`backend/MatchForecast.Api`).
- [ ] Create multi-stage `Dockerfile` for React 19 Frontend (`frontend`) using Nginx for production build serving and API reverse proxying.
- [ ] Create `docker-compose.yml` to orchestrate:
  - `matchforecast-api` (.NET 10 Web API)
  - `matchforecast-web` (React + Nginx Frontend)
  - `matchforecast-redis` (Redis Server)
- [ ] Define container networks, volume bindings, environment configurations, and healthcheck commands.

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
