using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Intervals.Api.Auth;

public interface IAccountSettingsService
{
    Task<AccountDetail> GetDetailAsync(Guid userId, CancellationToken cancellationToken = default);

    Task<PasswordManagementResult> ChangePasswordAsync(
        Guid userId,
        string currentPassword,
        string newPassword,
        string? correlationId,
        CancellationToken cancellationToken = default);

    Task<PasswordManagementResult> AddPasswordAsync(
        Guid userId,
        string email,
        string newPassword,
        string? correlationId,
        CancellationToken cancellationToken = default);

    Task<UnlinkResult> UnlinkAsync(
        Guid userId,
        string provider,
        string? correlationId,
        CancellationToken cancellationToken = default);
}

public sealed record AccountDetail(
    Guid UserId,
    string DisplayName,
    string? Email,
    bool EmailVerified,
    bool HasPassword,
    IReadOnlyList<AccountProvider> Providers);

public sealed record AccountProvider(
    string Id,
    string Label,
    bool Linked,
    string? Email,
    DateTimeOffset? LastLoginUtc);

public sealed record PasswordManagementResult(bool Success, string? FailureCode);

public sealed record UnlinkResult(bool Success, string? FailureCode);

/// <summary>
/// Failure code returned by <see cref="IAccountSettingsService.UnlinkAsync"/> when removing
/// the provider would leave the account with no usable sign-in method.
/// </summary>
public static class AccountSettingsResultCodes
{
    public const string LastLoginMethod = "last_login_method";
}
