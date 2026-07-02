namespace Intervals.Api.Data.Entities;

public sealed class AuthEvent
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid? UserId { get; set; }
    public string? Provider { get; set; }
    public string EventType { get; set; } = string.Empty;
    public DateTimeOffset OccurredUtc { get; set; } = DateTimeOffset.UtcNow;
    public bool Success { get; set; }
    public string? FailureCode { get; set; }
    public string? CorrelationId { get; set; }
}
