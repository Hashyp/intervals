using Intervals.Api.Auth;
using Xunit;

namespace Intervals.Api.Tests;

public class ReturnUrlValidatorTests
{
    [Theory]
    [InlineData("/")]
    [InlineData("/login")]
    [InlineData("/?mode=mixed")]
    [InlineData("/path/sub?q=1&x=2")]
    [InlineData("/#anchor")]
    public void Accepts_local_relative_paths(string url) => Assert.True(ReturnUrlValidator.IsSafe(url));

    [Theory]
    [InlineData("https://evil.com/")]
    [InlineData("//evil.com/")]
    [InlineData("/\\evil")]
    [InlineData("\\\\evil")]
    [InlineData("javascript:alert(1)")]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("relative-no-slash")]
    public void Rejects_unsafe_urls(string? url) => Assert.False(ReturnUrlValidator.IsSafe(url));

    [Fact]
    public void Sanitize_returns_default_for_unsafe() => Assert.Equal("/", ReturnUrlValidator.Sanitize("//evil.com/"));

    [Fact]
    public void Sanitize_returns_value_for_safe() => Assert.Equal("/login", ReturnUrlValidator.Sanitize("/login"));
}
