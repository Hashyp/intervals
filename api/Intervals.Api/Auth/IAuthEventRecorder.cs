using System;
using System.Threading;
using System.Threading.Tasks;

namespace Intervals.Api.Auth;

public interface IAuthEventRecorder
{
    Task RecordAsync(
        string eventType,
        Guid? userId,
        string? provider,
        bool success,
        string? correlationId,
        CancellationToken cancellationToken = default);
}
