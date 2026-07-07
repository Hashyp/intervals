using System;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Intervals.Api.Auth;
using Intervals.Api.Data;
using Intervals.Api.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Intervals.Api.Tests;

[Collection(nameof(AuthCollection))]
public sealed class AuthActionTokenServiceTests
{
    private static readonly Regex Base64UrlPattern =
        new("^[A-Za-z0-9_-]+$", RegexOptions.Compiled);

    private readonly AuthWebFactory _factory;

    public AuthActionTokenServiceTests(AuthWebFactory factory)
    {
        _factory = factory;
    }

    private static (IServiceScope Scope, IntervalsDbContext Db, AuthActionTokenService Service) Resolve(
        AuthWebFactory factory,
        TimeProvider? timeProvider = null)
    {
        var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IntervalsDbContext>();
        var service = new AuthActionTokenService(
            db,
            timeProvider ?? TimeProvider.System,
            NullLogger<AuthActionTokenService>.Instance);
        return (scope, db, service);
    }

    private async Task<AppUser> CreateUserAsync(string email)
    {
        var user = new AppUser
        {
            Id = Guid.NewGuid(),
            DisplayName = email,
            Email = email.ToLowerInvariant(),
            EmailNormalized = email.ToUpperInvariant(),
            CreatedUtc = DateTimeOffset.UtcNow,
            LastLoginUtc = DateTimeOffset.UtcNow,
        };
        await _factory.SeedUserAsync(user);
        return user;
    }

    private static string HashRawToken(string rawToken)
    {
        var rawBytes = DecodeBase64Url(rawToken);
        return EncodeBase64Url(SHA256.HashData(rawBytes));
    }

    private static byte[] DecodeBase64Url(string value)
    {
        var builder = new StringBuilder(value.Length + 2);
        foreach (var c in value)
        {
            builder.Append(c switch
            {
                '-' => '+',
                '_' => '/',
                _ => c,
            });
        }

        switch (builder.Length % 4)
        {
            case 2:
                builder.Append("==");
                break;
            case 3:
                builder.Append('=');
                break;
        }

        return Convert.FromBase64String(builder.ToString());
    }

    private static string EncodeBase64Url(byte[] bytes)
    {
        return Convert.ToBase64String(bytes)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }

    [Fact]
    public async Task Issue_returnsRawTokenAndStoresOnlyHash()
    {
        await _factory.ResetDatabaseAsync();
        var user = await CreateUserAsync("user@example.com");
        var (scope, db, service) = Resolve(_factory);
        await using var __ = db;
        using var _ = scope;

        var rawToken = await service.IssueAsync(
            user.Id,
            AuthActionTokenPurpose.EmailVerification,
            user.Email,
            TimeSpan.FromHours(1),
            null);

        Assert.False(string.IsNullOrWhiteSpace(rawToken));
        Assert.Equal(43, rawToken.Length);
        Assert.Matches(Base64UrlPattern, rawToken);

        var row = await db.AuthActionTokens.SingleAsync();
        Assert.NotEqual(rawToken, row.TokenHash);
        Assert.Equal(HashRawToken(rawToken), row.TokenHash);
        Assert.Equal(user.Id, row.UserId);
        Assert.Equal(AuthActionTokenPurpose.EmailVerification, row.Purpose);
        Assert.Equal("USER@EXAMPLE.COM", row.EmailNormalized);
        Assert.Null(row.ConsumedUtc);
        Assert.Null(row.RevokedUtc);
    }

    [Fact]
    public async Task Issue_revokesPriorActiveTokensForSamePurposeAndEmail()
    {
        await _factory.ResetDatabaseAsync();
        var user = await CreateUserAsync("user@example.com");
        var (scope, db, service) = Resolve(_factory);
        await using var __ = db;
        using var _ = scope;

        var first = await service.IssueAsync(
            user.Id,
            AuthActionTokenPurpose.PasswordReset,
            user.Email,
            TimeSpan.FromHours(1),
            null);
        var second = await service.IssueAsync(
            user.Id,
            AuthActionTokenPurpose.PasswordReset,
            user.Email,
            TimeSpan.FromHours(1),
            null);

        var rows = await db.AuthActionTokens
            .AsNoTracking()
            .Where(t => t.UserId == user.Id && t.Purpose == AuthActionTokenPurpose.PasswordReset)
            .ToListAsync();

        Assert.Equal(2, rows.Count);
        Assert.Single(rows, r => r.RevokedUtc is not null);
        Assert.Single(rows, r => r.RevokedUtc is null && r.ConsumedUtc is null);

        Assert.Null(await service.ValidateAsync(AuthActionTokenPurpose.PasswordReset, first));
        Assert.NotNull(await service.ValidateAsync(AuthActionTokenPurpose.PasswordReset, second));
    }

