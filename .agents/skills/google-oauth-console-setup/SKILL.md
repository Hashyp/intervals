---
name: google-oauth-console-setup
description: Configure Google OAuth for this repository by driving Google Auth Platform in a signed-in browser, creating a Web application OAuth client, downloading the one-time client secret JSON, and importing it into the Intervals ASP.NET API user secrets. Use when Google login is missing, `invalid_client` appears, a Google OAuth redirect URI must be registered, or local development needs `Authentication:Google:ClientId` and `Authentication:Google:ClientSecret`.
---

# Google OAuth Console Setup

## Purpose

Set up Google login for local Intervals development while keeping Google account credentials and OAuth client secrets out of chat, logs, source control, and shell history.

Use a signed-in browser for Google Console work, then import the downloaded OAuth client JSON with `scripts/import-google-oauth-client-json.py`.

## Safety Rules

- Never ask for, paste, print, summarize, or commit Google passwords, cookies, MFA codes, OAuth client secrets, or downloaded `client_secret*.json` contents.
- Do not use the Codex in-app browser for Google Console setup; it does not use the user's signed-in browser profile.
- Prefer `@Chrome` through the Codex Chrome extension for Google Console automation. Use Computer Use only when Chrome integration is unavailable and the user explicitly allows it.
- Keep the user present for Google login, MFA, consent, project selection, billing, organization policy prompts, and final creation of credentials.
- Treat every downloaded `client_secret*.json` file as a secret. Prefer a path outside the repository, such as the user's Downloads folder.
- If a secret JSON file is inside the repository, do not import it unless the user explicitly accepts that risk. Ask them to move it outside the repo instead.
- For existing OAuth clients, do not assume the full client secret can be retrieved later. Google may only show/download the full secret at creation time; rotate or create a new secret if needed.

## Workflow

1. Inspect the repo context:
   - API project: `api/Intervals.Api`
   - Google callback path: `/auth/callback/google`
   - user-secrets keys:
     - `Authentication:Google:ClientId`
     - `Authentication:Google:ClientSecret`

2. Determine the local callback URI:
   - If `apphost/Program.cs` pins the API HTTP endpoint, use `http://localhost:<port>/auth/callback/google`.
   - If the API port is dynamic and Aspire is running, use `aspire describe --format Json` to find the `api` resource HTTP URL.
   - If the port is dynamic and Aspire is not running, either start Aspire or recommend pinning a stable local API port before creating the Google client.

3. Open Google Auth Platform in the user's signed-in browser:
   - Use `@Chrome` when available.
   - Navigate to the Google Auth Platform Clients page, or Google Cloud Console -> Google Auth Platform -> Clients.
   - Let the user handle Google login, MFA, project choice, billing prompts, and account/security prompts.

4. Configure the Google Auth Platform project:
   - Ensure the selected Google Cloud project is the intended project for Intervals development.
   - Configure branding if prompted. Use a local-development app name such as `Intervals Local`.
   - Configure the audience/test users as needed for local testing. Add the user's Google account as a test user when the app is external/testing.

5. Create the OAuth client:
   - Client type: `Web application`.
   - Name: `Intervals local development` or similarly specific.
   - Authorized redirect URI: the callback URI from step 2.
   - Create the client and immediately download the OAuth client JSON.
   - Do not copy the client secret into chat. Ask only for the local path to the downloaded JSON file.

6. Import the downloaded JSON:

```bash
python3 .agents/skills/google-oauth-console-setup/scripts/import-google-oauth-client-json.py \
  /path/to/client_secret.json \
  --project api/Intervals.Api \
  --expected-redirect-uri http://localhost:<api-port>/auth/callback/google
```

The importer validates that the JSON is a Google `web` OAuth client, checks the redirect URI when provided, and writes only these local .NET user-secrets keys:

- `Authentication:Google:ClientId`
- `Authentication:Google:ClientSecret`

7. Verify without leaking values:
   - Use the importer's success output.
   - Do not run `dotnet user-secrets list` in a way that prints values into the conversation.
   - Restart Aspire or the API after changing secrets.

## Failure Handling

- `redirect_uri_mismatch`: update the Google OAuth client to include the exact API callback URI, including scheme, host, port, and path.
- `invalid_client`: the imported client ID/secret is missing, wrong, deleted, rotated, or from a different OAuth client. Create or rotate a Web application client and import the newly downloaded JSON.
- The downloaded JSON has `installed` instead of `web`: create a new OAuth client with type `Web application`.
- The secret is unavailable for an existing client: rotate/add a new client secret or create a fresh Web application client, then download the JSON immediately.
- The API port keeps changing: ask before editing `apphost/Program.cs` to pin the API HTTP endpoint.

## Tool Choice

- `@Chrome`: best for signed-in Google Console work.
- Computer Use: acceptable fallback for GUI-only console work when the user approves it.
- Local script: best for importing secrets into .NET user secrets.
- MCP: only useful if a dedicated Google/Auth Platform MCP server exists; do not invent Google API capabilities that are not available.
- `gcloud auth login`: useful for authenticating Google Cloud CLI, but not sufficient by itself for creating this app's normal Google Auth Platform Web application OAuth client.
