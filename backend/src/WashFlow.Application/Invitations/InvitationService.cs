using WashFlow.Application.Authentication;
using WashFlow.Domain.Identity;

namespace WashFlow.Application.Invitations;

public sealed class InvitationRequest { public string? Email { get; init; } }
public sealed class InvitationTokenRequest { public string? Token { get; init; } }
public sealed class InvitationRegistrationRequest
{
    public string? Token { get; init; }
    public string? Email { get; init; }
    public string? FullName { get; init; }
    public string? Password { get; init; }
}
public sealed record InvitationView(Guid Id, string Email, DateTimeOffset ExpiresAt, DateTimeOffset? AcceptedAt, DateTimeOffset? RevokedAt);
public enum InvitationStatus { Success, Forbidden, Invalid, Conflict }
public sealed record InvitationResult(InvitationStatus Status, InvitationView? Value = null);
public sealed class InvitationOptions { public TimeSpan Lifetime { get; init; } = TimeSpan.FromHours(24); }
public interface IInvitationTokens { string Generate(); TokenHash Hash(string raw); }
public interface IInvitationStore
{
    Task<InvitationResult> IssueAsync(Guid actor, Guid business, string? email, Guid? resendId, TokenHash hash, TimeSpan lifetime, CancellationToken ct);
    Task<IReadOnlyList<InvitationView>?> ListAsync(Guid actor, Guid business, CancellationToken ct);
    Task<InvitationStatus> RevokeAsync(Guid actor, Guid business, Guid invitation, CancellationToken ct);
    Task<InvitationStatus> AcceptAsync(Guid actor, TokenHash hash, CancellationToken ct);
    Task<bool> RegisterAsync(User user, EmailVerificationToken verification, TokenHash invitation, CancellationToken ct);
}
public sealed class InvitationService(IInvitationStore store, IInvitationTokens tokens, InvitationOptions options,
    IVerificationEmailQueue queue, IPasswordService passwords, EmailVerificationService verification)
{
    public static bool ValidToken(string? raw) => raw is { Length: 43 } && raw.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_');
    public async Task<InvitationResult> IssueAsync(Guid actor, Guid business, string? email, Guid? resend, CancellationToken ct)
    {
        var raw = tokens.Generate();
        var result = await store.IssueAsync(actor, business, email is null ? null : User.NormalizeEmail(email), resend, tokens.Hash(raw), options.Lifetime, ct);
        if (result.Status == InvitationStatus.Success && result.Value is { } value)
            queue.TryEnqueue(new() { Recipient = value.Email, Token = raw, ExpiresAt = value.ExpiresAt, Purpose = "employee-invitation" });
        return result;
    }
    public Task<IReadOnlyList<InvitationView>?> ListAsync(Guid actor, Guid business, CancellationToken ct) => store.ListAsync(actor, business, ct);
    public Task<InvitationStatus> RevokeAsync(Guid actor, Guid business, Guid invitation, CancellationToken ct) => store.RevokeAsync(actor, business, invitation, ct);
    public Task<InvitationStatus> AcceptAsync(Guid actor, string? raw, CancellationToken ct) => ValidToken(raw)
        ? store.AcceptAsync(actor, tokens.Hash(raw!), ct) : Task.FromResult(InvitationStatus.Invalid);
    public async Task<AuthOutcome<RegistrationAcknowledgement>> RegisterAsync(InvitationRegistrationRequest request, CancellationToken ct)
    {
        var errors = AuthValidation.Registration(new() { FullName = request.FullName, Email = request.Email, Password = request.Password }, false);
        if (errors.Count > 0) return new(null, errors);
        if (ValidToken(request.Token))
        {
            var user = new User(request.FullName!, request.Email!, passwords.Hash(request.Password!));
            var issue = verification.CreateIssue(user);
            if (await store.RegisterAsync(user, issue.Record, tokens.Hash(request.Token!), ct)) verification.Queue(issue.Email);
        }
        return new(new("Kayıt talebiniz alındı. E-postanızı doğrulayıp giriş yaptıktan sonra daveti kabul edin."));
    }
}
