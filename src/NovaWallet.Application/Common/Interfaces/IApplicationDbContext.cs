using Microsoft.EntityFrameworkCore;
using NovaWallet.Domain.Entities;

namespace NovaWallet.Application.Common.Interfaces;

public interface IApplicationDbContext
{
    DbSet<Wallet> Wallets { get; }
    DbSet<Transaction> Transactions { get; }
    DbSet<AuditLog> AuditLogs { get; }
    DbSet<IdempotencyRecord> IdempotencyRecords { get; }
    DbSet<OutboxMessage> OutboxMessages { get; }

    Task<Wallet?> GetWalletWithLockAsync(Guid walletId, CancellationToken cancellationToken = default);
    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
    Task<IDbTransactionScope> BeginTransactionAsync(CancellationToken cancellationToken = default);
}

public interface IDbTransactionScope : IAsyncDisposable
{
    Task CommitAsync(CancellationToken cancellationToken = default);
    Task RollbackAsync(CancellationToken cancellationToken = default);
}
