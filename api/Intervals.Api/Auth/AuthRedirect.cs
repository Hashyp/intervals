namespace Intervals.Api.Auth;

public static class AuthRedirect
{
    public static string Build(string webBaseUrl, string path, string? code = null)
    {
        var trimmedBase = (webBaseUrl ?? string.Empty).TrimEnd('/');
        var url = string.IsNullOrEmpty(trimmedBase) ? path : trimmedBase + path;

        if (!string.IsNullOrEmpty(code))
        {
            url += (url.Contains('?') ? "&" : "?") + "auth=" + code;
        }

        return url;
    }
}
