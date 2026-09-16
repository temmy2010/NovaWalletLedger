namespace NovaWallet.Infrastructure.Persistence;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using NovaWallet.Application.Common.Interfaces;
using NovaWallet.Domain.Entities;

public class ApplicationDbContext : DbContext, IApplicationDbContext
{
    public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options) : base(options)
    {
    }

    public DbSet<Wallet> Wallets => Set<Wallet>();
    public DbSet<Transaction> Transactions => Set<Transaction>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();
    public DbSet<IdempotencyRecord> IdempotencyRecords => Set<IdempotencyRecord>();
    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();

    public async Task<IDbTransactionScope> BeginTransactionAsync(CancellationToken cancellationToken = default)
    {
        var efTx = await Database.BeginTransactionAsync(cancellationToken);
        return new EfTransactionScope(efTx);
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Wallet schema
        modelBuilder.Entity<Wallet>(b =>
        {
            b.HasKey(w => w.Id);
            b.Property(w => w.CustomerId).IsRequired().HasMaxLength(100);
            b.Property(w => w.BalanceKobo).IsRequired();
            b.Property(w => w.Currency).IsRequired().HasMaxLength(3);
            b.Property(w => w.Bvn).HasMaxLength(11);
            b.Property(w => w.Nin).HasMaxLength(11);
            b.HasIndex(w => w.CustomerId);

            // Database constraint: balance must never be negative
            b.ToTable(t => t.HasCheckConstraint("CK_Wallets_Balance_NonNegative", "\"BalanceKobo\" >= 0 OR BalanceKobo >= 0"));
        });

        // Transaction history
        modelBuilder.Entity<Transaction>(b =>
        {
            b.HasKey(t => t.Id);
            b.Property(t => t.Reference).IsRequired().HasMaxLength(100);
            b.Property(t => t.Currency).IsRequired().HasMaxLength(3);
            b.Property(t => t.Channel).HasMaxLength(20);
            b.Property(t => t.Description).HasMaxLength(255);

            b.HasIndex(t => t.WalletId);
            b.HasIndex(t => t.Reference);
            b.HasIndex(t => new { t.WalletId, t.CreatedAtUtc });
            b.HasIndex(t => new { t.WalletId, t.Type, t.Status, t.CreatedAtUtc });
        });

        // Append-only audit log
        modelBuilder.Entity<AuditLog>(b =>
        {
            b.HasKey(a => a.Id);
            b.Property(a => a.Operation).IsRequired().HasMaxLength(50);
            b.Property(a => a.Reference).HasMaxLength(100);
            b.Property(a => a.CorrelationId).HasMaxLength(100);
            b.Property(a => a.PerformedBy).HasMaxLength(100);

            b.HasIndex(a => a.WalletId);
            b.HasIndex(a => new { a.WalletId, a.CreatedAtUtc });
        });

        // Idempotency records
        modelBuilder.Entity<IdempotencyRecord>(b =>
        {
            b.HasKey(i => i.Key);
            b.Property(i => i.Key).HasMaxLength(256);
            b.Property(i => i.RequestHash).IsRequired().HasMaxLength(128);
            b.Property(i => i.ResponseBody).IsRequired();
            b.HasIndex(i => i.ExpiresAtUtc);
        });

        // Outbox messages for domain events
        modelBuilder.Entity<OutboxMessage>(b =>
        {
            b.HasKey(o => o.Id);
            b.Property(o => o.EventType).IsRequired().HasMaxLength(100);
            b.Property(o => o.Payload).IsRequired();
            b.HasIndex(o => new { o.ProcessedAtUtc, o.CreatedAtUtc });
        });
    }

    private sealed class EfTransactionScope : IDbTransactionScope
    {
        private readonly IDbContextTransaction _tx;

        public EfTransactionScope(IDbContextTransaction tx) => _tx = tx;

        public Task CommitAsync(CancellationToken ct = default) => _tx.CommitAsync(ct);
        public Task RollbackAsync(CancellationToken ct = default) => _tx.RollbackAsync(ct);
        public ValueTask DisposeAsync() => _tx.DisposeAsync();
    }
}
