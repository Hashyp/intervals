namespace Intervals.Api.Auth;

public sealed class AuthOptions
{
    public const string SectionName = "Auth";
    public string CookieName { get; set; } = "Intervals.App";
    public TimeSpan SessionLifetime { get; set; } = TimeSpan.FromHours(8);
    public TimeSpan RememberMeLifetime { get; set; } = TimeSpan.FromDays(30);
    public string LoginPath { get; set; } = "/login";
    public PasswordOptions Password { get; set; } = new();
    public ForwardedHeadersConfig ForwardedHeaders { get; set; } = new();
}

public sealed class PasswordOptions
{
    public int MinLength { get; set; } = 8;
    public int MaxLength { get; set; } = 128;
    public int MaxFailedAttempts { get; set; } = 5;
    public TimeSpan LockoutDuration { get; set; } = TimeSpan.FromMinutes(5);
}

public sealed class ForwardedHeadersConfig
{
    // When true, all proxies/networks are trusted (clears KnownProxies/KnownNetworks).
    // Defaults to false; Development/Testing always behave as trust-all regardless.
    public bool TrustAll { get; set; }

    // Comma-separated list of trusted proxy IP addresses (no CIDRs) for production
    // deployments behind a known proxy. Invalid entries are skipped + logged.
    public string KnownProxies { get; set; } = string.Empty;
}
