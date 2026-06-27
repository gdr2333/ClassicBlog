using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Options;
using MimeKit;

namespace ClassicBlog.Services;

public interface IEmailService
{
    Task SendAsync(string to, string subject, string body);
}

/// <summary>
/// MailKit-based email sender. Best-effort: failures are logged, never thrown,
/// so a comment submission cannot fail because of an SMTP problem. When
/// <see cref="EmailOptions.Enabled"/> is false, the would-be email is logged
/// instead of sent (useful in development and for verifying notifications fire).
/// </summary>
public class EmailService(IOptions<EmailOptions> options, ILogger<EmailService> logger) : IEmailService
{
    private readonly EmailOptions _opts = options.Value;

    public async Task SendAsync(string to, string subject, string body)
    {
        if (string.IsNullOrWhiteSpace(to))
            return;

        if (!_opts.Enabled)
        {
            logger.LogInformation("[Email disabled] To: {To} | Subject: {Subject} | Body: {Body}", to, subject, body);
            return;
        }

        try
        {
            var message = new MimeMessage();
            message.From.Add(MailboxAddress.Parse(_opts.From));
            message.To.Add(MailboxAddress.Parse(to));
            message.Subject = subject;
            message.Body = new TextPart("plain") { Text = body };

            using var client = new SmtpClient();
            await client.ConnectAsync(_opts.Host, _opts.Port,
                _opts.UseSsl ? SecureSocketOptions.SslOnConnect : SecureSocketOptions.StartTls);

            if (!string.IsNullOrEmpty(_opts.Username))
                await client.AuthenticateAsync(_opts.Username, _opts.Password);

            await client.SendAsync(message);
            await client.DisconnectAsync(true);
            logger.LogInformation("Sent email to {To} with subject '{Subject}'", to, subject);
        }
        catch (Exception ex)
        {
            // Never let email failure break the comment flow.
            logger.LogError(ex, "Failed to send email to {To} with subject '{Subject}'", to, subject);
        }
    }
}
