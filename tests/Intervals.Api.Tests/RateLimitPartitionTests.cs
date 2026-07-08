using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using Intervals.Api.Email;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace Intervals.Api.Tests;

[Collection(nameof(AuthCollection))]
public sealed class RateLimitPartitionTests
{
    private readonly AuthWebFactory _factory;

    public RateLimitPartitionTests(AuthWebFactory factory)
    {
        _factory = factory;
    }

    // The "password-reset" policy permits 5 requests/min per partition. The limiter
    // runs before the endpoint handler, so once the bucket is drained the response is
    // 429 regardless of antiforgery/body validity. Partitioning is by authenticated
    // user id first, then by client IP (anonymous). The Testcontainers-backed test
    // host shares one client IP, so this test asserts the limiter engages at the
    // configured boundary on that single partition rather than faking a second IP.
    private const int ForgotPermitLimit = 5;

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

    [Fact]
    public async Task PasswordReset_forgotPartitionedByIp_returns429AfterLimit()
    {
        await _factory.ResetDatabaseAsync();
        var client = CreateClient();

        var statuses = new List<HttpStatusCode>();
        // Hammer a few beyond the 5/min limit; a unique email keeps each request valid.
        for (var i = 0; i < ForgotPermitLimit + 3; i++)
        {
            var token = await GetTokenAsync(client);
            var resp = await PostJsonAsync(
                client,
                "/auth/password/forgot",
                new { email = $"rl-{Guid.NewGuid():N}@example.com" },
                token);
            statuses.Add(resp.StatusCode);
        }

        // Requests within the limit must succeed, proving the limiter engages at the
        // boundary (not because of an unrelated error).
        Assert.True(
            statuses.Take(ForgotPermitLimit).All(s => s == HttpStatusCode.OK),
            "Requests within the limit should succeed. Observed: " + string.Join(", ", statuses));
        Assert.Contains(HttpStatusCode.TooManyRequests, statuses);
    }
}
