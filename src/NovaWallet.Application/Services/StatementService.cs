using Microsoft.EntityFrameworkCore;
using NovaWallet.Application.Common.Interfaces;
using NovaWallet.Application.DTOs;
using NovaWallet.Application.Interfaces;
using NovaWallet.Domain.Exceptions;

namespace NovaWallet.Application.Services;

public class StatementService : IStatementService
{
    private readonly IApplicationDbContext _dbContext;

    public StatementService(IApplicationDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<StatementResponse> GetStatementAsync(Guid walletId, StatementQuery query, CancellationToken cancellationToken = default)
    {
        var walletExists = await _dbContext.Wallets
            .AsNoTracking()
            .AnyAsync(w => w.Id == walletId, cancellationToken);

        if (!walletExists)
            throw new WalletNotFoundException(walletId);

        var pageNumber = query.PageNumber < 1 ? 1 : query.PageNumber;
        var pageSize = query.PageSize is < 1 or > 100 ? 20 : query.PageSize;

        var baseQuery = _dbContext.Transactions
            .AsNoTracking()
            .Where(t => t.WalletId == walletId);

        if (query.FromDateUtc.HasValue)
            baseQuery = baseQuery.Where(t => t.CreatedAtUtc >= query.FromDateUtc.Value);

        if (query.ToDateUtc.HasValue)
            baseQuery = baseQuery.Where(t => t.CreatedAtUtc <= query.ToDateUtc.Value);

        if (query.Type.HasValue)
            baseQuery = baseQuery.Where(t => t.Type == query.Type.Value);

        var totalCount = await baseQuery.CountAsync(cancellationToken);
        var totalPages = (int)Math.Ceiling(totalCount / (double)pageSize);

        var items = await baseQuery
            .OrderByDescending(t => t.CreatedAtUtc) // Newest first
            .Skip((pageNumber - 1) * pageSize)
            .Take(pageSize)
            .Select(t => new TransactionDto(
                t.Id,
                t.WalletId,
                t.Type,
                t.AmountKobo,
                t.BalanceAfterKobo,
                t.Currency,
                t.Reference,
                t.CounterpartyWalletId,
                t.Description,
                t.Channel,
                t.Status,
                t.CreatedAtUtc
            ))
            .ToListAsync(cancellationToken);

        return new StatementResponse(
            WalletId: walletId,
            PageNumber: pageNumber,
            PageSize: pageSize,
            TotalCount: totalCount,
            TotalPages: totalPages,
            Items: items
        );
    }
}
