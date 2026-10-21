using WashFlow.Domain.Common;

namespace WashFlow.Domain.Identity;

public sealed class User : IEntity
{
    private User() { }

    /// <param name="passwordHash">An encoded adaptive password hash, produced by the password service.</param>
    public User(string fullName, string email, string passwordHash)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fullName);
        ArgumentException.ThrowIfNullOrWhiteSpace(email);
        ArgumentException.ThrowIfNullOrWhiteSpace(passwordHash);
        FullName = fullName.Trim();
        Email = email.Trim();
        NormalizedEmail = NormalizeEmail(email);
        PasswordHash = passwordHash;
    }

    // Re-encoding the same verified password does not change the credential security stamp.
    public void UpgradePasswordHash(string passwordHash)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(passwordHash);
        PasswordHash = passwordHash;
    }

    public static string NormalizeEmail(string email)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(email);
        return email.Trim().ToUpperInvariant();
    }

    public Guid Id { get; private set; } = Guid.CreateVersion7();
    public string FullName { get; private set; } = null!;
    public string Email { get; private set; } = null!;
    public string NormalizedEmail { get; private set; } = null!;
    public string PasswordHash { get; private set; } = null!;
    // Future credential/email changes must rotate this stamp and invalidate outstanding credentials.
    public Guid SecurityStamp { get; private set; } = Guid.NewGuid();
    public DateTimeOffset? EmailVerifiedAt { get; set; }
    public DateTimeOffset CreatedAt { get; private set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}
