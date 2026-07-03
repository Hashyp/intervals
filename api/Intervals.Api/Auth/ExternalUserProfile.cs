namespace Intervals.Api.Auth;

public sealed record ExternalUserProfile(
    string Provider,
    string ProviderUserId,
    string? DisplayName,
    string? Email,
    bool EmailVerified,
    string? AvatarUrl);
