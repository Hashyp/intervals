using System;
using System.Threading;
using System.Threading.Tasks;
using Intervals.Api.Data;
using Intervals.Api.Data.Entities;
using Intervals.Api.Email;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Intervals.Api.Auth;

public static class EmailVerificationEndpoints
{
    public static WebApplication MapEmailVerificationEndpoints(this WebApplication app)
    {
        var webBaseUrl = app.Configuration["Web:BaseUrl"] ?? string.Empty;
        var group = app.MapGroup("/auth/email-verification");

        group.MapPost("/request", async (
            HttpContext context,
            IAntiforgery antiforgery,
            AuthActionTokenService tokens,
            IEmailSender emailSender,
            IOptions<EmailOptions> emailOptions,
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

            var userId = CurrentUser.GetUserId(context.User);
            if (userId is null)
            {
                return Results.Unauthorized();
            }

            var credential = await db.PasswordCredentials
                .AsNoTracking()
                .FirstOrDefaultAsync(p => p.UserId == userId, cancellationToken);

            if (credential is null || string.IsNullOrWhiteSpace(credential.Email))
            {
                return Results.Ok(new { ok = true });
            }

            try
            {
                var rawToken = await tokens.IssueAsync(
                    userId.Value,
                    AuthActionTokenPurpose.EmailVerification,
                    credential.Email,
                    TimeSpan.FromHours(emailOptions.Value.VerificationTokenLifetimeHours),
                    context.GetCorrelationId(),
                    cancellationToken);

                var trimmedBase = (emailOptions.Value.AppBaseUrl ?? string.Empty).TrimEnd('/');
                var verificationLink = $"{trimmedBase}/auth/email-verification/confirm?token={rawToken}";
                var (subject, html, text) = EmailTemplates.EmailVerification(credential.Email, verificationLink);

                await emailSender.SendEmailAsync(credential.Email, subject, html, text, cancellationToken);

                db.AuthEvents.Add(new AuthEvent
                {
                    UserId = userId,
                    EventType = AuthEventTypes.EmailVerificationSent,
                    OccurredUtc = DateTimeOffset.UtcNow,
                    Success = true,
                    CorrelationId = context.GetCorrelationId(),
                });
                await db.SaveChangesAsync(cancellationToken);
            }
            catch
            {
                // Best-effort: never leak whether the address exists or delivery failed.
            }

            return Results.Ok(new { ok = true });
        }).RequireAuthorization().RequireRateLimiting("verification");

        group.MapGet("/confirm", async (
            HttpContext context,
            string? token,
            AuthActionTokenService tokens,
            IntervalsDbContext db,
            IOptions<AuthOptions> authOptions,
            CancellationToken cancellationToken) =>
        {
            var loginPath = authOptions.Value.LoginPath;

            var consumed = await tokens.ConsumeAsync(
                AuthActionTokenPurpose.EmailVerification,
                token ?? string.Empty,
                cancellationToken);

            if (consumed is null)
            {
                db.AuthEvents.Add(new AuthEvent
                {
                    UserId = null,
                    EventType = AuthEventTypes.EmailVerified,
                    OccurredUtc = DateTimeOffset.UtcNow,
                    Success = false,
                    CorrelationId = context.GetCorrelationId(),
                });
                await db.SaveChangesAsync(cancellationToken);
                return Results.Redirect(AuthRedirect.Build(webBaseUrl, loginPath, "verification_failed"));
            }

            var credential = await db.PasswordCredentials
                .FirstOrDefaultAsync(c => c.UserId == consumed.UserId, cancellationToken);

            if (credential is not null)
            {
                credential.EmailVerified = true;
                credential.EmailVerifiedAtUtc = DateTimeOffset.UtcNow;
            }

            db.AuthEvents.Add(new AuthEvent
            {
                UserId = consumed.UserId,
                EventType = AuthEventTypes.EmailVerified,
                OccurredUtc = DateTimeOffset.UtcNow,
                Success = true,
                CorrelationId = context.GetCorrelationId(),
            });
            await db.SaveChangesAsync(cancellationToken);

            return Results.Redirect(AuthRedirect.Build(webBaseUrl, loginPath, "email_verified"));
        }).AllowAnonymous().RateLimit();

        return app;
    }
}
