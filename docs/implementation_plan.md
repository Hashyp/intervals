# Social Login Implementation Plan

Status: Draft
Date: 2026-07-03
Source documents:

- `docs/social-login-functional-spec.html`
- `docs/social-login-architecture.html`

## Scope

Implement required authenticated access for Intervals with Google and X social login. The app must not allow anonymous product access. API authorization is enforced server-side. The frontend provides a login page, authenticated app shell, remember-me option, and logout.

In scope:

- Google login.
- X login.
- HTTP-only cookie sessions.
- Remember-me persistent sessions.
- Server-side API authorization.
- Internal user accounts and provider links.
- PostgreSQL auth persistence through Aspire-managed local infrastructure.
- Project-based AppHost for Aspire integration testing.
- API integration tests using `WebApplicationFactory` and `Testcontainers.PostgreSql`.
- Full-system smoke tests using `Aspire.Hosting.Testing`.

Out of scope for this feature:

- Apple login. Phase 2.
- Facebook, Microsoft, GitHub login. Later optional work.
- Email/password signup.
- Email magic-link fallback.
- Cloud-synced practice progress.
- Server-side score/history storage.
- Provider token storage after login.

## Key Decisions

- Authentication owner: ASP.NET Core API.
- Browser session: secure HTTP-only app cookie.
- Login providers for first release: Google and X.
- Local infrastructure: Aspire-managed PostgreSQL, not SQLite.
- AppHost target: convert root file-based `apphost.cs` to a project-based AppHost.
- API integration tests: `WebApplicationFactory` plus `Testcontainers.PostgreSql`.
- Full-system tests: `Aspire.Hosting.Testing` against the project-based AppHost.
- Provider credentials: `.NET user secrets` for development and initial validation.

## Target Project Layout

```text
apphost/
  Intervals.AppHost.csproj
  Program.cs

api/
  Intervals.Api/
    Auth/
      AuthEndpoints.cs
      AuthOptions.cs
      AuthProviderNames.cs
      AuthResultCodes.cs
      CurrentUser.cs
      ExternalUserProfile.cs
      ReturnUrlValidator.cs
      XOAuthDefaults.cs
    Data/
      IntervalsDbContext.cs
      Entities/
        AppUser.cs
        ExternalLogin.cs
        AuthEvent.cs
    Program.cs

tests/
  Intervals.Api.Tests/
  Intervals.AppHost.Tests/

src/
  auth/
    AuthProvider.tsx
    LoginPage.tsx
    ProtectedApp.tsx
    sessionApi.ts
    returnUrl.ts
```

## Phase 1: AppHost Conversion and PostgreSQL

1. Create `apphost/Intervals.AppHost.csproj`.
   - Use `Aspire.AppHost.Sdk`.
   - Add `Aspire.Hosting.JavaScript`.
   - Add `Aspire.Hosting.PostgreSQL`.

2. Move the current root `apphost.cs` logic into `apphost/Program.cs`.
   - Preserve the existing API resource name `api`.
   - Preserve the existing Vite resource name `web`.
   - Preserve current `WithExternalHttpEndpoints()` behavior.

3. Add PostgreSQL resource wiring.

```csharp
var postgres = builder.AddPostgres("postgres")
    .WithDataVolume("intervals-postgres-data")
    .WithLifetime(ContainerLifetime.Persistent);

var intervalsDb = postgres.AddDatabase("intervalsdb");

var api = builder.AddProject("api", "../api/Intervals.Api/Intervals.Api.csproj")
    .WithReference(intervalsDb)
    .WaitFor(intervalsDb)
    .WithHttpEndpoint()
    .WithHttpHealthCheck("/health")
    .WithExternalHttpEndpoints();
```

4. Update `aspire.config.json`.
   - Change `appHost.path` from `apphost.cs` to `apphost/Intervals.AppHost.csproj`.
   - Keep current profile URLs unless Aspire project migration requires profile regeneration.

5. Retire the root `apphost.cs` after the project-based AppHost starts successfully.
   - Either remove it or leave a short migration note if the team wants a temporary compatibility window.
   - Do not keep two active AppHost definitions long term.

6. Verify AppHost startup.
   - Run `aspire start --non-interactive`.
   - Wait for `postgres`, `api`, and `web`.
   - Confirm the API receives `ConnectionStrings__intervalsdb`.

## Phase 2: Backend Dependencies and Persistence