    [Fact]
    public async Task Validate_returnsNullForUnknownToken()
    {
        await _factory.ResetDatabaseAsync();
        var (scope, db, service) = Resolve(_factory);
        await using var __ = db;
        using var _ = scope;

        var bogus = EncodeBase64Url(RandomNumberGenerator.GetBytes(32));
        Assert.Null(await service.ValidateAsync(AuthActionTokenPurpose.EmailVerification, bogus));
    }

    [Fact]
    public async Task Validate_returnsNullWhenRevoked()
    {
        await _factory.ResetDatabaseAsync();
        var user = await CreateUserAsync("user@example.com");
        var (scope, db, service) = Resolve(_factory);
        await using var __ = db;
        using var _ = scope;

        var raw = await service.IssueAsync(
            user.Id,
            AuthActionTokenPurpose.EmailVerification,
            user.Email,
            TimeSpan.FromHours(1),
            null);

        await service.RevokeAsync(user.Id, AuthActionTokenPurpose.EmailVerification, user.Email);

        Assert.Null(await service.ValidateAsync(AuthActionTokenPurpose.EmailVerification, raw));
    }

    [Fact]
    public async Task Validate_returnsNullWhenExpired()
    {
        await _factory.ResetDatabaseAsync();
        var user = await CreateUserAsync("user@example.com");
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
        var (scope, db, service) = Resolve(_factory, clock);
        await using var __ = db;
        using var _ = scope;

        var raw = await service.IssueAsync(
            user.Id,
            AuthActionTokenPurpose.EmailVerification,
            user.Email,
            TimeSpan.FromHours(1),
            null);

        Assert.NotNull(await service.ValidateAsync(AuthActionTokenPurpose.EmailVerification, raw));

        clock.Advance(TimeSpan.FromHours(2));

        Assert.Null(await service.ValidateAsync(AuthActionTokenPurpose.EmailVerification, raw));
    }

    [Fact]
    public async Task Consume_isOneTimeUse()
    {
        await _factory.ResetDatabaseAsync();
        var user = await CreateUserAsync("user@example.com");
        var (scope, db, service) = Resolve(_factory);
        await using var __ = db;
        using var _ = scope;

        var raw = await service.IssueAsync(
            user.Id,
            AuthActionTokenPurpose.PasswordReset,
            user.Email,
            TimeSpan.FromHours(1),
            null);

        var consumed = await service.ConsumeAsync(AuthActionTokenPurpose.PasswordReset, raw);
        Assert.NotNull(consumed);
        Assert.NotNull(consumed!.ConsumedUtc);

        var second = await service.ConsumeAsync(AuthActionTokenPurpose.PasswordReset, raw);
        Assert.Null(second);

        Assert.Null(await service.ValidateAsync(AuthActionTokenPurpose.PasswordReset, raw));
    }

    [Fact]
    public async Task Revoke_invalidatesOutstandingTokens()
    {
        await _factory.ResetDatabaseAsync();
        var user = await CreateUserAsync("user@example.com");
        var (scope, db, service) = Resolve(_factory);
        await using var __ = db;
        using var _ = scope;

        var raw = await service.IssueAsync(
            user.Id,
            AuthActionTokenPurpose.AccountMerge,
            user.Email,
            TimeSpan.FromHours(1),
            null);

        Assert.NotNull(await service.ValidateAsync(AuthActionTokenPurpose.AccountMerge, raw));

        await service.RevokeAsync(user.Id, AuthActionTokenPurpose.AccountMerge, user.Email);

        Assert.Null(await service.ValidateAsync(AuthActionTokenPurpose.AccountMerge, raw));

        var row = await db.AuthActionTokens.AsNoTracking().SingleAsync();
        Assert.NotNull(row.RevokedUtc);
    }

    internal sealed class FakeTimeProvider : TimeProvider
    {
        private DateTimeOffset _now;

        public FakeTimeProvider(DateTimeOffset start) => _now = start;

        public override DateTimeOffset GetUtcNow() => _now;

        public void Advance(TimeSpan delta) => _now += delta;
    }
}
