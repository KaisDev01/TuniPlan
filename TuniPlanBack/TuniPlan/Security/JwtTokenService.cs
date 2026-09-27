using System.Security.Claims;
using System.Text;
using BL.Interfaces;
using BL.Options;
using Entities;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace TuniPlan.Security;

/// <summary>Short-lived signed access tokens (HS256). Refresh tokens are opaque and stored hashed in the database.</summary>
public sealed class JwtTokenService(IOptions<JwtOptions> options) : ITokenService
{
    public const string SecurityStampClaim = "sst";
    private readonly JsonWebTokenHandler _handler = new();

    public (string Token, DateTime ExpiresAt) CreateAccessToken(User user)
    {
        var o = options.Value;
        var now = DateTime.UtcNow;
        var expires = now.AddMinutes(o.AccessTokenMinutes);

        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString("N")),
            new(JwtRegisteredClaimNames.GivenName, user.FirstName),
            new(JwtRegisteredClaimNames.FamilyName, user.LastName),
            new("phone_number", user.PhoneNumber),
            new(SecurityStampClaim, user.SecurityStamp),
        };
        if (user.Email is not null) claims.Add(new Claim(JwtRegisteredClaimNames.Email, user.Email));
        claims.AddRange(Roles.From(user.Roles).Select(r => new Claim("role", r)));

        var descriptor = new SecurityTokenDescriptor
        {
            Issuer = o.Issuer,
            Audience = o.Audience,
            IssuedAt = now,
            NotBefore = now,
            Expires = expires,
            Subject = new ClaimsIdentity(claims),
            SigningCredentials = new SigningCredentials(CreateKey(o.SigningKey), SecurityAlgorithms.HmacSha256)
        };
        return (_handler.CreateToken(descriptor), expires);
    }

    public static SymmetricSecurityKey CreateKey(string signingKey) => new(Encoding.UTF8.GetBytes(signingKey));
}
