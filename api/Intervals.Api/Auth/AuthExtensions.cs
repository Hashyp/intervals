using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading.RateLimiting;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OAuth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Intervals.Api.Data;
using Intervals.Api.Data.Entities;

namespace Intervals.Api.Auth;

public static class AuthExtensions
{
    public const string AppCookieScheme = CookieAuthenticationDefaults.AuthenticationScheme;
    public const string ExternalCookieScheme = "Identity.External";
    public const string RateLimitPolicy = "auth";

    public static WebApplicationBuilder AddIntervalsAuth(this WebApplicationBuilder builder)
    {
        builder.Services.Configure<AuthOptions>(
            builder.Configuration.GetSection(AuthOptions.SectionName));

        var authOptions =
            builder.Configuration.GetSection(AuthOptions.SectionName).Get<AuthOptions>()
            ?? new AuthOptions();
        var webBaseUrl = builder.Configuration["Web:BaseUrl"] ?? string.Empty;
        var cookieSecure = builder.Environment.IsProduction()
            ? CookieSecurePolicy.Always
            : CookieSecurePolicy.None;

        var authentication = builder.Services.AddAuthentication(options =>
        {
            options.DefaultScheme = AppCookieScheme;
            options.DefaultAuthenticateScheme = AppCookieScheme;
            options.DefaultSignInScheme = AppCookieScheme;
            options.DefaultChallengeScheme = AppCookieScheme;
        });

        authentication.AddCookie(AppCookieScheme, options =>
        {
            options.Cookie.Name = authOptions.CookieName;
            options.Cookie.HttpOnly = true;
            options.Cookie.SecurePolicy = cookieSecure;
            options.Cookie.SameSite = SameSiteMode.Lax;
            options.SlidingExpiration = true;
            options.ExpireTimeSpan = authOptions.SessionLifetime;
            options.LoginPath = authOptions.LoginPath;
            options.LogoutPath = "/auth/logout";
            options.AccessDeniedPath = authOptions.LoginPath;
            options.Events = new CookieAuthenticationEvents
            {
                OnRedirectToLogin = context =>
                {
                    if (IsApiRequest(context.Request))
                    {
                        return WriteApiError(context.HttpContext, StatusCodes.Status401Unauthorized, AuthResultCodes.AuthRequired, "Authentication is required.");
                    }

                    context.Response.Redirect(context.RedirectUri);
                    return Task.CompletedTask;
                },
                OnRedirectToAccessDenied = context =>
                {
                    if (IsApiRequest(context.Request))
                    {
                        return WriteApiError(context.HttpContext, StatusCodes.Status403Forbidden, AuthResultCodes.Forbidden, "Access is forbidden.");
                    }

                    context.Response.Redirect(context.RedirectUri);
                    return Task.CompletedTask;
                },
                OnValidatePrincipal = async context =>
                {
                    var principal = context.Principal;
                    if (principal is null)
                    {
                        return;
                    }

                    var userIdClaim = principal.FindFirst(CurrentUser.UserIdClaimType)?.Value;
                    if (string.IsNullOrEmpty(userIdClaim) || !Guid.TryParse(userIdClaim, out var userId))
                    {
                        return;
                    }

                    var cache = context.HttpContext.RequestServices.GetRequiredService<IMemoryCache>();
                    var cacheKey = SecurityStampCacheKey(userId);

                    try
                    {
                        if (!cache.TryGetValue(cacheKey, out SecurityStampCacheEntry cached))
                        {
                            var scopeFactory = context.HttpContext.RequestServices.GetRequiredService<IServiceScopeFactory>();
                            using var scope = scopeFactory.CreateScope();
                            var db = scope.ServiceProvider.GetRequiredService<IntervalsDbContext>();
                            var record = await db.AppUsers
                                .AsNoTracking()
                                .Where(u => u.Id == userId)
                                .Select(u => new { u.SecurityStamp, u.DisabledUtc })
                                .FirstOrDefaultAsync(context.HttpContext.RequestAborted);
                            cached = record is null
                                ? SecurityStampCacheEntry.Missing
                                : new SecurityStampCacheEntry(true, record.SecurityStamp, record.DisabledUtc);
                            cache.Set(cacheKey, cached, TimeSpan.FromMinutes(1));
                        }

                        if (!cached.Exists || cached.DisabledUtc is not null)
                        {
                            // Deleted or disabled (including merged-secondary) accounts must not
                            // keep using an existing cookie, even before the stamp is checked.
                            context.RejectPrincipal();
                            return;
                        }

                        var stampClaim = principal.FindFirst(AuthEndpoints.SecurityStampClaimType)?.Value;
                        if (stampClaim is null)
                        {
                            // Principals without a security-stamp claim (e.g. the test-login
                            // endpoint) skip stamp enforcement; the existence/disabled checks
                            // above still apply.
                            return;
                        }

                        if (string.IsNullOrEmpty(cached.Stamp))
                        {
                            // The user has never had a security stamp; nothing to invalidate yet.
                            return;
                        }

                        // Once a non-empty DB stamp exists, an empty or differing cookie claim
                        // means the cookie predates the stamp-rotating event (password reset,
                        // change, or merge) and must be treated as stale.
                        if (string.IsNullOrEmpty(stampClaim)
                            || !string.Equals(cached.Stamp, stampClaim, StringComparison.Ordinal))
                        {
                            context.RejectPrincipal();
                        }
                    }
                    catch
                    {
                        // Fail open: a DB error must not log users out.
                    }
                },
            };
        });

        authentication.AddCookie(ExternalCookieScheme, options =>
        {
            options.Cookie.Name = "Intervals.External";
            options.Cookie.HttpOnly = true;
            options.Cookie.SecurePolicy = cookieSecure;
            options.Cookie.SameSite = SameSiteMode.Lax;
            options.ExpireTimeSpan = TimeSpan.FromMinutes(10);
        });

        var googleSection = builder.Configuration.GetSection("Authentication:Google");
        var googleClientId = googleSection["ClientId"];
        if (!string.IsNullOrWhiteSpace(googleClientId))
        {
            authentication.AddGoogle(AuthProviderNames.GoogleScheme, options =>
            {
                options.ClientId = googleClientId!;
                options.ClientSecret = googleSection["ClientSecret"] ?? string.Empty;
                options.CallbackPath = "/auth/callback/google";
                options.SignInScheme = ExternalCookieScheme;
                options.SaveTokens = false;
                options.Scope.Clear();
                options.Scope.Add("openid");
                options.Scope.Add("email");
                options.Scope.Add("profile");
                options.Events = new OAuthEvents
                {
                    OnRemoteFailure = context =>
                    {
                        var code = IsCancellation(context.Failure)
                            ? AuthResultCodes.Cancelled
                            : AuthResultCodes.ProviderError;
                        context.HandleResponse();
                        context.Response.Redirect(AuthRedirect.Build(webBaseUrl, authOptions.LoginPath, code));
                        return Task.CompletedTask;
                    },
                };
            });
        }

        var microsoftSection = builder.Configuration.GetSection("Authentication:Microsoft");
        var microsoftClientId = microsoftSection["ClientId"];
        if (!string.IsNullOrWhiteSpace(microsoftClientId))
        {
            authentication.AddMicrosoftAccount(AuthProviderNames.MicrosoftScheme, options =>
            {
                options.ClientId = microsoftClientId!;
                options.ClientSecret = microsoftSection["ClientSecret"] ?? string.Empty;
                options.CallbackPath = "/auth/callback/microsoft";
                options.SignInScheme = ExternalCookieScheme;
                options.SaveTokens = false;
                options.Scope.Clear();
                options.Scope.Add("openid");
                options.Scope.Add("email");
                options.Scope.Add("profile");
                options.Events = new OAuthEvents
                {
                    OnRemoteFailure = context =>
                    {
                        var code = IsCancellation(context.Failure)
                            ? AuthResultCodes.Cancelled
                            : AuthResultCodes.ProviderError;
                        context.HandleResponse();
                        context.Response.Redirect(AuthRedirect.Build(webBaseUrl, authOptions.LoginPath, code));
                        return Task.CompletedTask;
                    },
                };
            });
        }

        var xSection = builder.Configuration.GetSection("Authentication:X");
        var xClientId = xSection["ClientId"];
        if (!string.IsNullOrWhiteSpace(xClientId))
        {
            authentication.AddOAuth(AuthProviderNames.XScheme, options =>
            {
                options.ClientId = xClientId!;
                options.ClientSecret = xSection["ClientSecret"] ?? string.Empty;
                options.CallbackPath = "/auth/callback/x";
                options.SignInScheme = ExternalCookieScheme;
                options.AuthorizationEndpoint = XOAuthDefaults.AuthorizationEndpoint;
                options.TokenEndpoint = XOAuthDefaults.TokenEndpoint;
                options.UserInformationEndpoint = XOAuthDefaults.UserInformationEndpoint;
                options.UsePkce = true;
                options.SaveTokens = false;
                options.Scope.Clear();
                options.Scope.Add(XOAuthDefaults.ScopeUsersRead);
                options.Scope.Add(XOAuthDefaults.ScopeUsersEmail);
                options.ClaimActions.MapJsonSubKey(System.Security.Claims.ClaimTypes.NameIdentifier, "data", "id");
                options.ClaimActions.MapJsonSubKey(System.Security.Claims.ClaimTypes.Name, "data", "name");
                options.ClaimActions.MapJsonSubKey("picture", "data", "profile_image_url");
                options.ClaimActions.MapJsonSubKey(System.Security.Claims.ClaimTypes.Email, "data", "email");
                options.Events = new OAuthEvents
                {
                    OnCreatingTicket = PopulateXClaimsAsync,
                    OnRemoteFailure = context =>
                    {
                        var code = IsCancellation(context.Failure)
                            ? AuthResultCodes.Cancelled
                            : AuthResultCodes.ProviderError;
                        context.HandleResponse();
                        context.Response.Redirect(AuthRedirect.Build(webBaseUrl, authOptions.LoginPath, code));
                        return Task.CompletedTask;
                    },
                };
            });
        }

        builder.Services.AddSingleton<IExternalProfileBuilder, ExternalProfileBuilder>();
        builder.Services.AddScoped<IAccountService, AccountService>();
        builder.Services.AddSingleton<PasswordPolicy>();
        builder.Services.AddScoped<IPasswordAccountService, PasswordAccountService>();
        builder.Services.AddSingleton<Microsoft.AspNetCore.Identity.PasswordHasher<AppUser>>();
        builder.Services.AddHttpContextAccessor();
        builder.Services.AddMemoryCache();

        builder.Services.AddAuthorization(options =>
        {
            options.FallbackPolicy = new AuthorizationPolicyBuilder()
                .RequireAuthenticatedUser()
                .Build();
        });

        builder.Services.AddAntiforgery(options =>
        {
            options.HeaderName = "X-CSRF-TOKEN";
            options.Cookie.Name = "Intervals.Antiforgery";
            options.Cookie.HttpOnly = true;
            options.Cookie.SecurePolicy = cookieSecure;
            options.Cookie.SameSite = SameSiteMode.Lax;
        });

        builder.Services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.AddPolicy(RateLimitPolicy, context => GetRateLimitPartition(context, permitLimit: 60, window: TimeSpan.FromMinutes(1)));
            options.AddPolicy("verification", context => GetRateLimitPartition(context, permitLimit: 5, window: TimeSpan.FromMinutes(1)));
            options.AddPolicy("password-reset", context => GetRateLimitPartition(context, permitLimit: 5, window: TimeSpan.FromMinutes(1)));
        });

        // Forwarded headers: trust-all is opt-in. In Development/Testing we keep the
        // permissive behavior (dev proxies/Vite/Aspire sit in front of the app). In
        // production the framework defaults (loopback only) apply unless an operator
        // explicitly enables TrustAll or lists KnownProxies via configuration.
        builder.Services.AddOptions<ForwardedHeadersOptions>()
            .Configure<ILoggerFactory>((options, loggerFactory) =>
            {
                options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
                var forwardedOptions = authOptions.ForwardedHeaders ?? new ForwardedHeadersConfig();
                var isDev = builder.Environment.IsDevelopment()
                    || builder.Environment.IsEnvironment("Testing");

                if (isDev || forwardedOptions.TrustAll)
                {
                    options.KnownIPNetworks.Clear();
                    options.KnownProxies.Clear();
                    return;
                }

                if (string.IsNullOrWhiteSpace(forwardedOptions.KnownProxies))
                {
                    return;
                }

                var logger = loggerFactory.CreateLogger(typeof(AuthExtensions));
                foreach (var entry in forwardedOptions.KnownProxies.Split(
                    ',',
                    StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                {
                    if (IPAddress.TryParse(entry, out var address))
                    {
                        options.KnownProxies.Add(address);
                    }
                    else
                    {
                        logger.LogWarning(
                            "Skipping invalid Auth:ForwardedHeaders:KnownProxies entry '{Entry}'.",
                            entry);
                    }
                }
            });

        return builder;
    }

    public static WebApplication UseIntervalsAuth(this WebApplication app)
    {
        app.UseForwardedHeaders();
        app.UseMiddleware<CorrelationIdMiddleware>();
        app.UseRateLimiter();
        app.UseAuthentication();
        app.UseAuthorization();
        return app;
    }

    public static IEndpointConventionBuilder RateLimit(this IEndpointConventionBuilder builder) =>
        builder.WithMetadata(new EnableRateLimitingAttribute(RateLimitPolicy));

    public static string SecurityStampCacheKey(Guid userId) => $"intervals:security_stamp:{userId}";

    internal sealed record SecurityStampCacheEntry(bool Exists, string? Stamp, DateTimeOffset? DisabledUtc)
    {
        public static SecurityStampCacheEntry Missing { get; } = new(false, null, null);
    }

    private static bool IsApiRequest(HttpRequest request) => request.Path.StartsWithSegments("/api");

    private static RateLimitPartition<string> GetRateLimitPartition(
        HttpContext context, int permitLimit, TimeSpan window)
    {
        // Authenticated callers are bucketed per user id so one user cannot burn a
        // shared global budget. Anonymous callers are bucketed per client IP (the
        // resolved RemoteIpAddress), with a stable fallback for tests/local dev.
        var userId = context.User.FindFirst(CurrentUser.UserIdClaimType)?.Value;
        var partitionKey = !string.IsNullOrEmpty(userId)
            ? userId
            : context.Connection.RemoteIpAddress?.ToString() ?? "anonymous";

        return RateLimitPartition.GetFixedWindowLimiter(partitionKey, _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = permitLimit,
            Window = window,
            QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
            QueueLimit = 0,
        });
    }

    private static Task WriteApiError(HttpContext context, int statusCode, string code, string message)
    {
        context.Response.StatusCode = statusCode;
        context.Response.ContentType = "application/json";
        return context.Response.WriteAsJsonAsync(new ApiError(code, message, context.GetCorrelationId()));
    }

    private static async Task PopulateXClaimsAsync(OAuthCreatingTicketContext context)
    {
        if (string.IsNullOrWhiteSpace(context.AccessToken))
        {
            throw new InvalidOperationException("X OAuth did not return an access token.");
        }

        using var request = new HttpRequestMessage(HttpMethod.Get, context.Options.UserInformationEndpoint);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", context.AccessToken);

        using var response = await context.Backchannel.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            context.HttpContext.RequestAborted);
        response.EnsureSuccessStatusCode();

        await using var body = await response.Content.ReadAsStreamAsync(context.HttpContext.RequestAborted);
        using var user = await JsonDocument.ParseAsync(
            body,
            cancellationToken: context.HttpContext.RequestAborted);
        context.RunClaimActions(user.RootElement);
    }

    private static bool IsCancellation(Exception? failure) =>
        failure is not null
        && (failure.Message.Contains("access_denied", StringComparison.OrdinalIgnoreCase)
            || failure.Message.Contains("cancel", StringComparison.OrdinalIgnoreCase));
}
