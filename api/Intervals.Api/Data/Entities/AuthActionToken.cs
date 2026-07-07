namespace Intervals.Api.Data.Entities;

public sealed class AuthActionToken
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UserId { get; set; }
    public AppUser? User { get; set; }
    public string Purpose { get; set; } = string.Empty;
    public string TokenHash { get; set; } = string.Empty;
    public string? Email { get; set; }
    public string? EmailNormalized { get; set; }
    public DateTimeOffset CreatedUtc { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset ExpiresUtc { get; set; }
    public DateTimeOffset? ConsumedUtc { get; set; }
    public DateTimeOffset? RevokedUtc { get; set; }
    public string? CorrelationId { get; set; }
}
