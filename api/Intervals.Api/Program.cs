using System.Security.Claims;
using Intervals.Api.Auth;
using Intervals.Api.Data;
using Intervals.Api.Email;
using Microsoft.AspNetCore.Authentication;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.AddNpgsqlDbContext<IntervalsDbContext>("intervalsdb");
builder.AddIntervalsAuth();
builder.Services.AddIntervalsEmail(builder.Configuration);
builder.Services.AddIntervalsAuthTokens();
builder.Services.AddIntervalsPasswordReset();
builder.Services.AddHealthChecks();

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var dbContext = scope.ServiceProvider.GetRequiredService<IntervalsDbContext>();
    await dbContext.Database.MigrateAsync();
}

app.UseIntervalsAuth();

app.MapAuthEndpoints();
app.MapEmailVerificationEndpoints();
app.MapPasswordResetEndpoints();

app.MapGet("/api/status", () =>
    Results.Ok(new ApiStatus(
        Service: "Intervals API",
        Message: "Minimal API connected to the Vite app",
        TimestampUtc: DateTimeOffset.UtcNow)))
    .RequireAuthorization();

app.MapHealthChecks("/health").AllowAnonymous();

if (app.Environment.IsEnvironment("Testing"))
{
    app.MapGet("/auth/test-login/{userId:guid}", async (Guid userId, HttpContext context, IAccountService accounts) =>
    {
        var user = await accounts.GetAsync(userId, context.RequestAborted);
        if (user is null)
        {
            return Results.NotFound();
        }

        var identity = new ClaimsIdentity(
            new[]
            {
                new Claim(CurrentUser.UserIdClaimType, user.Id.ToString()),
                new Claim(ClaimTypes.Name, user.DisplayName),
            },
            AuthExtensions.AppCookieScheme);

        await context.SignInAsync(AuthExtensions.AppCookieScheme, new ClaimsPrincipal(identity));
        return Results.Ok(new { ok = true });
    }).AllowAnonymous();
}

app.Run();

public partial class Program { }

internal sealed record ApiStatus(
    string Service,
    string Message,
    DateTimeOffset TimestampUtc);
