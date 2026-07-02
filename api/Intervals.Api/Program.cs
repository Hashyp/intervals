using Intervals.Api.Data;

var builder = WebApplication.CreateBuilder(args);

builder.AddNpgsqlDbContext<IntervalsDbContext>("intervalsdb");

builder.Services.AddHealthChecks();

var app = builder.Build();

app.MapHealthChecks("/health");

app.MapGet("/api/status", () =>
    Results.Ok(new ApiStatus(
        Service: "Intervals API",
        Message: "Minimal API connected to the Vite app",
        TimestampUtc: DateTimeOffset.UtcNow)));

app.Run();

internal sealed record ApiStatus(
    string Service,
    string Message,
    DateTimeOffset TimestampUtc);
