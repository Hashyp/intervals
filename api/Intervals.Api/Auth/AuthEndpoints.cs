using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Intervals.Api.Data.Entities;
using Intervals.Api.Email;

namespace Intervals.Api.Auth;

public static class AuthEndpoints
{
    public static WebApplication MapAuthEndpoints(this WebApplication app)
    {
        var webBaseUrl = app.Configuration["Web:BaseUrl"] ?? string.Empty;

        var group = app.MapGroup("/auth");

        group.MapPost("/login/{provider}", async (string provider, HttpContext context) =>
        {
            var authOptions = context.RequestServices.GetRequiredService<IOptions<AuthOptions>>().Value;
            var normalized = AuthProviderNames.Normalize(provider);

            if (!AuthProviderNames.IsValid(normalized))
            {
                context.Response.Redirect(AuthRedirect.Build(webBaseUrl, authOptions.LoginPath, AuthResultCodes.Unknown));
                return;
            }

            var form = await context.Request.ReadFormAsync(context.RequestAborted);
            var returnUrl = ReturnUrlValidator.Sanitize(form["returnUrl"].ToString());
            var rememberMe = IsRememberMe(form["rememberMe"].ToString());
            var scheme = AuthProviderNames.ToScheme(normalized);

            var schemeProvider = context.RequestServices.GetRequiredService<IAuthenticationSchemeProvider>();
            if (scheme is null || await schemeProvider.GetSchemeAsync(scheme) is null)
            {
                context.Response.Redirect(AuthRedirect.Build(webBaseUrl, authOptions.LoginPath, AuthResultCodes.Unknown));
                return;
            }

            var properties = new AuthenticationProperties
            {
                RedirectUri = $"/auth/complete/{normalized}",
            };
            properties.Items["returnUrl"] = returnUrl;
            properties.Items["rememberMe"] = rememberMe.ToString();
            properties.Items["provider"] = normalized;

            await context.ChallengeAsync(scheme, properties);
        }).AllowAnonymous().RateLimit();

        group.MapPost("/login/password", async (
            HttpContext context,
            IAntiforgery antiforgery,
            IPasswordAccountService passwordAccounts,
            IOptions<AuthOptions> authOptions,
            CancellationToken cancellationToken) =>
        {
            try
            {
                await antiforgery.ValidateRequestAsync(context);
            }
            catch (AntiforgeryValidationException)
            {
                return Results.BadRequest(new ApiError(
                    AuthResultCodes.InvalidRequest,
                    "Antiforgery validation failed.",
                    context.GetCorrelationId()));
            }

            var request = await context.Request.ReadFromJsonAsync<PasswordLoginRequest>(cancellationToken);
            if (request is null
                || string.IsNullOrWhiteSpace(request.Email)
                || string.IsNullOrWhiteSpace(request.Password))
            {
                return Results.BadRequest(new ApiError(
                    AuthResultCodes.InvalidRequest,
                    "Email and password are required.",
                    context.GetCorrelationId()));
            }

            var correlationId = context.GetCorrelationId();
            var result = await passwordAccounts.AuthenticateAsync(
                request.Email,
                request.Password,
                correlationId,
                cancellationToken);

            if (result.Success && result.User is not null)
            {
                var principal = BuildAppPrincipal(result.User);
                var rememberMe = request.RememberMe;
                var properties = new AuthenticationProperties
                {
                    IsPersistent = rememberMe,
                    ExpiresUtc = DateTimeOffset.UtcNow.Add(
                        rememberMe ? authOptions.Value.RememberMeLifetime : authOptions.Value.SessionLifetime),
                };

                await context.SignInAsync(AuthExtensions.AppCookieScheme, principal, properties);
                return Results.Ok(new PasswordAuthSuccess());
            }

            return result.FailureCode switch
            {
                AuthResultCodes.InvalidCredentials => Results.Json(
                    new ApiError(AuthResultCodes.InvalidCredentials, "Invalid credentials.", correlationId),
                    statusCode: StatusCodes.Status401Unauthorized),
                AuthResultCodes.LockedOut => Results.Json(
                    new ApiError(AuthResultCodes.LockedOut, "Account is locked.", correlationId),
                    statusCode: StatusCodes.Status423Locked),
                _ => Results.BadRequest(new ApiError(
                    AuthResultCodes.InvalidRequest,
                    "Login failed.",
                    correlationId)),
            };
        }).AllowAnonymous().RateLimit();

        group.MapPost("/register", async (
            HttpContext context,
            IAntiforgery antiforgery,
            IPasswordAccountService passwordAccounts,
            AuthActionTokenService tokens,
            IEmailSender emailSender,
            IOptions<EmailOptions> emailOptions,
            CancellationToken cancellationToken) =>
        {
            try
            {
                await antiforgery.ValidateRequestAsync(context);
            }
            catch (AntiforgeryValidationException)
            {
                return Results.BadRequest(new ApiError(
                    AuthResultCodes.InvalidRequest,
                    "Antiforgery validation failed.",
                    context.GetCorrelationId()));
            }

            var request = await context.Request.ReadFromJsonAsync<RegisterRequest>(cancellationToken);
            if (request is null
                || string.IsNullOrWhiteSpace(request.Email)
                || string.IsNullOrWhiteSpace(request.Password))
            {
                return Results.BadRequest(new ApiError(
                    AuthResultCodes.InvalidRequest,
                    "Email and password are required.",
                    context.GetCorrelationId()));
            }

            if (request.Email.Length > PasswordAccountService.MaxEmailLength)
            {
                return Results.BadRequest(new ApiError(
                    AuthResultCodes.InvalidRequest,
                    "Email is too long.",
                    context.GetCorrelationId()));
            }

            var correlationId = context.GetCorrelationId();
            var result = await passwordAccounts.RegisterAsync(
                request.Email,
                request.Password,
                correlationId,
                cancellationToken);

            if (result.Success && result.User is not null)
            {
                var principal = BuildAppPrincipal(result.User);
                await context.SignInAsync(AuthExtensions.AppCookieScheme, principal);
                await SendVerificationEmailAsync(
                    result.User,
                    tokens,
                    emailSender,
                    emailOptions.Value,
                    context.GetCorrelationId(),
                    cancellationToken);
                return Results.Ok(new PasswordAuthSuccess());
            }

            return result.FailureCode switch
            {
                AuthResultCodes.WeakPassword => Results.BadRequest(new ApiError(
                    AuthResultCodes.WeakPassword,
                    "Password does not meet requirements.",
                    correlationId)),
                AuthResultCodes.EmailTaken => Results.Json(
                    new ApiError(AuthResultCodes.EmailTaken, "Email is already registered.", correlationId),
                    statusCode: StatusCodes.Status409Conflict),
                _ => Results.BadRequest(new ApiError(
                    AuthResultCodes.InvalidRequest,
                    "Registration failed.",
                    correlationId)),
            };
        }).AllowAnonymous().RateLimit();

        group.MapGet("/complete/{provider}", async (
            string provider,
            HttpContext context,
            IAccountService accounts,
            IExternalProfileBuilder profileBuilder,
            IOptions<AuthOptions> authOptions,
            CancellationToken cancellationToken) =>
        {
            var loginPath = authOptions.Value.LoginPath;
            var normalized = AuthProviderNames.Normalize(provider);

            var authenticateResult = await context.AuthenticateAsync(AuthExtensions.ExternalCookieScheme);

            if (!AuthProviderNames.IsValid(normalized)
                || !authenticateResult.Succeeded
                || authenticateResult.Principal is null)
            {
                await context.SignOutAsync(AuthExtensions.ExternalCookieScheme);
                return Results.Redirect(AuthRedirect.Build(webBaseUrl, loginPath, AuthResultCodes.Unknown));
            }

            ExternalUserProfile profile;
            try
            {
                profile = profileBuilder.Build(normalized, authenticateResult.Principal);
            }
            catch (InvalidOperationException)
            {
                await context.SignOutAsync(AuthExtensions.ExternalCookieScheme);
                return Results.Redirect(AuthRedirect.Build(webBaseUrl, loginPath, AuthResultCodes.ProviderError));
            }

            var correlationId = context.GetCorrelationId();
            var result = await accounts.LoginAsync(profile, correlationId, cancellationToken);

            var rememberMe = GetRememberMe(authenticateResult.Properties);
            var returnUrl = GetReturnUrl(authenticateResult.Properties);

            var principal = BuildAppPrincipal(result.User);
            var properties = new AuthenticationProperties
            {
                IsPersistent = rememberMe,
                ExpiresUtc = DateTimeOffset.UtcNow.Add(
                    rememberMe ? authOptions.Value.RememberMeLifetime : authOptions.Value.SessionLifetime),
            };

            await context.SignInAsync(AuthExtensions.AppCookieScheme, principal, properties);
            await context.SignOutAsync(AuthExtensions.ExternalCookieScheme);

            return Results.Redirect(AuthRedirect.Build(webBaseUrl, returnUrl));
        }).AllowAnonymous().RateLimit();

        group.MapPost("/logout", async (
            HttpContext context,
            IAccountService accounts,
            IAntiforgery antiforgery,
            IOptions<AuthOptions> authOptions,
            CancellationToken cancellationToken) =>
        {
            try
            {
                await antiforgery.ValidateRequestAsync(context);
            }
            catch (AntiforgeryValidationException)
            {
                return Results.BadRequest(new ApiError("antiforgery_failed", "Antiforgery validation failed.", context.GetCorrelationId()));
            }

            var userId = CurrentUser.GetUserId(context.User);
            await accounts.RecordLogoutAsync(userId, context.GetCorrelationId(), cancellationToken);
            await context.SignOutAsync(AuthExtensions.AppCookieScheme);
            return Results.Redirect(AuthRedirect.Build(webBaseUrl, authOptions.Value.LoginPath));
        }).RequireAuthorization().RateLimit();

        group.MapGet("/antiforgery-token", (HttpContext context, IAntiforgery antiforgery) =>
        {
            var tokens = antiforgery.GetAndStoreTokens(context);
            return Results.Json(new { token = tokens.RequestToken });
        }).AllowAnonymous();

        app.MapGet("/api/session", async (
            HttpContext context,
            IAccountService accounts,
            CancellationToken cancellationToken) =>
        {
            var userId = CurrentUser.GetUserId(context.User);
            if (userId is null)
            {
                return Results.Unauthorized();
            }

            var user = await accounts.GetAsync(userId.Value, cancellationToken);
            if (user is null)
            {
                return Results.Unauthorized();
            }

            var linked = await accounts.GetLinkedProvidersAsync(userId.Value, cancellationToken);

            var providers = new List<ProviderStatus>
            {
                new(AuthProviderNames.Google, "Google", linked.Contains(AuthProviderNames.Google)),
                new(AuthProviderNames.Microsoft, "Microsoft", linked.Contains(AuthProviderNames.Microsoft)),
                new(AuthProviderNames.X, "X", linked.Contains(AuthProviderNames.X)),
                new(AuthProviderNames.Password, "Email", user.PasswordCredential is not null),
            };

            var emailVerified = user.PasswordCredential?.EmailVerified == true
                || user.ExternalLogins.Any(e => e.EmailVerified);

            return Results.Ok(new SessionResponse(
                new SessionUser(user.Id.ToString(), user.DisplayName, user.Email, user.AvatarUrl, emailVerified),
                providers));
        }).RequireAuthorization().RateLimit();

        return app;
    }

