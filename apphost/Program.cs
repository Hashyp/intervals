var builder = DistributedApplication.CreateBuilder(args);

var postgres = builder.AddPostgres("postgres")
    .WithDataVolume("intervals-postgres-data")
    .WithLifetime(ContainerLifetime.Persistent);

var intervalsDb = postgres.AddDatabase("intervalsdb");

var api = builder.AddProject("api", "../api/Intervals.Api/Intervals.Api.csproj")
    .WithReference(intervalsDb)
    .WaitFor(intervalsDb)
    .WithHttpEndpoint()
    .WithHttpHealthCheck("/health")
    .WithExternalHttpEndpoints();

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

builder.Build().Run();
