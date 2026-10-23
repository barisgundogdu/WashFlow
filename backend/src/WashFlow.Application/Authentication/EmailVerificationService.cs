using WashFlow.Domain.Identity;

namespace WashFlow.Application.Authentication;

public sealed class EmailVerificationService(IEmailVerificationStore store, IVerificationTokenGenerator tokens,
    IVerificationEmailQueue queue, VerificationOptions options, TimeProvider clock)
{
    public VerificationIssue CreateIssue(User user)
    {
        var now = clock.GetUtcNow();
        var raw = tokens.Generate();
        return new()
        {
            Record = new() { UserId = user.Id, NormalizedEmail = user.NormalizedEmail, SecurityStamp = user.SecurityStamp,
                TokenHash = tokens.Hash(raw), CreatedAt = now, ExpiresAt = now + options.Lifetime },
            Email = new() { Recipient = user.Email, Token = raw, ExpiresAt = now + options.Lifetime }
        };
    }

    public void Queue(VerificationEmail email) => queue.TryEnqueue(email);

    public async Task<bool> VerifyAsync(VerifyEmailRequest request, CancellationToken ct)
    {
        if (request.Token is not { Length: 43 } raw || raw.Any(c => !(char.IsAsciiLetterOrDigit(c) || c is '-' or '_')))
            return false;
        return await store.VerifyAsync(tokens.Hash(raw), clock.GetUtcNow(), ct);
    }

    public async Task<AuthOutcome<VerificationAcknowledgement>> ResendAsync(ResendVerificationRequest request, CancellationToken ct)
    {
        var errors = AuthValidation.Email(request.Email);
        if (errors.Count > 0) return new(null, errors);
        var now = clock.GetUtcNow();
        var raw = tokens.Generate();
        var recipient = await store.ReplaceAsync(User.NormalizeEmail(request.Email!), tokens.Hash(raw), now, options, ct);
        if (recipient is not null)
            queue.TryEnqueue(new() { Recipient = recipient.User.Email, Token = raw, ExpiresAt = recipient.ExpiresAt });
        return new(new("Adres uygunsa doğrulama e-postası gönderilecektir."));
    }
}
