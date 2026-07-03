namespace Intervals.Api.Auth;

public sealed class AuthOptions
{
    public const string SectionName = "Auth";
    public string CookieName { get; set; } = "Intervals.App";
    public TimeSpan SessionLifetime { get; set; } = TimeSpan.FromHours(8);
    public TimeSpan RememberMeLifetime { get; set; } = TimeSpan.FromDays(30);
    public string LoginPath { get; set; } = "/login";
}
