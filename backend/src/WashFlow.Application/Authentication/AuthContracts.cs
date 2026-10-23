using WashFlow.Domain.Identity;

namespace WashFlow.Application.Authentication;

// Classes deliberately avoid record-generated ToString methods containing credentials.
public sealed class RegistrationRequest
{
    public string? FullName { get; init; }
    public string? OwnerFullName { get; init; }
    public string? BusinessName { get; init; }
    public string? Email { get; init; }
    public string? Password { get; init; }
}
public sealed class LoginRequest
{
    public string? Email { get; init; }
    public string? Password { get; init; }
    public string? DeviceName { get; init; }
}
public sealed class RefreshRequest { public string? RefreshToken { get; init; } }
public sealed class TokenResponse
{
    public required string AccessToken { get; init; }
    public required string RefreshToken { get; init; }
    public required DateTimeOffset AccessTokenExpiresAt { get; init; }
    public required DateTimeOffset SessionExpiresAt { get; init; }
    public string TokenType => "Bearer";
}
public sealed record AuthOutcome<T>(T? Value, Dictionary<string, string[]>? Errors = null)
{
    public bool Succeeded => Value is not null;
}
public sealed record RegistrationAcknowledgement(string Message);
public interface IPasswordService
{
    string Hash(string password);
    bool Verify(string? storedHash, string password, out bool needsRehash);
}
public interface IAccessTokenIssuer
{
    (string Token, DateTimeOffset ExpiresAt) Issue(DeviceSession session);
}
public interface IRefreshTokenGenerator
{
    string Generate();
    TokenHash Hash(string token);
}
public interface IAuthStore
{
    Task<bool> RegisterAsync(User user, string? businessName, EmailVerificationToken verification, CancellationToken ct);
    Task<User?> FindUserAsync(string normalizedEmail, CancellationToken ct);
    Task<DeviceSession?> StartSessionAsync(User user, string? upgradedHash, string deviceName, TokenHash hash, DateTimeOffset now, CancellationToken ct);
    Task<DeviceSession?> RotateAsync(TokenHash oldHash, TokenHash newHash, DateTimeOffset now, CancellationToken ct);
    Task LogoutAsync(Guid userId, Guid sessionId, DateTimeOffset now, CancellationToken ct);
    Task<bool> IsSessionActiveAsync(Guid userId, Guid sessionId, DateTimeOffset now, CancellationToken ct);
}
