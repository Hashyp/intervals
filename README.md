# Intervals

Ear training for recognizing musical intervals — a Vite/React frontend with an
ASP.NET Core API, orchestrated by .NET Aspire. The app requires a signed-in
account; sign-in is via Google, Microsoft, or X (social login) or email +
password. Password accounts verify their email and can recover via password
reset; signed-in users manage their sign-in methods from `/account-settings`.

## Project layout

```
apphost/                 Aspire AppHost (project-based) — orchestrates api, web, postgres, mailpit
api/Intervals.Api/       ASP.NET Core minimal API — auth, sessions, status
  Auth/                  Cookie auth, Google/Microsoft/X providers, password accounts,
                         email verification, password reset, account linking & merge, sessions
  Data/                  EF Core model (AppUser, ExternalLogin, AuthEvent) + migrations
src/                     Vite + React training app
  auth/                  Auth shell: AuthProvider, LoginPage, ProtectedApp, AccountBar,
                         ForgotPasswordPage, ResetPasswordPage, EmailVerificationBanner
tests/
  Intervals.Api.Tests/   API integration tests (WebApplicationFactory + Testcontainers.PostgreSql)
  Intervals.AppHost.Tests/  Aspire distributed smoke tests (Aspire.Hosting.Testing)
docs/                    Project documentation
```

## Prerequisites

- **.NET 10 SDK** (`10.0.301` or later) — for the API and AppHost
- **Node.js 20+** and npm — for the React frontend
- **Docker** — Aspire runs PostgreSQL in a container during local development
- **Aspire CLI** (`aspire`) — comes with the .NET Aspire workload; verify with `aspire --version`

## First-time setup

```bash
npm install
dotnet restore   # restores api, apphost, and test projects
```

### DevPod (recommended dev environment)

The repo ships a devcontainer. Canonical names:

```bash
devpod up .
devpod ssh intervals                 # or: docker exec -it intervals-devcontainer bash
```

## Running the app

The canonical way to run everything (PostgreSQL + API + web frontend) is Aspire:

```bash
aspire run
```

Open the dashboard URL printed at startup. The `web` resource is the React app;
the `api` resource is the backend. The API auto-applies EF Core migrations on
startup and Aspire injects the PostgreSQL connection string.

The frontend talks to the API through the same origin via the Vite dev proxy,
which forwards `/api` and `/auth` to the `api` resource.

A **Mailpit** resource (`mailpit`) also starts automatically in run mode and
captures every outbound auth email — email-verification and password-reset
messages land here instead of being sent to a real mailbox. Open the Mailpit
web UI from the Aspire dashboard (or directly at `http://localhost:8025`;
SMTP listens on `1025`). The AppHost injects `Mail__Smtp__Host` and
`Mail__Smtp__Port` from Mailpit for you, so no email configuration is needed
for local development. Mailpit is dev-only — it does **not** start during the
distributed tests.

### Running parts in isolation

```bash
npm run dev                              # frontend only (Vite, port 5173)
dotnet run --project api/Intervals.Api   # API only (needs ConnectionStrings:intervalsdb)
```

## Authentication & required provider secrets

The app requires authentication. Anonymous requests to `/api/*` return `401`.
Login uses **Google**, **Microsoft**, and **X** OAuth, plus email + password
accounts (which verify their email and can recover via password reset). Each
OAuth provider needs its own credentials, stored in .NET **user secrets**
(never committed). The app runs without them, but the social-login buttons
will not complete a real provider flow until they are set. Email/password
sign-in, verification, and password reset work without any provider secrets.

| Provider  | User-secrets key                     | Where it comes from          |
|-----------|--------------------------------------|------------------------------|
| Google    | `Authentication:Google:ClientId`     | Google Cloud Console — OAuth client ID |
| Google    | `Authentication:Google:ClientSecret` | Google Cloud Console — OAuth client secret |
| Microsoft | `Authentication:Microsoft:ClientId`     | Azure — Microsoft Entra ID app registration client ID |
| Microsoft | `Authentication:Microsoft:ClientSecret` | Azure — Microsoft Entra ID app registration client secret |
| X         | `Authentication:X:ClientId`          | X developer portal — OAuth 2.0 client id |
| X         | `Authentication:X:ClientSecret`      | X developer portal — OAuth 2.0 client secret |

