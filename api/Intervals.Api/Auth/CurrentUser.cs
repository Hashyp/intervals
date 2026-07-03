using System;
using System.Security.Claims;

namespace Intervals.Api.Auth;

public static class CurrentUser
{
    public const string UserIdClaimType = "intervals:user_id";

    public static Guid? GetUserId(ClaimsPrincipal? principal)
    {
        if (principal?.Identity?.IsAuthenticated != true)
        {
            return null;
        }

        var claim = principal.FindFirst(UserIdClaimType);
        return Guid.TryParse(claim?.Value, out var id) ? id : null;
    }
}
