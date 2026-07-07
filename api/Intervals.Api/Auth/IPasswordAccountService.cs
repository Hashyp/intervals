using System;
using System.Threading;
using System.Threading.Tasks;
using Intervals.Api.Data.Entities;

namespace Intervals.Api.Auth;

public interface IPasswordAccountService
{
    Task<PasswordRegisterResult> RegisterAsync(
        string email,
        string password,
        string? correlationId,
        CancellationToken cancellationToken = default);

    Task<PasswordLoginResult> AuthenticateAsync(
        string email,
        string password,
        string? correlationId,
        CancellationToken cancellationToken = default);
}

public sealed record PasswordRegisterResult(bool Success, AppUser? User, string? FailureCode);

public sealed record PasswordLoginResult(bool Success, AppUser? User, string? FailureCode);