    private static bool IsRememberMe(string? value) =>
        !string.IsNullOrWhiteSpace(value)
        && (string.Equals(value, "true", StringComparison.OrdinalIgnoreCase)
            || string.Equals(value, "on", StringComparison.OrdinalIgnoreCase)
            || string.Equals(value, "yes", StringComparison.OrdinalIgnoreCase));

    private static bool GetRememberMe(AuthenticationProperties? properties) =>
        bool.TryParse(properties?.Items?["rememberMe"], out var rememberMe) && rememberMe;

    private static string GetReturnUrl(AuthenticationProperties? properties) =>
        ReturnUrlValidator.Sanitize(properties?.Items?["returnUrl"]);

    private static ClaimsPrincipal BuildAppPrincipal(AppUser user)
    {
        var identity = new ClaimsIdentity(
            new[]
            {
                new Claim(CurrentUser.UserIdClaimType, user.Id.ToString()),
                new Claim(ClaimTypes.Name, string.IsNullOrWhiteSpace(user.DisplayName) ? "Intervals user" : user.DisplayName),
            },
            AuthExtensions.AppCookieScheme);

        if (!string.IsNullOrWhiteSpace(user.Email))
        {
            identity.AddClaim(new Claim(ClaimTypes.Email, user.Email));
        }

        return new ClaimsPrincipal(identity);
    }

    private static async Task SendVerificationEmailAsync(
        AppUser user,
        AuthActionTokenService tokens,
        IEmailSender emailSender,
        EmailOptions emailOptions,
        string? correlationId,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(user.Email))
        {
            return;
        }

        string rawToken;
        try
        {
            rawToken = await tokens.IssueAsync(
                user.Id,
                AuthActionTokenPurpose.EmailVerification,
                user.Email,
                TimeSpan.FromHours(emailOptions.VerificationTokenLifetimeHours),
                correlationId,
                cancellationToken);
        }
        catch
        {
            return;
        }

        var trimmedBase = (emailOptions.AppBaseUrl ?? string.Empty).TrimEnd('/');
        var verificationLink = $"{trimmedBase}/auth/email-verification/confirm?token={rawToken}";
        var (subject, html, text) = EmailTemplates.EmailVerification(user.DisplayName, verificationLink);

        try
        {
            await emailSender.SendEmailAsync(user.Email, subject, html, text, cancellationToken);
        }
        catch
        {
            // Best-effort: a mail delivery failure must not break registration.
        }
    }
}
