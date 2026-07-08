using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Http;

namespace Intervals.Api.Auth;

public static class AuthRequests
{
    public static async Task<IResult?> ValidateAntiforgeryAsync(
        HttpContext context,
        IAntiforgery antiforgery,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await antiforgery.ValidateRequestAsync(context);
            return null;
        }
        catch (AntiforgeryValidationException)
        {
            return Results.BadRequest(new ApiError(
                AuthResultCodes.InvalidRequest,
                "Antiforgery validation failed.",
                context.GetCorrelationId()));
        }
    }
}