Add API packages:

- `Aspire.Npgsql.EntityFrameworkCore.PostgreSQL`
- `Microsoft.EntityFrameworkCore`
- `Microsoft.EntityFrameworkCore.Design`
- `Npgsql.EntityFrameworkCore.PostgreSQL`
- `Microsoft.AspNetCore.Authentication.Google`

Add test packages later in the test phase:

- `Microsoft.AspNetCore.Mvc.Testing`
- `Testcontainers.PostgreSql`
- `Aspire.Hosting.Testing`

Update `api/Intervals.Api/Program.cs`:

```csharp
builder.AddNpgsqlDbContext<IntervalsDbContext>("intervalsdb");
```

Create `IntervalsDbContext` and entities.

`AppUser`:

- `Id`
- `DisplayName`
- `Email`
- `EmailNormalized`
- `AvatarUrl`
- `CreatedUtc`
- `LastLoginUtc`
- `DisabledUtc`

`ExternalLogin`:

- `Id`
- `UserId`
- `Provider`
- `ProviderUserId`
- `Email`
- `EmailVerified`
- `DisplayName`
- `AvatarUrl`
- `CreatedUtc`
- `LastLoginUtc`

`AuthEvent`:

- `Id`
- `UserId`
- `Provider`
- `EventType`
- `OccurredUtc`
- `Success`
- `FailureCode`
- `CorrelationId`

Constraints:

- Primary key on every table.
- Foreign key from `ExternalLogin.UserId` to `AppUser.Id`.
- Unique index on `ExternalLogin.Provider` plus `ExternalLogin.ProviderUserId`.
- Index on `AppUser.EmailNormalized`, but do not make email globally unique.
- Never store provider access tokens, refresh tokens, authorization codes, ID tokens, or raw provider payloads.

Create first EF Core migration and verify it applies to Aspire PostgreSQL.

## Phase 3: Backend Authentication and Authorization

Configure cookie authentication:

- Main cookie name: `Intervals.App`.
- HTTP-only.
- Secure outside development.
- `SameSite=Lax`.
- Standard session lifetime: 8 hours recommended.
- Remember-me lifetime: 30 days recommended.
- Sliding expiration enabled.

Configure external providers:

Google:

- Use Google provider middleware or OIDC-capable provider middleware.
- Scopes: `openid`, `email`, `profile`.
- Durable provider key: Google subject claim.

X:

- Use generic OAuth 2.0 handler.
- Enable PKCE.
- Scopes: `users.read`, `users.email`.
- Durable provider key: X user id.
- Treat email as optional.

User secrets:

```bash
dotnet user-secrets init --project api/Intervals.Api
dotnet user-secrets set "Authentication:Google:ClientId" "<google-client-id>" --project api/Intervals.Api
dotnet user-secrets set "Authentication:Google:ClientSecret" "<google-client-secret>" --project api/Intervals.Api
dotnet user-secrets set "Authentication:X:ClientId" "<x-client-id>" --project api/Intervals.Api
dotnet user-secrets set "Authentication:X:ClientSecret" "<x-client-secret>" --project api/Intervals.Api
```

Configure authorization:

- Add fallback authorization policy requiring authenticated users.
- Apply `RequireAuthorization()` explicitly to all `/api/*` endpoints.
- Apply `AllowAnonymous()` only to:
  - login start endpoints,
  - provider callback endpoints,
  - static assets needed by login,
  - private infrastructure health checks.

Health endpoint decision:

- Keep `/health` available only for Aspire or private hosting infrastructure.
- Do not treat `/health` as a public anonymous application endpoint.

## Phase 4: Backend Auth Services and Endpoints

Create auth service responsibilities:

- Validate provider names.
- Validate local relative return URLs.
- Normalize provider claims into `ExternalUserProfile`.
- Create new `AppUser` records.
- Reuse existing users by provider link.
- Update provider link profile fields and `LastLoginUtc`.
- Record auth events without sensitive payloads.
- Issue app cookie with persistent expiration only when remember-me is selected.

Endpoints:

`POST /auth/login/{provider}`

- Anonymous.
- Accepts provider, return URL, and remember-me flag.
- Rejects unknown providers.
- Validates return URL as local and relative.
- Starts external provider challenge.
- Stores return URL and remember-me choice in protected auth properties.

`GET /auth/callback/{provider}`

