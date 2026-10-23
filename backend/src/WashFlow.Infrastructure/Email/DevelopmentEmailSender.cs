using WashFlow.Application.Authentication;

namespace WashFlow.Infrastructure.Email;

/// <summary>Process-memory mailbox for local development/tests. No files or logs. Development-only loopback API supports one-time reads.</summary>
public sealed class DevelopmentEmailSender : IEmailSender
{
    private readonly Dictionary<string, VerificationEmail> messages = new(StringComparer.OrdinalIgnoreCase);
    private readonly object gate = new();
    public Task SendAsync(VerificationEmail email, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        lock (gate)
        {
            if (messages.Count >= 100) messages.Remove(messages.Keys.First());
            messages[email.Recipient] = email;
        }
        return Task.CompletedTask;
    }
    public bool TryTake(string recipient, out VerificationEmail? email)
    {
        lock (gate) return messages.Remove(recipient, out email);
    }
}
