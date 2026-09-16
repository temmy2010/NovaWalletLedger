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
        var wallet = new Wallet(
            id: Guid.NewGuid(),
            customerId: request.CustomerId,
            kycTier: request.KycTier,
            bvn: request.Bvn,
            nin: request.Nin);

        _dbContext.Wallets.Add(wallet);
        await _dbContext.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("New wallet {WalletId} created for customer {CustomerId}", wallet.Id, wallet.CustomerId);

        var dto = new WalletDto
        {
            Id = wallet.Id,
            CustomerId = wallet.CustomerId,
            BalanceKobo = wallet.BalanceKobo,
            Currency = wallet.Currency,
            KycTier = wallet.KycTier,
            IsActive = wallet.IsActive,
            CreatedAtUtc = wallet.CreatedAtUtc
        };

        return dto;
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

        var response = new BalanceResponse
        {
            WalletId = wallet.Id,
            CustomerId = wallet.CustomerId,
            BalanceKobo = wallet.BalanceKobo,
            Currency = wallet.Currency,
            FormattedNaira = wallet.BalanceKobo / 100.0m,
            AsOfUtc = _clock.UtcNow
        };

        return response;
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

        long preBalance = wallet.BalanceKobo;
        wallet.Credit(request.AmountKobo);
        long postBalance = wallet.BalanceKobo;

        string reference = string.IsNullOrWhiteSpace(request.Reference)
            ? $"NIP-DEP-{Guid.NewGuid():N}"
            : request.Reference.Trim();

        var txn = new Transaction(
            id: Guid.NewGuid(),
            walletId: wallet.Id,
            type: TransactionType.Credit,
            amountKobo: request.AmountKobo,
            balanceAfterKobo: postBalance,
            reference: reference,
            counterpartyWalletId: null,
            description: request.Description ?? "Inbound NIP Deposit",
            channel: request.Channel ?? "NIP",
            status: TransactionStatus.Success);

        var audit = new AuditLog(
            id: Guid.NewGuid(),
            walletId: wallet.Id,
            operation: "CREDIT",
            amountKobo: request.AmountKobo,
            preBalanceKobo: preBalance,
            postBalanceKobo: postBalance,
            reference: reference,
            correlationId: correlationId,
            performedBy: performedBy ?? "NIP_GATEWAY");

        _dbContext.Transactions.Add(txn);
        _dbContext.AuditLogs.Add(audit);

        await _dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        _logger.LogInformation("Wallet {WalletId} credited with {Amount} kobo. Ref: {Reference}", walletId, request.AmountKobo, reference);

        var response = new CreditWalletResponse
        {
            TransactionId = txn.Id,
            WalletId = wallet.Id,
            AmountKobo = request.AmountKobo,
            BalanceAfterKobo = postBalance,
            Currency = wallet.Currency,
            Reference = reference,
            CompletedAtUtc = txn.CreatedAtUtc
        };

        return response;
    }
}
