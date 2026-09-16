namespace NovaWallet.Api.Controllers;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NovaWallet.Application.Common.Interfaces;

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
        string customerId = string.IsNullOrWhiteSpace(request?.CustomerId) ? "CUST-FIRSTBANK-001" : request.CustomerId;
        string role = string.IsNullOrWhiteSpace(request?.Role) ? "Customer" : request.Role;

        string token = _jwtTokenService.GenerateToken(customerId, role);

        var response = new TokenResponse
        {
            AccessToken = token,
            TokenType = "Bearer",
            ExpiresInSeconds = 3600,
            CustomerId = customerId,
            Role = role
        };

        return Ok(response);
    }
}

public class TokenRequest
{
    public string? CustomerId { get; set; }
    public string? Role { get; set; }

    public TokenRequest() { }

    public TokenRequest(string? customerId, string? role)
    {
        CustomerId = customerId;
        Role = role;
    }
}

public class TokenResponse
{
    public string AccessToken { get; set; } = string.Empty;
    public string TokenType { get; set; } = "Bearer";
    public int ExpiresInSeconds { get; set; } = 3600;
    public string CustomerId { get; set; } = string.Empty;
    public string Role { get; set; } = string.Empty;
}
