#:sdk Aspire.AppHost.Sdk@13.4.2
#:package Aspire.Hosting.JavaScript@13.4.2

var builder = DistributedApplication.CreateBuilder(args);

var api = builder.AddProject("api", "api/Intervals.Api/Intervals.Api.csproj")
    .WithHttpEndpoint()
    .WithHttpHealthCheck("/health")
    .WithExternalHttpEndpoints();

#pragma warning disable ASPIREJAVASCRIPT001
builder.AddViteApp("web", ".")
    .WithReference(api)
    .WithEnvironment("BROWSER", "none")
    .WaitFor(api)
    .WithExternalHttpEndpoints()
    .PublishAsStaticWebsite(apiPath: "/api", apiTarget: api);
#pragma warning restore ASPIREJAVASCRIPT001

builder.Build().Run();
