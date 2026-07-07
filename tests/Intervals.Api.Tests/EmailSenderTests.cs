using Intervals.Api.Email;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace Intervals.Api.Tests;

public sealed class EmailSenderTests
{
    [Fact]
    public async Task FakeEmailSender_CapturesSentEmails()
    {
        var sender = new FakeEmailSender();

        await sender.SendEmailAsync("alice@example.com", "Welcome", "<p>hi</p>", "hi");
        await sender.SendEmailAsync("bob@example.com", "Verify", "<p>hi bob</p>", "hi bob");

        var sent = sender.SentEmails;
        Assert.Equal(2, sent.Count);
        Assert.Equal("alice@example.com", sent[0].RecipientEmail);
        Assert.Equal("Welcome", sent[0].Subject);
        Assert.Equal("bob@example.com", sent[1].RecipientEmail);
        Assert.Equal("Verify", sent[1].Subject);
        Assert.Equal("<p>hi bob</p>", sent[1].HtmlBody);
        Assert.Equal("hi bob", sent[1].TextBody);
    }

    [Fact]
    public async Task FakeEmailSender_Reset_ClearsCapturedEmails()
    {
        var sender = new FakeEmailSender();
        await sender.SendEmailAsync("a@example.com", "s", "h", "t");

        sender.Reset();

        Assert.Empty(sender.SentEmails);
    }

    [Fact]
    public void EmailTemplates_EmailVerification_BuildsLink()
    {
        var link = "https://app.intervals.local/verify?token=abc123";
        var (subject, html, text) = EmailTemplates.EmailVerification("Ada", link);

        Assert.False(string.IsNullOrWhiteSpace(subject));
        Assert.Contains(link, html);
        Assert.Contains(link, text);
        Assert.Contains("Ada", text);
    }

    [Fact]
    public void EmailTemplates_PasswordReset_BuildsLink()
    {
        var link = "https://app.intervals.local/reset?token=xyz789";
        var (subject, html, text) = EmailTemplates.PasswordReset("Grace", link);

        Assert.False(string.IsNullOrWhiteSpace(subject));
        Assert.Contains(link, html);
        Assert.Contains(link, text);
        Assert.Contains("Grace", text);
    }

    [Fact]
    public void EmailTemplates_HtmlEscapesDisplayName()
    {
        var (subject, html, text) =
            EmailTemplates.EmailVerification("<b>Bold</b>", "https://example/verify");

        Assert.DoesNotContain("<b>Bold</b>", html);
        Assert.Contains("&lt;b&gt;Bold&lt;/b&gt;", html);
        Assert.Contains("<b>Bold</b>", text);
    }

    [Fact]
    public void EmailOptions_BindsFromConfiguration()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Mail:FromAddress"] = "alerts@intervals.local",
                ["Mail:FromName"] = "Intervals Alerts",
                ["Mail:AppBaseUrl"] = "https://app.intervals.local",
                ["Mail:VerificationTokenLifetimeHours"] = "48",
                ["Mail:PasswordResetTokenLifetimeHours"] = "2",
                ["Mail:Smtp:Host"] = "mailhog",
                ["Mail:Smtp:Port"] = "2525",
            })
            .Build();

        var services = new ServiceCollection();
        services.AddIntervalsEmail(config);
        using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptions<EmailOptions>>().Value;

        Assert.Equal("alerts@intervals.local", options.FromAddress);
        Assert.Equal("Intervals Alerts", options.FromName);
        Assert.Equal("https://app.intervals.local", options.AppBaseUrl);
        Assert.Equal(48, options.VerificationTokenLifetimeHours);
        Assert.Equal(2, options.PasswordResetTokenLifetimeHours);
        Assert.Equal("mailhog", options.Smtp.Host);
        Assert.Equal(2525, options.Smtp.Port);
    }

    [Fact]
    public void EmailServiceExtensions_RegistersSmtpEmailSender()
    {
        var config = new ConfigurationBuilder().Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddIntervalsEmail(config);
        using var provider = services.BuildServiceProvider();

        var sender = provider.GetRequiredService<IEmailSender>();
        Assert.IsType<SmtpEmailSender>(sender);
    }
}
