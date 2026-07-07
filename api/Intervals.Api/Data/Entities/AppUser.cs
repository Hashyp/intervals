namespace Intervals.Api.Data.Entities;

public sealed class AppUser
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string DisplayName { get; set; } = string.Empty;
    public string? Email { get; set; }
    public string? EmailNormalized { get; set; }
    public string? AvatarUrl { get; set; }
    public DateTimeOffset CreatedUtc { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? LastLoginUtc { get; set; }
    public DateTimeOffset? DisabledUtc { get; set; }
    public string? SecurityStamp { get; set; }

    public List<ExternalLogin> ExternalLogins { get; set; } = new();

    public PasswordCredential? PasswordCredential { get; set; }
}
