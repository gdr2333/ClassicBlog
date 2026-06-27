namespace ClassicBlog.Services;

/// <summary>SMTP settings for the MailKit-based email sender.</summary>
public class EmailOptions
{
    /// <summary>If false, messages are not sent (the would-be email is logged instead).</summary>
    public bool Enabled { get; set; }

    public string Host { get; set; } = "smtp.example.com";
    public int Port { get; set; } = 587;

    /// <summary>true = implicit TLS (SslOnConnect, e.g. port 465); false = STARTTLS (e.g. port 587).</summary>
    public bool UseSsl { get; set; } = false;

    public string Username { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public string From { get; set; } = "blog@example.com";
}
