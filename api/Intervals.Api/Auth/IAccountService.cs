using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Intervals.Api.Data.Entities;

namespace Intervals.Api.Auth;

public interface IAccountService
{
    Task<AccountLoginResult> LoginAsync(
        ExternalUserProfile profile,
        string? correlationId,
        CancellationToken cancellationToken = default);

    Task<AppUser?> GetAsync(Guid userId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<string>> GetLinkedProvidersAsync(
        Guid userId,
        CancellationToken cancellationToken = default);

    Task RecordLogoutAsync(Guid? userId, string? correlationId, CancellationToken cancellationToken = default);
}

public sealed record AccountLoginResult(AppUser User, bool CreatedUser);
