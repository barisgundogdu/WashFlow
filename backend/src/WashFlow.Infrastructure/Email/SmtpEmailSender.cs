using System.Net;
using System.Net.Mail;
using WashFlow.Application.Authentication;

namespace WashFlow.Infrastructure.Email;

public sealed class SmtpSettings
{
    public required string Host { get; init; }
    public required string From { get; init; }
    public int Port { get; init; } = 587;
    public string? Username { get; init; }
    public string? Password { get; init; }
}
public sealed class SmtpEmailSender(SmtpSettings settings) : IEmailSender
{
    public async Task SendAsync(VerificationEmail email, CancellationToken ct)
    {
        using var client = new SmtpClient(settings.Host, settings.Port)
        {
            EnableSsl = true, UseDefaultCredentials = false,
            Credentials = settings.Username is null ? null : new NetworkCredential(settings.Username, settings.Password)
        };
        using var message = new MailMessage(settings.From, email.Recipient)
        {
            Subject = "WashFlow e-posta doğrulaması",
            Body = $"E-posta adresinizi doğrulamak için bu kodu kullanın:\n{email.Token}\n" +
                $"Son geçerlilik zamanı (UTC): {email.ExpiresAt:O}\nBu kaydı siz oluşturmadıysanız e-postayı yok sayabilirsiniz.",
            IsBodyHtml = false
        };
        await client.SendMailAsync(message, ct);
    }
}
