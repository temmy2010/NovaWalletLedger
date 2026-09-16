using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using NovaWallet.Application.Common.Interfaces;
using NovaWallet.Domain.Entities;

namespace NovaWallet.Infrastructure.Persistence;

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
        var efTransaction = await Database.BeginTransactionAsync(cancellationToken);
        return new EfDbTransactionScope(efTransaction);
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // 1. Wallet Configuration
        modelBuilder.Entity<Wallet>(entity =>
        {
            entity.HasKey(w => w.Id);

            entity.Property(w => w.CustomerId)
                .IsRequired()
                .HasMaxLength(100);

            entity.Property(w => w.BalanceKobo)
                .IsRequired();

            entity.Property(w => w.Currency)
                .IsRequired()
                .HasMaxLength(3);

            entity.Property(w => w.Bvn)
                .HasMaxLength(11);

            entity.Property(w => w.Nin)
                .HasMaxLength(11);

            entity.HasIndex(w => w.CustomerId);

            // Non-negotiable hard constraint: Database-level invariant that balance never drops below zero
            entity.ToTable(t => t.HasCheckConstraint("CK_Wallets_BalanceKobo_NonNegative", "\"BalanceKobo\" >= 0 OR BalanceKobo >= 0"));
        });

        // 2. Transaction Configuration
        modelBuilder.Entity<Transaction>(entity =>
        {
            entity.HasKey(t => t.Id);

            entity.Property(t => t.Reference)
                .IsRequired()
                .HasMaxLength(100);

            entity.Property(t => t.Currency)
                .IsRequired()
                .HasMaxLength(3);

            entity.Property(t => t.Channel)
                .HasMaxLength(20);

            entity.Property(t => t.Description)
                .HasMaxLength(255);

            entity.HasIndex(t => t.WalletId);
            entity.HasIndex(t => t.Reference);
            entity.HasIndex(t => new { t.WalletId, t.CreatedAtUtc });
            entity.HasIndex(t => new { t.WalletId, t.Type, t.Status, t.CreatedAtUtc });
        });

        // 3. AuditLog Configuration (Append-Only Immutable Trail)
        modelBuilder.Entity<AuditLog>(entity =>
        {
            entity.HasKey(a => a.Id);

            entity.Property(a => a.Operation)
                .IsRequired()
                .HasMaxLength(50);

            entity.Property(a => a.Reference)
                .HasMaxLength(100);

            entity.Property(a => a.CorrelationId)
                .HasMaxLength(100);

            entity.Property(a => a.PerformedBy)
                .HasMaxLength(100);

            entity.HasIndex(a => a.WalletId);
            entity.HasIndex(a => new { a.WalletId, a.CreatedAtUtc });
        });

        // 4. IdempotencyRecord Configuration
        modelBuilder.Entity<IdempotencyRecord>(entity =>
        {
            entity.HasKey(i => i.Key);

            entity.Property(i => i.Key)
                .HasMaxLength(256);

            entity.Property(i => i.RequestHash)
                .IsRequired()
                .HasMaxLength(128);

            entity.Property(i => i.ResponseBody)
                .IsRequired();

            entity.HasIndex(i => i.ExpiresAtUtc);
        });

        // 5. OutboxMessage Configuration
        modelBuilder.Entity<OutboxMessage>(entity =>
        {
            entity.HasKey(o => o.Id);

            entity.Property(o => o.EventType)
                .IsRequired()
                .HasMaxLength(100);

            entity.Property(o => o.Payload)
                .IsRequired();

            entity.HasIndex(o => new { o.ProcessedAtUtc, o.CreatedAtUtc });
        });
    }

    private sealed class EfDbTransactionScope : IDbTransactionScope
    {
        private readonly IDbContextTransaction _transaction;

        public EfDbTransactionScope(IDbContextTransaction transaction)
        {
            _transaction = transaction;
        }

        public async Task CommitAsync(CancellationToken cancellationToken = default)
        {
            await _transaction.CommitAsync(cancellationToken);
        }

        public async Task RollbackAsync(CancellationToken cancellationToken = default)
        {
            await _transaction.RollbackAsync(cancellationToken);
        }

        public async ValueTask DisposeAsync()
        {
            await _transaction.DisposeAsync();
        }
    }
}
