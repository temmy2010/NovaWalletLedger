using System.Security.Claims;

namespace NovaWallet.Application.Common.Interfaces;

public interface IJwtTokenService
{
    string GenerateToken(string customerId, string role, IEnumerable<Claim>? additionalClaims = null);
}
