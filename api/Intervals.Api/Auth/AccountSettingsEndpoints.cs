using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace Intervals.Api.Auth;

public static class AccountSettingsEndpoints
{
    public static IServiceCollection AddIntervalsAccountSettings(this IServiceCollection services)
    {
        services.AddScoped<IAccountSettingsService, AccountSettingsService>();
        return services;
    }

    public static WebApplication MapAccountSettingsEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/auth/account").RequireAuthorization();

        group.MapGet("/", async (
            HttpContext context,
            IAccountSettingsService settings,
            CancellationToken cancellationToken) =>
        {
            var userId = CurrentUser.GetUserId(context.User);
            if (userId is null)
            {
                return Results.Unauthorized();
            }

            var detail = await settings.GetDetailAsync(userId.Value, cancellationToken);
            return Results.Ok(detail);
        }).RateLimit();

        group.MapPost("/password/change", async (
            HttpContext context,
            IAntiforgery antiforgery,
            IAccountSettingsService settings,
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

            var request = await context.Request.ReadFromJsonAsync<ChangePasswordRequest>(cancellationToken);
            if (request is null
                || string.IsNullOrWhiteSpace(request.CurrentPassword)
                || string.IsNullOrWhiteSpace(request.NewPassword))
            {
                return Results.BadRequest(new ApiError(
                    AuthResultCodes.InvalidRequest,
                    "Current and new passwords are required.",
                    context.GetCorrelationId()));
            }

            var correlationId = context.GetCorrelationId();
            var result = await settings.ChangePasswordAsync(
                userId.Value,
                request.CurrentPassword,
                request.NewPassword,
                correlationId,
                cancellationToken);

            if (result.Success)
            {
                return Results.Ok(new { ok = true });
            }

            return result.FailureCode switch
            {
                AuthResultCodes.InvalidCredentials => Results.Json(
                    new ApiError(AuthResultCodes.InvalidCredentials, "Current password is incorrect.", correlationId),
                    statusCode: StatusCodes.Status401Unauthorized),
                AuthResultCodes.WeakPassword => Results.BadRequest(new ApiError(
                    AuthResultCodes.WeakPassword,
                    "Password does not meet requirements.",
                    correlationId)),
                _ => Results.BadRequest(new ApiError(
                    AuthResultCodes.InvalidRequest,
                    "Unable to change password.",
                    correlationId)),
            };
        }).RateLimit();

        group.MapPost("/password/add", async (
            HttpContext context,
            IAntiforgery antiforgery,
            IAccountSettingsService settings,
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

            var request = await context.Request.ReadFromJsonAsync<AddPasswordRequest>(cancellationToken);
            if (request is null
                || string.IsNullOrWhiteSpace(request.Email)
                || string.IsNullOrWhiteSpace(request.NewPassword))
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
            var result = await settings.AddPasswordAsync(
                userId.Value,
                request.Email,
                request.NewPassword,
                correlationId,
                cancellationToken);

            if (result.Success)
            {
                return Results.Ok(new { ok = true });
            }

            return result.FailureCode switch
            {
                AuthResultCodes.EmailTaken => Results.Json(
                    new ApiError(AuthResultCodes.EmailTaken, "Email is already in use.", correlationId),
                    statusCode: StatusCodes.Status409Conflict),
                AuthResultCodes.WeakPassword => Results.BadRequest(new ApiError(
                    AuthResultCodes.WeakPassword,
                    "Password does not meet requirements.",
                    correlationId)),
                _ => Results.BadRequest(new ApiError(
                    AuthResultCodes.InvalidRequest,
                    "Unable to add password.",
                    correlationId)),
            };
        }).RateLimit();

        group.MapDelete("/providers/{provider}", async (
            string provider,
            HttpContext context,
            IAntiforgery antiforgery,
            IAccountSettingsService settings,
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

            var correlationId = context.GetCorrelationId();
            var result = await settings.UnlinkAsync(
                userId.Value,
                provider,
                correlationId,
                cancellationToken);

            if (result.Success)
            {
                return Results.Ok(new { ok = true });
            }

            return result.FailureCode switch
            {
                AccountSettingsResultCodes.LastLoginMethod => Results.Json(
                    new ApiError(
                        AccountSettingsResultCodes.LastLoginMethod,
                        "Unlinking would leave your account with no sign-in method.",
                        correlationId),
                    statusCode: StatusCodes.Status409Conflict),
                _ => Results.BadRequest(new ApiError(
                    AuthResultCodes.InvalidRequest,
                    "Unable to unlink provider.",
                    correlationId)),
            };
        }).RateLimit();

        return app;
    }
}

public sealed record ChangePasswordRequest(string CurrentPassword, string NewPassword);

public sealed record AddPasswordRequest(string Email, string NewPassword);
