using System.Security.Claims;

namespace NovaWallet.Application.Common.Interfaces;

public interface IJwtTokenService
{
    int ExpirySeconds { get; }
    string GenerateToken(string customerId, string role, IEnumerable<Claim>? additionalClaims = null);
}
