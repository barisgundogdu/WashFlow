using WashFlow.Domain.Identity;

namespace WashFlow.Application.Authentication;

public sealed class AuthService(IAuthStore store, IPasswordService passwords,
    IRefreshTokenGenerator refreshTokens, IAccessTokenIssuer accessTokens, TimeProvider clock, EmailVerificationService verification)
{
    public async Task<AuthOutcome<RegistrationAcknowledgement>> RegisterAsync(RegistrationRequest request, bool business, CancellationToken ct)
    {
        var errors = AuthValidation.Registration(request, business);
        if (errors.Count > 0) return new(null, errors);
        // Hash even for duplicates; never attach a business to an existing unauthenticated account.
        var user = new User((business ? request.OwnerFullName : request.FullName)!, request.Email!, passwords.Hash(request.Password!));
        var issue = verification.CreateIssue(user);
        if (await store.RegisterAsync(user, business ? request.BusinessName!.Trim() : null, issue.Record, ct))
            verification.Queue(issue.Email);
        return new(new("Kayıt talebiniz alındı. Erişim için e-posta doğrulaması gereklidir."));
    }

    public async Task<AuthOutcome<TokenResponse>> LoginAsync(LoginRequest request, CancellationToken ct)
    {
        var errors = AuthValidation.Credentials(request.Email, request.Password);
        if (request.DeviceName is { Length: > 200 }) errors["deviceName"] = ["Cihaz adı 200 karakteri aşamaz."];
        if (errors.Count > 0) return new(null, errors);
        var user = await store.FindUserAsync(User.NormalizeEmail(request.Email!), ct);
        var valid = passwords.Verify(user?.PasswordHash, request.Password!, out var rehash);
        if (!valid || user?.EmailVerifiedAt is null) return new(null);
        var raw = refreshTokens.Generate();
        var session = await store.StartSessionAsync(user, rehash ? passwords.Hash(request.Password!) : null,
            string.IsNullOrWhiteSpace(request.DeviceName) ? "Mobil cihaz" : request.DeviceName.Trim(),
            refreshTokens.Hash(raw), clock.GetUtcNow(), ct);
        return new(session is null ? null : Response(session, raw));
    }

    public async Task<AuthOutcome<TokenResponse>> RefreshAsync(RefreshRequest request, CancellationToken ct)
    {
        // Canonical 32-byte base64url credentials only; failures never disclose token state.
        if (request.RefreshToken is not { Length: 43 } token || token.Any(c => !(char.IsAsciiLetterOrDigit(c) || c is '-' or '_')))
            return new(null);
        var raw = refreshTokens.Generate();
        var session = await store.RotateAsync(refreshTokens.Hash(token), refreshTokens.Hash(raw), clock.GetUtcNow(), ct);
        return new(session is null ? null : Response(session, raw));
    }

    public Task LogoutAsync(Guid userId, Guid sessionId, CancellationToken ct) =>
        store.LogoutAsync(userId, sessionId, clock.GetUtcNow(), ct);

    private TokenResponse Response(DeviceSession session, string refreshToken)
    {
        var access = accessTokens.Issue(session);
        return new() { AccessToken = access.Token, RefreshToken = refreshToken,
            AccessTokenExpiresAt = access.ExpiresAt, SessionExpiresAt = session.ExpiresAt };
    }
}
