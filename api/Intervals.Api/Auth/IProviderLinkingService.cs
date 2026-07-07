namespace Intervals.Api.Auth;

public interface IProviderLinkingService
{
    Task<LinkCompletionResult> CompleteLinkAsync(
        ExternalUserProfile profile,
        Guid primaryUserId,
        string? correlationId,
        CancellationToken cancellationToken = default);
}

public sealed record LinkCompletionResult(LinkOutcome Outcome, Guid? SecondaryUserId = null);

public enum LinkOutcome
{
    AlreadyLinked,
    Linked,
    Collision,
}
