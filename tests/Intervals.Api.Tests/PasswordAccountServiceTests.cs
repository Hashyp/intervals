using System;
using System.Linq;
using System.Threading.Tasks;
using Intervals.Api.Auth;
using Intervals.Api.Data;
using Intervals.Api.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Intervals.Api.Tests;

[Collection(nameof(AuthCollection))]
public sealed class PasswordAccountServiceTests
{
    private readonly AuthWebFactory _factory;

    public PasswordAccountServiceTests(AuthWebFactory factory)
    {
        _factory = factory;
    }

    private static (IServiceScope Scope, IPasswordAccountService Service, IntervalsDbContext Db) Resolve(AuthWebFactory factory)
    {
        var scope = factory.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<IPasswordAccountService>();
        var db = scope.ServiceProvider.GetRequiredService<IntervalsDbContext>();
        return (scope, service, db);
    }

    [Fact]
    public async Task Register_with_valid_email_and_password_succeeds_and_creates_credential()
    {
        await _factory.ResetDatabaseAsync();
        var (scope, service, db) = Resolve(_factory);
        await using var __ = db;
        using var _ = scope;

        var result = await service.RegisterAsync("user@example.com", "supersecret", null);

        Assert.True(result.Success);
        Assert.NotNull(result.User);

        var userCount = await db.AppUsers.CountAsync();
        var credCount = await db.PasswordCredentials.CountAsync();
        Assert.Equal(1, userCount);
        Assert.Equal(1, credCount);

        var cred = await db.PasswordCredentials.SingleAsync();
        Assert.False(cred.EmailVerified);
        Assert.False(string.IsNullOrWhiteSpace(cred.PasswordHash));
        Assert.Equal("USER@EXAMPLE.COM", cred.EmailNormalized);
    }

    [Fact]
    public async Task Register_with_already_registered_case_insensitive_email_returns_email_taken()
    {
        await _factory.ResetDatabaseAsync();
        var (scope, service, db) = Resolve(_factory);
        await using var __ = db;
        using var _ = scope;

        var first = await service.RegisterAsync("User@Example.com", "supersecret", null);
        Assert.True(first.Success);

        var second = await service.RegisterAsync("user@example.com", "anothersecret", null);

        Assert.False(second.Success);
        Assert.Equal(AuthResultCodes.EmailTaken, second.FailureCode);
        Assert.Equal(1, await db.AppUsers.CountAsync());
        Assert.Equal(1, await db.PasswordCredentials.CountAsync());
    }

    [Fact]
    public async Task Register_with_too_short_password_returns_weak_password()
    {
        await _factory.ResetDatabaseAsync();
        var (scope, service, db) = Resolve(_factory);
        await using var __ = db;
        using var _ = scope;

        var result = await service.RegisterAsync("user@example.com", "short", null);

        Assert.False(result.Success);
        Assert.Equal(AuthResultCodes.WeakPassword, result.FailureCode);
        Assert.Equal(0, await db.AppUsers.CountAsync());
    }

    [Fact]
    public async Task Authenticate_with_correct_password_after_registering_returns_same_user()
    {
        await _factory.ResetDatabaseAsync();
        var (scope, service, db) = Resolve(_factory);
        await using var __ = db;
        using var _ = scope;

        var registered = await service.RegisterAsync("user@example.com", "supersecret", null);
        var login = await service.AuthenticateAsync("USER@example.com", "supersecret", null);

        Assert.True(login.Success);
        Assert.Equal(registered.User!.Id, login.User!.Id);
    }

    [Fact]
    public async Task Authenticate_with_wrong_password_returns_invalid_credentials_and_increments_count()
    {
        await _factory.ResetDatabaseAsync();
        var (scope, service, db) = Resolve(_factory);
        await using var __ = db;
        using var _ = scope;

        var registered = await service.RegisterAsync("user@example.com", "supersecret", null);

        var login = await service.AuthenticateAsync("user@example.com", "wrongpassword", null);

        Assert.False(login.Success);
        Assert.Equal(AuthResultCodes.InvalidCredentials, login.FailureCode);

        var cred = await db.PasswordCredentials.FirstAsync(c => c.UserId == registered.User!.Id);
        Assert.Equal(1, cred.FailedAttemptCount);
    }

