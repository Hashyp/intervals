using System;
using System.Collections.Generic;
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

public sealed class AccountSettingsService(
    IntervalsDbContext db,
    PasswordPolicy passwordPolicy,
    PasswordHasher<AppUser> passwordHasher,
    AuthActionTokenService tokens,
    IEmailSender emailSender,
    IOptions<EmailOptions> emailOptions,
    ILogger<AccountSettingsService> logger) : IAccountSettingsService
{
    private const int MaxEmailLength = 320;
    private readonly EmailOptions _emailOptions = emailOptions.Value;

    public async Task<AccountDetail> GetDetailAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var user = await db.AppUsers
            .Include(u => u.PasswordCredential)
            .Include(u => u.ExternalLogins)
            .AsNoTracking()
            .FirstOrDefaultAsync(u => u.Id == userId, cancellationToken);

        if (user is null)
        {
            return new AccountDetail(userId, string.Empty, null, false, false, Array.Empty<AccountProvider>());
        }

        var providers = BuildProviders(user);
        var email = user.PasswordCredential?.Email
            ?? user.ExternalLogins.FirstOrDefault(e => !string.IsNullOrWhiteSpace(e.Email))?.Email
            ?? user.Email;
        var emailVerified = user.PasswordCredential?.EmailVerified == true
            || user.ExternalLogins.Any(e => e.EmailVerified);

        return new AccountDetail(
            user.Id,
            user.DisplayName,
            email,
            emailVerified,
            user.PasswordCredential is not null,
            providers);
    }

    public async Task<PasswordManagementResult> ChangePasswordAsync(
        Guid userId,
        string currentPassword,
        string newPassword,
        string? correlationId,
        CancellationToken cancellationToken = default)
    {
        var user = await db.AppUsers
            .Include(u => u.PasswordCredential)
            .FirstOrDefaultAsync(u => u.Id == userId, cancellationToken);

        var credential = user?.PasswordCredential;
        if (user is null || credential is null)
        {
            return new PasswordManagementResult(false, AuthResultCodes.InvalidRequest);
        }

        var verification = passwordHasher.VerifyHashedPassword(user, credential.PasswordHash, currentPassword);
        if (verification == PasswordVerificationResult.Failed)
        {
            await RecordAsync(
                AuthEventTypes.PasswordChanged,
                user.Id,
                AuthProviderNames.Password,
                success: false,
                correlationId,
                cancellationToken);
            return new PasswordManagementResult(false, AuthResultCodes.InvalidCredentials);
        }

        if (!passwordPolicy.IsValid(newPassword, out _))
        {
            await RecordAsync(
                AuthEventTypes.PasswordChanged,
                user.Id,
                AuthProviderNames.Password,
                success: false,
                correlationId,
                cancellationToken);
            return new PasswordManagementResult(false, AuthResultCodes.WeakPassword);
        }

        var now = DateTimeOffset.UtcNow;
        credential.PasswordHash = passwordHasher.HashPassword(user, newPassword);
        credential.UpdatedAtUtc = now;
        user.SecurityStamp = Guid.NewGuid().ToString("N");

        await db.SaveChangesAsync(cancellationToken);

        await tokens.RevokeAsync(
            user.Id,
            AuthActionTokenPurpose.PasswordReset,
            credential.Email,
            cancellationToken);

        await RecordAsync(
            AuthEventTypes.PasswordChanged,
            user.Id,
            AuthProviderNames.Password,
            success: true,
            correlationId,
            cancellationToken);

        logger.LogInformation("Password changed for user {UserId}.", user.Id);

        return new PasswordManagementResult(true, null);
    }

    public async Task<PasswordManagementResult> AddPasswordAsync(
        Guid userId,
        string email,
        string newPassword,
        string? correlationId,
        CancellationToken cancellationToken = default)
    {
        if (!passwordPolicy.IsValid(newPassword, out _))
        {
            return new PasswordManagementResult(false, AuthResultCodes.WeakPassword);
        }

        var normalized = NormalizeEmail(email);
        if (normalized is null)
        {
            return new PasswordManagementResult(false, AuthResultCodes.InvalidRequest);
        }

        var user = await db.AppUsers
            .Include(u => u.PasswordCredential)
            .Include(u => u.ExternalLogins)
            .FirstOrDefaultAsync(u => u.Id == userId, cancellationToken);

        if (user is null)
        {
            return new PasswordManagementResult(false, AuthResultCodes.InvalidRequest);
        }

        if (user.PasswordCredential is not null)
        {
            return new PasswordManagementResult(false, AuthResultCodes.InvalidRequest);
        }

        if (await db.PasswordCredentials.AnyAsync(c => c.EmailNormalized == normalized, cancellationToken))
        {
            return new PasswordManagementResult(false, AuthResultCodes.EmailTaken);
        }

        var matchingExternal = user.ExternalLogins.FirstOrDefault(e =>
            !string.IsNullOrWhiteSpace(e.Email) && NormalizeEmail(e.Email) == normalized);

        var verified = matchingExternal is not null && matchingExternal.EmailVerified;
        var now = DateTimeOffset.UtcNow;
        var trimmedEmail = email.Trim().ToLowerInvariant();

        var credential = new PasswordCredential
        {
            UserId = user.Id,
            Email = trimmedEmail,
            EmailNormalized = normalized,
            PasswordHash = passwordHasher.HashPassword(user, newPassword),
            EmailVerified = verified,
            EmailVerifiedAtUtc = verified ? now : null,
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
        };

        db.PasswordCredentials.Add(credential);
        user.SecurityStamp = Guid.NewGuid().ToString("N");
        await db.SaveChangesAsync(cancellationToken);

        if (!verified)
        {
            await SendVerificationEmailAsync(user, trimmedEmail, correlationId, cancellationToken);
        }

        await RecordAsync(
            AuthEventTypes.PasswordAdded,
            user.Id,
            AuthProviderNames.Password,
            success: true,
            correlationId,
            cancellationToken);

        logger.LogInformation("Password added for user {UserId} ({Email}).", user.Id, trimmedEmail);

        return new PasswordManagementResult(true, null);
    }

    public async Task<UnlinkResult> UnlinkAsync(
        Guid userId,
        string provider,
        string? correlationId,
        CancellationToken cancellationToken = default)
    {
        var normalized = AuthProviderNames.Normalize(provider);
        if (!AuthProviderNames.IsValid(normalized))
        {
            return new UnlinkResult(false, AuthResultCodes.InvalidRequest);
        }

        var user = await db.AppUsers
            .Include(u => u.PasswordCredential)
            .Include(u => u.ExternalLogins)
            .FirstOrDefaultAsync(u => u.Id == userId, cancellationToken);

        if (user is null)
        {
            return new UnlinkResult(false, AuthResultCodes.InvalidRequest);
        }

        var external = user.ExternalLogins.FirstOrDefault(e => e.Provider == normalized);
        if (external is null)
        {
            return new UnlinkResult(true, null);
        }

        var hasPassword = user.PasswordCredential is not null;
        var remainingOthers = user.ExternalLogins.Count(e => e.Provider != normalized);
        var usableAfter = (hasPassword ? 1 : 0) + remainingOthers;
        if (usableAfter == 0)
        {
            return new UnlinkResult(false, AccountSettingsResultCodes.LastLoginMethod);
        }

        db.ExternalLogins.Remove(external);
        await db.SaveChangesAsync(cancellationToken);

        await RecordAsync(
            AuthEventTypes.AccountUnlinked,
            user.Id,
            normalized,
            success: true,
            correlationId,
            cancellationToken);

        logger.LogInformation("Provider {Provider} unlinked for user {UserId}.", normalized, user.Id);

        return new UnlinkResult(true, null);
    }

    private static List<AccountProvider> BuildProviders(AppUser user)
    {
        var externalByProvider = user.ExternalLogins
            .GroupBy(e => e.Provider)
            .ToDictionary(g => g.Key, g => g.OrderByDescending(e => e.LastLoginUtc ?? DateTimeOffset.MinValue).First(), StringComparer.OrdinalIgnoreCase);

        static AccountProvider External(string id, string label, IReadOnlyDictionary<string, ExternalLogin> map)
        {
            if (map.TryGetValue(id, out var ext))
            {
                return new AccountProvider(id, label, true, ext.Email, ext.LastLoginUtc);
            }
            return new AccountProvider(id, label, false, null, null);
        }

        var providers = new List<AccountProvider>
        {
            External(AuthProviderNames.Google, "Google", externalByProvider),
            External(AuthProviderNames.Microsoft, "Microsoft", externalByProvider),
            External(AuthProviderNames.X, "X", externalByProvider),
        };

        var passwordCredential = user.PasswordCredential;
        providers.Add(new AccountProvider(
            AuthProviderNames.Password,
            "Email",
            passwordCredential is not null,
            passwordCredential?.Email,
            passwordCredential?.LastLoginUtc));

        return providers;
    }

    private async Task SendVerificationEmailAsync(
        AppUser user,
        string email,
        string? correlationId,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(email))
        {
            return;
        }

        string rawToken;
        try
        {
            rawToken = await tokens.IssueAsync(
                user.Id,
                AuthActionTokenPurpose.EmailVerification,
                email,
                TimeSpan.FromHours(Math.Max(1, _emailOptions.VerificationTokenLifetimeHours)),
                correlationId,
                cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to issue email-verification token for user {UserId}.", user.Id);
            return;
        }

        var trimmedBase = (_emailOptions.AppBaseUrl ?? string.Empty).TrimEnd('/');
        var verificationLink = $"{trimmedBase}/auth/email-verification/confirm?token={rawToken}";
        var (subject, html, text) = EmailTemplates.EmailVerification(user.DisplayName, verificationLink);

        try
        {
            await emailSender.SendEmailAsync(email, subject, html, text, cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to send verification email for user {UserId}.", user.Id);
        }
    }

    private async Task RecordAsync(
        string eventType,
        Guid userId,
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
