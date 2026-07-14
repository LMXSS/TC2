namespace Notifications.Api.Infrastructure.Email;

public class SmtpOptions
{
    public const string SectionName = "Smtp";

    public bool Enabled { get; set; } = true;
    public string Host { get; set; } = "localhost";
    public int Port { get; set; } = 1025;
    public string FromAddress { get; set; } = "noreply@fcg.local";
    public string FromName { get; set; } = "FIAP Cloud Games";
}
