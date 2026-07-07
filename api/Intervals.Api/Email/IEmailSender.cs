namespace Intervals.Api.Email;

public interface IEmailSender
{
    Task SendEmailAsync(
        string recipientEmail,
        string subject,
        string htmlBody,
        string textBody,
        CancellationToken cancellationToken = default);
}
