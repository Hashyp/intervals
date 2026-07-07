namespace Intervals.Api.Email;

public sealed class EmailOptions
{
    public const string SectionName = "Mail";

    public string FromAddress { get; set; } = "no-reply@intervals.local";
    public string FromName { get; set; } = "Intervals";
    public string AppBaseUrl { get; set; } = "http://localhost:5173";
    public SmtpOptions Smtp { get; set; } = new();
    public int VerificationTokenLifetimeHours { get; set; } = 24;
    public int PasswordResetTokenLifetimeHours { get; set; } = 1;
}

public sealed class SmtpOptions
{
    public string Host { get; set; } = "localhost";
    public int Port { get; set; } = 1025;
}
