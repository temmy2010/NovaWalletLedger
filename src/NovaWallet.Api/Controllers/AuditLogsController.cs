namespace NovaWallet.Api.Controllers;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using NovaWallet.Application.Common.Interfaces;
using NovaWallet.Application.DTOs;
using NovaWallet.Domain.Exceptions;

[ApiController]
[Route("api/wallets/{id:guid}/audit-logs")]
[Authorize]
public class AuditLogsController : ControllerBase
{
    private readonly IApplicationDbContext _dbContext;

    public AuditLogsController(IApplicationDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    /// <summary>
    /// Queries the append-only, immutable audit trail for all balance mutations of a specific wallet.
    /// </summary>
    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyList<AuditLogDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> GetAuditLogs(Guid id, CancellationToken cancellationToken)
    {
        var walletExists = await _dbContext.Wallets
            .AsNoTracking()
            .AnyAsync(w => w.Id == id, cancellationToken);

        if (!walletExists)
            throw new WalletNotFoundException(id);

        var auditLogs = await _dbContext.AuditLogs
            .AsNoTracking()
            .Where(a => a.WalletId == id)
            .OrderByDescending(a => a.CreatedAtUtc)
            .Select(a => new AuditLogDto(
                a.Id,
                a.WalletId,
                a.Operation,
                a.AmountKobo,
                a.PreBalanceKobo,
                a.PostBalanceKobo,
                a.Reference,
                a.CorrelationId,
                a.PerformedBy,
                a.CreatedAtUtc
            ))
            .ToListAsync(cancellationToken);

        return Ok(auditLogs);
    }
}