> The PostgreSQL password (`Parameters:postgres-password`) is auto-generated and
> auto-persisted by Aspire into the AppHost's user secrets — you do not set it.

### Register the redirect URIs

At each provider, register the API callback URI. It is the **HTTP endpoint URL of
the `api` resource** (shown in the Aspire dashboard when the app is running) plus
the callback path:

```
http://localhost:<api-port>/auth/callback/google     # Google
http://localhost:<api-port>/auth/callback/microsoft   # Microsoft
http://localhost:<api-port>/auth/callback/x           # X
```

Find the current `<api-port>`:

```bash
aspire describe --format Json \
  | python3 -c "import sys,json;d=json.load(sys.stdin);print([r['urls'][0]['url'] for r in d['resources'] if r.get('resourceType')=='Project'][0])"
```

The port can change between runs. To keep redirect URIs stable, pin the API HTTP
port in `apphost/Program.cs`:

```csharp
.WithHttpEndpoint(name: "http", port: 5199, targetPort: 8080)
```

### Google setup

1. Google Cloud Console → **APIs & Services → OAuth consent screen** → External,
   add yourself under **Test users**.
2. **Credentials → Create credentials → OAuth client ID → Web application**.
3. Add `http://localhost:<api-port>/auth/callback/google` to **Authorized
   redirect URIs**.
4. Copy the Client ID and Client Secret.

### Microsoft setup

1. Azure portal → **Microsoft Entra ID → App registrations → New registration**.
2. Pick **Accounts in any Microsoft Entra ID directory (Any Microsoft Entra ID
   tenant - Multitenant)** or **Personal Microsoft accounts only**, depending on
   who should sign in. Leave **Redirect URI** for now.
3. Under **Manage → Authentication → Add a platform → Web**, add
   `https://localhost:<api-port>/auth/callback/microsoft` as a redirect URI.
4. Under **Manage → Certificates & secrets → New client secret**, create a
   secret and copy its **Value** immediately (Microsoft hides it later).
5. Copy the **Application (client) ID** from the Overview page.
6. The app requests only the `openid`, `email`, and `profile` scopes (no
   `offline_access`) — no extra API permissions need to be granted.

### X setup

1. X developer portal → your app → **User authentication settings**.
2. Enable **OAuth 2.0** (Web App) and turn on **Authorize apps with PKCE**
   (the app uses PKCE). Scopes used: `users.read`, `users.email`.
3. Set the redirect URI to `http://localhost:<api-port>/auth/callback/x`.
4. Set the **Website URL** to a complete public `https://` URL, such as the
   production/staging site or public project page. X may reject `localhost` here
   even when the callback URI is local.
5. Copy the OAuth 2.0 Client ID and Client Secret. (X email availability varies,
   so the app treats email as optional and keys on the stable X user id.)

### Store secrets locally

```bash
PROJECT=api/Intervals.Api
dotnet user-secrets set "Authentication:Google:ClientId"       "<google-client-id>"       --project $PROJECT
dotnet user-secrets set "Authentication:Google:ClientSecret"   "<google-client-secret>"   --project $PROJECT
dotnet user-secrets set "Authentication:Microsoft:ClientId"    "<microsoft-client-id>"    --project $PROJECT
dotnet user-secrets set "Authentication:Microsoft:ClientSecret" "<microsoft-client-secret>" --project $PROJECT
dotnet user-secrets set "Authentication:X:ClientId"            "<x-client-id>"            --project $PROJECT
dotnet user-secrets set "Authentication:X:ClientSecret"        "<x-client-secret>"        --project $PROJECT
```

Verify: `dotnet user-secrets list --project api/Intervals.Api`

Secrets are read at API startup — restart Aspire after changing them
(`aspire stop` then `aspire run`).

### What fails when secrets are missing or still dummy

- A provider whose `ClientId` is empty is **not registered**; its login button
  redirects back to `/login?auth=unknown`.
- A `ClientId` still set to the **dummy placeholder** starts the provider
  challenge but is rejected — e.g. Google shows
  `Error 401: invalid_client` / "The OAuth client was not found". Replace it with
  a real client id from the provider console.
