namespace Intervals.Api.Email;

public sealed class FakeEmailSender : IEmailSender
{
    private readonly List<SentEmail> _sent = new();

    public IReadOnlyList<SentEmail> SentEmails => _sent;

    public Task SendEmailAsync(
        string recipientEmail,
        string subject,
        string htmlBody,
        string textBody,
        CancellationToken cancellationToken = default)
    {
        _sent.Add(new SentEmail(recipientEmail, subject, htmlBody, textBody));
        return Task.CompletedTask;
    }

    public void Reset() => _sent.Clear();
}

public sealed record SentEmail(
    string RecipientEmail,
    string Subject,
    string HtmlBody,
    string TextBody);
