using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;

namespace Intervals.Api.Auth;

public interface IAccountMergeService
{
    Task<PendingMergeDetail?> GetPendingMergeAsync(
        Guid primaryUserId,
        HttpContext httpContext,
        CancellationToken cancellationToken = default);

    Task<bool> MergeAsync(
        Guid primaryUserId,
        HttpContext httpContext,
        string? correlationId,
        CancellationToken cancellationToken = default);

    void SetPendingMerge(HttpContext httpContext, Guid primaryUserId, Guid secondaryUserId, string provider);

    void ClearPendingMerge(HttpContext httpContext);
}

public sealed record PendingMergeDetail(
    Guid PrimaryUserId,
    string PrimaryDisplayName,
    string? PrimaryEmail,
    Guid SecondaryUserId,
    string SecondaryDisplayName,
    string? SecondaryEmail,
    string Provider);
