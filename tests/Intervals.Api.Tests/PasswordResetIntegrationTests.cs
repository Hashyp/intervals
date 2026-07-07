using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Intervals.Api.Auth;
using Intervals.Api.Email;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace Intervals.Api.Tests;

[Collection(nameof(AuthCollection))]
public sealed class PasswordResetIntegrationTests
{
    private readonly AuthWebFactory _factory;

    public PasswordResetIntegrationTests(AuthWebFactory factory)
    {
        _factory = factory;
    }

    private const string ValidPassword = "supersecret-password-1";
    private const string ResetPassword = "brand-new-password-7";

    private static string UniqueEmail() => $"reset-{Guid.NewGuid():N}@example.com";

    private (HttpClient client, FakeEmailSender sender) CreateClient()
    {
        FakeEmailSender? sender = null;
        var client = _factory.WithWebHostBuilder(b =>
        {
            b.ConfigureTestServices(services =>
            {
                sender = new FakeEmailSender();
                services.RemoveAll<IEmailSender>();
                services.AddSingleton<IEmailSender>(sender);
            });
        }).CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            HandleCookies = true,
        });
        return (client, sender!);
    }

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

    private static async Task RegisterAsync(HttpClient client, string email)
    {
        var token = await GetTokenAsync(client);
        var resp = await PostJsonAsync(
            client,
            "/auth/register",
            new { email, password = ValidPassword },
            token);
        resp.EnsureSuccessStatusCode();
    }

    private static async Task<HttpResponseMessage> LoginAsync(
        HttpClient client, string email, string password)
    {
        var token = await GetTokenAsync(client);
        return await PostJsonAsync(
            client,
            "/auth/login/password",
            new { email, password, rememberMe = false },
            token);
    }

    private static async Task RequestResetAsync(HttpClient client, string email)
    {
        var token = await GetTokenAsync(client);
        var resp = await PostJsonAsync(
            client,
            "/auth/password/forgot",
            new { email },
            token);
        resp.EnsureSuccessStatusCode();
    }

    private static async Task<HttpResponseMessage> ResetAsync(
        HttpClient client, string token, string password)
    {
        var csrf = await GetTokenAsync(client);
        return await PostJsonAsync(
            client,
            "/auth/password/reset",
            new { token = token, password = password },
            csrf);
    }

    private static string ExtractToken(string body)
    {
        var match = Regex.Match(body, @"token=([A-Za-z0-9_-]+)");
        Assert.True(match.Success, "Expected a reset token in the email body.");
        return match.Groups[1].Value;
    }

    private static string ExtractResetToken(IReadOnlyList<SentEmail> sent)
    {
        var reset = sent.LastOrDefault(e => e.Subject == "Reset your Intervals password")
            ?? throw new InvalidOperationException("No password reset email was sent.");
        Assert.Contains("/reset-password?token=", reset.HtmlBody);
        return ExtractToken(reset.TextBody);
    }

    [Fact]
    public async Task Forgot_forKnownEmail_sendsResetLink_andIsGeneric()
    {
        await _factory.ResetDatabaseAsync();
        var (client, sender) = CreateClient();

        var email = UniqueEmail();
        await RegisterAsync(client, email);

        var token = await GetTokenAsync(client);
        var resp = await PostJsonAsync(
            client,
            "/auth/password/forgot",
            new { email },
            token);

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
        Assert.True(doc.RootElement.GetProperty("ok").GetBoolean());

        var sent = sender.SentEmails.LastOrDefault(e => e.Subject == "Reset your Intervals password");
        Assert.NotNull(sent);
        Assert.Equal(email, sent!.RecipientEmail);
        Assert.Contains("/reset-password?token=", sent.HtmlBody);
        Assert.Contains("/reset-password?token=", sent.TextBody);
    }

    [Fact]
    public async Task Forgot_forUnknownEmail_returnsGenericSuccessAndSendsNothing()
    {
        await _factory.ResetDatabaseAsync();
        var (client, sender) = CreateClient();

        var token = await GetTokenAsync(client);
        var resp = await PostJsonAsync(
            client,
            "/auth/password/forgot",
            new { email = "nobody-" + Guid.NewGuid().ToString("N") + "@example.com" },
            token);

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
        Assert.True(doc.RootElement.GetProperty("ok").GetBoolean());
        Assert.Empty(sender.SentEmails);
    }

    [Fact]
    public async Task Forgot_withoutAntiforgery_returns_400()
    {
        await _factory.ResetDatabaseAsync();
        var (client, _) = CreateClient();

        var resp = await PostJsonAsync(
            client,
            "/auth/password/forgot",
            new { email = "user@example.com" },
            token: null);

        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
        Assert.Equal(AuthResultCodes.InvalidRequest, doc.RootElement.GetProperty("code").GetString());
    }

    [Fact]
    public async Task Reset_setsNewPasswordAndClearsLockout()
    {
        await _factory.ResetDatabaseAsync();
        var (client, sender) = CreateClient();

        var email = UniqueEmail();
        await RegisterAsync(client, email);

        await RequestResetAsync(client, email);
        var resetToken = ExtractResetToken(sender.SentEmails);

        var resetResp = await ResetAsync(client, resetToken, ResetPassword);
        Assert.Equal(HttpStatusCode.OK, resetResp.StatusCode);
        using var resetDoc = JsonDocument.Parse(await resetResp.Content.ReadAsStringAsync());
        Assert.True(resetDoc.RootElement.GetProperty("ok").GetBoolean());

        var login = await LoginAsync(client, email, ResetPassword);
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);

        var sessionResp = await client.GetAsync("/api/session");
        sessionResp.EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task Reset_unlocksPreviouslyLockedAccount()
    {
        await _factory.ResetDatabaseAsync();
        var (client, sender) = CreateClient();

        var email = UniqueEmail();
        await RegisterAsync(client, email);

        HttpStatusCode last = HttpStatusCode.OK;
        for (var i = 0; i < 5; i++)
        {
            last = (await LoginAsync(client, email, "totally-wrong-password")).StatusCode;
        }
        Assert.Equal(HttpStatusCode.Locked, last);

        await RequestResetAsync(client, email);
        var resetToken = ExtractResetToken(sender.SentEmails);

        var resetResp = await ResetAsync(client, resetToken, ResetPassword);
        Assert.Equal(HttpStatusCode.OK, resetResp.StatusCode);

        var login = await LoginAsync(client, email, ResetPassword);
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
    }

    [Fact]
    public async Task Reset_marksEmailVerified()
    {
        await _factory.ResetDatabaseAsync();
        var (client, sender) = CreateClient();

        var email = UniqueEmail();
        await RegisterAsync(client, email);

        var sessionAfterRegister = await LoginAsync(client, email, ValidPassword);
        Assert.Equal(HttpStatusCode.OK, sessionAfterRegister.StatusCode);
        var sessionRespBefore = await client.GetAsync("/api/session");
        sessionRespBefore.EnsureSuccessStatusCode();
        using var beforeDoc = JsonDocument.Parse(await sessionRespBefore.Content.ReadAsStringAsync());
        Assert.False(beforeDoc.RootElement.GetProperty("user").GetProperty("emailVerified").GetBoolean());

        await RequestResetAsync(client, email);
        var resetToken = ExtractResetToken(sender.SentEmails);
        var resetResp = await ResetAsync(client, resetToken, ResetPassword);
        Assert.Equal(HttpStatusCode.OK, resetResp.StatusCode);

        var login = await LoginAsync(client, email, ResetPassword);
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);

        var sessionResp = await client.GetAsync("/api/session");
        sessionResp.EnsureSuccessStatusCode();
        using var doc = JsonDocument.Parse(await sessionResp.Content.ReadAsStringAsync());
        Assert.True(doc.RootElement.GetProperty("user").GetProperty("emailVerified").GetBoolean());
    }

    [Fact]
    public async Task Reset_replayFails()
    {
        await _factory.ResetDatabaseAsync();
        var (client, sender) = CreateClient();

        var email = UniqueEmail();
        await RegisterAsync(client, email);

        await RequestResetAsync(client, email);
        var resetToken = ExtractResetToken(sender.SentEmails);

        var first = await ResetAsync(client, resetToken, ResetPassword);
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);

        var second = await ResetAsync(client, resetToken, "yet-another-pw-9");
        Assert.Equal(HttpStatusCode.BadRequest, second.StatusCode);
        using var doc = JsonDocument.Parse(await second.Content.ReadAsStringAsync());
        Assert.Equal(AuthResultCodes.InvalidRequest, doc.RootElement.GetProperty("code").GetString());

        var loginWithFirstReset = await LoginAsync(client, email, ResetPassword);
        Assert.Equal(HttpStatusCode.OK, loginWithFirstReset.StatusCode);

        var loginWithReplay = await LoginAsync(client, email, "yet-another-pw-9");
        Assert.Equal(HttpStatusCode.Unauthorized, loginWithReplay.StatusCode);
    }

    [Fact]
    public async Task Reset_weakPasswordFails()
    {
        await _factory.ResetDatabaseAsync();
        var (client, sender) = CreateClient();

        var email = UniqueEmail();
        await RegisterAsync(client, email);

        await RequestResetAsync(client, email);
        var resetToken = ExtractResetToken(sender.SentEmails);

        var resetResp = await ResetAsync(client, resetToken, "short");
        Assert.Equal(HttpStatusCode.BadRequest, resetResp.StatusCode);
        using var doc = JsonDocument.Parse(await resetResp.Content.ReadAsStringAsync());
        Assert.Equal(AuthResultCodes.WeakPassword, doc.RootElement.GetProperty("code").GetString());

        var loginWithOld = await LoginAsync(client, email, ValidPassword);
        Assert.Equal(HttpStatusCode.OK, loginWithOld.StatusCode);

        var loginWithWeak = await LoginAsync(client, email, "short");
        Assert.Equal(HttpStatusCode.Unauthorized, loginWithWeak.StatusCode);
    }

    [Fact]
    public async Task Reset_weakPasswordDoesNotConsumeToken()
    {
        await _factory.ResetDatabaseAsync();
        var (client, sender) = CreateClient();

        var email = UniqueEmail();
        await RegisterAsync(client, email);

        await RequestResetAsync(client, email);
        var resetToken = ExtractResetToken(sender.SentEmails);

        var weak = await ResetAsync(client, resetToken, "short");
        Assert.Equal(HttpStatusCode.BadRequest, weak.StatusCode);
        using var weakDoc = JsonDocument.Parse(await weak.Content.ReadAsStringAsync());
        Assert.Equal(AuthResultCodes.WeakPassword, weakDoc.RootElement.GetProperty("code").GetString());

        // The same reset link must still work with a valid password — a weak-password
        // attempt must not have burned the one-time token.
        var ok = await ResetAsync(client, resetToken, ResetPassword);
        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);

        var login = await LoginAsync(client, email, ResetPassword);
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
    }

    [Fact]
    public async Task Reset_invalidTokenFails()
    {
        await _factory.ResetDatabaseAsync();
        var (client, _) = CreateClient();

        var resetResp = await ResetAsync(client, "not-a-real-token", ResetPassword);
        Assert.Equal(HttpStatusCode.BadRequest, resetResp.StatusCode);
        using var doc = JsonDocument.Parse(await resetResp.Content.ReadAsStringAsync());
        Assert.Equal(AuthResultCodes.InvalidRequest, doc.RootElement.GetProperty("code").GetString());
    }

    [Fact]
    public async Task Reset_withoutAntiforgery_returns_400()
    {
        await _factory.ResetDatabaseAsync();
        var (client, _) = CreateClient();

        var resp = await PostJsonAsync(
            client,
            "/auth/password/reset",
            new { token = "anything", password = ResetPassword },
            token: null);

        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
        Assert.Equal(AuthResultCodes.InvalidRequest, doc.RootElement.GetProperty("code").GetString());
    }
}