    [Fact]
    public async Task Authenticate_repeatedly_with_wrong_passwords_eventually_locks_out()
    {
        await _factory.ResetDatabaseAsync();
        var (scope, service, db) = Resolve(_factory);
        await using var __ = db;
        using var _ = scope;

        var registered = await service.RegisterAsync("user@example.com", "supersecret", null);

        PasswordLoginResult? last = null;
        for (var i = 0; i < 5; i++)
        {
            last = await service.AuthenticateAsync("user@example.com", "wrongpassword", null);
        }

        Assert.NotNull(last);
        Assert.False(last!.Success);
        Assert.Equal(AuthResultCodes.LockedOut, last.FailureCode);

        var cred = await db.PasswordCredentials.FirstAsync(c => c.UserId == registered.User!.Id);
        Assert.NotNull(cred.LockoutUntilUtc);
        Assert.True(cred.LockoutUntilUtc > DateTimeOffset.UtcNow);
        Assert.Equal(0, cred.FailedAttemptCount);

        var locked = await service.AuthenticateAsync("user@example.com", "wrongpassword", null);
        Assert.False(locked.Success);
        Assert.Equal(AuthResultCodes.LockedOut, locked.FailureCode);
    }

    [Fact]
    public async Task Authenticate_with_unknown_email_returns_invalid_credentials_and_records_failure_event()
    {
        await _factory.ResetDatabaseAsync();
        var (scope, service, db) = Resolve(_factory);
        await using var __ = db;
        using var _ = scope;

        var login = await service.AuthenticateAsync("nobody@example.com", "whateverpassword", null);

        Assert.False(login.Success);
        Assert.Equal(AuthResultCodes.InvalidCredentials, login.FailureCode);

        var failures = await db.AuthEvents
            .Where(e => e.EventType == AuthEventTypes.LoginFailure
                && e.UserId == null
                && e.Provider == AuthProviderNames.Password)
            .ToListAsync();
        Assert.NotEmpty(failures);
    }

    [Fact]
    public async Task Authenticate_records_login_success_and_failure_events()
    {
        await _factory.ResetDatabaseAsync();
        var (scope, service, db) = Resolve(_factory);
        await using var __ = db;
        using var _ = scope;

        var registered = await service.RegisterAsync("user@example.com", "supersecret", null);

        await service.AuthenticateAsync("user@example.com", "supersecret", null);
        await service.AuthenticateAsync("user@example.com", "wrongpassword", null);

        var events = await db.AuthEvents
            .Where(e => e.Provider == AuthProviderNames.Password
                && (e.EventType == AuthEventTypes.LoginSuccess
                    || e.EventType == AuthEventTypes.LoginFailure))
            .ToListAsync();

        Assert.Contains(events, e => e.EventType == AuthEventTypes.LoginSuccess && e.UserId == registered.User!.Id);
        Assert.Contains(events, e => e.EventType == AuthEventTypes.LoginFailure && e.UserId == registered.User!.Id);
    }

    [Fact]
    public async Task Register_records_register_success_event()
    {
        await _factory.ResetDatabaseAsync();
        var (scope, service, db) = Resolve(_factory);
        await using var __ = db;
        using var _ = scope;

        var registered = await service.RegisterAsync("user@example.com", "supersecret", null);

        var events = await db.AuthEvents
            .Where(e => e.EventType == AuthEventTypes.RegisterSuccess
                && e.Provider == AuthProviderNames.Password)
            .ToListAsync();

        Assert.NotEmpty(events);
        Assert.Equal(registered.User!.Id, events[0].UserId);
    }

    [Fact]
    public async Task Register_with_long_local_part_clamps_display_name_instead_of_throwing()
    {
        await _factory.ResetDatabaseAsync();
        var (scope, service, db) = Resolve(_factory);
        await using var __ = db;
        using var _ = scope;

        var longLocal = new string('a', 300);
        var email = longLocal + "@x.com";

        var result = await service.RegisterAsync(email, "supersecret", null);

        Assert.True(result.Success);
        var user = await db.AppUsers.SingleAsync();
        Assert.True(user.DisplayName.Length <= 256);
        Assert.Equal(longLocal[..256], user.DisplayName);
    }
}
