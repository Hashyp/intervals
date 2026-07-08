using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Intervals.Api.Auth;

public static class ProviderLinkingEndpoints
{
    public const string MergeConfirmPath = "/merge-confirm";

    public static IServiceCollection AddIntervalsProviderLinking(this IServiceCollection services)
    {
        services.AddScoped<IProviderLinkingService, ProviderLinkingService>();
        services.AddScoped<IAccountMergeService, AccountMergeService>();
        return services;
    }

    public static WebApplication MapProviderLinkingEndpoints(this WebApplication app)
    {
        var webBaseUrl = app.Configuration["Web:BaseUrl"] ?? string.Empty;

        var group = app.MapGroup("/auth/providers");

        group.MapPost("/link/{provider}", async (string provider, HttpContext context, IAntiforgery antiforgery) =>
        {
            if (await AuthRequests.ValidateAntiforgeryAsync(context, antiforgery) is { } antiforgeryError)
            {
                return antiforgeryError;
            }

            var authOptions = context.RequestServices.GetRequiredService<IOptions<AuthOptions>>().Value;
            var normalized = AuthProviderNames.Normalize(provider);

            if (!AuthProviderNames.IsValid(normalized))
            {
                context.Response.Redirect(AuthRedirect.Build(webBaseUrl, "/account-settings", AuthResultCodes.Unknown));
                return Results.Empty;
            }

            var userId = CurrentUser.GetUserId(context.User);
            if (userId is null)
            {
                context.Response.Redirect(AuthRedirect.Build(webBaseUrl, authOptions.LoginPath, AuthResultCodes.AuthRequired));
                return Results.Empty;
            }

            var scheme = AuthProviderNames.ToScheme(normalized);
            var schemeProvider = context.RequestServices.GetRequiredService<IAuthenticationSchemeProvider>();
            if (scheme is null || await schemeProvider.GetSchemeAsync(scheme) is null)
            {
                context.Response.Redirect(AuthRedirect.Build(webBaseUrl, "/account-settings", AuthResultCodes.Unknown));
                return Results.Empty;
            }

            var form = await context.Request.ReadFormAsync(context.RequestAborted);
            var returnUrl = ReturnUrlValidator.Sanitize(form["returnUrl"].ToString());

            var properties = new AuthenticationProperties
            {
                RedirectUri = $"/auth/providers/complete/{normalized}",
            };
            properties.Items["mode"] = "link";
            properties.Items["linkUserId"] = userId.Value.ToString();
            properties.Items["returnUrl"] = returnUrl;

            await context.ChallengeAsync(scheme, properties);
            return Results.Empty;
        }).RequireAuthorization().RateLimit();

        group.MapGet("/complete/{provider}", async (
            string provider,
            HttpContext context,
            IExternalProfileBuilder profileBuilder,
            IProviderLinkingService linking,
            IAccountMergeService merge,
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
                return Results.Redirect(AuthRedirect.Build(webBaseUrl, "/account-settings", AuthResultCodes.ProviderError));
            }

            var linkUserIdStr = authenticateResult.Properties?.Items?["linkUserId"];
            if (linkUserIdStr is null || !Guid.TryParse(linkUserIdStr, out var primaryUserId))
            {
                await context.SignOutAsync(AuthExtensions.ExternalCookieScheme);
                return Results.Redirect(AuthRedirect.Build(webBaseUrl, loginPath, AuthResultCodes.AuthRequired));
            }

            ExternalUserProfile profile;
            try
            {
                profile = profileBuilder.Build(normalized, authenticateResult.Principal);
            }
            catch (InvalidOperationException)
            {
                await context.SignOutAsync(AuthExtensions.ExternalCookieScheme);
                return Results.Redirect(AuthRedirect.Build(webBaseUrl, "/account-settings", AuthResultCodes.ProviderError));
            }

            var result = await linking.CompleteLinkAsync(
                profile,
                primaryUserId,
                context.GetCorrelationId(),
                cancellationToken);

            await context.SignOutAsync(AuthExtensions.ExternalCookieScheme);

            if (result.Outcome == LinkOutcome.Collision && result.SecondaryUserId is not null)
            {
                merge.SetPendingMerge(context, primaryUserId, result.SecondaryUserId.Value, normalized);
                return Results.Redirect(AuthRedirect.Build(webBaseUrl, MergeConfirmPath));
            }

            var returnUrl = ReturnUrlValidator.Sanitize(authenticateResult.Properties?.Items?["returnUrl"]);
            return Results.Redirect(AuthRedirect.Build(webBaseUrl, returnUrl));
        }).AllowAnonymous().RateLimit();

        group.MapGet("/pending-merge", async (
            HttpContext context,
            IAccountMergeService merge,
            CancellationToken cancellationToken) =>
        {
            var userId = CurrentUser.GetUserId(context.User);
            if (userId is null)
            {
                return Results.Unauthorized();
            }

            var detail = await merge.GetPendingMergeAsync(userId.Value, context, cancellationToken);
            return detail is null ? Results.NotFound() : Results.Ok(detail);
        }).RequireAuthorization().RateLimit();

        group.MapPost("/merge", async (
            HttpContext context,
            IAntiforgery antiforgery,
            IAccountMergeService merge,
            CancellationToken cancellationToken) =>
        {
            if (await AuthRequests.ValidateAntiforgeryAsync(context, antiforgery) is { } antiforgeryError)
            {
                return antiforgeryError;
            }

            var userId = CurrentUser.GetUserId(context.User);
            if (userId is null)
            {
                return Results.Unauthorized();
            }

            var ok = await merge.MergeAsync(userId.Value, context, context.GetCorrelationId(), cancellationToken);
            return ok
                ? Results.Ok(new { ok = true })
                : Results.BadRequest(new ApiError(
                    AuthResultCodes.InvalidRequest,
                    "No pending merge.",
                    context.GetCorrelationId()));
        }).RequireAuthorization().RateLimit();

        group.MapPost("/merge/cancel", async (
            HttpContext context,
            IAntiforgery antiforgery,
            IAccountMergeService merge,
            CancellationToken cancellationToken) =>
        {
            if (await AuthRequests.ValidateAntiforgeryAsync(context, antiforgery) is { } antiforgeryError)
            {
                return antiforgeryError;
            }

            merge.ClearPendingMerge(context);
            return Results.Ok(new { ok = true });
        }).RequireAuthorization().RateLimit();

        return app;
    }
}
