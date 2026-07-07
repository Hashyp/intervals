namespace Intervals.Api.Auth;

public static class AuthResultCodes
{
    public const string AuthRequired = "auth_required";
    public const string Forbidden = "forbidden";
    public const string Cancelled = "cancelled";
    public const string ProviderError = "provider_error";
    public const string Unknown = "unknown";
    public const string InvalidCredentials = "invalid_credentials";
    public const string EmailTaken = "email_taken";
    public const string WeakPassword = "weak_password";
    public const string LockedOut = "locked_out";
    public const string Disabled = "disabled";
    public const string InvalidRequest = "invalid_request";
}
