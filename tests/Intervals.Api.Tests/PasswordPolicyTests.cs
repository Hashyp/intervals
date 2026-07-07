using Intervals.Api.Auth;
using Microsoft.Extensions.Options;
using Xunit;

namespace Intervals.Api.Tests;

public sealed class PasswordPolicyTests
{
    private static PasswordPolicy CreatePolicy(int minLength = 8, int maxLength = 128)
    {
        var options = Options.Create(new AuthOptions
        {
            Password = new PasswordOptions
            {
                MinLength = minLength,
                MaxLength = maxLength,
                MaxFailedAttempts = 5,
                LockoutDuration = System.TimeSpan.FromMinutes(5),
            },
        });
        return new PasswordPolicy(options);
    }

    [Fact]
    public void Valid_long_enough_password_returns_true()
    {
        var policy = CreatePolicy();
        var ok = policy.IsValid("supersecret", out var code);
        Assert.True(ok);
        Assert.Equal(string.Empty, code);
    }

    [Fact]
    public void Too_short_password_returns_false()
    {
        var policy = CreatePolicy();
        var ok = policy.IsValid("short", out var code);
        Assert.False(ok);
        Assert.Equal(AuthResultCodes.WeakPassword, code);
    }

    [Fact]
    public void Too_long_password_returns_false()
    {
        var policy = CreatePolicy(maxLength: 5);
        var ok = policy.IsValid("thisiswaytoolong", out var code);
        Assert.False(ok);
        Assert.Equal(AuthResultCodes.WeakPassword, code);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\t")]
    public void Whitespace_null_or_empty_returns_false(string? password)
    {
        var policy = CreatePolicy();
        var ok = policy.IsValid(password, out var code);
        Assert.False(ok);
        Assert.Equal(AuthResultCodes.WeakPassword, code);
    }

    [Fact]
    public void Exactly_min_length_returns_true()
    {
        var policy = CreatePolicy(minLength: 4);
        var ok = policy.IsValid("abcd", out _);
        Assert.True(ok);
    }

    [Fact]
    public void Exactly_max_length_returns_true()
    {
        var policy = CreatePolicy(minLength: 1, maxLength: 4);
        var ok = policy.IsValid("abcd", out _);
        Assert.True(ok);
    }
}
