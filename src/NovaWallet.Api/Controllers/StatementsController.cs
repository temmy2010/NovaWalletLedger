using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NovaWallet.Application.DTOs;
using NovaWallet.Application.Interfaces;

namespace NovaWallet.Api.Controllers;

[ApiController]
[Route("api/wallets/{id:guid}/statement")]
[Authorize]
public class StatementsController : ControllerBase
{
    private readonly IStatementService _statementService;

    public StatementsController(IStatementService statementService)
    {
        _statementService = statementService;
    }

    // Returns paginated transaction history for a wallet, sorted newest first.
    [HttpGet]
    [ProducesResponseType(typeof(StatementResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> GetStatement(Guid id, [FromQuery] StatementQuery query, CancellationToken cancellationToken)
    {
        var result = await _statementService.GetStatementAsync(id, query, cancellationToken);
        return Ok(result);
    }
}
