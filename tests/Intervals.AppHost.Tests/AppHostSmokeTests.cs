using System.Net;
using Aspire.Hosting;
using Aspire.Hosting.Testing;
using Projects;
using Xunit;

namespace Intervals.AppHost.Tests;

public sealed class AppHostSmokeTests
{
    [Fact]
    public async Task AppHost_starts_postgres_and_api_and_enforces_auth_boundary()
    {
        var builder = await DistributedApplicationTestingBuilder
            .CreateAsync<Intervals_AppHost>();
        await using var app = await builder.BuildAsync();
        await app.StartAsync();

        // The API resource has WaitFor(intervalsdb), so reaching the Running state
        // transitively proves postgres/intervalsdb came up first.
        await app.ResourceNotifications
            .WaitForResourceAsync("api", "Running")
            .WaitAsync(TimeSpan.FromMinutes(3));

        var apiClient = app.CreateHttpClient("api");
        await WaitForHealthAsync(apiClient);

        var health = await apiClient.GetAsync("/health");
        Assert.Equal(HttpStatusCode.OK, health.StatusCode);

        var status = await apiClient.GetAsync("/api/status");
        Assert.Equal(HttpStatusCode.Unauthorized, status.StatusCode);

        var session = await apiClient.GetAsync("/api/session");
        Assert.Equal(HttpStatusCode.Unauthorized, session.StatusCode);
    }

    private static async Task WaitForHealthAsync(HttpClient client)
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(1));
        while (!cts.IsCancellationRequested)
        {
            try
            {
                var response = await client.GetAsync("/health", cts.Token);
                if (response.IsSuccessStatusCode)
                {
                    return;
                }
            }
            catch (HttpRequestException)
            {
                // API not ready yet; keep polling.
            }

            await Task.Delay(TimeSpan.FromSeconds(2), cts.Token);
        }

        throw new TimeoutException("API /health did not become healthy in time.");
    }
}
