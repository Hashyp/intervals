using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Intervals.Api.Auth;
using Intervals.Api.Data;
using Intervals.Api.Data.Entities;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Intervals.Api.Tests;

[Collection(nameof(AuthCollection))]
public sealed class ProviderLinkingIntegrationTests
{
    private readonly AuthWebFactory _factory;

    public ProviderLinkingIntegrationTests(AuthWebFactory factory)
    {
        _factory = factory;
    }

    private static (IServiceScope Scope, IProviderLinkingService Linking, IAccountMergeService Merge, IntervalsDbContext Db) Resolve(AuthWebFactory factory)
    {
        var scope = factory.Services.CreateScope();
        var linking = scope.ServiceProvider.GetRequiredService<IProviderLinkingService>();
        var merge = scope.ServiceProvider.GetRequiredService<IAccountMergeService>();
        var db = scope.ServiceProvider.GetRequiredService<IntervalsDbContext>();
        return (scope, linking, merge, db);
    }

    private static AppUser NewUser(string displayName, string? email = null, string? securityStamp = null) => new()
    {
        DisplayName = displayName,
        Email = email,
        EmailNormalized = email is null ? null : email.ToUpperInvariant(),
        CreatedUtc = DateTimeOffset.UtcNow,
        LastLoginUtc = DateTimeOffset.UtcNow,
        SecurityStamp = securityStamp,
    };

    private static ExternalLogin NewExternal(Guid userId, string provider, string providerUserId, string? email = null) => new()
    {
        UserId = userId,
        Provider = provider,
        ProviderUserId = providerUserId,
        Email = email,
        EmailVerified = true,
        CreatedUtc = DateTimeOffset.UtcNow,
        LastLoginUtc = DateTimeOffset.UtcNow,
    };

    private static PasswordCredential NewPassword(Guid userId, string email) => new()
    {
        UserId = userId,
        Email = email,
        EmailNormalized = email.ToUpperInvariant(),
        PasswordHash = "hashed",
        EmailVerified = false,
        CreatedAtUtc = DateTimeOffset.UtcNow,
        UpdatedAtUtc = DateTimeOffset.UtcNow,
    };

