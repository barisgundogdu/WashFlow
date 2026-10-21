namespace WashFlow.Domain.Identity;

/// <summary>A SHA-256 digest of a cryptographically random token, never the token itself.</summary>
public sealed class TokenHash : IEquatable<TokenHash>
{
    public string Value { get; }

    public TokenHash(string value)
    {
        if (value is null || value.Length != 64 || value.Any(c => !char.IsAsciiHexDigit(c)))
            throw new ArgumentException("A 64-character SHA-256 hexadecimal digest is required.", nameof(value));
        Value = value.ToLowerInvariant();
    }

    public bool Equals(TokenHash? other) => other is not null && Value == other.Value;
    public override bool Equals(object? obj) => obj is TokenHash other && Equals(other);
    public override int GetHashCode() => Value.GetHashCode(StringComparison.Ordinal);
    public override string ToString() => "[REDACTED]";
}
