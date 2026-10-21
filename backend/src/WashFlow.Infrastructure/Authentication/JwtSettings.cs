using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;

namespace WashFlow.Infrastructure.Authentication;

public sealed class JwtSettings
{
    public required string Issuer { get; init; }
    public required string Audience { get; init; }
    public required byte[] SigningKey { get; init; }
    public static JwtSettings Read(IConfiguration configuration)
    {
        byte[] key;
        try { key = Convert.FromBase64String(configuration["Jwt:SigningKey"] ?? ""); }
        catch (FormatException) { throw new InvalidOperationException("Jwt__SigningKey geçerli Base64 biçiminde olmalıdır."); }
        if (key.Length < 32 || string.IsNullOrWhiteSpace(configuration["Jwt:Issuer"]) || string.IsNullOrWhiteSpace(configuration["Jwt:Audience"]))
            throw new InvalidOperationException("Jwt__Issuer, Jwt__Audience ve en az 32 baytlık Base64 Jwt__SigningKey gereklidir.");
        return new() { Issuer = configuration["Jwt:Issuer"]!, Audience = configuration["Jwt:Audience"]!, SigningKey = key };
    }
    public TokenValidationParameters ValidationParameters() => new()
    {
        ValidateIssuer = true, ValidIssuer = Issuer,
        ValidateAudience = true, ValidAudience = Audience,
        ValidateIssuerSigningKey = true, IssuerSigningKey = new SymmetricSecurityKey(SigningKey),
        RequireSignedTokens = true, RequireExpirationTime = true, ValidateLifetime = true,
        ValidAlgorithms = [SecurityAlgorithms.HmacSha256], ClockSkew = TimeSpan.Zero
    };
}
