using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using Intervals.Api.Auth;
using Intervals.Api.Data.Entities;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace Intervals.Api.Tests;

[Collection(nameof(AuthCollection))]
public sealed class OAuthStartAntiforgeryIntegrationTests
{
    private readonly AuthWebFactory _factory;

    public OAuthStartAntiforgeryIntegrationTests(AuthWebFactory factory)
    {
        _factory = factory;
    }

    private HttpClient CreateClient() =>
        _factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            HandleCookies = true,
        });

    private static async Task<string> GetAntiforgeryTokenAsync(HttpClient client)
    {
        var resp = await client.GetAsync("/auth/antiforgery-token");
        resp.EnsureSuccessStatusCode();
        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
        return doc.RootElement.GetProperty("token").GetString()!;
    }

    private static async Task<HttpResponseMessage> PostFormAsync(
        HttpClient client, string path, IDictionary<string, string> form)
    {
        var content = new FormUrlEncodedContent(form);
        return await client.PostAsync(path, content);
    }

    private static async Task<string?> ReadCodeAsync(HttpResponseMessage resp)
    {
        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
        return doc.RootElement.GetProperty("code").GetString();
    }

    private static async Task<AppUser> SeedUserAsync(AuthWebFactory factory)
    {
        var user = new AppUser
        {
            DisplayName = "OAuth Start User",
            Email = "oauth-start@example.com",
            EmailNormalized = "OAUTH-START@EXAMPLE.COM",
            CreatedUtc = DateTimeOffset.UtcNow,
            LastLoginUtc = DateTimeOffset.UtcNow,
            SecurityStamp = "stamp",
        };
        await factory.SeedUserAsync(user);
        return user;
    }

    [Fact]
    public async Task Login_start_withoutAntiforgery_returns400()
    {
        await _factory.ResetDatabaseAsync();
        var client = CreateClient();

        var resp = await PostFormAsync(
            client,
            "/auth/login/google",
            new Dictionary<string, string> { ["returnUrl"] = "/" });

        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
        Assert.Equal(AuthResultCodes.InvalidRequest, await ReadCodeAsync(resp));
    }

    [Fact]
    public async Task Login_start_withAntiforgery_proceedsToChallenge()
    {
        await _factory.ResetDatabaseAsync();
        var client = CreateClient();

        var token = await GetAntiforgeryTokenAsync(client);
        var resp = await PostFormAsync(
            client,
            "/auth/login/google",
            new Dictionary<string, string>
            {
                ["returnUrl"] = "/",
                ["__RequestVerificationToken"] = token,
            });

        // A successful OAuth start challenges the remote handler, which issues a
        // 302 redirect to the provider's authorization endpoint.
        Assert.Equal(HttpStatusCode.Redirect, resp.StatusCode);
        Assert.NotNull(resp.Headers.Location);
    }

    [Fact]
    public async Task Link_start_withoutAntiforgery_returns400()
    {
        await _factory.ResetDatabaseAsync();
        var client = CreateClient();

        // Authenticate first so RequireAuthorization passes, isolating the
        // antiforgery check.
        var user = await SeedUserAsync(_factory);
        var loginResp = await client.GetAsync($"/auth/test-login/{user.Id}");
        loginResp.EnsureSuccessStatusCode();

        var resp = await PostFormAsync(
            client,
            "/auth/providers/link/google",
            new Dictionary<string, string> { ["returnUrl"] = "/" });

        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
        Assert.Equal(AuthResultCodes.InvalidRequest, await ReadCodeAsync(resp));
    }

    [Fact]
    public async Task Login_start_invalidProvider_redirects_evenWithAntiforgery()
    {
        await _factory.ResetDatabaseAsync();
        var client = CreateClient();

        var token = await GetAntiforgeryTokenAsync(client);
        var resp = await PostFormAsync(
            client,
            "/auth/login/not-a-real-provider",
            new Dictionary<string, string>
            {
                ["returnUrl"] = "/",
                ["__RequestVerificationToken"] = token,
            });

        // Antiforgery passes, but the provider is unknown -> redirect to login.
        Assert.Equal(HttpStatusCode.Redirect, resp.StatusCode);
        Assert.NotNull(resp.Headers.Location);
        Assert.Contains("unknown", resp.Headers.Location!.ToString());
    }

    [Fact]
    public async Task Providers_availability_reports_configured_schemes()
    {
        await _factory.ResetDatabaseAsync();
        var client = CreateClient();

        var resp = await client.GetAsync("/api/auth/providers");

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
        var providers = doc.RootElement.GetProperty("providers").EnumerateArray().ToDictionary(
            p => p.GetProperty("id").GetString()!,
            p => p.GetProperty("available").GetBoolean());

        Assert.True(providers[AuthProviderNames.Google]);
        Assert.True(providers[AuthProviderNames.Microsoft]);
        Assert.True(providers[AuthProviderNames.X]);
    }
}
