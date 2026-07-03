using System.Security.Claims;

namespace Intervals.Api.Auth;

public interface IExternalProfileBuilder
{
    ExternalUserProfile Build(string provider, ClaimsPrincipal principal);
}
