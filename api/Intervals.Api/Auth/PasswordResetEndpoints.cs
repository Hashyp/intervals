using System.Threading;
using System.Threading.Tasks;
using Intervals.Api.Data;
using Intervals.Api.Data.Entities;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Intervals.Api.Auth;

public static class PasswordResetEndpoints
{
    public static IServiceCollection AddIntervalsPasswordReset(this IServiceCollection services)
    {
        services.AddScoped<IPasswordResetService, PasswordResetService>();
        return services;
    }

    public static WebApplication MapPasswordResetEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/auth/password");

        group.MapPost("/forgot", async (
            HttpContext context,
            IAntiforgery antiforgery,
            IPasswordResetService resets,
            IntervalsDbContext db,
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

            var body = await context.Request.ReadFromJsonAsync<ForgotPasswordRequest>(cancellationToken);
            if (body is null || string.IsNullOrWhiteSpace(body.Email))
            {
                return Results.BadRequest(new ApiError(
                    AuthResultCodes.InvalidRequest,
                    "Email is required.",
                    context.GetCorrelationId()));
            }

            var normalized = NormalizeEmail(body.Email);
            if (normalized is not null)
            {
                var credential = await db.PasswordCredentials
                    .AsNoTracking()
                    .FirstOrDefaultAsync(c => c.EmailNormalized == normalized, cancellationToken);

                if (credential is not null)
                {
                    db.AuthEvents.Add(new AuthEvent
                    {
                        UserId = credential.UserId,
                        EventType = AuthEventTypes.PasswordResetRequested,
                        OccurredUtc = DateTimeOffset.UtcNow,
                        Success = true,
                        CorrelationId = context.GetCorrelationId(),
                    });
                    await db.SaveChangesAsync(cancellationToken);
                }
            }

            await resets.RequestResetAsync(body.Email, context.GetCorrelationId(), cancellationToken);

            return Results.Ok(new { ok = true });
        }).AllowAnonymous().RequireRateLimiting("password-reset");

        group.MapPost("/reset", async (
            HttpContext context,
            IAntiforgery antiforgery,
            IPasswordResetService resets,
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

            var body = await context.Request.ReadFromJsonAsync<ResetPasswordRequest>(cancellationToken);
            if (body is null
                || string.IsNullOrWhiteSpace(body.Token)
                || string.IsNullOrWhiteSpace(body.Password))
            {
                return Results.BadRequest(new ApiError(
                    AuthResultCodes.InvalidRequest,
                    "Token and password are required.",
                    context.GetCorrelationId()));
            }

            var correlationId = context.GetCorrelationId();
            var result = await resets.ResetAsync(
                body.Token,
                body.Password,
                correlationId,
                cancellationToken);

            if (result.Success)
            {
                return Results.Ok(new { ok = true });
            }

            return result.FailureCode switch
            {
                AuthResultCodes.WeakPassword => Results.BadRequest(new ApiError(
                    AuthResultCodes.WeakPassword,
                    "Password does not meet requirements.",
                    correlationId)),
                _ => Results.Json(
                    new ApiError(
                        AuthResultCodes.InvalidRequest,
                        "Password reset failed.",
                        correlationId),
                    statusCode: StatusCodes.Status400BadRequest),
            };
        }).AllowAnonymous().RateLimit();

        return app;
    }

    private const int MaxEmailLength = 320;

    private static string? NormalizeEmail(string? email)
    {
        if (string.IsNullOrWhiteSpace(email))
        {
            return null;
        }

        var trimmed = email.Trim();
        if (trimmed.Length > MaxEmailLength)
        {
            return null;
        }

        return trimmed.ToUpperInvariant();
    }
}

public sealed record ForgotPasswordRequest(string Email);

public sealed record ResetPasswordRequest(string Token, string Password);
