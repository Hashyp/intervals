using System;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
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

                    var stampClaim = principal.FindFirst(AuthEndpoints.SecurityStampClaimType)?.Value;
                    if (stampClaim is null)
                    {
                        // Principals without a security-stamp claim (e.g. the test-login endpoint)
                        // are not subject to stamp-based invalidation.
                        return;
                    }

                    var cache = context.HttpContext.RequestServices.GetRequiredService<IMemoryCache>();
                    var cacheKey = $"intervals:security_stamp:{userId}";

                    try
                    {
                        if (!cache.TryGetValue(cacheKey, out (bool Exists, string? Stamp) cached))
                        {
                            var scopeFactory = context.HttpContext.RequestServices.GetRequiredService<IServiceScopeFactory>();
                            using var scope = scopeFactory.CreateScope();
                            var db = scope.ServiceProvider.GetRequiredService<IntervalsDbContext>();
                            var record = await db.AppUsers
                                .AsNoTracking()
                                .Where(u => u.Id == userId)
                                .Select(u => new { u.SecurityStamp })
                                .FirstOrDefaultAsync(context.HttpContext.RequestAborted);
                            cached = record is null ? (false, (string?)null) : (true, record.SecurityStamp);
                            cache.Set(cacheKey, cached, TimeSpan.FromMinutes(1));
                        }

                        if (!cached.Exists)
                        {
                            context.RejectPrincipal();
                            return;
                        }

                        if (string.IsNullOrEmpty(cached.Stamp) || string.IsNullOrEmpty(stampClaim))
                        {
                            return;
                        }

                        if (!string.Equals(cached.Stamp, stampClaim, StringComparison.Ordinal))
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
            options.AddFixedWindowLimiter(RateLimitPolicy, window =>
            {
                window.PermitLimit = 60;
                window.Window = TimeSpan.FromMinutes(1);
                window.QueueProcessingOrder = System.Threading.RateLimiting.QueueProcessingOrder.OldestFirst;
                window.QueueLimit = 0;
            });
            options.AddFixedWindowLimiter("verification", window =>
            {
                window.PermitLimit = 5;
                window.Window = TimeSpan.FromMinutes(1);
                window.QueueProcessingOrder = System.Threading.RateLimiting.QueueProcessingOrder.OldestFirst;
                window.QueueLimit = 0;
            });
            options.AddFixedWindowLimiter("password-reset", window =>
            {
                window.PermitLimit = 5;
                window.Window = TimeSpan.FromMinutes(1);
                window.QueueProcessingOrder = System.Threading.RateLimiting.QueueProcessingOrder.OldestFirst;
                window.QueueLimit = 0;
            });
        });

        builder.Services.Configure<ForwardedHeadersOptions>(options =>
        {
            options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
            options.KnownIPNetworks.Clear();
            options.KnownProxies.Clear();
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

    private static bool IsApiRequest(HttpRequest request) => request.Path.StartsWithSegments("/api");

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