- Microsoft returns `invalid_client` / `AADSTS7000215` if the client secret is
  wrong, expired, or the **Value** (not the **Secret ID**) was not copied.
- Email/password sign-up, email verification, and password reset do **not**
  need any OAuth secrets — they work as soon as Mailpit is running.

### Account security flows

These flows work without any OAuth provider secrets; they only need Mailpit
running (automatic under `aspire run`).

- **Email verification** — registering a password account sends a verification
  email with a 24h token (`Mail:VerificationTokenLifetimeHours`). Clicking the
  link hits `GET /auth/email-verification/confirm?token=...`. Unverified users
  see a banner with a resend button that calls the authenticated, rate-limited
  `POST /auth/email-verification/request`, which always returns generic success
  (no user enumeration).
- **Password reset** — `/forgot-password` (`POST /auth/password/forgot`,
  anonymous, rate-limited, generic success, no user enumeration) emails a 1h
  token (`Mail:PasswordResetTokenLifetimeHours`); `/reset-password` consumes it
  via `POST /auth/password/reset`. A successful reset clears lockout, rotates
  the security stamp (invalidating other sessions), and marks the email
  verified.
- **Account settings (`/account-settings`)** — signed-in users manage their
  sign-in methods: add a password to a social-only account, change password,
  link more providers (Google/Microsoft/X) via an OAuth challenge, and unlink a
  provider. Unlinking is blocked when it would leave the account with no
  remaining sign-in method.
- **Safe account merge** — if a provider being linked is already attached to
  another account, the app asks the user to explicitly confirm a merge (proof
  of control of both accounts). Matching emails **never** auto-merge; merge is
  always an explicit, confirmed action.

## Testing

```bash
npm test                                              # frontend (Vitest)
dotnet test tests/Intervals.Api.Tests                 # API integration (Testcontainers Postgres)
dotnet test tests/Intervals.AppHost.Tests             # Aspire distributed smoke
```

- Frontend tests run against jsdom with mocked `fetch`.
- API integration tests spin up a disposable PostgreSQL container, so Docker must
  be running. They cover anonymous rejection, authenticated session/status,
  account creation and reuse, no email auto-merge, auth-event audit, and logout.
- The distributed test launches the real AppHost (PostgreSQL + API) and asserts
  the auth boundary end-to-end.

## npm scripts

```bash
npm run dev       # Vite dev server (host 0.0.0.0, port 5173)
npm run build     # type-check (tsc -b) + production build to dist/
npm run preview   # preview the production build
npm run test      # run Vitest once
npm run test:watch
```

## Troubleshooting

- **`28P01: password authentication failed for user "postgres"`** — a stale
  persistent PostgreSQL volume holds a different password than the current run.
  Remove the volume and let Aspire recreate it:
  `docker volume rm intervals-postgres-data`, then `aspire run` again.
- **`invalid_client` from Google/Microsoft/X** — provider secret is missing,
  still the dummy placeholder, or (for Microsoft) the secret **Value** was not
  copied; see [Authentication & required provider secrets](#authentication--required-provider-secrets).
- **No verification / reset email arrives** — under `aspire run` these are
  captured by Mailpit; open the `mailpit` resource in the Aspire dashboard
  (or `http://localhost:8025`). Running the API outside Aspire requires
  `Mail__Smtp__Host`/`Mail__Smtp__Port` (and the rest of the `Mail:*` overrides)
  to point at a real SMTP server.
- **Login works but redirects to the wrong origin** — `Web__BaseUrl` is injected
  by the AppHost in run mode; if you run the API outside Aspire, set it yourself.

## Production notes

Before a public launch: move provider secrets from user secrets into the hosting
platform's managed secret store, register the production-origin callback URIs
(`https://<origin>/auth/callback/google`, `.../microsoft`, `.../x`), persist
ASP.NET Core data-protection keys, and run the API behind HTTPS with forwarded
headers configured. Replace the local Mailpit SMTP target with a real
transactional email provider by setting `Mail:Smtp:Host`/`Port` (and
`Mail:FromAddress`/`FromName`/`AppBaseUrl`) in the hosting environment.
