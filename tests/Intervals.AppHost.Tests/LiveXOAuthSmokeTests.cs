using System.Net;
using System.Text.Json;
using Aspire.Hosting;
using Aspire.Hosting.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Playwright;
using Projects;
using Xunit;
using Xunit.Abstractions;

namespace Intervals.AppHost.Tests;

public sealed class LiveXOAuthSmokeTests
{
    private const string EnabledVariable = "INTERVALS_RUN_LIVE_X_AUTH";
    private const string ApiBaseUrl = "http://localhost:5199";
    private readonly ITestOutputHelper _output;

    public LiveXOAuthSmokeTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact(Timeout = 360_000)]
    public async Task Live_X_oauth_login_creates_authenticated_session()
    {
        if (!IsEnabled())
        {
            _output.WriteLine(
                $"Set {EnabledVariable}=1 to run the live X OAuth smoke test. " +
                "The test is disabled by default because it opens a real browser and requires interactive X login.");
            return;
        }

        var secrets = XOAuthSecrets.LoadFromApiUserSecrets();
        using var env = new ScopedEnvironment(
            ("Authentication__X__ClientId", secrets.ClientId),
            ("Authentication__X__ClientSecret", secrets.ClientSecret),
            ("ASPNETCORE_ENVIRONMENT", "Development"),
            ("DOTNET_ENVIRONMENT", "Development"));

        var builder = await DistributedApplicationTestingBuilder
            .CreateAsync<Intervals_AppHost>();
        await using var app = await builder.BuildAsync();
        await app.StartAsync();

        await app.ResourceNotifications
            .WaitForResourceAsync("api", "Running")
            .WaitAsync(TimeSpan.FromMinutes(3));

        var apiClient = app.CreateHttpClient("api");
        await WaitForHealthAsync(apiClient);

        using var playwright = await Playwright.CreateAsync();
        await using var browser = await LaunchBrowserAsync(playwright);
        await using var context = await browser.NewContextAsync();
        var page = await context.NewPageAsync();
        page.SetDefaultTimeout((float)LoginTimeout.TotalMilliseconds);

        await SubmitXLoginFormAsync(page);
        await WaitForProviderFlowToReturnAsync(page);

        Assert.DoesNotContain("auth=provider_error", page.Url, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("auth=unknown", page.Url, StringComparison.OrdinalIgnoreCase);
        Assert.StartsWith($"{ApiBaseUrl}/api/session", page.Url, StringComparison.OrdinalIgnoreCase);

        var bodyText = await page.Locator("body").InnerTextAsync();
        using var document = JsonDocument.Parse(bodyText);
        var root = document.RootElement;

        Assert.True(root.TryGetProperty("user", out var user), bodyText);
        Assert.False(string.IsNullOrWhiteSpace(user.GetProperty("id").GetString()));

        Assert.True(root.TryGetProperty("providers", out var providers), bodyText);
        Assert.Contains(providers.EnumerateArray(), provider =>
            provider.GetProperty("id").GetString() == "x"
            && provider.GetProperty("linked").GetBoolean());
    }

    private static TimeSpan LoginTimeout =>
        int.TryParse(Environment.GetEnvironmentVariable("INTERVALS_LIVE_X_AUTH_TIMEOUT_SECONDS"), out var seconds)
            && seconds > 0
            ? TimeSpan.FromSeconds(seconds)
            : TimeSpan.FromMinutes(5);

    private static bool IsEnabled() =>
        string.Equals(Environment.GetEnvironmentVariable(EnabledVariable), "1", StringComparison.Ordinal)
        || string.Equals(Environment.GetEnvironmentVariable(EnabledVariable), "true", StringComparison.OrdinalIgnoreCase);

    private static async Task<IBrowser> LaunchBrowserAsync(IPlaywright playwright)
    {
        var channel = Environment.GetEnvironmentVariable("INTERVALS_LIVE_X_BROWSER_CHANNEL");

        try
        {
            return await playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
            {
                Headless = false,
                Channel = string.IsNullOrWhiteSpace(channel) ? null : channel,
            });
        }
        catch (PlaywrightException ex) when (ex.Message.Contains("Executable doesn't exist", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "Playwright browser binaries are not installed. Run " +
                "`pwsh tests/Intervals.AppHost.Tests/bin/Debug/net10.0/playwright.ps1 install chromium` " +
                "after building the test project, or set INTERVALS_LIVE_X_BROWSER_CHANNEL=chrome to use installed Chrome.",
                ex);
        }
    }

    private static async Task SubmitXLoginFormAsync(IPage page)
    {
        await page.SetContentAsync(
            $$"""
            <!doctype html>
            <html lang="en">
            <body>
              <form id="x-login" method="post" action="{{ApiBaseUrl}}/auth/login/x">
                <input type="hidden" name="returnUrl" value="/api/session">
                <input type="hidden" name="rememberMe" value="true">
                <button type="submit">Continue with X</button>
              </form>
              <script>document.getElementById("x-login").submit();</script>
            </body>
            </html>
            """);
    }

    private static async Task WaitForProviderFlowToReturnAsync(IPage page)
    {
        var deadline = DateTimeOffset.UtcNow.Add(LoginTimeout);
        while (DateTimeOffset.UtcNow < deadline)
        {
            if (IsTerminalReturnUrl(page.Url))
            {
                await page.WaitForLoadStateAsync(LoadState.Load);
                return;
            }

            await Task.Delay(TimeSpan.FromSeconds(1));
        }

        throw new TimeoutException(
            $"X OAuth did not return to {ApiBaseUrl} within {LoginTimeout.TotalSeconds:N0} seconds. " +
            "Complete the X login, MFA, consent, or provider error screen in the opened browser.");
    }

    private static bool IsTerminalReturnUrl(string url) =>
        url.StartsWith($"{ApiBaseUrl}/api/session", StringComparison.OrdinalIgnoreCase)
        || (url.StartsWith(ApiBaseUrl, StringComparison.OrdinalIgnoreCase)
            && url.Contains("auth=", StringComparison.OrdinalIgnoreCase));

    private static async Task WaitForHealthAsync(HttpClient client)
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(1));
        while (!cts.IsCancellationRequested)
        {
            try
            {
                var response = await client.GetAsync("/health", cts.Token);
                if (response.StatusCode == HttpStatusCode.OK)
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

    private sealed record XOAuthSecrets(string ClientId, string ClientSecret)
    {
        public static XOAuthSecrets LoadFromApiUserSecrets()
        {
            var configuration = new ConfigurationBuilder()
                .AddUserSecrets(typeof(Program).Assembly, optional: true)
                .Build();

            var clientId = configuration["Authentication:X:ClientId"];
            var clientSecret = configuration["Authentication:X:ClientSecret"];

            if (string.IsNullOrWhiteSpace(clientId) || string.IsNullOrWhiteSpace(clientSecret))
            {
                throw new InvalidOperationException(
                    "Missing X OAuth user secrets for api/Intervals.Api. Set " +
                    "`Authentication:X:ClientId` and `Authentication:X:ClientSecret` with `dotnet user-secrets`.");
            }

            if (clientId == "test-x-client-id" || clientSecret == "test-x-client-secret")
            {
                throw new InvalidOperationException(
                    "The live X OAuth smoke test requires real X OAuth user secrets, not test placeholders.");
            }

            return new XOAuthSecrets(clientId, clientSecret);
        }
    }

    private sealed class ScopedEnvironment : IDisposable
    {
        private readonly IReadOnlyList<(string Key, string? PreviousValue)> _previousValues;

        public ScopedEnvironment(params (string Key, string Value)[] values)
        {
            _previousValues = values
                .Select(value => (value.Key, Environment.GetEnvironmentVariable(value.Key)))
                .ToList();

            foreach (var (key, value) in values)
            {
                Environment.SetEnvironmentVariable(key, value);
            }
        }

        public void Dispose()
        {
            foreach (var (key, value) in _previousValues)
            {
                Environment.SetEnvironmentVariable(key, value);
            }
        }
    }
}
