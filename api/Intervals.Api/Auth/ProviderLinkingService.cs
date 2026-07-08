using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Intervals.Api.Data;
using Intervals.Api.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Intervals.Api.Auth;

public sealed class ProviderLinkingService(
    IntervalsDbContext db,
    IAuthEventRecorder recorder,
    ILogger<ProviderLinkingService> logger) : IProviderLinkingService
{
    public const string LinkSuccessEventType = "account_link_success";

    public async Task<LinkCompletionResult> CompleteLinkAsync(
        ExternalUserProfile profile,
        Guid primaryUserId,
        string? correlationId,
        CancellationToken cancellationToken = default)
    {
        var existing = await db.ExternalLogins.FirstOrDefaultAsync(
            e => e.Provider == profile.Provider && e.ProviderUserId == profile.ProviderUserId,
            cancellationToken);

        if (existing is not null)
        {
            if (existing.UserId == primaryUserId)
            {
                return new LinkCompletionResult(LinkOutcome.AlreadyLinked);
            }

            logger.LogWarning(
                "Provider link collision for {Provider}/{ProviderUserId}: belongs to {SecondaryUserId}, requested by {PrimaryUserId}.",
                profile.Provider,
                profile.ProviderUserId,
                existing.UserId,
                primaryUserId);

            return new LinkCompletionResult(LinkOutcome.Collision, existing.UserId);
        }

        await AttachAsync(profile, primaryUserId, correlationId, cancellationToken);

        await recorder.RecordAsync(
            LinkSuccessEventType,
            primaryUserId,
            profile.Provider,
            success: true,
            correlationId,
            cancellationToken);

        logger.LogInformation(
            "Provider link success for user {UserId} via {Provider}.",
            primaryUserId,
            profile.Provider);

        return new LinkCompletionResult(LinkOutcome.Linked);
    }

    private async Task AttachAsync(
        ExternalUserProfile profile,
        Guid primaryUserId,
        string? correlationId,
        CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        db.ExternalLogins.Add(new ExternalLogin
        {
            UserId = primaryUserId,
            Provider = profile.Provider,
            ProviderUserId = profile.ProviderUserId,
            Email = profile.Email,
            EmailVerified = profile.EmailVerified,
            DisplayName = profile.DisplayName,
            AvatarUrl = profile.AvatarUrl,
            CreatedUtc = now,
            LastLoginUtc = now,
        });

        await db.SaveChangesAsync(cancellationToken);
    }
}
