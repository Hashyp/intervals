using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Intervals.Api.Auth;
using Intervals.Api.Data;
using Intervals.Api.Data.Entities;
using Intervals.Api.Email;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace Intervals.Api.Tests;

[Collection(nameof(AuthCollection))]
public sealed class AuditEventsIntegrationTests
{
    private readonly AuthWebFactory _factory;

    public AuditEventsIntegrationTests(AuthWebFactory factory)
    {
        _factory = factory;
    }

    private const string ValidPassword = "supersecret-password-1";
    private const string ResetPassword = "brand-new-password-7";

    private static string UniqueEmail(string prefix) => $"{prefix}-{Guid.NewGuid():N}@example.com";

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

    private static async Task<string> GetAntiforgeryTokenAsync(HttpClient client)
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
        var token = await GetAntiforgeryTokenAsync(client);
        var resp = await PostJsonAsync(
            client,
            "/auth/register",
            new { email, password = ValidPassword },
            token);
        resp.EnsureSuccessStatusCode();
    }

    private static string ExtractToken(string body)
    {
        var match = Regex.Match(body, @"token=([A-Za-z0-9_-]+)");
        Assert.True(match.Success, "Expected a token in the email body.");
        return match.Groups[1].Value;
    }

    private async Task<EmailAuditRows> CaptureUserAndEventsAsync(string email)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IntervalsDbContext>();
        var user = await db.AppUsers.AsNoTracking().FirstAsync(u => u.Email == email);
        var events = await db.AuthEvents.AsNoTracking().Where(e => e.UserId == user.Id).ToListAsync();
        return new EmailAuditRows(user.Id, events);
    }

    private async Task<System.Collections.Generic.List<AuthEvent>> AllEventsAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IntervalsDbContext>();
        return await db.AuthEvents.AsNoTracking().ToListAsync();
    }

    private sealed record EmailAuditRows(Guid UserId, System.Collections.Generic.List<AuthEvent> Events);

    [Fact]
    public async Task Resend_recordsEmailVerificationSentEvent()
    {
        await _factory.ResetDatabaseAsync();
        var (client, sender) = CreateClient();

        var email = UniqueEmail("audit-resend");
        await RegisterAsync(client, email);
        sender.Reset();

        var token = await GetAntiforgeryTokenAsync(client);
        var resp = await PostJsonAsync(
            client,
            "/auth/email-verification/request",
            new { },
            token);
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);

        var rows = await CaptureUserAndEventsAsync(email);
        Assert.Contains(rows.Events, e =>
            e.EventType == AuthEventTypes.EmailVerificationSent &&
            e.UserId == rows.UserId &&
            e.Success);
    }

    [Fact]
    public async Task Confirm_recordsEmailVerifiedSuccess()
    {
        await _factory.ResetDatabaseAsync();
        var (client, sender) = CreateClient();

        var email = UniqueEmail("audit-confirm");
        await RegisterAsync(client, email);

        var token = ExtractToken(sender.SentEmails[0].TextBody);
        var confirm = await client.GetAsync($"/auth/email-verification/confirm?token={token}");
        Assert.Equal(HttpStatusCode.Redirect, confirm.StatusCode);

        var rows = await CaptureUserAndEventsAsync(email);
        Assert.Contains(rows.Events, e =>
            e.EventType == AuthEventTypes.EmailVerified &&
            e.UserId == rows.UserId &&
            e.Success);
    }

    [Fact]
    public async Task Confirm_unknownToken_recordsEmailVerifiedFailureWithoutUserId()
    {
        await _factory.ResetDatabaseAsync();
        var (client, _) = CreateClient();

        var resp = await client.GetAsync("/auth/email-verification/confirm?token=not-a-real-token");
        Assert.Equal(HttpStatusCode.Redirect, resp.StatusCode);

        var events = await AllEventsAsync();
        Assert.Contains(events, e =>
            e.EventType == AuthEventTypes.EmailVerified &&
            e.UserId == null &&
            !e.Success);
    }

    [Fact]
    public async Task Forgot_knownEmail_recordsPasswordResetRequested()
    {
        await _factory.ResetDatabaseAsync();
        var (client, _) = CreateClient();

        var email = UniqueEmail("audit-forgot");
        await RegisterAsync(client, email);

        var token = await GetAntiforgeryTokenAsync(client);
        var resp = await PostJsonAsync(
            client,
            "/auth/password/forgot",
            new { email },
            token);
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);

        var rows = await CaptureUserAndEventsAsync(email);
        Assert.Contains(rows.Events, e =>
            e.EventType == AuthEventTypes.PasswordResetRequested &&
            e.UserId == rows.UserId &&
            e.Success);
    }

    [Fact]
    public async Task Forgot_unknownEmail_recordsNoPasswordResetRequested()
    {
        await _factory.ResetDatabaseAsync();
        var (client, _) = CreateClient();

        var token = await GetAntiforgeryTokenAsync(client);
        var resp = await PostJsonAsync(
            client,
            "/auth/password/forgot",
            new { email = "nobody-" + Guid.NewGuid().ToString("N") + "@example.com" },
            token);
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);

        var events = await AllEventsAsync();
        Assert.DoesNotContain(events, e => e.EventType == AuthEventTypes.PasswordResetRequested);
    }

    [Fact]
    public async Task Reset_recordsExactlyOnePasswordResetCompletionEvent()
    {
        await _factory.ResetDatabaseAsync();
        var (client, sender) = CreateClient();

        var email = UniqueEmail("audit-reset");
        await RegisterAsync(client, email);

        var forgotToken = await GetAntiforgeryTokenAsync(client);
        var forgotResp = await PostJsonAsync(
            client,
            "/auth/password/forgot",
            new { email },
            forgotToken);
        forgotResp.EnsureSuccessStatusCode();

        var resetEmail = sender.SentEmails.Last(e => e.Subject == "Reset your Intervals password");
        var resetToken = ExtractToken(resetEmail.TextBody);

        var csrf = await GetAntiforgeryTokenAsync(client);
        var resetResp = await PostJsonAsync(
            client,
            "/auth/password/reset",
            new { token = resetToken, password = ResetPassword },
            csrf);
        Assert.Equal(HttpStatusCode.OK, resetResp.StatusCode);

        var rows = await CaptureUserAndEventsAsync(email);
        var completions = rows.Events.Where(e => e.EventType == "password_reset" && e.Success).ToList();
        Assert.Single(completions);
        Assert.Equal(rows.UserId, completions[0].UserId);
    }
}
