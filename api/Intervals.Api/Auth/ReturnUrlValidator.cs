using System;

namespace Intervals.Api.Auth;

public static class ReturnUrlValidator
{
    public const string DefaultReturnUrl = "/";

    public static bool IsSafe(string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return false;
        }

        if (url.Contains('\\'))
        {
            return false;
        }

        if (url.StartsWith("//", StringComparison.Ordinal))
        {
            return false;
        }

        if (!url.StartsWith("/", StringComparison.Ordinal))
        {
            return false;
        }

        if (Uri.IsWellFormedUriString(url, UriKind.Absolute))
        {
            return false;
        }

        var hashIndex = url.IndexOf('#');
        var withoutFragment = hashIndex >= 0 ? url.Substring(0, hashIndex) : url;
        return Uri.IsWellFormedUriString(withoutFragment, UriKind.Relative);
    }

    public static string Sanitize(string? url) => IsSafe(url) ? url! : DefaultReturnUrl;
}
