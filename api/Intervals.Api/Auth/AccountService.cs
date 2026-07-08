using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Intervals.Api.Data;
using Intervals.Api.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Intervals.Api.Auth;

public sealed class AccountService(
    IntervalsDbContext db,
    IAuthEventRecorder recorder,
    ILogger<AccountService> logger) : IAccountService
{
    public async Task<AccountLoginResult> LoginAsync(
        ExternalUserProfile profile,
        string? correlationId,
        CancellationToken cancellationToken = default)
    {
        var provider = profile.Provider;
        var external = await db.ExternalLogins.FirstOrDefaultAsync(
            x => x.Provider == provider && x.ProviderUserId == profile.ProviderUserId,
            cancellationToken);

        AppUser user;
        bool created;

        if (external is null)
        {
            user = new AppUser
            {
                DisplayName = string.IsNullOrWhiteSpace(profile.DisplayName) ? "Intervals user" : profile.DisplayName,
                Email = profile.Email,
                EmailNormalized = AuthEmail.Normalize(profile.Email, int.MaxValue),
                AvatarUrl = profile.AvatarUrl,
                CreatedUtc = DateTimeOffset.UtcNow,
                LastLoginUtc = DateTimeOffset.UtcNow,
            };

            external = new ExternalLogin
            {
                Provider = provider,
                ProviderUserId = profile.ProviderUserId,
                Email = profile.Email,
                EmailVerified = profile.EmailVerified,
                DisplayName = profile.DisplayName,
                AvatarUrl = profile.AvatarUrl,
                CreatedUtc = DateTimeOffset.UtcNow,
                LastLoginUtc = DateTimeOffset.UtcNow,
            };
            user.ExternalLogins.Add(external);
            db.AppUsers.Add(user);
            await db.SaveChangesAsync(cancellationToken);
            created = true;
        }
        else
        {
            user = await db.AppUsers.FirstAsync(x => x.Id == external.UserId, cancellationToken);

            external.Email = profile.Email;
            external.EmailVerified = profile.EmailVerified;
            external.DisplayName = profile.DisplayName;
            external.AvatarUrl = profile.AvatarUrl;
            external.LastLoginUtc = DateTimeOffset.UtcNow;

            user.LastLoginUtc = DateTimeOffset.UtcNow;
            if (!string.IsNullOrWhiteSpace(profile.DisplayName))
            {
                user.DisplayName = profile.DisplayName;
            }

            user.Email = profile.Email;
            user.EmailNormalized = AuthEmail.Normalize(profile.Email, int.MaxValue);
            user.AvatarUrl = profile.AvatarUrl;
            await db.SaveChangesAsync(cancellationToken);
            created = false;
        }

        await recorder.RecordAsync(
            AuthEventTypes.LoginSuccess,
            user.Id,
            provider,
            success: true,
            correlationId,
            cancellationToken);

        logger.LogInformation(
            "Auth login success for user {UserId} via {Provider} (created={Created}).",
            user.Id,
            provider,
            created);

        return new AccountLoginResult(user, created);
    }

    public Task<AppUser?> GetAsync(Guid userId, CancellationToken cancellationToken = default) =>
        db.AppUsers
            .Include(u => u.PasswordCredential)
            .Include(u => u.ExternalLogins)
            .FirstOrDefaultAsync(x => x.Id == userId, cancellationToken);

    public async Task<IReadOnlyList<string>> GetLinkedProvidersAsync(
        Guid userId,
        CancellationToken cancellationToken = default) =>
        await db.ExternalLogins
            .Where(x => x.UserId == userId)
            .Select(x => x.Provider)
            .ToListAsync(cancellationToken);

    public async Task RecordLogoutAsync(
        Guid? userId,
        string? correlationId,
        CancellationToken cancellationToken = default)
    {
        await recorder.RecordAsync(AuthEventTypes.Logout, userId, provider: null, success: true, correlationId, cancellationToken);
    }
}
