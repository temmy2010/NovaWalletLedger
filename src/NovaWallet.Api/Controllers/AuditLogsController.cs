namespace NovaWallet.Api.Controllers;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NovaWallet.Application.Interfaces;

[ApiController]
[Route("api/wallets/{id:guid}/audit-logs")]
[Authorize]
public class AuditLogsController : ControllerBase
{
    private readonly IAuditService _auditService;

    public AuditLogsController(IAuditService auditService)
    {
        _auditService = auditService;
    }

    /// <summary>
    /// Queries the append-only, immutable audit trail for all balance mutations of a specific wallet.
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> GetAuditLogs(Guid id, CancellationToken cancellationToken)
    {
        var auditLogs = await _auditService.GetAuditLogsAsync(id, cancellationToken);
        return Ok(auditLogs);
    }
}
