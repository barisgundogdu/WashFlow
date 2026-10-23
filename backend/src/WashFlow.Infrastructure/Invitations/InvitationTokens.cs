using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.WebUtilities;
using WashFlow.Application.Invitations;
using WashFlow.Domain.Identity;
namespace WashFlow.Infrastructure.Invitations;
public sealed class InvitationTokens : IInvitationTokens
{
    public string Generate() => WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(32));
    public TokenHash Hash(string raw) => new(Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes("WashFlow:employee-invitation:v1:" + raw))));
}
