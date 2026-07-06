using System.Net;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Intervals.Api.Auth;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.OAuth;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace Intervals.Api.Tests;

[Collection(nameof(AuthCollection))]
public sealed class XOAuthOptionsTests
{
    private readonly AuthWebFactory _factory;

    public XOAuthOptionsTests(AuthWebFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task X_creating_ticket_fetches_user_info_and_maps_claims()
    {
        var options = _factory.Services
            .GetRequiredService<IOptionsMonitor<OAuthOptions>>()
            .Get(AuthProviderNames.XScheme);
        var handler = new RecordingUserInfoHandler(
            """
            {
              "data": {
                "id": "x-user-123",
                "name": "X User",
                "profile_image_url": "https://img.example/x.png",
                "email": "x@example.com"
              }
            }
            """);
        using var backchannel = new HttpClient(handler);
        using var tokens = OAuthTokenResponse.Success(JsonDocument.Parse(
            """{"access_token":"x-access-token","token_type":"bearer"}"""));
        using var emptyUser = JsonDocument.Parse("{}");
        var principal = new ClaimsPrincipal(new ClaimsIdentity(AuthProviderNames.XScheme));
        var context = new OAuthCreatingTicketContext(
            principal,
            new AuthenticationProperties(),
            new DefaultHttpContext(),
            new AuthenticationScheme(AuthProviderNames.XScheme, "X", typeof(OAuthHandler<OAuthOptions>)),
            options,
            backchannel,
            tokens,
            emptyUser.RootElement);

        await options.Events.CreatingTicket(context);

        Assert.Equal(options.UserInformationEndpoint, handler.Request?.RequestUri?.ToString());
        Assert.Equal("Bearer", handler.Request?.Headers.Authorization?.Scheme);
        Assert.Equal("x-access-token", handler.Request?.Headers.Authorization?.Parameter);
        Assert.Equal("x-user-123", principal.FindFirst(ClaimTypes.NameIdentifier)?.Value);
        Assert.Equal("X User", principal.FindFirst(ClaimTypes.Name)?.Value);
        Assert.Equal("https://img.example/x.png", principal.FindFirst("picture")?.Value);
        Assert.Equal("x@example.com", principal.FindFirst(ClaimTypes.Email)?.Value);
    }

    private sealed class RecordingUserInfoHandler : HttpMessageHandler
    {
        private readonly string _body;

        public RecordingUserInfoHandler(string body)
        {
            _body = body;
        }

        public HttpRequestMessage? Request { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Request = request;
            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(_body, Encoding.UTF8, "application/json"),
            };
            return Task.FromResult(response);
        }
    }
}
