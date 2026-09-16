namespace NovaWallet.Api.Controllers;

using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NovaWallet.Application.DTOs;
using NovaWallet.Application.Interfaces;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class WalletsController : ControllerBase
{
    private readonly IWalletService _walletService;
    private readonly IValidator<CreateWalletRequest> _createValidator;
    private readonly IValidator<CreditWalletRequest> _creditValidator;

    public WalletsController(
        IWalletService walletService,
        IValidator<CreateWalletRequest> createValidator,
        IValidator<CreditWalletRequest> creditValidator)
    {
        _walletService = walletService;
        _createValidator = createValidator;
        _creditValidator = creditValidator;
    }

    /// <summary>
    /// Creates a new wallet for a customer with starting balance zero.
    /// </summary>
    [HttpPost]
    [ProducesResponseType(typeof(WalletDto), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> CreateWallet([FromBody] CreateWalletRequest request, CancellationToken cancellationToken)
    {
        var validationResult = await _createValidator.ValidateAsync(request, cancellationToken);
        if (!validationResult.IsValid)
            throw new ValidationException(validationResult.Errors);

        var result = await _walletService.CreateWalletAsync(request, cancellationToken);
        return CreatedAtAction(nameof(GetBalance), new { id = result.Id }, result);
    }

    /// <summary>
    /// Retrieves current balance and currency (NGN) with amounts in kobo.
    /// </summary>
    [HttpGet("{id:guid}/balance")]
    [ProducesResponseType(typeof(BalanceResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> GetBalance(Guid id, CancellationToken cancellationToken)
    {
        var result = await _walletService.GetBalanceAsync(id, cancellationToken);
        return Ok(result);
    }

    /// <summary>
    /// Credits funds into a wallet (simulating an inbound NIP transfer).
    /// </summary>
    [HttpPost("{id:guid}/credit")]
    [ProducesResponseType(typeof(CreditWalletResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> CreditWallet(
        Guid id,
        [FromBody] CreditWalletRequest request,
        CancellationToken cancellationToken)
    {
        var validationResult = await _creditValidator.ValidateAsync(request, cancellationToken);
        if (!validationResult.IsValid)
            throw new ValidationException(validationResult.Errors);

        var correlationId = HttpContext.Items.TryGetValue("X-Correlation-Id", out var cid) ? cid?.ToString() : null;
        var performedBy = User.Identity?.Name ?? "SYSTEM_NIP_GATEWAY";

        var result = await _walletService.CreditWalletAsync(id, request, correlationId, performedBy, cancellationToken);
        return Ok(result);
    }
}
