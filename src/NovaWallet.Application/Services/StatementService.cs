namespace NovaWallet.Application.Services;

using Microsoft.EntityFrameworkCore;
using NovaWallet.Application.Common.Interfaces;
using NovaWallet.Application.DTOs;
using NovaWallet.Application.Interfaces;
using NovaWallet.Domain.Entities;
using NovaWallet.Domain.Exceptions;

public class StatementService : IStatementService
{
    private readonly IApplicationDbContext _dbContext;

    public StatementService(IApplicationDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<StatementResponse> GetStatementAsync(Guid walletId, StatementQuery query, CancellationToken cancellationToken = default)
    {
        bool walletExists = await _dbContext.Wallets
            .AsNoTracking()
            .AnyAsync(w => w.Id == walletId, cancellationToken);

        if (!walletExists)
        {
            throw new WalletNotFoundException(walletId);
        }

        int pageNumber = query.PageNumber < 1 ? 1 : query.PageNumber;
        int pageSize = (query.PageSize < 1 || query.PageSize > 100) ? 20 : query.PageSize;

        IQueryable<Transaction> baseQuery = _dbContext.Transactions
            .AsNoTracking()
            .Where(t => t.WalletId == walletId);

        if (query.FromDateUtc.HasValue)
        {
            baseQuery = baseQuery.Where(t => t.CreatedAtUtc >= query.FromDateUtc.Value);
        }

        if (query.ToDateUtc.HasValue)
        {
            baseQuery = baseQuery.Where(t => t.CreatedAtUtc <= query.ToDateUtc.Value);
        }

        if (query.Type.HasValue)
        {
            baseQuery = baseQuery.Where(t => t.Type == query.Type.Value);
        }

        int totalCount = await baseQuery.CountAsync(cancellationToken);
        int totalPages = (int)Math.Ceiling(totalCount / (double)pageSize);

        List<TransactionDto> items = await baseQuery
            .OrderByDescending(t => t.CreatedAtUtc)
            .Skip((pageNumber - 1) * pageSize)
            .Take(pageSize)
            .Select(t => new TransactionDto
            {
                Id = t.Id,
                WalletId = t.WalletId,
                Type = t.Type,
                AmountKobo = t.AmountKobo,
                BalanceAfterKobo = t.BalanceAfterKobo,
                Currency = t.Currency,
                Reference = t.Reference,
                CounterpartyWalletId = t.CounterpartyWalletId,
                Description = t.Description,
                Channel = t.Channel,
                Status = t.Status,
                CreatedAtUtc = t.CreatedAtUtc
            })
            .ToListAsync(cancellationToken);

        var response = new StatementResponse
        {
            WalletId = walletId,
            PageNumber = pageNumber,
            PageSize = pageSize,
            TotalCount = totalCount,
            TotalPages = totalPages,
            Items = items
        };

        return response;
    }
}
