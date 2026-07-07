using System;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using Intervals.Api.Auth;
using Intervals.Api.Data;
using Intervals.Api.Email;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace Intervals.Api.Tests;

[Collection(nameof(AuthCollection))]
public sealed class SessionInvalidationIntegrationTests
{
    private readonly AuthWebFactory _factory;

    public SessionInvalidationIntegrationTests(AuthWebFactory factory)
    {
        _factory = factory;
    }

    private const string ValidPassword = "supersecret-password-1";

    private static string UniqueEmail() => $"sess-{Guid.NewGuid():N}@example.com";

    private HttpClient CreateClient()
    {
        return _factory.WithWebHostBuilder(b =>
        {
            b.ConfigureTestServices(services =>
            {
                services.RemoveAll<IEmailSender>();
                services.AddSingleton<IEmailSender, FakeEmailSender>();
            });
        }).CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            HandleCookies = true,
        });
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

    private static async Task SetUserDisabledAsync(AuthWebFactory factory, string email)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IntervalsDbContext>();
        await db.AppUsers
            .Where(u => u.Email == email)
            .ExecuteUpdateAsync(s => s.SetProperty(u => u.DisabledUtc, DateTimeOffset.UtcNow));
    }

    private static async Task RotateSecurityStampAsync(AuthWebFactory factory, string email)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IntervalsDbContext>();
        await db.AppUsers
            .Where(u => u.Email == email)
            .ExecuteUpdateAsync(s => s.SetProperty(u => u.SecurityStamp, "rotated-stamp"));
    }

    [Fact]
    public async Task ActiveAccount_sessionIsValid()
    {
        await _factory.ResetDatabaseAsync();
        var client = CreateClient();

        var email = UniqueEmail();
        await RegisterAsync(client, email);

        var resp = await client.GetAsync("/api/session");
        resp.EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task DisabledAccount_existingSessionIsRejected()
    {
        await _factory.ResetDatabaseAsync();
        var client = CreateClient();

        var email = UniqueEmail();
        await RegisterAsync(client, email);

        // The register response issued an app cookie; disable the user out from under it.
        await SetUserDisabledAsync(_factory, email);

        var resp = await client.GetAsync("/api/session");
        Assert.Equal(HttpStatusCode.Unauthorized, resp.StatusCode);
    }

    [Fact]
    public async Task RotatedSecurityStamp_invalidatesSessionIssuedBeforeTheStamp()
    {
        await _factory.ResetDatabaseAsync();
        var client = CreateClient();

        var email = UniqueEmail();
        await RegisterAsync(client, email);
        // A freshly registered user has no SecurityStamp yet, so the cookie carries an
        // empty stamp claim.

        // Simulate a password reset/change rotating the stamp after this cookie was issued.
        await RotateSecurityStampAsync(_factory, email);

        var resp = await client.GetAsync("/api/session");
        Assert.Equal(HttpStatusCode.Unauthorized, resp.StatusCode);
    }
}
