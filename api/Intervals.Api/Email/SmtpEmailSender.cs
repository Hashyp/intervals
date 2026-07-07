using System.Net.Mail;
using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Intervals.Api.Email;

public sealed class SmtpEmailSender : IEmailSender
{
    private readonly IOptions<EmailOptions> _options;
    private readonly ILogger<SmtpEmailSender> _logger;

    public SmtpEmailSender(IOptions<EmailOptions> options, ILogger<SmtpEmailSender> logger)
    {
        _options = options;
        _logger = logger;
    }

    public async Task SendEmailAsync(
        string recipientEmail,
        string subject,
        string htmlBody,
        string textBody,
        CancellationToken cancellationToken = default)
    {
        var smtp = _options.Value.Smtp;
        using var client = new SmtpClient(smtp.Host, smtp.Port);
        using var message = new MailMessage
        {
            From = new MailAddress(_options.Value.FromAddress, _options.Value.FromName, Encoding.UTF8),
            Subject = subject,
            SubjectEncoding = Encoding.UTF8,
            BodyEncoding = Encoding.UTF8,
        };
        message.To.Add(recipientEmail);
        message.AlternateViews.Add(
            AlternateView.CreateAlternateViewFromString(htmlBody, Encoding.UTF8, "text/html"));
        message.AlternateViews.Add(
            AlternateView.CreateAlternateViewFromString(textBody, Encoding.UTF8, "text/plain"));

        try
        {
            await client.SendMailAsync(message, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send email to {Recipient}", recipientEmail);
            throw;
        }
    }
}
