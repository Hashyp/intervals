using System;
using System.Threading;
using System.Threading.Tasks;
using Intervals.Api.Data;
using Intervals.Api.Data.Entities;

namespace Intervals.Api.Auth;

public sealed class AuthEventRecorder(IntervalsDbContext db) : IAuthEventRecorder
{
    public async Task RecordAsync(
        string eventType,
        Guid? userId,
        string? provider,
        bool success,
        string? correlationId,
        CancellationToken cancellationToken = default)
    {
        db.AuthEvents.Add(new AuthEvent
        {
            UserId = userId,
            Provider = provider,
            EventType = eventType,
            OccurredUtc = DateTimeOffset.UtcNow,
            Success = success,
            CorrelationId = correlationId,
        });
        await db.SaveChangesAsync(cancellationToken);
    }
}
