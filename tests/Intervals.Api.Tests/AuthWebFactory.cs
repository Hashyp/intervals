using System.Collections.Generic;
using Intervals.Api.Data;
using Intervals.Api.Data.Entities;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Testcontainers.PostgreSql;
using Xunit;

namespace Intervals.Api.Tests;

public sealed class AuthWebFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder()
        .WithImage("postgres:18")
        .Build();

    public string ConnectionString { get; private set; } = string.Empty;

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.ConfigureAppConfiguration((_, config) =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:intervalsdb"] = ConnectionString,
                ["Web:BaseUrl"] = string.Empty,
            });
        });
    }

    public async Task InitializeAsync()
    {
        await _container.StartAsync();
        ConnectionString = _container.GetConnectionString();
        Environment.SetEnvironmentVariable("ConnectionStrings__intervalsdb", ConnectionString);
        Environment.SetEnvironmentVariable("Authentication__Google__ClientId", "test-google-client-id");
        Environment.SetEnvironmentVariable("Authentication__Google__ClientSecret", "test-google-client-secret");
        Environment.SetEnvironmentVariable("Authentication__Microsoft__ClientId", "test-microsoft-client-id");
        Environment.SetEnvironmentVariable("Authentication__Microsoft__ClientSecret", "test-microsoft-client-secret");
        Environment.SetEnvironmentVariable("Authentication__X__ClientId", "test-x-client-id");
        Environment.SetEnvironmentVariable("Authentication__X__ClientSecret", "test-x-client-secret");
        Environment.SetEnvironmentVariable("Web__BaseUrl", string.Empty);
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IntervalsDbContext>();
        await db.Database.MigrateAsync();
    }

    public async Task ResetDatabaseAsync()
    {
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IntervalsDbContext>();
        await db.Database.ExecuteSqlRawAsync(
            """TRUNCATE TABLE "PasswordCredentials", "ExternalLogins", "AuthEvents", "AppUsers" RESTART IDENTITY CASCADE;""");
    }

    public async Task SeedUserAsync(AppUser user)
    {
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IntervalsDbContext>();
        db.AppUsers.Add(user);
        await db.SaveChangesAsync();
    }

    public async new Task DisposeAsync()
    {
        await _container.DisposeAsync();
    }
}
