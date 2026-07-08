# Auth Improvements — Progress & Handoff

Branch: `feat/auth-improvements` (forked from `master` @ `16f01f2`)
Source spec: `docs/improvements.html` (9 findings: P1×3, P2×4, P3×2)
Last updated: 2026-07-08

## How to verify the current state
```bash
dotnet build api/Intervals.Api/Intervals.Api.csproj
dotnet test tests/Intervals.Api.Tests/Intervals.Api.Tests.csproj   # 140 passing
npm test                                                           # 79 passing
npx tsc --noEmit                                                   # clean
```

## Status at a glance
| Finding | Priority | Status |
|---|---|---|
| 1.2 Antiforgery on OAuth login/link starts | P1 | ✅ committed (Wave 1) |
| 1.3 Server-side pending-merge cookie expiry | P1 | ✅ committed (Wave 1) |
| 1.4 Partitioned rate limits | P1 | ✅ committed (Wave 1) |
| 1.5 Safe forwarded-headers (opt-in trust) | P2 | ✅ committed (Wave 1) |
| 2.1 Consolidate remember-me | P2 | ✅ committed (Wave 1) |
| 2.5 Scrub reset token from URL | P2 | ✅ committed (Wave 1) |
| 2.6 Provider availability (backend endpoint + frontend) | P3 | ✅ committed (Wave 1) |
| 2.4 CSS polish + banner styles + focus-visible | P3 | ✅ committed (Wave 1) |
| 1.1 Centralize auth events / email normalization / antiforgery helper | P2 | ⚠️ **done, UNCOMMITTED** (Wave 2 BE) |
| 2.3 Extract shared UI primitives + refactor pages | P3 | ⚠️ **done, UNCOMMITTED** (Wave 2 FE) |

## What's committed
Commit `ca7652f` "feat(auth): antiforgery, partitioned rate limits, merge expiry, and UI consolidation" contains all of Wave 1 (findings 1.2, 1.3, 1.4, 1.5, 2.1, 2.4, 2.5, 2.6). Backend + frontend tests green at that commit (130 backend / 79 frontend).

### Cross-stream contracts (in effect, relied upon by tests)
- **Antiforgery form field**: native social/link forms include `<input type="hidden" name="__RequestVerificationToken" value={token} />`. Token preloaded via existing `getAntiforgeryToken()` in `src/auth/sessionApi.ts`. Backend `IAntiforgery.ValidateRequestAsync` accepts the default form field OR the `X-CSRF-TOKEN` header. Buttons disabled while token loads.
- **Provider availability**: `GET /api/auth/providers` (anonymous) → `{ providers: [{ id: "google"|"microsoft"|"x", available: boolean }] }` based on registered auth schemes. Frontend hides unavailable providers (fail-open on fetch error).
- **CSS class names** (defined in `src/styles.css`, used by pages): `.auth-providers`, `.auth-providers__item`, `.auth-form--inline`, `.auth-form-actions`, `.auth-merge-accounts`, `.account-bar__verify` (+ `-text`/`-button`/`-error`).
- **Rate-limit policies**: names unchanged (`auth`, `verification`, `password-reset`); now partitioned by authenticated user id, else client IP.
- **Forwarded headers**: trust-all only in Development/Testing or when `Auth:ForwardedHeaders:TrustAll=true`; production is loopback-only unless `Auth:ForwardedHeaders:KnownProxies` (CSV) is set.

## What's done but UNCOMMITTED (Wave 2 BE — finding 1.1)
The backend centralization refactor is complete and green (140 backend tests, +10 new helper tests). **Recommended: commit this before continuing.**

New files:
- `api/Intervals.Api/Auth/IAuthEventRecorder.cs` + `AuthEventRecorder.cs` — scoped service replacing duplicated `RecordAsync` private methods.
- `api/Intervals.Api/Auth/AuthEmail.cs` — `Normalize(string? email, int maxLength = 320)` replacing duplicated `NormalizeEmail`.
- `api/Intervals.Api/Auth/AuthRequests.cs` — `ValidateAntiforgeryAsync` returning `IResult?` replacing repeated try/catch blocks.
- `tests/Intervals.Api.Tests/AuthHelpersTests.cs` — 10 pure-helper tests.

Refactored (behavior-preserving): `AccountService.cs`, `PasswordAccountService.cs`, `PasswordResetService.cs`, `AccountSettingsService.cs`, `ProviderLinkingService.cs`, `AuthActionTokenService.cs`, `AuthEndpoints.cs`, `AccountSettingsEndpoints.cs`, `PasswordResetEndpoints.cs`, `EmailVerificationEndpoints.cs`, `ProviderLinkingEndpoints.cs`, `AuthExtensions.cs` (DI registration).

Intentionally NOT refactored (correctly, to preserve behavior):
- `AccountMergeService.cs` — its inline `AuthEvent` adds are inside one DB transaction with a shared timestamp + single `SaveChangesAsync`; routing through the recorder would split saves and change timing. Leave as-is.
- `/auth/logout` antiforgery block — uniquely uses error code `"antiforgery_failed"` (not `AuthResultCodes.InvalidRequest`); leave as-is.

