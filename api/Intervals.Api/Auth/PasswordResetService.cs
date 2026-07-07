using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Intervals.Api.Data;
using Intervals.Api.Data.Entities;
using Intervals.Api.Email;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Intervals.Api.Auth;

public sealed class PasswordResetService(
    IntervalsDbContext db,
    PasswordPolicy passwordPolicy,
    PasswordHasher<AppUser> passwordHasher,
    IEmailSender emailSender,
    IOptions<EmailOptions> emailOptions,
    AuthActionTokenService tokens,
    ILogger<PasswordResetService> logger) : IPasswordResetService
{
    private const string PasswordResetEventType = "password_reset";
    private const int MaxEmailLength = 320;

    private readonly EmailOptions _emailOptions = emailOptions.Value;

    public async Task RequestResetAsync(
        string email,
        string? correlationId,
        CancellationToken cancellationToken = default)
    {
        var normalized = NormalizeEmail(email);
        if (normalized is null)
        {
            return;
        }

        var credential = await db.PasswordCredentials
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.EmailNormalized == normalized, cancellationToken);

        if (credential is null || string.IsNullOrWhiteSpace(credential.Email))
        {
            return;
        }

        try
        {
            var rawToken = await tokens.IssueAsync(
                credential.UserId,
                AuthActionTokenPurpose.PasswordReset,
                credential.Email,
                TimeSpan.FromHours(Math.Max(1, _emailOptions.PasswordResetTokenLifetimeHours)),
                correlationId,
                cancellationToken);

            var trimmedBase = (_emailOptions.AppBaseUrl ?? string.Empty).TrimEnd('/');
            var resetLink = $"{trimmedBase}/reset-password?token={rawToken}";
            var displayName = await ResolveDisplayNameAsync(credential.UserId, cancellationToken);
            var (subject, html, text) = EmailTemplates.PasswordReset(displayName, resetLink);

            await emailSender.SendEmailAsync(credential.Email, subject, html, text, cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex,
                "Failed to send password reset email for user {UserId}.",
                credential.UserId);
        }
    }

    public async Task<PasswordResetResult> ResetAsync(
        string rawToken,
        string newPassword,
        string? correlationId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(rawToken))
        {
            return new PasswordResetResult(false, AuthResultCodes.InvalidRequest);
        }

        var consumed = await tokens.ConsumeAsync(
            AuthActionTokenPurpose.PasswordReset,
            rawToken,
            cancellationToken);

        if (consumed is null)
        {
            return new PasswordResetResult(false, AuthResultCodes.InvalidRequest);
        }

        var credential = await db.PasswordCredentials
            .Include(c => c.User)
            .FirstOrDefaultAsync(c => c.UserId == consumed.UserId, cancellationToken);

        if (credential is null || credential.User is null)
        {
            return new PasswordResetResult(false, AuthResultCodes.InvalidRequest);
        }

        if (!passwordPolicy.IsValid(newPassword, out _))
        {
            await RecordAsync(
                PasswordResetEventType,
                consumed.UserId,
                AuthProviderNames.Password,
                success: false,
                correlationId,
                cancellationToken);
            return new PasswordResetResult(false, AuthResultCodes.WeakPassword);
        }

        var user = credential.User;
        var now = DateTimeOffset.UtcNow;

        credential.PasswordHash = passwordHasher.HashPassword(user, newPassword);
        credential.FailedAttemptCount = 0;
        credential.LockoutUntilUtc = null;
        credential.EmailVerified = true;
        credential.EmailVerifiedAtUtc = now;
        credential.LastLoginUtc = null;
        credential.UpdatedAtUtc = now;
        user.SecurityStamp = Guid.NewGuid().ToString("N");

        await db.SaveChangesAsync(cancellationToken);

        await tokens.RevokeAsync(
            consumed.UserId,
            AuthActionTokenPurpose.PasswordReset,
            consumed.Email,
            cancellationToken);

        await RecordAsync(
            PasswordResetEventType,
            consumed.UserId,
            AuthProviderNames.Password,
            success: true,
            correlationId,
            cancellationToken);

        logger.LogInformation(
            "Password reset success for user {UserId} ({Email}).",
            user.Id,
            credential.Email);

        return new PasswordResetResult(true, null);
    }

    private async Task<string> ResolveDisplayNameAsync(Guid userId, CancellationToken cancellationToken)
    {
        var user = await db.AppUsers
            .AsNoTracking()
            .FirstOrDefaultAsync(u => u.Id == userId, cancellationToken);

        if (user is null)
        {
            return "there";
        }

        return string.IsNullOrWhiteSpace(user.DisplayName) ? "there" : user.DisplayName;
    }

    private async Task RecordAsync(
        string eventType,
        Guid? userId,
        string? provider,
        bool success,
        string? correlationId,
        CancellationToken cancellationToken)
    {
        db.AuthEvents.Add(new AuthEvent
        {
            UserId = userId,
            Provider = provider,
            EventType = eventType,
            OccurredUtc = DateTimeOffset.UtcNow,
            Success = success,
            CorrelationId = correlationId,
        });
        await db.SaveChangesAsync(cancellationToken);
    }

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
