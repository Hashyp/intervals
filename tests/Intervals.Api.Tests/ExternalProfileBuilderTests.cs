using System.Security.Claims;
using Intervals.Api.Auth;
using Xunit;

namespace Intervals.Api.Tests;

public class ExternalProfileBuilderTests
{
    private readonly ExternalProfileBuilder _builder = new();

    [Fact]
    public void Maps_google_subject_to_provider_user_id()
    {
        var principal = new ClaimsPrincipal(new ClaimsIdentity(new[]
        {
            new Claim(ClaimTypes.NameIdentifier, "google-sub-123"),
            new Claim(ClaimTypes.Name, "Avery"),
            new Claim(ClaimTypes.Email, "avery@example.com"),
            new Claim("email_verified", "true"),
            new Claim("picture", "https://img/avatar.png"),
        }, "Google"));

        var profile = _builder.Build("google", principal);

        Assert.Equal("google", profile.Provider);
        Assert.Equal("google-sub-123", profile.ProviderUserId);
        Assert.Equal("Avery", profile.DisplayName);
        Assert.Equal("avery@example.com", profile.Email);
        Assert.True(profile.EmailVerified);
        Assert.Equal("https://img/avatar.png", profile.AvatarUrl);
    }

    [Fact]
    public void Maps_x_id_without_email()
    {
        var principal = new ClaimsPrincipal(new ClaimsIdentity(new[]
        {
            new Claim(ClaimTypes.NameIdentifier, "x-999"),
            new Claim(ClaimTypes.Name, "X User"),
        }, "X"));

        var profile = _builder.Build("x", principal);

        Assert.Equal("x", profile.Provider);
        Assert.Equal("x-999", profile.ProviderUserId);
        Assert.Null(profile.Email);
        Assert.False(profile.EmailVerified);
    }

    [Fact]
    public void Throws_when_subject_missing()
    {
        var principal = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim(ClaimTypes.Name, "NoId") }, "Google"));
        Assert.Throws<System.InvalidOperationException>(() => _builder.Build("google", principal));
    }

    [Fact]
    public void Maps_microsoft_subject_from_name_identifier()
    {
        var principal = new ClaimsPrincipal(new ClaimsIdentity(new[]
        {
            new Claim(ClaimTypes.NameIdentifier, "microsoft-oid-123"),
            new Claim(ClaimTypes.Name, "Sam"),
            new Claim(ClaimTypes.Email, "sam@example.com"),
            new Claim("email_verified", "true"),
        }, "Microsoft"));

        var profile = _builder.Build("microsoft", principal);

        Assert.Equal("microsoft", profile.Provider);
        Assert.Equal("microsoft-oid-123", profile.ProviderUserId);
        Assert.Equal("Sam", profile.DisplayName);
        Assert.Equal("sam@example.com", profile.Email);
        Assert.True(profile.EmailVerified);
    }

    [Fact]
    public void Maps_microsoft_subject_from_oid_when_name_identifier_absent()
    {
        var principal = new ClaimsPrincipal(new ClaimsIdentity(new[]
        {
            new Claim("oid", "microsoft-oid-fallback"),
            new Claim(ClaimTypes.Name, "Sam"),
        }, "Microsoft"));

        var profile = _builder.Build("microsoft", principal);

        Assert.Equal("microsoft", profile.Provider);
        Assert.Equal("microsoft-oid-fallback", profile.ProviderUserId);
    }

    [Fact]
    public void Maps_microsoft_profile_without_email()
    {
        var principal = new ClaimsPrincipal(new ClaimsIdentity(new[]
        {
            new Claim(ClaimTypes.NameIdentifier, "microsoft-oid-456"),
            new Claim(ClaimTypes.Name, "Sam"),
        }, "Microsoft"));

        var profile = _builder.Build("microsoft", principal);

        Assert.Equal("microsoft", profile.Provider);
        Assert.Equal("microsoft-oid-456", profile.ProviderUserId);
        Assert.Null(profile.Email);
        Assert.False(profile.EmailVerified);
    }
}
