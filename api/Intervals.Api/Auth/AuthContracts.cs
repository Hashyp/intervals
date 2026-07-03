namespace Intervals.Api.Auth;

public sealed record SessionResponse(SessionUser User, IReadOnlyList<ProviderStatus> Providers);

public sealed record SessionUser(string Id, string DisplayName, string? Email, string? AvatarUrl);

public sealed record ProviderStatus(string Id, string Label, bool Linked);

public sealed record ApiError(string Code, string Message, string? CorrelationId);
