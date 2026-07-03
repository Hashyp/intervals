using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using Intervals.Api.Auth;
using Intervals.Api.Data;
using Intervals.Api.Data.Entities;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Intervals.Api.Tests;

[Collection(nameof(AuthCollection))]
public sealed class AuthIntegrationTests
{
    private readonly AuthWebFactory _factory;

    public AuthIntegrationTests(AuthWebFactory factory)
    {
        _factory = factory;
    }

    private static ExternalUserProfile GoogleProfile(string id, string? email = "user@example.com") =>
        new("google", id, "Test User", email, EmailVerified: true, "https://img/avatar.png");

    private HttpClient CreateClient() =>
        _factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            HandleCookies = true,
        });

    private static (AccountLoginResult Result, IServiceScope Scope) LoginViaService(
        AuthWebFactory factory, ExternalUserProfile profile)
    {
        var scope = factory.Services.CreateScope();
        var accounts = scope.ServiceProvider.GetRequiredService<IAccountService>();
        var result = accounts.LoginAsync(profile, correlationId: null).GetAwaiter().GetResult();
        return (result, scope);
    }

    private static IntervalsDbContext NewDb(AuthWebFactory factory)
    {
        var scope = factory.Services.CreateScope();
        return scope.ServiceProvider.GetRequiredService<IntervalsDbContext>();
    }

    [Fact]
    public async Task Anonymous_session_returns_401()
    {
        await _factory.ResetDatabaseAsync();
        var client = CreateClient();

        var resp = await client.GetAsync("/api/session");

        Assert.Equal(HttpStatusCode.Unauthorized, resp.StatusCode);
    }

    [Fact]
    public async Task Anonymous_status_returns_401()
    {
        await _factory.ResetDatabaseAsync();
        var client = CreateClient();

        var resp = await client.GetAsync("/api/status");

        Assert.Equal(HttpStatusCode.Unauthorized, resp.StatusCode);
    }

    [Fact]
    public async Task Authenticated_session_returns_user_summary()
    {
        await _factory.ResetDatabaseAsync();
        var (login, scope) = LoginViaService(_factory, GoogleProfile("google-sub-1"));
        using (scope) { }

        var client = CreateClient();
        var loginResp = await client.GetAsync($"/auth/test-login/{login.User.Id}");
        loginResp.EnsureSuccessStatusCode();

        var sessionResp = await client.GetAsync("/api/session");
        sessionResp.EnsureSuccessStatusCode();

        using var doc = JsonDocument.Parse(await sessionResp.Content.ReadAsStringAsync());
        var root = doc.RootElement;
        Assert.Equal("Test User", root.GetProperty("user").GetProperty("displayName").GetString());

        var providers = root.GetProperty("providers").EnumerateArray();
        JsonElement? google = null;
        foreach (var p in providers)
        {
            if (p.GetProperty("id").GetString() == "google")
            {
                google = p;
            }
        }
        Assert.NotNull(google);
        Assert.True(google!.Value.GetProperty("linked").GetBoolean());
    }

    [Fact]
    public async Task Authenticated_status_succeeds()
    {
        await _factory.ResetDatabaseAsync();
        var (login, scope) = LoginViaService(_factory, GoogleProfile("google-sub-2"));
        using (scope) { }

        var client = CreateClient();
        var loginResp = await client.GetAsync($"/auth/test-login/{login.User.Id}");
        loginResp.EnsureSuccessStatusCode();

        var statusResp = await client.GetAsync("/api/status");
        Assert.Equal(HttpStatusCode.OK, statusResp.StatusCode);
    }

    [Fact]
    public async Task Login_creates_user_and_provider_link_then_reuses()
    {
        await _factory.ResetDatabaseAsync();
        var profile = GoogleProfile("google-sub-3");

        var (first, scope1) = LoginViaService(_factory, profile);
        using (scope1) { }
        Assert.True(first.CreatedUser);

        var (second, scope2) = LoginViaService(_factory, profile);
        using (scope2) { }
        Assert.False(second.CreatedUser);

        await using var db = NewDb(_factory);
        var userCount = await db.AppUsers.CountAsync();
        var externalCount = await db.ExternalLogins.CountAsync();
        Assert.Equal(1, userCount);
        Assert.Equal(1, externalCount);
    }

    [Fact]
    public async Task Email_match_does_not_auto_merge_accounts()
    {
        await _factory.ResetDatabaseAsync();

        var (first, scope1) = LoginViaService(_factory, GoogleProfile("google-A", "same@example.com"));
        using (scope1) { }
        Assert.True(first.CreatedUser);

        var (second, scope2) = LoginViaService(_factory, GoogleProfile("google-B", "same@example.com"));
        using (scope2) { }
        Assert.True(second.CreatedUser);
        Assert.NotEqual(first.User.Id, second.User.Id);

        await using var db = NewDb(_factory);
        var userCount = await db.AppUsers.CountAsync();
        var externalCount = await db.ExternalLogins.CountAsync();
        Assert.Equal(2, userCount);
        Assert.Equal(2, externalCount);
    }

    [Fact]
    public async Task Login_records_auth_event()
    {
        await _factory.ResetDatabaseAsync();

        var (login, scope) = LoginViaService(_factory, GoogleProfile("google-sub-4"));
        using (scope) { }

        await using var db = NewDb(_factory);
        var events = await db.AuthEvents.ToListAsync();
        Assert.Contains(events, e =>
            e.EventType == AuthEventTypes.LoginSuccess && e.Success && e.Provider == "google" && e.UserId == login.User.Id);
    }

    [Fact]
    public async Task Logout_clears_session()
    {
        await _factory.ResetDatabaseAsync();
        var (login, scope) = LoginViaService(_factory, GoogleProfile("google-sub-5"));
        using (scope) { }

        var client = CreateClient();
        var loginResp = await client.GetAsync($"/auth/test-login/{login.User.Id}");
        loginResp.EnsureSuccessStatusCode();

        var tokenResp = await client.GetAsync("/auth/antiforgery-token");
        tokenResp.EnsureSuccessStatusCode();
        using var tokenDoc = JsonDocument.Parse(await tokenResp.Content.ReadAsStringAsync());
        var token = tokenDoc.RootElement.GetProperty("token").GetString();
        Assert.False(string.IsNullOrEmpty(token));

        var postLogout = new HttpRequestMessage(HttpMethod.Post, "/auth/logout");
        postLogout.Headers.Add("X-CSRF-TOKEN", token);
        var logoutResp = await client.SendAsync(postLogout);
        Assert.Equal(HttpStatusCode.Redirect, logoutResp.StatusCode);

        var sessionResp = await client.GetAsync("/api/session");
        Assert.Equal(HttpStatusCode.Unauthorized, sessionResp.StatusCode);
    }

    [Fact]
    public async Task Logout_requires_authentication()
    {
        await _factory.ResetDatabaseAsync();
        var client = CreateClient();

        var tokenResp = await client.GetAsync("/auth/antiforgery-token");
        tokenResp.EnsureSuccessStatusCode();
        using var tokenDoc = JsonDocument.Parse(await tokenResp.Content.ReadAsStringAsync());
        var token = tokenDoc.RootElement.GetProperty("token").GetString();

        var postLogout = new HttpRequestMessage(HttpMethod.Post, "/auth/logout");
        postLogout.Headers.Add("X-CSRF-TOKEN", token);
        var logoutResp = await client.SendAsync(postLogout);

        Assert.True(
            logoutResp.StatusCode == HttpStatusCode.Redirect ||
            logoutResp.StatusCode == HttpStatusCode.Unauthorized,
            $"Expected 302 or 401, got {(int)logoutResp.StatusCode}");
    }

    [Fact]
    public async Task Callback_without_external_cookie_redirects_to_login()
    {
        await _factory.ResetDatabaseAsync();
        var client = CreateClient();

        var resp = await client.GetAsync("/auth/complete/google");

        Assert.Equal(HttpStatusCode.Redirect, resp.StatusCode);
        Assert.NotNull(resp.Headers.Location);
        Assert.Contains("/login", resp.Headers.Location!.ToString());
    }
}
