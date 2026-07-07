using System.Threading;
using System.Threading.Tasks;

namespace Intervals.Api.Auth;

public interface IPasswordResetService
{
    Task RequestResetAsync(
        string email,
        string? correlationId,
        CancellationToken cancellationToken = default);

    Task<PasswordResetResult> ResetAsync(
        string rawToken,
        string newPassword,
        string? correlationId,
        CancellationToken cancellationToken = default);
}

public sealed record PasswordResetResult(bool Success, string? FailureCode);