- Anonymous.
- Completes provider login.
- Validates provider state through middleware.
- Creates or updates internal account and provider link.
- Issues app cookie.
- Redirects to validated return URL.
- On cancellation or provider error, redirects to `/login` with a safe error code.

`POST /auth/logout`

- Authenticated.
- Clears app cookie.
- Records logout event.
- Redirects to `/login`.

`GET /api/session`

- Authenticated.
- Returns current user summary and enabled provider status.

`GET /api/status`

- Authenticated.
- Keep existing response shape unless frontend needs changes.

Shared error shape:

```json
{
  "code": "auth_required",
  "message": "Authentication is required.",
  "correlationId": "..."
}
```

## Phase 5: Frontend Auth Shell

Add `src/auth/sessionApi.ts`.

- `getSession()` calls `GET /api/session`.
- Use `credentials: "same-origin"`.
- Treat `401` as anonymous state.

Add `src/auth/returnUrl.ts`.

- Accept only local path/search/hash values.
- Reject external URLs, protocol-relative URLs, backslashes, and empty unsafe input.

Add `src/auth/AuthProvider.tsx`.

- Loads session on startup.
- Exposes:
  - `loading`,
  - `authenticated`,
  - `user`,
  - `refreshSession`.

Add `src/auth/LoginPage.tsx`.

- Show Intervals brand/name.
- Show Google and X login buttons.
- Show remember-me checkbox.
- Use POST form submission to `/auth/login/google` and `/auth/login/x`.
- Include hidden `returnUrl`.
- Show safe messages for:
  - cancelled login,
  - provider failure,
  - unknown auth error.
- Do not show Apple button in MVP.

Add `src/auth/ProtectedApp.tsx`.

- Shows compact loading state while session is loading.
- Shows `LoginPage` when anonymous.
- Renders existing training UI only when authenticated.

Update `src/App.tsx`.

- Keep training behavior intact.
- Move current training UI behind `ProtectedApp` or extract the current training component if needed.
- Add account summary and logout control in a small header/account area.

Update Vite/Aspire routing:

- Current Vite proxy only handles `/api`.
- Add `/auth` proxy to the API target for local dev.
- Ensure static publishing or hosting routes both `/api` and `/auth` to the API in deployed/static scenarios.

## Phase 6: Frontend Tests

Add Vitest and Testing Library coverage for:

- Login page renders Google and X buttons.
- Remember-me checkbox changes submitted value.
- Safe return URL is included in login form.
- Unsafe return URL is replaced by `/`.
- Anonymous session shows login page.
- Authenticated session renders training UI.
- Session loading state does not render training UI.
- Provider cancellation query displays neutral retry message.
- Logout control posts to `/auth/logout`.

Keep existing interval-training tests passing.

## Phase 7: Backend API Integration Tests

Create `tests/Intervals.Api.Tests`.

Use:

- `WebApplicationFactory`.
- `Testcontainers.PostgreSql`.
- test authentication scheme.
- per-test or per-class database reset.

Default API integration test cases:

- Anonymous `GET /api/session` returns `401`.
- Anonymous `GET /api/status` returns `401`.
- Authenticated `GET /api/session` returns user summary.
- Authenticated `GET /api/status` succeeds.
- Logout clears session cookie.
- Return URL validator accepts local relative paths.
- Return URL validator rejects absolute, protocol-relative, and backslash URLs.
- Account creation creates `AppUser` and `ExternalLogin`.
- Returning login reuses existing provider link and user.
- Duplicate provider id cannot create two links.
- Email match alone does not auto-merge accounts.
- Auth events are recorded without token/code payloads.

Provider callback testing:

- Unit test claim normalization for Google and X.
- Unit test auth account service with normalized provider profiles.
- Integration test callback-adjacent behavior with fake external profile/test auth plumbing.
- Keep live Google/X OAuth flows as manual verification or Aspire/browser smoke tests because real providers require registered credentials and external redirects.

## Phase 8: Aspire Distributed App Tests

Create `tests/Intervals.AppHost.Tests`.

Use:

- `Aspire.Hosting.Testing`.
- `DistributedApplicationTestingBuilder`.
- Project reference to `apphost/Intervals.AppHost.csproj`.

Test cases:

- AppHost starts.
- `postgres` becomes healthy.
- `api` becomes healthy.
- `web` becomes healthy.
- API receives PostgreSQL connection through Aspire resource reference.
- Anonymous API request through the running app returns `401`.
- Login page is reachable from the web resource.

