namespace NovaWallet.Api.Controllers;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NovaWallet.Application.Common.Interfaces;
using NovaWallet.Application.DTOs;

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
    public IActionResult GenerateToken([FromBody] TokenRequest request)
    {
        string customerId = string.IsNullOrWhiteSpace(request?.CustomerId) ? "CUST-FIRSTBANK-001" : request.CustomerId;
        string role = string.IsNullOrWhiteSpace(request?.Role) ? "Customer" : request.Role;

        string token = _jwtTokenService.GenerateToken(customerId, role);

        var response = new TokenResponse
        {
            AccessToken = token,
            TokenType = "Bearer",
            ExpiresInSeconds = _jwtTokenService.ExpirySeconds,
            CustomerId = customerId,
            Role = role
        };

        return Ok(response);
    }
}
