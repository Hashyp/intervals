using Intervals.Api.Auth;
using Xunit;

namespace Intervals.Api.Tests;

public class AuthHelpersTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\t")]
    public void Normalize_returns_null_for_blank_input(string? email) =>
        Assert.Null(AuthEmail.Normalize(email));

    [Theory]
    [InlineData("User@Example.com", "USER@EXAMPLE.COM")]
    [InlineData("  User@Example.com  ", "USER@EXAMPLE.COM")]
    [InlineData("\tuser@example.com\n", "USER@EXAMPLE.COM")]
    public void Normalize_trims_and_upper_cases(string email, string expected) =>
        Assert.Equal(expected, AuthEmail.Normalize(email));

    [Fact]
    public void Normalize_default_cap_accepts_length_up_to_320()
    {
        var email = new string('a', 308) + "@example.com";
        Assert.Equal(320, email.Length);
        Assert.Equal(email.ToUpperInvariant(), AuthEmail.Normalize(email));
    }

    [Fact]
    public void Normalize_default_cap_rejects_length_over_320()
    {
        var email = new string('a', 309) + "@example.com";
        Assert.Equal(321, email.Length);
        Assert.Null(AuthEmail.Normalize(email));
    }

    [Fact]
    public void Normalize_without_cap_preserves_overlength_email()
    {
        var email = new string('a', 400) + "@example.com";
        Assert.True(email.Length > 320);
        Assert.Equal(email.ToUpperInvariant(), AuthEmail.Normalize(email, int.MaxValue));
    }
}
