namespace Intervals.Api.Auth;

public sealed class AuthOptions
{
    public const string SectionName = "Auth";
    public string CookieName { get; set; } = "Intervals.App";
    public TimeSpan SessionLifetime { get; set; } = TimeSpan.FromHours(8);
    public TimeSpan RememberMeLifetime { get; set; } = TimeSpan.FromDays(30);
    public string LoginPath { get; set; } = "/login";
    public PasswordOptions Password { get; set; } = new();
}

public sealed class PasswordOptions
{
    public int MinLength { get; set; } = 8;
    public int MaxLength { get; set; } = 128;
    public int MaxFailedAttempts { get; set; } = 5;
    public TimeSpan LockoutDuration { get; set; } = TimeSpan.FromMinutes(5);
}
