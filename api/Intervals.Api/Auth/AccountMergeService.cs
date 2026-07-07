using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Intervals.Api.Data;
using Intervals.Api.Data.Entities;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Intervals.Api.Auth;

public sealed class AccountMergeService(
    IntervalsDbContext db,
    IDataProtectionProvider dataProtection,
    ILogger<AccountMergeService> logger) : IAccountMergeService
{
    public const string CookieName = "Intervals.PendingMerge";
    public const string MergedEventType = "account_merged";
    public const string MergedSecondaryEventType = "account_merged_secondary";
    private static readonly TimeSpan PendingLifetime = TimeSpan.FromMinutes(10);
    private readonly IDataProtector _protector = dataProtection.CreateProtector("Intervals.AccountMerge.PendingMerge");

    public async Task<PendingMergeDetail?> GetPendingMergeAsync(
        Guid primaryUserId,
        HttpContext httpContext,
        CancellationToken cancellationToken = default)
    {
        var (cookiePrimary, cookieSecondary, provider) = ReadPendingMerge(httpContext);
        if (cookiePrimary != primaryUserId || cookieSecondary is null || provider is null)
        {
            return null;
        }

        var primary = await db.AppUsers.FirstOrDefaultAsync(u => u.Id == primaryUserId, cancellationToken);
        var secondary = await db.AppUsers.FirstOrDefaultAsync(u => u.Id == cookieSecondary.Value, cancellationToken);

        if (primary is null || secondary is null || secondary.MergedIntoUserId is not null)
        {
            return null;
        }

        return new PendingMergeDetail(
            primary.Id,
            primary.DisplayName,
            primary.Email,
            secondary.Id,
            secondary.DisplayName,
            secondary.Email,
            provider);
    }

    public async Task<bool> MergeAsync(
        Guid primaryUserId,
        HttpContext httpContext,
        string? correlationId,
        CancellationToken cancellationToken = default)
    {
        var (cookiePrimary, cookieSecondary, provider) = ReadPendingMerge(httpContext);
        if (cookiePrimary != primaryUserId || cookieSecondary is null || provider is null)
        {
            return false;
        }

        var strategy = db.Database.CreateExecutionStrategy();
        var merged = false;
        await strategy.ExecuteAsync(async () =>
        {
            db.ChangeTracker.Clear();

            await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);

            var primary = await db.AppUsers
                .Include(u => u.ExternalLogins)
                .Include(u => u.PasswordCredential)
                .FirstAsync(u => u.Id == primaryUserId, cancellationToken);

            var secondary = await db.AppUsers
                .Include(u => u.ExternalLogins)
                .Include(u => u.PasswordCredential)
                .FirstOrDefaultAsync(u => u.Id == cookieSecondary.Value, cancellationToken);

            if (secondary is null || secondary.MergedIntoUserId is not null)
            {
                await transaction.RollbackAsync(cancellationToken);
                return;
            }

            var now = DateTimeOffset.UtcNow;

            foreach (var external in secondary.ExternalLogins)
            {
                external.UserId = primary.Id;
            }

            if (primary.PasswordCredential is null && secondary.PasswordCredential is not null)
            {
                secondary.PasswordCredential.UserId = primary.Id;
            }

            secondary.MergedIntoUserId = primary.Id;
            secondary.MergedUtc = now;
            secondary.DisabledUtc = now;

            primary.SecurityStamp = Guid.NewGuid().ToString("N");

            db.AuthEvents.Add(new AuthEvent
            {
                UserId = primary.Id,
                Provider = provider,
                EventType = MergedEventType,
                OccurredUtc = now,
                Success = true,
                CorrelationId = correlationId,
            });
            db.AuthEvents.Add(new AuthEvent
            {
                UserId = secondary.Id,
                Provider = provider,
                EventType = MergedSecondaryEventType,
                OccurredUtc = now,
                Success = true,
                CorrelationId = correlationId,
            });

            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            merged = true;
        });

        if (merged)
        {
            ClearPendingMerge(httpContext);
            logger.LogInformation(
                "Merged secondary user {SecondaryUserId} into primary user {PrimaryUserId}.",
                cookieSecondary.Value,
                primaryUserId);
        }

        return merged;
    }

    public void SetPendingMerge(HttpContext httpContext, Guid primaryUserId, Guid secondaryUserId, string provider)
    {
        var payload = $"{primaryUserId}:{secondaryUserId}:{provider}";
        var protectedValue = _protector.Protect(payload);
        httpContext.Response.Cookies.Append(CookieName, protectedValue, new CookieOptions
        {
            HttpOnly = true,
            SameSite = SameSiteMode.Lax,
            Secure = httpContext.Request.IsHttps,
            Expires = DateTimeOffset.UtcNow.Add(PendingLifetime),
        });
    }

    public void ClearPendingMerge(HttpContext httpContext)
    {
        httpContext.Response.Cookies.Delete(CookieName);
    }

    private (Guid? Primary, Guid? Secondary, string? Provider) ReadPendingMerge(HttpContext httpContext)
    {
        var raw = httpContext.Request.Cookies[CookieName];
        if (string.IsNullOrWhiteSpace(raw))
        {
            return (null, null, null);
        }

        // The pending-merge cookie is client-settable, so it must be authenticated:
        // an attacker cannot forge a primary:secondary:provider payload without the
        // data-protection key. Tampered or unsigned values are treated as absent.
        string payload;
        try
        {
            payload = _protector.Unprotect(raw);
        }
        catch
        {
            return (null, null, null);
        }

        var segments = payload.Split(':');
        if (segments.Length != 3)
        {
            return (null, null, null);
        }

        if (!Guid.TryParse(segments[0], out var primary) || !Guid.TryParse(segments[1], out var secondary))
        {
            return (null, null, null);
        }

        var provider = segments[2];
        if (string.IsNullOrWhiteSpace(provider))
        {
            return (null, null, null);
        }

        return (primary, secondary, provider);
    }
}
