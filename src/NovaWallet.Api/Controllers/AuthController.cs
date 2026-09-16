using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NovaWallet.Application.Common.Interfaces;

namespace NovaWallet.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class AuthController : ControllerBase
{
    private readonly IJwtTokenService _jwtTokenService;

    public AuthController(IJwtTokenService jwtTokenService)
    {
        _jwtTokenService = jwtTokenService;
    }

    /// <summary>
    /// Generates a JWT Bearer token for testing and evaluation purposes.
    /// </summary>
    [HttpPost("token")]
    [AllowAnonymous]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public IActionResult GenerateToken([FromBody] TokenRequest request)
    {
        var customerId = string.IsNullOrWhiteSpace(request.CustomerId) ? "CUST-DEFAULT-001" : request.CustomerId;
        var role = string.IsNullOrWhiteSpace(request.Role) ? "Customer" : request.Role;

        var token = _jwtTokenService.GenerateToken(customerId, role);

        return Ok(new TokenResponse(
            AccessToken: token,
            TokenType: "Bearer",
            ExpiresInSeconds: 3600,
            CustomerId: customerId,
            Role: role
        ));
    }
}

public record TokenRequest(string? CustomerId, string? Role);
public record TokenResponse(string AccessToken, string TokenType, int ExpiresInSeconds, string CustomerId, string Role);
