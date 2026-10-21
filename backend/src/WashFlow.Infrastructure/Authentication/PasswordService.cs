using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using WashFlow.Application.Authentication;

namespace WashFlow.Infrastructure.Authentication;

public sealed class PasswordService : IPasswordService
{
    private readonly PasswordHasher<object> hasher = new(Options.Create(new PasswordHasherOptions { IterationCount = 210_000 }));
    private readonly object subject = new();
    private readonly string dummyHash;
    public PasswordService() => dummyHash = hasher.HashPassword(subject, Guid.NewGuid().ToString());
    public string Hash(string password) => hasher.HashPassword(subject, password);
    public bool Verify(string? storedHash, string password, out bool needsRehash)
    {
        PasswordVerificationResult result;
        try { result = hasher.VerifyHashedPassword(subject, storedHash ?? dummyHash, password); }
        catch (FormatException) { result = PasswordVerificationResult.Failed; }
        needsRehash = result == PasswordVerificationResult.SuccessRehashNeeded;
        return storedHash is not null && result != PasswordVerificationResult.Failed;
    }
}
