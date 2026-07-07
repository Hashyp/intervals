namespace Intervals.Api.Email;

public static class EmailTemplates
{
    public static (string Subject, string Html, string Text) EmailVerification(
        string displayName,
        string verificationLink)
    {
        const string subject = "Verify your Intervals account";
        var name = string.IsNullOrWhiteSpace(displayName) ? "there" : displayName;
        var hours = "24";

        var html = $"""
                    <!doctype html>
                    <html lang="en">
                    <head><meta charset="utf-8"><title>{subject}</title></head>
                    <body>
                      <p>Hi {WebEncode(name)},</p>
                      <p>Please verify your Intervals account by clicking the button below.</p>
                      <p>This link expires in {WebEncode(hours)} hours.</p>
                      <p>
                        <a href="{WebEncode(verificationLink)}"
                           style="display:inline-block;padding:10px 18px;background:#2563eb;color:#ffffff;text-decoration:none;border-radius:6px;">
                          Verify email
                        </a>
                      </p>
                      <p>If the button does not work, copy and paste this link into your browser:</p>
                      <p>{WebEncode(verificationLink)}</p>
                      <p>If you did not create an account, you can safely ignore this email.</p>
                      <p>&mdash; The Intervals team</p>
                    </body>
                    </html>
                    """;

        var text = $"""
                    Hi {name},

                    Verify your Intervals account by opening this link in your browser:
                    {verificationLink}

                    This link expires in {hours} hours.

                    If you did not create an account, you can safely ignore this email.

                    - The Intervals team
                    """;

        return (subject, html, text);
    }

    public static (string Subject, string Html, string Text) PasswordReset(
        string displayName,
        string resetLink)
    {
        const string subject = "Reset your Intervals password";
        var name = string.IsNullOrWhiteSpace(displayName) ? "there" : displayName;
        var hours = "1";

        var html = $"""
                    <!doctype html>
                    <html lang="en">
                    <head><meta charset="utf-8"><title>{subject}</title></head>
                    <body>
                      <p>Hi {WebEncode(name)},</p>
                      <p>We received a request to reset the password for your Intervals account.</p>
                      <p>This link expires in {WebEncode(hours)} hour.</p>
                      <p>
                        <a href="{WebEncode(resetLink)}"
                           style="display:inline-block;padding:10px 18px;background:#2563eb;color:#ffffff;text-decoration:none;border-radius:6px;">
                          Reset password
                        </a>
                      </p>
                      <p>If the button does not work, copy and paste this link into your browser:</p>
                      <p>{WebEncode(resetLink)}</p>
                      <p>If you did not request a password reset, you can safely ignore this email.</p>
                      <p>&mdash; The Intervals team</p>
                    </body>
                    </html>
                    """;

        var text = $"""
                    Hi {name},

                    Reset your Intervals password by opening this link in your browser:
                    {resetLink}

                    This link expires in {hours} hour.

                    If you did not request a password reset, you can safely ignore this email.

                    - The Intervals team
                    """;

        return (subject, html, text);
    }

    private static string WebEncode(string value) =>
        System.Net.WebUtility.HtmlEncode(value ?? string.Empty);
}
