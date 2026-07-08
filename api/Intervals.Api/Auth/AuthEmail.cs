namespace Intervals.Api.Auth;

public static class AuthEmail
{
    public static string? Normalize(string? email, int maxLength = 320)
    {
        if (string.IsNullOrWhiteSpace(email))
        {
            return null;
        }

        var trimmed = email.Trim();
        if (trimmed.Length > maxLength)
        {
            return null;
        }

        return trimmed.ToUpperInvariant();
    }
}
