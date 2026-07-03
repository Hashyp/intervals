# Social Login — Required Secrets & Setup

Status: Draft
Date: 2026-07-03
Applies to branch: `feature/social-login`

This document lists every external secret/credential the social-login feature
needs, where to get it, where to store it locally, and what fails when it is
missing.

> Secrets are never committed. They live in .NET user secrets during local
> development and must move to the hosting platform's managed secret store
> before a public production launch.

## Required secrets at a glance

| Provider | User-secrets key | Value source | Required for |
|----------|------------------|--------------|--------------|
| Google | `Authentication:Google:ClientId` | Google Cloud Console — OAuth client ID | Google login |
| Google | `Authentication:Google:ClientSecret` | Google Cloud Console — OAuth client secret | Google login |
| X       | `Authentication:X:ClientId`        | X developer portal — OAuth 2.0 client id | X login |
| X       | `Authentication:X:ClientSecret`    | X developer portal — OAuth 2.0 client secret | X login |

The PostgreSQL password (`Parameters:postgres-password`) is **auto-generated
and auto-persisted** by Aspire into the AppHost's user secrets; you do not set
it manually.

## What you must register at each provider

Each provider needs an **authorized redirect URI** that exactly matches what the
API sends. The app uses the ASP.NET Core external-provider callback path, so the
URI is:

```
http://localhost:<api-port>/auth/callback/google   # Google
http://localhost:<api-port>/auth/callback/x         # X
```

`<api-port>` is the **HTTP endpoint URL of the `api` resource** shown in the
Aspire dashboard / `aspire describe` when you run `aspire run`.

### Finding the exact `<api-port>`

While the app is running:

```bash
aspire describe --format Json \
  | python3 -c "import sys,json;d=json.load(sys.stdin);print([r['urls'][0]['url'] for r in d['resources'] if r.get('resourceType')=='Project'][0])"
```

That prints something like `http://localhost:41921`. Your redirect URIs are then:

```
http://localhost:41921/auth/callback/google
http://localhost:41921/auth/callback/x
```

Tip: the port can change between runs. To keep redirect URIs stable (so you do
not have to re-register them), pin the API HTTP port in `apphost/Program.cs`:

```csharp
.WithHttpEndpoint(name: "http", port: 5199, target: 8080)
```

## Google setup

1. Open Google Cloud Console → **APIs & Services → OAuth consent screen**.
   - User type: **External**.
   - Add yourself (and any testers) under **Test users**.
2. Go to **APIs & Services → Credentials → Create credentials → OAuth client ID**.
   - Application type: **Web application**.
   - Under **Authorized redirect URIs**, add:
     `http://localhost:<api-port>/auth/callback/google`
3. Copy the **Client ID** and **Client secret**.

## X (Twitter) setup

1. Open the X developer portal → your app → **User authentication settings**.
2. Enable **OAuth 2.0**, type **Web App**, and turn on **Authorize apps with
   PKCE**.
3. Scopes requested by the app: `users.read`, `users.email`.
4. Set the **Callback / Redirect URI** to:
   `http://localhost:<api-port>/auth/callback/x`
5. Copy the **OAuth 2.0 Client ID** and **Client Secret**.

Note: X email availability varies by app/user, so the app treats email as
optional and uses the stable X user id as the durable identity.

## Storing secrets locally (user secrets)

The API project already has a `UserSecretsId`. Set each value with:

```bash
PROJECT=api/Intervals.Api

dotnet user-secrets set "Authentication:Google:ClientId"     "<google-client-id>"     --project $PROJECT
dotnet user-secrets set "Authentication:Google:ClientSecret" "<google-client-secret>" --project $PROJECT
dotnet user-secrets set "Authentication:X:ClientId"          "<x-client-id>"          --project $PROJECT
dotnet user-secrets set "Authentication:X:ClientSecret"      "<x-client-secret>"      --project $PROJECT
```

These are stored under `~/.microsoft/usersecrets/<id>/secrets.json`, outside the
repo, so they are never committed.

## Verifying they are set

```bash
dotnet user-secrets list --project api/Intervals.Api
```

You should see all four `Authentication:*` keys (values will be redacted/hidden
depending on your shell).

## What happens when a secret is missing

The API only registers a provider when its `ClientId` is non-empty. So:

- If **both** providers are missing/empty: the login buttons redirect back to
  `/login?auth=unknown`.
- If a `ClientId` is the **dummy placeholder** (`dummy-google-client-id`, etc.):
  the provider challenge starts, but the provider rejects the request — e.g.
  Google shows `Error 401: invalid_client` / "The OAuth client was not found".
  Replace the dummy with a real client id from the provider console.

## Restart after changing secrets

User secrets are read at API startup. After setting new values:

```bash
aspire stop
aspire run
```

## Production note

For a public launch, replace user secrets with the deployment platform's managed
secret store (e.g. Azure Key Vault, container secrets, etc.) and register the
provider redirect URIs under the final production origin:

```
https://<production-origin>/auth/callback/google
https://<production-origin>/auth/callback/x
```
