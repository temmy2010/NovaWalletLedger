namespace NovaWallet.Application.Services;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NovaWallet.Application.Common.Interfaces;
using NovaWallet.Application.DTOs;
using NovaWallet.Application.Interfaces;
using NovaWallet.Domain.Entities;
using NovaWallet.Domain.Enums;
using NovaWallet.Domain.Exceptions;

public class WalletService : IWalletService
{
    private readonly IApplicationDbContext _dbContext;
    private readonly IDateTimeProvider _clock;
    private readonly ILogger<WalletService> _logger;

    public WalletService(
        IApplicationDbContext dbContext,
        IDateTimeProvider clock,
        ILogger<WalletService> logger)
    {
        _dbContext = dbContext;
        _clock = clock;
        _logger = logger;
    }

    public async Task<WalletDto> CreateWalletAsync(CreateWalletRequest request, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.CustomerId))
        {
            throw new ArgumentException("Customer ID cannot be empty.", nameof(request.CustomerId));
        }

        var wallet = new Wallet
        {
            Id = Guid.NewGuid(),
            CustomerId = request.CustomerId.Trim(),
            KycTier = request.KycTier,
            Bvn = request.Bvn,
            Nin = request.Nin,
            BalanceKobo = 0L,
            Currency = "NGN",
            IsActive = true,
            CreatedAtUtc = _clock.UtcNow,
            UpdatedAtUtc = _clock.UtcNow
        };

        _dbContext.Wallets.Add(wallet);
        await _dbContext.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("New wallet {WalletId} created for customer {CustomerId}", wallet.Id, wallet.CustomerId);

        return new WalletDto
        {
            Id = wallet.Id,
            CustomerId = wallet.CustomerId,
            BalanceKobo = wallet.BalanceKobo,
            Currency = wallet.Currency,
            KycTier = wallet.KycTier,
            IsActive = wallet.IsActive,
            CreatedAtUtc = wallet.CreatedAtUtc
        };
    }

    public async Task<BalanceResponse> GetBalanceAsync(Guid walletId, CancellationToken cancellationToken = default)
    {
        Wallet? wallet = await _dbContext.Wallets
            .AsNoTracking()
            .FirstOrDefaultAsync(w => w.Id == walletId, cancellationToken);

        if (wallet == null)
        {
            throw new WalletNotFoundException(walletId);
        }

        return new BalanceResponse
        {
            WalletId = wallet.Id,
            CustomerId = wallet.CustomerId,
            BalanceKobo = wallet.BalanceKobo,
            Currency = wallet.Currency,
            FormattedNaira = wallet.BalanceKobo / 100.0m,
            AsOfUtc = _clock.UtcNow
        };
    }

    public async Task<CreditWalletResponse> CreditWalletAsync(
        Guid walletId,
        CreditWalletRequest request,
        string? correlationId = null,
        string? performedBy = null,
        CancellationToken cancellationToken = default)
    {
        if (request.AmountKobo <= 0)
        {
            throw new InvalidAmountException(request.AmountKobo);
        }

        await using IDbTransactionScope transaction = await _dbContext.BeginTransactionAsync(cancellationToken);

        Wallet? wallet = await _dbContext.Wallets.FirstOrDefaultAsync(w => w.Id == walletId, cancellationToken);
        if (wallet == null)
        {
            throw new WalletNotFoundException(walletId);
        }

        if (!wallet.IsActive)
        {
            throw new WalletInactiveException(walletId);
        }

        long preBalance = wallet.BalanceKobo;
        wallet.BalanceKobo += request.AmountKobo;
        wallet.UpdatedAtUtc = _clock.UtcNow;
        long postBalance = wallet.BalanceKobo;

        string reference = string.IsNullOrWhiteSpace(request.Reference)
            ? $"NIP-DEP-{Guid.NewGuid():N}"
            : request.Reference.Trim();

        var txn = new Transaction
        {
            Id = Guid.NewGuid(),
            WalletId = wallet.Id,
            Type = TransactionType.Credit,
            AmountKobo = request.AmountKobo,
            BalanceAfterKobo = postBalance,
            Reference = reference,
            CounterpartyWalletId = null,
            Description = request.Description ?? "Inbound NIP Deposit",
            Channel = request.Channel ?? "NIP",
            Status = TransactionStatus.Success,
            CreatedAtUtc = _clock.UtcNow
        };

        var audit = new AuditLog
        {
            Id = Guid.NewGuid(),
            WalletId = wallet.Id,
            Operation = "CREDIT",
            AmountKobo = request.AmountKobo,
            PreBalanceKobo = preBalance,
            PostBalanceKobo = postBalance,
            Reference = reference,
            CorrelationId = correlationId,
            PerformedBy = performedBy ?? "NIP_GATEWAY",
            CreatedAtUtc = _clock.UtcNow
        };

        _dbContext.Transactions.Add(txn);
        _dbContext.AuditLogs.Add(audit);

        await _dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        _logger.LogInformation("Wallet {WalletId} credited with {Amount} kobo. Ref: {Reference}", walletId, request.AmountKobo, reference);

        return new CreditWalletResponse
        {
            TransactionId = txn.Id,
            WalletId = wallet.Id,
            AmountKobo = request.AmountKobo,
            BalanceAfterKobo = postBalance,
            Currency = wallet.Currency,
            Reference = reference,
            CompletedAtUtc = txn.CreatedAtUtc
        };
    }
}
