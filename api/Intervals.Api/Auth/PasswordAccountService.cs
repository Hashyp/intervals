using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Intervals.Api.Data;
using Intervals.Api.Data.Entities;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Intervals.Api.Auth;

public sealed class PasswordAccountService(
    IntervalsDbContext db,
    IOptions<AuthOptions> options,
    PasswordPolicy passwordPolicy,
    PasswordHasher<AppUser> hasher,
    ILogger<PasswordAccountService> logger) : IPasswordAccountService
{
    public const int MaxEmailLength = 320;
    private const int MaxDisplayNameLength = 256;

    private readonly AuthOptions _options = options.Value;

    public async Task<PasswordRegisterResult> RegisterAsync(
        string email,
        string password,
        string? correlationId,
        CancellationToken cancellationToken = default)
    {
        var normalized = NormalizeEmail(email);

        if (normalized is null)
        {
            return new PasswordRegisterResult(false, null, AuthResultCodes.WeakPassword);
        }

        if (!passwordPolicy.IsValid(password, out _))
        {
            return new PasswordRegisterResult(false, null, AuthResultCodes.WeakPassword);
        }

        var existing = await db.PasswordCredentials
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.EmailNormalized == normalized, cancellationToken);

        if (existing is not null)
        {
            _ = hasher.HashPassword(new AppUser(), password);
            return new PasswordRegisterResult(false, null, AuthResultCodes.EmailTaken);
        }

        var trimmedEmail = email.Trim();
        var atIndex = trimmedEmail.IndexOf('@');
        var displayName = atIndex > 0 ? trimmedEmail[..atIndex] : "Intervals user";
        if (displayName.Length > MaxDisplayNameLength)
        {
            displayName = displayName[..MaxDisplayNameLength];
        }

        var user = new AppUser
        {
            DisplayName = displayName,
            Email = trimmedEmail.ToLowerInvariant(),
            EmailNormalized = normalized,
            CreatedUtc = DateTimeOffset.UtcNow,
            LastLoginUtc = DateTimeOffset.UtcNow,
        };

        var credential = new PasswordCredential
        {
            UserId = user.Id,
            Email = user.Email,
            EmailNormalized = normalized,
            PasswordHash = hasher.HashPassword(user, password),
            EmailVerified = false,
        };

        db.AppUsers.Add(user);
        db.PasswordCredentials.Add(credential);
        await db.SaveChangesAsync(cancellationToken);

        await RecordAsync(
            AuthEventTypes.RegisterSuccess,
            user.Id,
            AuthProviderNames.Password,
            success: true,
            correlationId,
            cancellationToken);

        logger.LogInformation(
            "Password register success for user {UserId} ({Email}).",
            user.Id,
            user.Email);

        return new PasswordRegisterResult(true, user, null);
    }

    public async Task<PasswordLoginResult> AuthenticateAsync(
        string email,
        string password,
        string? correlationId,
        CancellationToken cancellationToken = default)
    {
        var normalized = NormalizeEmail(email);

        if (normalized is null)
        {
            return new PasswordLoginResult(false, null, AuthResultCodes.InvalidCredentials);
        }

        var credential = await db.PasswordCredentials
            .Include(c => c.User)
            .FirstOrDefaultAsync(c => c.EmailNormalized == normalized, cancellationToken);

        if (credential is null)
        {
            var dummy = new AppUser();
            var throwawayHash = hasher.HashPassword(dummy, password);
            _ = hasher.VerifyHashedPassword(dummy, throwawayHash, password);

            await RecordAsync(
                AuthEventTypes.LoginFailure,
                userId: null,
                AuthProviderNames.Password,
                success: false,
                correlationId,
                cancellationToken);

            return new PasswordLoginResult(false, null, AuthResultCodes.InvalidCredentials);
        }

        if (credential.User is { DisabledUtc: not null })
        {
            await RecordAsync(
                AuthEventTypes.LoginFailure,
                credential.UserId,
                AuthProviderNames.Password,
                success: false,
                correlationId,
                cancellationToken);

            return new PasswordLoginResult(false, null, AuthResultCodes.Disabled);
        }

        if (credential.LockoutUntilUtc is { } lockout && lockout > DateTimeOffset.UtcNow)
        {
            await RecordAsync(
                AuthEventTypes.LoginFailure,
                credential.UserId,
                AuthProviderNames.Password,
                success: false,
                correlationId,
                cancellationToken);

            return new PasswordLoginResult(false, null, AuthResultCodes.LockedOut);
        }

        var user = credential.User!;
        var verification = hasher.VerifyHashedPassword(user, credential.PasswordHash, password);

        if (verification == PasswordVerificationResult.Failed)
        {
            credential.FailedAttemptCount++;
            var justLocked = false;
            if (credential.FailedAttemptCount >= _options.Password.MaxFailedAttempts)
            {
                credential.LockoutUntilUtc = DateTimeOffset.UtcNow.Add(_options.Password.LockoutDuration);
                credential.FailedAttemptCount = 0;
                justLocked = true;
            }

            credential.UpdatedAtUtc = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync(cancellationToken);

            await RecordAsync(
                AuthEventTypes.LoginFailure,
                credential.UserId,
                AuthProviderNames.Password,
                success: false,
                correlationId,
                cancellationToken);

            return justLocked
                ? new PasswordLoginResult(false, null, AuthResultCodes.LockedOut)
                : new PasswordLoginResult(false, null, AuthResultCodes.InvalidCredentials);
        }

        if (verification == PasswordVerificationResult.SuccessRehashNeeded)
        {
            credential.PasswordHash = hasher.HashPassword(user, password);
        }

        credential.FailedAttemptCount = 0;
        credential.LastLoginUtc = DateTimeOffset.UtcNow;
        credential.UpdatedAtUtc = DateTimeOffset.UtcNow;
        user.LastLoginUtc = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(cancellationToken);

        await RecordAsync(
            AuthEventTypes.LoginSuccess,
            user.Id,
            AuthProviderNames.Password,
            success: true,
            correlationId,
            cancellationToken);

        logger.LogInformation(
            "Password login success for user {UserId} ({Email}).",
            user.Id,
            user.Email);

        return new PasswordLoginResult(true, user, null);
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

    private static string? NormalizeEmail(string? email) =>
        string.IsNullOrWhiteSpace(email) ? null : email.Trim().ToUpperInvariant();
}
