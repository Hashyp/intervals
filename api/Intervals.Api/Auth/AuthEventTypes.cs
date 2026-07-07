namespace Intervals.Api.Auth;

public static class AuthEventTypes
{
    public const string LoginStart = "login_start";
    public const string LoginSuccess = "login_success";
    public const string LoginFailure = "login_failure";
    public const string Logout = "logout";
    public const string RegisterSuccess = "register_success";
    public const string PasswordChanged = "password_changed";
    public const string PasswordAdded = "password_added";
    public const string AccountUnlinked = "account_unlinked";
    public const string EmailVerificationSent = "email_verification_sent";
    public const string EmailVerified = "email_verified";
    public const string PasswordResetRequested = "password_reset_requested";
}
