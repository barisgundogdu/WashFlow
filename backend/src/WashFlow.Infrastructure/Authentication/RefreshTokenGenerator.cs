using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.WebUtilities;
using WashFlow.Application.Authentication;
using WashFlow.Domain.Identity;

namespace WashFlow.Infrastructure.Authentication;

public sealed class RefreshTokenGenerator : IRefreshTokenGenerator
{
    public string Generate() => WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(32));
    public TokenHash Hash(string token) => new(Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token))));
}
