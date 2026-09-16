namespace NovaWallet.Application.DTOs;

public class TokenRequest
{
    public string? CustomerId { get; set; }
    public string? Role { get; set; }
}

public class TokenResponse
{
    public string AccessToken { get; set; } = string.Empty;
    public string TokenType { get; set; } = "Bearer";
    public int ExpiresInSeconds { get; set; } = 3600;
    public string CustomerId { get; set; } = string.Empty;
    public string Role { get; set; } = string.Empty;
}
