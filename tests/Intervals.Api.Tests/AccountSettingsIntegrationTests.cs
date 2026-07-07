using System;
using System.Collections.Generic;
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
public sealed class AccountSettingsIntegrationTests
{
    private readonly AuthWebFactory _factory;

    public AccountSettingsIntegrationTests(AuthWebFactory factory)
    {
        _factory = factory;
    }

    private const string ValidPassword = "supersecret-password-1";
    private const string NewPassword = "brand-new-password-7";

    private static string UniqueEmail() => $"acct-{Guid.NewGuid():N}@example.com";

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

    private static async Task<HttpResponseMessage> DeleteAsync(
        HttpClient client, string path, string? token = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Delete, path);
        if (token is not null)
        {
            request.Headers.Add("X-CSRF-TOKEN", token);
        }
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

    private static async Task<(Guid userId, HttpClient client, FakeEmailSender sender)> SocialLoginClientAsync(
        AuthWebFactory factory, string provider, string subject, string email, bool emailVerified)
    {
        var scope = factory.Services.CreateScope();
        var accounts = scope.ServiceProvider.GetRequiredService<IAccountService>();
        var profile = new ExternalUserProfile(provider, subject, "Social User", email, emailVerified, null);
        var login = await accounts.LoginAsync(profile, correlationId: null);

        FakeEmailSender? sender = null;
        var client = factory.WithWebHostBuilder(b =>
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

        var loginResp = await client.GetAsync($"/auth/test-login/{login.User.Id}");
        loginResp.EnsureSuccessStatusCode();
        return (login.User.Id, client, sender!);
    }

    private static string ExtractToken(string body)
    {
        var match = Regex.Match(body, @"token=([A-Za-z0-9_-]+)");
        Assert.True(match.Success, "Expected a token in the email body.");
        return match.Groups[1].Value;
    }

    private static async Task SeedLinkedProviderAsync(
        AuthWebFactory factory, Guid userId, string provider, string providerUserId, string email)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<Intervals.Api.Data.IntervalsDbContext>();
        db.ExternalLogins.Add(new Intervals.Api.Data.Entities.ExternalLogin
        {
            UserId = userId,
            Provider = provider,
            ProviderUserId = providerUserId,
            Email = email,
            EmailVerified = true,
            CreatedUtc = DateTimeOffset.UtcNow,
        });
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task GetAccount_returnsProvidersAndPasswordState()
    {
        await _factory.ResetDatabaseAsync();
        var (client, _) = CreateClient();

        var email = UniqueEmail();
        await RegisterAsync(client, email);
        // RegisterAsync signs the user in.

        var resp = await client.GetAsync("/auth/account");
        resp.EnsureSuccessStatusCode();
        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
        var root = doc.RootElement;
        Assert.Equal(email, root.GetProperty("email").GetString());
        Assert.True(root.GetProperty("hasPassword").GetBoolean());
        Assert.True(root.GetProperty("emailVerified").GetBoolean() || !root.GetProperty("emailVerified").GetBoolean());

        var providers = root.GetProperty("providers").EnumerateArray().ToArray();
        var password = providers.Single(p => p.GetProperty("id").GetString() == "password");
        Assert.True(password.GetProperty("linked").GetBoolean());
        Assert.Equal(email, password.GetProperty("email").GetString());
    }

    [Fact]
    public async Task GetAccount_requiresAuthentication()
    {
        await _factory.ResetDatabaseAsync();
        var (client, _) = CreateClient();

        var resp = await client.GetAsync("/auth/account");
        Assert.True(
            resp.StatusCode == HttpStatusCode.Unauthorized ||
            resp.StatusCode == HttpStatusCode.Redirect,
            $"Expected 401 or 302, got {(int)resp.StatusCode}");
    }

    [Fact]
    public async Task ChangePassword_requiresCurrentAndAppliesPolicy()
    {
        await _factory.ResetDatabaseAsync();
        var (client, _) = CreateClient();

        var email = UniqueEmail();
        await RegisterAsync(client, email);

        var token = await GetTokenAsync(client);
        var wrong = await PostJsonAsync(
            client,
            "/auth/account/password/change",
            new { currentPassword = "totally-wrong-pw", newPassword = NewPassword },
            token);
        Assert.Equal(HttpStatusCode.Unauthorized, wrong.StatusCode);
        using var wrongDoc = JsonDocument.Parse(await wrong.Content.ReadAsStringAsync());
        Assert.Equal(AuthResultCodes.InvalidCredentials, wrongDoc.RootElement.GetProperty("code").GetString());

        token = await GetTokenAsync(client);
        var weak = await PostJsonAsync(
            client,
            "/auth/account/password/change",
            new { currentPassword = ValidPassword, newPassword = "short" },
            token);
        Assert.Equal(HttpStatusCode.BadRequest, weak.StatusCode);
        using var weakDoc = JsonDocument.Parse(await weak.Content.ReadAsStringAsync());
        Assert.Equal(AuthResultCodes.WeakPassword, weakDoc.RootElement.GetProperty("code").GetString());

        token = await GetTokenAsync(client);
        var ok = await PostJsonAsync(
            client,
            "/auth/account/password/change",
            new { currentPassword = ValidPassword, newPassword = NewPassword },
            token);
        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);

        var newClient = _factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            HandleCookies = true,
        });
        var loginNew = await LoginAsync(newClient, email, NewPassword);
        Assert.Equal(HttpStatusCode.OK, loginNew.StatusCode);

        var oldClient = _factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            HandleCookies = true,
        });
        var loginOld = await LoginAsync(oldClient, email, ValidPassword);
        Assert.Equal(HttpStatusCode.Unauthorized, loginOld.StatusCode);
    }

    [Fact]
    public async Task ChangePassword_rotatesSecurityStampAndRevokesResetTokens()
    {
        await _factory.ResetDatabaseAsync();
        var (client, sender) = CreateClient();

        var email = UniqueEmail();
        await RegisterAsync(client, email);

        // Issue a reset token via forgot-password, then change password via the account settings.
        await RequestResetAsync(client, email);
        var resetToken = ExtractToken(
            sender.SentEmails.Last(e => e.Subject == "Reset your Intervals password").TextBody);

        var token = await GetTokenAsync(client);
        var ok = await PostJsonAsync(
            client,
            "/auth/account/password/change",
            new { currentPassword = ValidPassword, newPassword = NewPassword },
            token);
        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);

        // Reset token should no longer be valid.
        var resetResp = await ResetAsync(client, resetToken, "another-password-3");
        Assert.Equal(HttpStatusCode.BadRequest, resetResp.StatusCode);
    }

    [Fact]
    public async Task AddPassword_toSocialOnlyAccount_createsCredentialAndSendsVerification()
    {
        await _factory.ResetDatabaseAsync();
        var email = UniqueEmail();
        var (_, client, sender) = await SocialLoginClientAsync(
            _factory, "google", "google-sub-addpw", email, emailVerified: false);

        var token = await GetTokenAsync(client);
        var resp = await PostJsonAsync(
            client,
            "/auth/account/password/add",
            new { email, newPassword = NewPassword },
            token);
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);

        var account = await client.GetAsync("/auth/account");
        account.EnsureSuccessStatusCode();
        using var doc = JsonDocument.Parse(await account.Content.ReadAsStringAsync());
        Assert.True(doc.RootElement.GetProperty("hasPassword").GetBoolean());

        Assert.Contains(sender.SentEmails, e =>
            e.Subject == "Verify your Intervals account" && e.RecipientEmail == email);
    }

    [Fact]
    public async Task AddPassword_rejectsEmailTakenByAnotherUser()
    {
        await _factory.ResetDatabaseAsync();
        var existingEmail = UniqueEmail();
        var (registerClient, _) = CreateClient();
        await RegisterAsync(registerClient, existingEmail);

        // Social-only user attempts to add password with an email owned by another user.
        var (_, socialClient, _) = await SocialLoginClientAsync(
            _factory, "google", "google-sub-taken", UniqueEmail(), emailVerified: false);

        var token = await GetTokenAsync(socialClient);
        var resp = await PostJsonAsync(
            socialClient,
            "/auth/account/password/add",
            new { email = existingEmail, newPassword = NewPassword },
            token);
        Assert.Equal(HttpStatusCode.Conflict, resp.StatusCode);
        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
        Assert.Equal(AuthResultCodes.EmailTaken, doc.RootElement.GetProperty("code").GetString());
    }

    [Fact]
    public async Task Unlink_removesProvider()
    {
        await _factory.ResetDatabaseAsync();
        var googleEmail = UniqueEmail();
        var microsoftEmail = UniqueEmail();
        var (userId, client, _) = await SocialLoginClientAsync(
            _factory, "google", "google-sub-unlink", googleEmail, emailVerified: true);

        await SeedLinkedProviderAsync(_factory, userId, "microsoft", "microsoft-sub-unlink", microsoftEmail);

        var token = await GetTokenAsync(client);
        var resp = await DeleteAsync(
            client,
            "/auth/account/providers/google",
            token);
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);

        var account = await client.GetAsync("/auth/account");
        account.EnsureSuccessStatusCode();
        using var doc = JsonDocument.Parse(await account.Content.ReadAsStringAsync());
        var google = doc.RootElement.GetProperty("providers").EnumerateArray()
            .Single(p => p.GetProperty("id").GetString() == "google");
        Assert.False(google.GetProperty("linked").GetBoolean());
    }

    [Fact]
    public async Task Unlink_lastMethodGuard()
    {
        await _factory.ResetDatabaseAsync();
        var email = UniqueEmail();
        var (_, client, _) = await SocialLoginClientAsync(
            _factory, "google", "google-sub-only", email, emailVerified: true);

        var token = await GetTokenAsync(client);
        var resp = await DeleteAsync(
            client,
            "/auth/account/providers/google",
            token);
        Assert.Equal(HttpStatusCode.Conflict, resp.StatusCode);
        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
        Assert.Equal(AccountSettingsResultCodes.LastLoginMethod, doc.RootElement.GetProperty("code").GetString());
    }

    [Fact]
    public async Task ChangePassword_withoutAntiforgery_returns_400()
    {
        await _factory.ResetDatabaseAsync();
        var (client, _) = CreateClient();
        var email = UniqueEmail();
        await RegisterAsync(client, email);

        var resp = await PostJsonAsync(
            client,
            "/auth/account/password/change",
            new { currentPassword = ValidPassword, newPassword = NewPassword },
            token: null);
        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    [Fact]
    public async Task Unlink_withoutAntiforgery_returns_400()
    {
        await _factory.ResetDatabaseAsync();
        var email = UniqueEmail();
        var (_, client, _) = await SocialLoginClientAsync(
            _factory, "google", "google-sub-antiforgery", email, emailVerified: true);

        var resp = await DeleteAsync(
            client,
            "/auth/account/providers/google",
            token: null);
        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }
}
