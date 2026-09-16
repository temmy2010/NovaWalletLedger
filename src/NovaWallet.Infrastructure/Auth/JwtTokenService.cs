using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;
using NovaWallet.Application.Common.Interfaces;

namespace NovaWallet.Infrastructure.Auth;

public class JwtTokenService : IJwtTokenService
{
    private readonly string _secretKey;
    private readonly string _issuer;
    private readonly string _audience;
    private readonly int _expiryMinutes;

    public int ExpirySeconds => _expiryMinutes * 60;

    public JwtTokenService(IConfiguration configuration)
    {
        _secretKey = configuration["Jwt:SecretKey"] 
            ?? throw new InvalidOperationException("Configuration 'Jwt:SecretKey' is required in appsettings.json.");
        _issuer = configuration["Jwt:Issuer"] 
            ?? throw new InvalidOperationException("Configuration 'Jwt:Issuer' is required in appsettings.json.");
        _audience = configuration["Jwt:Audience"] 
            ?? throw new InvalidOperationException("Configuration 'Jwt:Audience' is required in appsettings.json.");

        var expiryConfig = configuration["Jwt:ExpiryMinutes"] 
            ?? throw new InvalidOperationException("Configuration 'Jwt:ExpiryMinutes' is required in appsettings.json.");

        _expiryMinutes = int.Parse(expiryConfig);
    }

    public string GenerateToken(string customerId, string role, IEnumerable<Claim>? additionalClaims = null)
    {
        var claims = new List<Claim>
        {
            new Claim(JwtRegisteredClaimNames.Sub, customerId),
            new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
            new Claim(JwtRegisteredClaimNames.Iat, DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString(), ClaimValueTypes.Integer64),
            new Claim(ClaimTypes.Role, role),
            new Claim("customer_id", customerId)
        };

        if (additionalClaims != null)
        {
            claims.AddRange(additionalClaims);
        }

        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_secretKey));
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var tokenDescriptor = new SecurityTokenDescriptor
        {
            Subject = new ClaimsIdentity(claims),
            Expires = DateTime.UtcNow.AddMinutes(_expiryMinutes),
            Issuer = _issuer,
            Audience = _audience,
            SigningCredentials = credentials
        };

        var tokenHandler = new JwtSecurityTokenHandler();
        var token = tokenHandler.CreateToken(tokenDescriptor);
        return tokenHandler.WriteToken(token);
    }
}