## What's done but UNCOMMITTED (Wave 2 FE — finding 2.3)
The 5 shared UI primitives are now wired into all auth pages. The refactor is behavior-preserving and produces identical observable DOM (same `className`s, `<form method/action>`, hidden inputs, button text/`role`s, `role="alert"`/`role="status"` messages, and `htmlFor`/`id` linkages). Native provider/link forms stayed native (not converted to fetch). Verified green: `npx tsc --noEmit` clean, `npm test` = 79 passing, `dotnet test` = 140 passing.

Primitives (untracked, created in the prior session, now IMPORTED by the pages):
- `src/auth/ui/AuthCard.tsx`
- `src/auth/ui/AuthField.tsx`
- `src/auth/ui/AuthMessage.tsx`
- `src/auth/ui/PasswordConfirmFields.tsx`
- `src/auth/ui/SubmitButton.tsx`

Pages refactored to use the primitives:
- `src/auth/LoginPage.tsx` — `AuthCard` (branded title), `AuthField`, `PasswordConfirmFields` (register mode), `AuthMessage`, `SubmitButton` (incl. social provider buttons via `className`).
- `src/auth/ForgotPasswordPage.tsx` — `AuthCard`, `AuthField`, `AuthMessage`, `SubmitButton`.
- `src/auth/ResetPasswordPage.tsx` — `AuthCard` (both no-token and main branches), `PasswordConfirmFields`, `AuthMessage`, `SubmitButton`.
- `src/auth/MergeConfirmPage.tsx` — `AuthCard` (all 3 branches), `AuthMessage`. Subtitle inlined as a child `<p>` to preserve exact whitespace; action buttons kept as plain `type="button"`.
- `src/auth/AccountSettingsPage.tsx` — `AuthCard`, `AuthField`, `PasswordConfirmFields` (change + add forms; change form passes `passwordName="newPassword"`), `AuthMessage`, `SubmitButton` (link buttons; unlink buttons stay plain `type="button"`).

Intentionally NOT converted to `SubmitButton` (correctly, to preserve DOM — `SubmitButton` forces `type="submit"`):
- Unlink buttons (`type="button"`, conditional label from busy state, not a pending boolean).
- Merge confirm/cancel buttons (`type="button"`).
- Mode-toggle "Create account"/"Back to sign in" buttons (`type="button"`).
- `EmailVerificationBanner` — a `<span>` banner using `account-bar__verify*` classes, not a card; `AuthMessage` renders `<p className="auth-message">`, so it is not applicable. Left as-is (its inline styles were already moved out in Wave 1 / 2.4).

### Verification
```bash
npx tsc --noEmit                                                   # clean
npm test                                                           # 79 passing
dotnet test tests/Intervals.Api.Tests/Intervals.Api.Tests.csproj   # 140 passing
```

## Suggested session plan for pickup
1. `git checkout feat/auth-improvements` (verify on this branch).
2. Commit the uncommitted BE-REFACTOR (1.1) — verified green (140 tests). Suggested message: `refactor(auth): centralize auth events, email normalization, and antiforgery helper`.
3. Commit the FE-REFACTOR (2.3) — verified green (79 tests, tsc clean). Suggested message: `refactor(auth): extract shared auth UI primitives`.
4. Merge `feat/auth-improvements` into `master` and push when ready.

All implementation work from `docs/improvements.html` is now complete (9/9 findings). The only remaining steps are commit/merge, which were intentionally not performed (commits require explicit user request).

## Notes / non-goals from the spec (already decided)
- SameSite=Strict: dropped (Lax is correct for OAuth callbacks).
- Antiforgery cookie HttpOnly: kept (token fetched via JSON endpoint, not `document.cookie`).
- Missing-provider buttons: handled via availability endpoint (disable/hide), not a backend bug.

## Working-tree file inventory (uncommitted)
Modified (M) — backend, Wave 2 BE (1.1): `AccountService.cs`, `AccountSettingsEndpoints.cs`, `AccountSettingsService.cs`, `AuthActionTokenService.cs`, `AuthEndpoints.cs`, `AuthExtensions.cs`, `EmailVerificationEndpoints.cs`, `PasswordAccountService.cs`, `PasswordResetEndpoints.cs`, `PasswordResetService.cs`, `ProviderLinkingEndpoints.cs`, `ProviderLinkingService.cs`
Modified (M) — frontend, Wave 2 FE (2.3): `src/auth/LoginPage.tsx`, `src/auth/ForgotPasswordPage.tsx`, `src/auth/ResetPasswordPage.tsx`, `src/auth/MergeConfirmPage.tsx`, `src/auth/AccountSettingsPage.tsx`
Untracked (??): `api/Intervals.Api/Auth/AuthEmail.cs`, `api/Intervals.Api/Auth/AuthEventRecorder.cs`, `api/Intervals.Api/Auth/AuthRequests.cs`, `api/Intervals.Api/Auth/IAuthEventRecorder.cs`, `tests/Intervals.Api.Tests/AuthHelpersTests.cs`, `src/auth/ui/` (5 files: `AuthCard.tsx`, `AuthField.tsx`, `AuthMessage.tsx`, `PasswordConfirmFields.tsx`, `SubmitButton.tsx`), `docs/auth-improvements-progress.md`