Keep this layer small. It is intended to catch resource-graph, startup, and cross-process wiring issues. It is not the main place for detailed auth edge cases.

## Phase 9: Security Hardening

Add rate limiting for:

- `/auth/login/{provider}`,
- `/auth/callback/{provider}`,
- `/auth/logout`,
- `/api/session`.

Add antiforgery posture:

- Login form POST should use the selected antiforgery strategy or be explicitly justified as low risk with provider state protection.
- Future state-changing API calls must use antiforgery protection because auth is cookie based.

Add secure logging:

- Log auth success/failure, provider, event type, user id when available, and correlation id.
- Never log provider tokens, auth codes, raw callback query values, client secrets, or raw provider payloads.

Add forwarded headers configuration:

- Required before auth when hosted behind proxy.
- Needed so callback URL generation uses the external scheme and host.

Add data protection plan:

- Local development can use default keys.
- Production must persist data-protection keys so cookies survive restarts and multiple instances.

## Phase 10: Manual Provider Setup and Verification

Google developer app:

- Register local callback URL for the API/AppHost URL.
- Add Google client id and secret to user secrets.
- Verify first login creates user and provider link.
- Verify returning login reuses same user.

X developer app:

- Register local callback URL for the API/AppHost URL.
- Enable OAuth 2.0 authorization code with PKCE.
- Request identity/email scopes allowed by the app.
- Add X client id and secret to user secrets.
- Verify email may be absent and login still works by X user id.

Manual local verification:

- Start with Aspire.
- Visit web URL while signed out.
- Confirm login page appears.
- Confirm training UI is inaccessible while signed out.
- Login with Google.
- Logout.
- Login with X.
- Check `/api/status` is `401` when signed out and succeeds when signed in.
- Check remember-me cookie persistence.

## Acceptance Checklist

- Signed-out root visit shows login page.
- Signed-out users cannot access training UI.
- Google login works.
- X login works.
- First-time login creates internal user.
- Returning login reuses internal user.
- Remember-me creates persistent session.
- Logout clears session.
- Anonymous `/api/session` returns `401`.
- Anonymous `/api/status` returns `401`.
- Authenticated `/api/session` returns user summary.
- Authenticated `/api/status` succeeds.
- Provider secrets are in user secrets, not committed.
- Provider tokens and auth codes are not stored or logged.
- Frontend tests pass.
- API integration tests pass.
- Aspire distributed smoke tests pass.
- Existing app tests pass.

## Implementation Order and Parallelization

### Required Sequential Foundation

1. Convert file-based `apphost.cs` to project-based `apphost/Intervals.AppHost.csproj`.
2. Add Aspire PostgreSQL resource and wire `intervalsdb` to the API with `WithReference()` and `WaitFor()`.
3. Add API PostgreSQL packages and `IntervalsDbContext`.
4. Add first EF migration and verify against Aspire PostgreSQL.

These tasks are sequential because the API persistence and Aspire tests depend on the project-based AppHost and database connection name.

### Parallel Work After Foundation

Backend auth can proceed in parallel with frontend shell work once endpoint contracts are agreed.

Backend track:

1. Configure cookies, Google, X OAuth, fallback authorization, and `/api/status` protection.
2. Implement auth services, account creation, provider links, auth events, and session endpoint.
3. Implement login, callback, and logout endpoints.
4. Add backend unit tests and API integration tests with Testcontainers.

Frontend track:

1. Add `AuthProvider`, `sessionApi`, `returnUrl`, `LoginPage`, and `ProtectedApp`.
2. Move current training UI behind authenticated state.
3. Add login form posts for Google and X with remember-me.
4. Add account/logout UI and frontend tests.

Testing track:

1. Build API integration test harness with `WebApplicationFactory` and `Testcontainers.PostgreSql`.
2. Build Aspire distributed test project once the project-based AppHost exists.
3. Add small AppHost smoke tests while backend/frontend implementation continues.

### Final Integration Sequence

1. Wire frontend `/auth` proxy and deployment/static route behavior.
2. Run frontend tests.
3. Run API integration tests.
4. Run Aspire distributed smoke tests.
5. Configure Google user secrets and verify live Google login.
6. Configure X user secrets and verify live X login.
7. Run full local Aspire verification: anonymous rejection, login, remember-me, logout, and authenticated API access.

