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
public sealed class EmailVerificationIntegrationTests
{
    private readonly AuthWebFactory _factory;

    public EmailVerificationIntegrationTests(AuthWebFactory factory)
    {
        _factory = factory;
    }

    private const string ValidPassword = "supersecret-password-1";

    private static string UniqueEmail() => $"verify-{Guid.NewGuid():N}@example.com";

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

    private static async Task<string> RegisterAsync(HttpClient client, string email)
    {
        var token = await GetTokenAsync(client);
        var resp = await PostJsonAsync(
            client,
            "/auth/register",
            new { email, password = ValidPassword },
            token);
        resp.EnsureSuccessStatusCode();
        return email;
    }

    private static string ExtractToken(string body)
    {
        var match = Regex.Match(body, @"token=([A-Za-z0-9_-]+)");
        Assert.True(match.Success, "Expected a verification token in the email body.");
        return match.Groups[1].Value;
    }

    [Fact]
    public async Task Register_sendsVerificationEmail()
    {
        await _factory.ResetDatabaseAsync();
        var (client, sender) = CreateClient();

        var email = UniqueEmail();
        await RegisterAsync(client, email);

        var sent = sender.SentEmails;
        Assert.Single(sent);
        Assert.Equal(email, sent[0].RecipientEmail);
        Assert.Equal("Verify your Intervals account", sent[0].Subject);
        Assert.Contains("/auth/email-verification/confirm?token=", sent[0].HtmlBody);
        Assert.Contains("/auth/email-verification/confirm?token=", sent[0].TextBody);
    }

    [Fact]
    public async Task Confirm_consumesTokenAndMarksVerified()
    {
        await _factory.ResetDatabaseAsync();
        var (client, sender) = CreateClient();

        var email = UniqueEmail();
        await RegisterAsync(client, email);

        var token = ExtractToken(sender.SentEmails[0].TextBody);

        var confirm = await client.GetAsync($"/auth/email-verification/confirm?token={token}");
        Assert.Equal(HttpStatusCode.Redirect, confirm.StatusCode);
        var location = confirm.Headers.Location?.ToString() ?? string.Empty;
        Assert.Contains("auth=email_verified", location);

        var sessionResp = await client.GetAsync("/api/session");
        sessionResp.EnsureSuccessStatusCode();
        using var doc = JsonDocument.Parse(await sessionResp.Content.ReadAsStringAsync());
        Assert.True(doc.RootElement.GetProperty("user").GetProperty("emailVerified").GetBoolean());
    }

    [Fact]
    public async Task Confirm_replayFails()
    {
        await _factory.ResetDatabaseAsync();
        var (client, sender) = CreateClient();

        var email = UniqueEmail();
        await RegisterAsync(client, email);

        var token = ExtractToken(sender.SentEmails[0].TextBody);

        var first = await client.GetAsync($"/auth/email-verification/confirm?token={token}");
        Assert.Equal(HttpStatusCode.Redirect, first.StatusCode);
        Assert.Contains(
            "auth=email_verified",
            first.Headers.Location?.ToString() ?? string.Empty);

        var second = await client.GetAsync($"/auth/email-verification/confirm?token={token}");
        Assert.Equal(HttpStatusCode.Redirect, second.StatusCode);
        Assert.Contains(
            "auth=verification_failed",
            second.Headers.Location?.ToString() ?? string.Empty);
    }

    [Fact]
    public async Task Confirm_expiredOrUnknownTokenFails()
    {
        await _factory.ResetDatabaseAsync();
        var (client, _) = CreateClient();

        var resp = await client.GetAsync("/auth/email-verification/confirm?token=not-a-real-token");
        Assert.Equal(HttpStatusCode.Redirect, resp.StatusCode);
        var location = resp.Headers.Location?.ToString() ?? string.Empty;
        Assert.Contains("auth=verification_failed", location);
    }

    [Fact]
    public async Task Resend_forAuthenticatedUser_sendsEmailAndIsGeneric()
    {
        await _factory.ResetDatabaseAsync();
        var (client, sender) = CreateClient();

        var email = UniqueEmail();
        await RegisterAsync(client, email);

        Assert.Single(sender.SentEmails);

        var token = await GetTokenAsync(client);
        var resp = await PostJsonAsync(
            client,
            "/auth/email-verification/request",
            new { },
            token);

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
        Assert.True(doc.RootElement.GetProperty("ok").GetBoolean());

        Assert.Equal(2, sender.SentEmails.Count);
        Assert.Equal(email, sender.SentEmails[1].RecipientEmail);
        Assert.Equal("Verify your Intervals account", sender.SentEmails[1].Subject);
    }

    [Fact]
    public async Task Resend_withoutAntiforgery_returns_400()
    {
        await _factory.ResetDatabaseAsync();
        var (client, _) = CreateClient();

        var email = UniqueEmail();
        await RegisterAsync(client, email);

        var resp = await PostJsonAsync(
            client,
            "/auth/email-verification/request",
            new { },
            token: null);

        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
        Assert.Equal(AuthResultCodes.InvalidRequest, doc.RootElement.GetProperty("code").GetString());
    }
}
