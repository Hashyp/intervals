var builder = DistributedApplication.CreateBuilder(args);

var postgres = builder.AddPostgres("postgres");

if (builder.ExecutionContext.IsRunMode)
{
    // Persist accounts across local AppHost restarts. The generated password is
    // stored in the AppHost user secrets so it stays stable between runs.
    postgres
        .WithDataVolume("intervals-postgres-data")
        .WithLifetime(ContainerLifetime.Persistent);
}

var intervalsDb = postgres.AddDatabase("intervalsdb");

var api = builder.AddProject("api", "../api/Intervals.Api/Intervals.Api.csproj")
    .WithReference(intervalsDb)
    .WaitFor(intervalsDb)
    .WithHttpEndpoint(name: "http", port: 5199, targetPort: 8080)
    .WithHttpHealthCheck("/health")
    .WithExternalHttpEndpoints();

// The Vite web frontend and local-dev API settings are only wired up for local
// `aspire run`. Distributed tests launch the AppHost without a browser frontend,
// so guarding the web resource keeps those tests fast and deterministic.
if (builder.ExecutionContext.IsRunMode)
{
#pragma warning disable ASPIREJAVASCRIPT001
    var web = builder.AddViteApp("web", "..")
        .WithReference(api)
        .WithEnvironment("BROWSER", "none")
        .WaitFor(api)
        .WithExternalHttpEndpoints()
        .PublishAsStaticWebsite(apiPath: "/api", apiTarget: api);
#pragma warning restore ASPIREJAVASCRIPT001

    api.WithEnvironment(context =>
    {
        context.EnvironmentVariables["Web__BaseUrl"] = web.GetEndpoint("http");
    });

    api.WithEnvironment("ASPNETCORE_ENVIRONMENT", "Development");
}

builder.Build().Run();
