using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using Intervals.Api.Auth;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace Intervals.Api.Tests;

[Collection(nameof(AuthCollection))]
public sealed class PasswordAuthIntegrationTests
{
    private readonly AuthWebFactory _factory;

    public PasswordAuthIntegrationTests(AuthWebFactory factory)
    {
        _factory = factory;
    }

    private HttpClient CreateClient() =>
        _factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            HandleCookies = true,
        });

    private static async Task<string> GetTokenAsync(HttpClient client)
    {
        var resp = await client.GetAsync("/auth/antiforgery-token");
        resp.EnsureSuccessStatusCode();
        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
        return doc.RootElement.GetProperty("token").GetString()!;
    }

    private static async Task<HttpResponseMessage> PostJsonAsync(
        HttpClient client, string path, object body, string? token = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, path);
        if (token is not null)
        {
            request.Headers.Add("X-CSRF-TOKEN", token);
        }
        request.Content = JsonContent.Create(body);
        return await client.SendAsync(request);
    }

    private const string ValidPassword = "supersecret-password-1";

    [Fact]
    public async Task Register_valid_then_session_reports_password_linked_and_unverified()
    {
        await _factory.ResetDatabaseAsync();
        var client = CreateClient();

        var token = await GetTokenAsync(client);
        var resp = await PostJsonAsync(
            client,
            "/auth/register",
            new { email = "user@example.com", password = ValidPassword },
            token);
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);

        using var regDoc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
        Assert.True(regDoc.RootElement.GetProperty("ok").GetBoolean());

        var sessionResp = await client.GetAsync("/api/session");
        sessionResp.EnsureSuccessStatusCode();

        using var sessionDoc = JsonDocument.Parse(await sessionResp.Content.ReadAsStringAsync());
        var root = sessionDoc.RootElement;
        Assert.False(root.GetProperty("user").GetProperty("emailVerified").GetBoolean());

        var providers = root.GetProperty("providers").EnumerateArray();
        JsonElement? password = null;
        foreach (var p in providers)
        {
            if (p.GetProperty("id").GetString() == AuthProviderNames.Password)
            {
                password = p;
            }
        }
        Assert.NotNull(password);
        Assert.True(password!.Value.GetProperty("linked").GetBoolean());
    }

    [Fact]
    public async Task Register_duplicate_email_returns_409()
    {
        await _factory.ResetDatabaseAsync();
        var client = CreateClient();

        var token1 = await GetTokenAsync(client);
        var first = await PostJsonAsync(
            client, "/auth/register",
            new { email = "user@example.com", password = ValidPassword }, token1);
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);

        var token2 = await GetTokenAsync(client);
        var second = await PostJsonAsync(
            client, "/auth/register",
            new { email = "user@example.com", password = ValidPassword }, token2);

        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
        using var doc = JsonDocument.Parse(await second.Content.ReadAsStringAsync());
        Assert.Equal(AuthResultCodes.EmailTaken, doc.RootElement.GetProperty("code").GetString());
    }

    [Fact]
    public async Task Register_weak_password_returns_400()
    {
        await _factory.ResetDatabaseAsync();
        var client = CreateClient();

        var token = await GetTokenAsync(client);
        var resp = await PostJsonAsync(
            client, "/auth/register",
            new { email = "user@example.com", password = "short" }, token);

        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
        Assert.Equal(AuthResultCodes.WeakPassword, doc.RootElement.GetProperty("code").GetString());
    }

    [Fact]
    public async Task Register_with_oversized_email_returns_400_not_500()
    {
        await _factory.ResetDatabaseAsync();
        var client = CreateClient();

        var token = await GetTokenAsync(client);
        var oversizedEmail = new string('a', PasswordAccountService.MaxEmailLength) + "@x.com";
        var resp = await PostJsonAsync(
            client,
            "/auth/register",
            new { email = oversizedEmail, password = ValidPassword },
            token);

        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
        Assert.Equal(AuthResultCodes.InvalidRequest, doc.RootElement.GetProperty("code").GetString());
    }

    [Fact]
    public async Task Register_without_antiforgery_returns_400()
    {
        await _factory.ResetDatabaseAsync();
        var client = CreateClient();

        var resp = await PostJsonAsync(
            client, "/auth/register",
            new { email = "user@example.com", password = ValidPassword }, token: null);

        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
        Assert.Equal(AuthResultCodes.InvalidRequest, doc.RootElement.GetProperty("code").GetString());
    }

    [Fact]
    public async Task Login_with_correct_credentials_authenticates_session()
    {
        await _factory.ResetDatabaseAsync();
        var client = CreateClient();

        var registerToken = await GetTokenAsync(client);
        await PostJsonAsync(
            client, "/auth/register",
            new { email = "user@example.com", password = ValidPassword }, registerToken);

        var loginToken = await GetTokenAsync(client);
        var login = await PostJsonAsync(
            client, "/auth/login/password",
            new { email = "user@example.com", password = ValidPassword, rememberMe = false },
            loginToken);
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);

        var sessionResp = await client.GetAsync("/api/session");
        sessionResp.EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task Login_with_wrong_password_returns_401()
    {
        await _factory.ResetDatabaseAsync();
        var client = CreateClient();

        var registerToken = await GetTokenAsync(client);
        await PostJsonAsync(
            client, "/auth/register",
            new { email = "user@example.com", password = ValidPassword }, registerToken);

        var loginToken = await GetTokenAsync(client);
        var login = await PostJsonAsync(
            client, "/auth/login/password",
            new { email = "user@example.com", password = "totally-wrong-password", rememberMe = false },
            loginToken);

        Assert.Equal(HttpStatusCode.Unauthorized, login.StatusCode);
        using var doc = JsonDocument.Parse(await login.Content.ReadAsStringAsync());
        Assert.Equal(AuthResultCodes.InvalidCredentials, doc.RootElement.GetProperty("code").GetString());
    }

    [Fact]
    public async Task Login_with_unknown_user_returns_401()
    {
        await _factory.ResetDatabaseAsync();
        var client = CreateClient();

        var token = await GetTokenAsync(client);
        var login = await PostJsonAsync(
            client, "/auth/login/password",
            new { email = "nobody@example.com", password = ValidPassword, rememberMe = false },
            token);

        Assert.Equal(HttpStatusCode.Unauthorized, login.StatusCode);
        using var doc = JsonDocument.Parse(await login.Content.ReadAsStringAsync());
        Assert.Equal(AuthResultCodes.InvalidCredentials, doc.RootElement.GetProperty("code").GetString());
    }

    [Fact]
    public async Task Login_locks_out_after_max_failed_attempts()
    {
        await _factory.ResetDatabaseAsync();
        var client = CreateClient();

        var registerToken = await GetTokenAsync(client);
        await PostJsonAsync(
            client, "/auth/register",
            new { email = "user@example.com", password = ValidPassword }, registerToken);

        HttpStatusCode last = HttpStatusCode.OK;
        for (var i = 0; i < 5; i++)
        {
            var token = await GetTokenAsync(client);
            var resp = await PostJsonAsync(
                client, "/auth/login/password",
                new { email = "user@example.com", password = "totally-wrong-password", rememberMe = false },
                token);
            last = resp.StatusCode;
        }

        Assert.Equal(HttpStatusCode.Locked, last);

        var tokenAfter = await GetTokenAsync(client);
        var next = await PostJsonAsync(
            client, "/auth/login/password",
            new { email = "user@example.com", password = "totally-wrong-password", rememberMe = false },
            tokenAfter);
        Assert.Equal(HttpStatusCode.Locked, next.StatusCode);
    }

    [Fact]
    public async Task Login_without_antiforgery_returns_400()
    {
        await _factory.ResetDatabaseAsync();
        var client = CreateClient();

        var resp = await PostJsonAsync(
            client, "/auth/login/password",
            new { email = "user@example.com", password = ValidPassword, rememberMe = false },
            token: null);

        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
        Assert.Equal(AuthResultCodes.InvalidRequest, doc.RootElement.GetProperty("code").GetString());
    }
}