    private async Task SeedAsync(params AppUser[] users)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IntervalsDbContext>();
        db.AppUsers.AddRange(users);
        await db.SaveChangesAsync();
    }

    private static HttpContext NewHttpContext()
    {
        var context = new DefaultHttpContext();
        context.Request.Scheme = "http";
        return context;
    }

    private static void RoundTripCookies(HttpContext context)
    {
        var setCookies = context.Response.Headers.SetCookie.ToList();
        context.Response.Headers.Remove("Set-Cookie");

        var cookies = new System.Collections.Generic.Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var part in context.Request.Headers.Cookie.ToString()
            .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var eq = part.IndexOf('=');
            if (eq > 0)
            {
                cookies[part[..eq]] = part[(eq + 1)..];
            }
        }

        foreach (var sc in setCookies)
        {
            var semi = sc.IndexOf(';');
            var nameValue = semi >= 0 ? sc[..semi] : sc;
            var eq = nameValue.IndexOf('=');
            if (eq <= 0)
            {
                continue;
            }

            var name = nameValue[..eq];
            var value = nameValue[(eq + 1)..];
            if (string.IsNullOrEmpty(value))
            {
                cookies.Remove(name);
            }
            else
            {
                cookies[name] = value;
            }
        }

        context.Request.Headers.Cookie = string.Join("; ", cookies.Select(kv => $"{kv.Key}={kv.Value}"));
    }

    private HttpClient CreateClient() =>
        _factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            HandleCookies = true,
        });

    [Fact]
    public async Task CompleteLink_attaches_provider_when_unlinked()
    {
        await _factory.ResetDatabaseAsync();
        var primary = NewUser("Primary", "primary@example.com");
        var other = NewUser("Other", "other@example.com");
        await SeedAsync(primary, other);

        var (scope, linking, _, db) = Resolve(_factory);
        await using var _db = db;
        using var _ = scope;

        var profile = new ExternalUserProfile("google", "google-new-1", "Primary User", "primary@example.com", true, null);
        var result = await linking.CompleteLinkAsync(profile, primary.Id, correlationId: null);

        Assert.Equal(LinkOutcome.Linked, result.Outcome);

        var external = await db.ExternalLogins.SingleAsync(e => e.Provider == "google" && e.ProviderUserId == "google-new-1");
        Assert.Equal(primary.Id, external.UserId);

        var events = await db.AuthEvents.Where(e => e.UserId == primary.Id).ToListAsync();
        Assert.Contains(events, e => e.EventType == "account_link_success" && e.Success && e.Provider == "google");
    }

    [Fact]
    public async Task CompleteLink_is_noop_when_already_linked_to_same_user()
    {
        await _factory.ResetDatabaseAsync();
        var primary = NewUser("Primary", "primary@example.com");
        primary.ExternalLogins.Add(NewExternal(primary.Id, "google", "google-same"));
        await SeedAsync(primary);

        var (scope, linking, _, db) = Resolve(_factory);
        await using var _db = db;
        using var _ = scope;

        var profile = new ExternalUserProfile("google", "google-same", "Primary User", "primary@example.com", true, null);
        var result = await linking.CompleteLinkAsync(profile, primary.Id, correlationId: null);

        Assert.Equal(LinkOutcome.AlreadyLinked, result.Outcome);
        var count = await db.ExternalLogins.CountAsync(e => e.Provider == "google" && e.ProviderUserId == "google-same");
        Assert.Equal(1, count);
    }

    [Fact]
    public async Task CompleteLink_returns_collision_when_provider_belongs_to_another_user()
    {
        await _factory.ResetDatabaseAsync();
        var primary = NewUser("Primary", "primary@example.com");
        var secondary = NewUser("Secondary", "secondary@example.com");
        secondary.ExternalLogins.Add(NewExternal(secondary.Id, "google", "google-owned"));
        await SeedAsync(primary, secondary);

        var (scope, linking, _, db) = Resolve(_factory);
        await using var _db = db;
        using var _ = scope;

        var profile = new ExternalUserProfile("google", "google-owned", "Secondary User", "secondary@example.com", true, null);
        var result = await linking.CompleteLinkAsync(profile, primary.Id, correlationId: null);

        Assert.Equal(LinkOutcome.Collision, result.Outcome);
        Assert.Equal(secondary.Id, result.SecondaryUserId);

        // No auto-merge: the external login still belongs to the secondary user.
        var external = await db.ExternalLogins.SingleAsync(e => e.Provider == "google" && e.ProviderUserId == "google-owned");
        Assert.Equal(secondary.Id, external.UserId);
        Assert.Null(await db.AppUsers.FirstOrDefaultAsync(u => u.Id == secondary.Id && u.MergedIntoUserId != null));
    }

    [Fact]
    public async Task MergeAsync_moves_external_login_and_secondary_password_into_primary()
    {
        await _factory.ResetDatabaseAsync();
        var primary = NewUser("Primary", "primary@example.com", securityStamp: "old-stamp");
        var secondary = NewUser("Secondary", "secondary@example.com");
        secondary.ExternalLogins.Add(NewExternal(secondary.Id, "google", "google-sec", "secondary@example.com"));
        secondary.PasswordCredential = NewPassword(secondary.Id, "secondary@example.com");
        await SeedAsync(primary, secondary);

        var (scope, _, merge, db) = Resolve(_factory);
        await using var _db = db;
        using var _ = scope;

        var context = NewHttpContext();
        merge.SetPendingMerge(context, primary.Id, secondary.Id, "google");
        RoundTripCookies(context);

        var ok = await merge.MergeAsync(primary.Id, context, correlationId: null);
        Assert.True(ok);

        var mergedSecondary = await db.AppUsers.FirstAsync(u => u.Id == secondary.Id);
        Assert.Equal(primary.Id, mergedSecondary.MergedIntoUserId);
        Assert.NotNull(mergedSecondary.MergedUtc);
        Assert.NotNull(mergedSecondary.DisabledUtc);

        var movedExternal = await db.ExternalLogins.FirstAsync(e => e.Provider == "google" && e.ProviderUserId == "google-sec");
        Assert.Equal(primary.Id, movedExternal.UserId);

        var primaryCred = await db.PasswordCredentials.FirstAsync(c => c.UserId == primary.Id);
        Assert.Equal("SECONDARY@EXAMPLE.COM", primaryCred.EmailNormalized);
        Assert.Null(await db.PasswordCredentials.FirstOrDefaultAsync(c => c.UserId == secondary.Id));

        var refreshedPrimary = await db.AppUsers.FirstAsync(u => u.Id == primary.Id);
        Assert.NotEqual("old-stamp", refreshedPrimary.SecurityStamp);
        Assert.NotNull(refreshedPrimary.SecurityStamp);

        var events = await db.AuthEvents.ToListAsync();
        Assert.Contains(events, e => e.EventType == "account_merged" && e.UserId == primary.Id && e.Success);
        Assert.Contains(events, e => e.EventType == "account_merged_secondary" && e.UserId == secondary.Id && e.Success);
    }

    [Fact]
    public async Task MergeAsync_keeps_primary_password_when_both_have_passwords()
    {
        await _factory.ResetDatabaseAsync();
        var primary = NewUser("Primary", "primary@example.com");
        primary.PasswordCredential = NewPassword(primary.Id, "primary@example.com");
        var secondary = NewUser("Secondary", "secondary@example.com");
        secondary.ExternalLogins.Add(NewExternal(secondary.Id, "microsoft", "ms-sec", "secondary@example.com"));
        secondary.PasswordCredential = NewPassword(secondary.Id, "secondary@example.com");
        await SeedAsync(primary, secondary);

        var (scope, _, merge, db) = Resolve(_factory);
        await using var _db = db;
        using var _ = scope;

        var context = NewHttpContext();
        merge.SetPendingMerge(context, primary.Id, secondary.Id, "microsoft");
        RoundTripCookies(context);

        var ok = await merge.MergeAsync(primary.Id, context, correlationId: null);
        Assert.True(ok);

        // Primary keeps its own password; secondary's credential remains on the (now disabled) secondary.
        var primaryCred = await db.PasswordCredentials.FirstAsync(c => c.UserId == primary.Id);
        Assert.Equal("PRIMARY@EXAMPLE.COM", primaryCred.EmailNormalized);

        var secondaryCred = await db.PasswordCredentials.FirstAsync(c => c.UserId == secondary.Id);
        Assert.Equal("SECONDARY@EXAMPLE.COM", secondaryCred.EmailNormalized);

        var movedExternal = await db.ExternalLogins.FirstAsync(e => e.Provider == "microsoft" && e.ProviderUserId == "ms-sec");
        Assert.Equal(primary.Id, movedExternal.UserId);
    }

    [Fact]
    public async Task PendingMerge_cookie_round_trips_clears_and_rejects_primary_mismatch()
    {
        await _factory.ResetDatabaseAsync();
        var primary = NewUser("Primary", "primary@example.com");
        var secondary = NewUser("Secondary", "secondary@example.com");
        await SeedAsync(primary, secondary);

        var (scope, _, merge, _) = Resolve(_factory);
        using var _ = scope;

        var context = NewHttpContext();
        merge.SetPendingMerge(context, primary.Id, secondary.Id, "google");
        RoundTripCookies(context);

        var detail = await merge.GetPendingMergeAsync(primary.Id, context);
        Assert.NotNull(detail);
        Assert.Equal(primary.Id, detail!.PrimaryUserId);
        Assert.Equal(secondary.Id, detail.SecondaryUserId);
        Assert.Equal("google", detail.Provider);

        var other = Guid.NewGuid();
        var mismatch = await merge.GetPendingMergeAsync(other, context);
        Assert.Null(mismatch);

        merge.ClearPendingMerge(context);
        RoundTripCookies(context);

        var afterClear = await merge.GetPendingMergeAsync(primary.Id, context);
        Assert.Null(afterClear);
    }

    [Fact]
    public async Task Complete_endpoint_without_external_cookie_redirects_to_account_settings()
    {
        await _factory.ResetDatabaseAsync();
        var client = CreateClient();

        var resp = await client.GetAsync("/auth/providers/complete/google");

        Assert.Equal(HttpStatusCode.Redirect, resp.StatusCode);
        Assert.NotNull(resp.Headers.Location);
        var location = resp.Headers.Location!.ToString();
        Assert.Contains("/account-settings", location);
        Assert.Contains("provider_error", location);
    }

    [Fact]
    public async Task PendingMerge_endpoint_unauthenticated_is_rejected()
    {
        await _factory.ResetDatabaseAsync();
        var client = CreateClient();

        var resp = await client.GetAsync("/auth/providers/pending-merge");

        Assert.True(
            resp.StatusCode == HttpStatusCode.Unauthorized
                || resp.StatusCode == HttpStatusCode.Redirect,
            $"Expected 401 or redirect, got {(int)resp.StatusCode}");
    }

    [Fact]
    public async Task PendingMerge_endpoint_authenticated_with_no_cookie_returns_404()
    {
        await _factory.ResetDatabaseAsync();
        var primary = NewUser("Primary", "primary@example.com");
        await SeedAsync(primary);

        var client = CreateClient();
        var loginResp = await client.GetAsync($"/auth/test-login/{primary.Id}");
        loginResp.EnsureSuccessStatusCode();

        var resp = await client.GetAsync("/auth/providers/pending-merge");

        Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);
    }
}
