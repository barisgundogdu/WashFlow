using WashFlow.Domain.Identity;

namespace WashFlow.Application.Authentication;

public sealed class VerifyEmailRequest { public string? Token { get; init; } }
public sealed class ResendVerificationRequest { public string? Email { get; init; } }
public sealed record VerificationAcknowledgement(string Message);

// Never use records/credential-bearing ToString implementations for delivery objects.
public sealed class VerificationEmail
{
    public required string Recipient { get; init; }
    public required string Token { get; init; }
    public required DateTimeOffset ExpiresAt { get; init; }
}
public sealed class VerificationIssue
{
    public required EmailVerificationToken Record { get; init; }
    public required VerificationEmail Email { get; init; }
}
public sealed class VerificationOptions
{
    public TimeSpan Lifetime { get; init; } = TimeSpan.FromMinutes(60);
    public TimeSpan ResendCooldown { get; init; } = TimeSpan.FromSeconds(60);
}
public interface IVerificationTokenGenerator
{
    string Generate();
    TokenHash Hash(string token);
}
public interface IEmailSender { Task SendAsync(VerificationEmail email, CancellationToken ct); }
public interface IVerificationEmailQueue { bool TryEnqueue(VerificationEmail email); }
public sealed record VerificationRecipient(User User, DateTimeOffset ExpiresAt);
public interface IEmailVerificationStore
{
    // Returns the recipient only if an eligible replacement was saved under the user lock.
    Task<VerificationRecipient?> ReplaceAsync(string normalizedEmail, TokenHash hash, DateTimeOffset now, VerificationOptions options, CancellationToken ct);
    Task<bool> VerifyAsync(TokenHash hash, DateTimeOffset now, CancellationToken ct);
}
