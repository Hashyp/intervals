namespace Intervals.Api.Auth;

public static class XOAuthDefaults
{
    public const string AuthorizationEndpoint = "https://x.com/i/v2/oauth2/authorize";
    public const string TokenEndpoint = "https://api.x.com/2/oauth2/token";
    public const string UserInformationEndpoint = "https://api.x.com/2/users/me?user.fields=id,name,profile_image_url";
    public const string ScopeUsersRead = "users.read";
    public const string ScopeUsersEmail = "users.email";
}
