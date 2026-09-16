namespace NovaWallet.Application.Services;

using Microsoft.EntityFrameworkCore;
using NovaWallet.Application.Common.Interfaces;
using NovaWallet.Application.DTOs;
using NovaWallet.Application.Interfaces;
using NovaWallet.Domain.Exceptions;

public class AuditService : IAuditService
{
    private readonly IApplicationDbContext _dbContext;

    public AuditService(IApplicationDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<List<AuditLogDto>> GetAuditLogsAsync(Guid walletId, CancellationToken cancellationToken = default)
    {
        bool walletExists = await _dbContext.Wallets
            .AsNoTracking()
            .AnyAsync(w => w.Id == walletId, cancellationToken);

        if (!walletExists)
        {
            throw new WalletNotFoundException(walletId);
        }

        return await _dbContext.AuditLogs
            .AsNoTracking()
            .Where(a => a.WalletId == walletId)
            .OrderByDescending(a => a.CreatedAtUtc)
            .Select(a => new AuditLogDto
            {
                Id = a.Id,
                WalletId = a.WalletId,
                Operation = a.Operation,
                AmountKobo = a.AmountKobo,
                PreBalanceKobo = a.PreBalanceKobo,
                PostBalanceKobo = a.PostBalanceKobo,
                Reference = a.Reference,
                CorrelationId = a.CorrelationId,
                PerformedBy = a.PerformedBy,
                CreatedAtUtc = a.CreatedAtUtc
            })
            .ToListAsync(cancellationToken);
    }
}
