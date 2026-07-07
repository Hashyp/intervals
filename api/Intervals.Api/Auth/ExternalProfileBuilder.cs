using System;
using System.Security.Claims;

namespace Intervals.Api.Auth;

public sealed class ExternalProfileBuilder : IExternalProfileBuilder
{
    public ExternalUserProfile Build(string provider, ClaimsPrincipal principal)
    {
        var normalized = AuthProviderNames.Normalize(provider);
        var providerUserId = GetProviderUserId(normalized, principal);
        if (string.IsNullOrWhiteSpace(providerUserId))
        {
            throw new InvalidOperationException(
                "External principal is missing a durable provider user id.");
        }

        var displayName =
            principal.FindFirst(ClaimTypes.Name)?.Value
            ?? principal.FindFirst("name")?.Value;

        var email =
            principal.FindFirst(ClaimTypes.Email)?.Value
            ?? principal.FindFirst("email")?.Value;

        var emailVerified = TryGetBooleanClaim(principal, "email_verified");

        var avatarUrl =
            principal.FindFirst("picture")?.Value
            ?? principal.FindFirst("urn:google:picture")?.Value
            ?? principal.FindFirst("avatar")?.Value;

        return new ExternalUserProfile(normalized, providerUserId, displayName, email, emailVerified, avatarUrl);
    }

    private static string? GetProviderUserId(string provider, ClaimsPrincipal principal)
    {
        if (provider == AuthProviderNames.Google)
        {
            return principal.FindFirst(ClaimTypes.NameIdentifier)?.Value
                ?? principal.FindFirst("sub")?.Value;
        }

        if (provider == AuthProviderNames.Microsoft)
        {
            return principal.FindFirst(ClaimTypes.NameIdentifier)?.Value
                ?? principal.FindFirst("oid")?.Value
                ?? principal.FindFirst("sub")?.Value;
        }

        if (provider == AuthProviderNames.X)
        {
            return principal.FindFirst(ClaimTypes.NameIdentifier)?.Value
                ?? principal.FindFirst("urn:x:id")?.Value
                ?? principal.FindFirst("sub")?.Value;
        }

        return principal.FindFirst(ClaimTypes.NameIdentifier)?.Value;
    }

    private static bool TryGetBooleanClaim(ClaimsPrincipal principal, string claimType)
    {
        var value = principal.FindFirst(claimType)?.Value;
        return bool.TryParse(value, out var result) && result;
    }
}
