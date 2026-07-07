namespace Intervals.Api.Auth;

public sealed record SessionResponse(SessionUser User, IReadOnlyList<ProviderStatus> Providers);

public sealed record SessionUser(string Id, string DisplayName, string? Email, string? AvatarUrl, bool EmailVerified);

public sealed record ProviderStatus(string Id, string Label, bool Linked);

public sealed record ApiError(string Code, string Message, string? CorrelationId);

public sealed record RegisterRequest(string Email, string Password);

public sealed record PasswordLoginRequest(string Email, string Password, bool RememberMe);

public sealed record PasswordAuthSuccess(bool Ok = true);
