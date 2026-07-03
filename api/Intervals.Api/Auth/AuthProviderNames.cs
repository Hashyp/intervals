using System;

namespace Intervals.Api.Auth;

public static class AuthProviderNames
{
    public const string Google = "google";
    public const string X = "x";
    public const string GoogleScheme = "Google";
    public const string XScheme = "X";

    public static readonly IReadOnlySet<string> All =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase) { Google, X };

    public static bool IsValid(string? provider) =>
        !string.IsNullOrWhiteSpace(provider) && All.Contains(provider);

    public static string Normalize(string? provider) =>
        string.IsNullOrWhiteSpace(provider) ? string.Empty : provider.ToLowerInvariant();

    public static string? ToScheme(string? provider) => Normalize(provider) switch
    {
        Google => GoogleScheme,
        X => XScheme,
        _ => null,
    };
}
