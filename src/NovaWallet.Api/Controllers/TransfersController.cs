namespace NovaWallet.Api.Controllers;

using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using NovaWallet.Application.DTOs;
using NovaWallet.Application.Interfaces;

[ApiController]
[Route("api/[controller]")]
[Authorize]
[EnableRateLimiting("transfer_policy")]
public class TransfersController : ControllerBase
{
    private readonly ITransferService _transferService;
    private readonly IValidator<TransferRequest> _validator;

    public TransfersController(
        ITransferService transferService,
        IValidator<TransferRequest> validator)
    {
        _transferService = transferService;
        _validator = validator;
    }

    /// <summary>
    /// Moves funds atomically from one wallet to another.
    /// Concurrency-safe, deadlock-free, strictly non-negative, and supports Idempotency-Key header.
    /// </summary>
    [HttpPost]
    public async Task<IActionResult> Transfer([FromBody] TransferRequest request, [FromHeader(Name = "Idempotency-Key")] string? idempotencyKey, CancellationToken cancellationToken)
    {
        var validationResult = await _validator.ValidateAsync(request, cancellationToken);
        if (!validationResult.IsValid)
            throw new ValidationException(validationResult.Errors);

        var correlationId = HttpContext.Items.TryGetValue("X-Correlation-Id", out var cid) ? cid?.ToString() : null;
        var performedBy = User.Identity?.Name ?? "API_USER";

        var result = await _transferService.TransferFundsAsync(request, idempotencyKey, correlationId, performedBy, cancellationToken);

        return Ok(result);
    }
}
