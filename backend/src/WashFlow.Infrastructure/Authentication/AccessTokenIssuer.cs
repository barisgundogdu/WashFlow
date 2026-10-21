using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.IdentityModel.Tokens;
using WashFlow.Application.Authentication;
using WashFlow.Domain.Identity;

namespace WashFlow.Infrastructure.Authentication;

public sealed class AccessTokenIssuer(JwtSettings settings, TimeProvider clock) : IAccessTokenIssuer
{
    public (string Token, DateTimeOffset ExpiresAt) Issue(DeviceSession session)
    {
        var now = DateTimeOffset.FromUnixTimeSeconds(clock.GetUtcNow().ToUnixTimeSeconds());
        var expiry = now.AddMinutes(15);
        if (expiry > session.ExpiresAt) expiry = DateTimeOffset.FromUnixTimeSeconds(session.ExpiresAt.ToUnixTimeSeconds());
        var token = new JwtSecurityToken(settings.Issuer, settings.Audience,
            [new Claim("sub", session.UserId.ToString()), new Claim("sid", session.Id.ToString()),
             new Claim("jti", Guid.NewGuid().ToString())], now.UtcDateTime, expiry.UtcDateTime,
            new SigningCredentials(new SymmetricSecurityKey(settings.SigningKey), SecurityAlgorithms.HmacSha256));
        return (new JwtSecurityTokenHandler().WriteToken(token), expiry);
    }
}
