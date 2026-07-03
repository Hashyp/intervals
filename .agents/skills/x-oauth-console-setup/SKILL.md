---
name: x-oauth-console-setup
description: Configure X OAuth 2.0 login for this repository by driving the X Developer Portal in a signed-in browser, setting the Web App redirect URI, retrieving the OAuth 2.0 Client ID and Client Secret from Keys and Tokens, and importing them into the Intervals ASP.NET API user secrets. Use when X login is missing, an X OAuth redirect URI must be registered, X provider credentials need setup, or local development needs `Authentication:X:ClientId` and `Authentication:X:ClientSecret`.
---

# X OAuth Console Setup

## Purpose

Set up X social login for local Intervals development while keeping X account credentials and OAuth client secrets out of chat, logs, source control, and shell history.

Use a signed-in browser for X Developer Portal work, then import the OAuth 2.0 Client ID and Client Secret with `scripts/set-x-oauth-user-secrets.py`.

## Safety Rules

- Never ask for, paste, print, summarize, or commit X passwords, cookies, MFA codes, OAuth client secrets, bearer tokens, access tokens, or API keys.
- Do not ask the user to paste the X Client Secret into chat.
- Prefer the local importer script because it prompts on the terminal and hides both the Client ID and Client Secret.
- Prefer `@Chrome` through the Codex Chrome extension for X Developer Portal automation. Use Computer Use only when Chrome integration is unavailable and the user explicitly allows it.
- Keep the user present for X login, MFA, app creation, paid access prompts, terms prompts, and final credential reveal/regeneration.
- If credentials are regenerated, import the new values immediately and restart Aspire or the API.

## Workflow

1. Inspect the repo context:
   - API project: `api/Intervals.Api`
   - X callback path: `/auth/callback/x`
   - user-secrets keys:
     - `Authentication:X:ClientId`
     - `Authentication:X:ClientSecret`
   - app scopes used by the API:
     - `users.read`
     - `users.email`

2. Determine the local callback URI:
   - If `apphost/Program.cs` pins the API HTTP endpoint, use `http://localhost:<port>/auth/callback/x`.
   - In this repo, the local callback URI should normally be `http://localhost:5199/auth/callback/x`.
   - If the port changes, update the X Developer Portal callback URL to match exactly.

3. Open the X Developer Portal in the user's signed-in browser:
   - Use `@Chrome` when available.
   - Navigate to `https://developer.x.com/en/portal/dashboard`.
   - Let the user handle X login, MFA, project/app selection, plan/access prompts, and terms prompts.

4. Configure the X app:
   - Select the intended project and app, or create one for local Intervals development.
   - Open the app's **User authentication settings**.
   - Enable OAuth 2.0.
   - App type: Web App, Automated App or Bot, depending on the current X portal wording. Prefer the option that creates a confidential web client with a Client Secret.
   - Enable PKCE when offered. The Intervals API uses PKCE.
   - Callback/Redirect URI: `http://localhost:5199/auth/callback/x`
   - Website URL: use a complete public `https://` URL for the app or project, such as a production/staging site, public project page, or public repository URL. Do not use `http://localhost:5173`; X may reject localhost for this required app website field even when the callback URI can be localhost.
   - Scopes/permissions: include `users.read` and `users.email`. Avoid broader scopes unless the app code requires them.
   - Save the settings.

5. Retrieve OAuth 2.0 credentials:
   - Open the app's **Keys and Tokens** page.
   - Use the OAuth 2.0 **Client ID** and **Client Secret**.
   - If the Client Secret is not visible, use the portal's regenerate/rotate action and import the new secret immediately.
   - Do not copy secrets into chat.

6. Import the credentials:

```bash
python3 .agents/skills/x-oauth-console-setup/scripts/set-x-oauth-user-secrets.py \
  --project api/Intervals.Api
```

The script prompts locally for:

- X OAuth 2.0 Client ID
- X OAuth 2.0 Client Secret

It writes only these local .NET user-secrets keys:

- `Authentication:X:ClientId`
- `Authentication:X:ClientSecret`

7. Verify without leaking values:
   - Use the importer's success output.
   - Do not run `dotnet user-secrets list` in a way that prints values into the conversation.
   - Restart Aspire or the API after changing secrets.

## Failure Handling

- `redirect_uri_mismatch` or provider redirect errors: update the X callback URL to exactly `http://localhost:5199/auth/callback/x`, including scheme, host, port, and path.
- X login starts but token exchange fails: confirm the app uses OAuth 2.0, PKCE is enabled, the client secret is current, and the app type is a confidential web app when possible.
- Missing email: X email availability varies by account and access level; the Intervals app treats email as optional and keys on the stable X user id.
- No visible Client Secret: regenerate/rotate the OAuth 2.0 Client Secret in **Keys and Tokens**, then import the new value immediately.
- `Not a valid URL format` while saving app info: replace localhost in **Website URL** with a complete public `https://` URL. Clear optional URL fields if unused; do not leave partial values such as `https://`.
- API port changes: ask before editing `apphost/Program.cs`; keep X Developer Portal redirect URI aligned with the actual API callback URL.

## Tool Choice

- `@Chrome`: best for signed-in X Developer Portal work.
- Computer Use: acceptable fallback for GUI-only portal work when the user approves it.
- Local script: best for importing X credentials into .NET user secrets.
- MCP: only useful if a dedicated X Developer Portal MCP server exists; do not invent X portal API capabilities.
