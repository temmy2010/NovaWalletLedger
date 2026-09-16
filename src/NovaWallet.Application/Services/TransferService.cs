namespace NovaWallet.Application.Services;

using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NovaWallet.Application.Common.Interfaces;
using NovaWallet.Application.DTOs;
using NovaWallet.Application.Interfaces;
using NovaWallet.Domain.Entities;
using NovaWallet.Domain.Enums;
using NovaWallet.Domain.Exceptions;

public class TransferService : ITransferService
{
    // CBN guidelines & system policy: ₦500,000 daily limit per wallet
    private const long MaxDailyTransferKobo = 50_000_000L; 

    private readonly IApplicationDbContext _dbContext;
    private readonly IDateTimeProvider _clock;
    private readonly ILogger<TransferService> _logger;

    public TransferService(
        IApplicationDbContext dbContext,
        IDateTimeProvider clock,
        ILogger<TransferService> logger)
    {
        _dbContext = dbContext;
        _clock = clock;
        _logger = logger;
    }

    public async Task<TransferResponse> TransferFundsAsync(
        TransferRequest request,
        string? idempotencyKey = null,
        string? correlationId = null,
        string? performedBy = null,
        CancellationToken cancellationToken = default)
    {
        if (request.SourceWalletId == request.DestinationWalletId)
            throw new SameWalletTransferException();

        if (request.AmountKobo <= 0)
            throw new InvalidAmountException(request.AmountKobo);

        // Check if an idempotency key was supplied
        string? payloadHash = null;
        if (!string.IsNullOrWhiteSpace(idempotencyKey))
        {
            payloadHash = HashRequestPayload(request);

            var existingRecord = await _dbContext.IdempotencyRecords
                .AsNoTracking()
                .FirstOrDefaultAsync(r => r.Key == idempotencyKey, cancellationToken);

            if (existingRecord != null)
            {
                if (existingRecord.RequestHash != payloadHash)
                {
                    _logger.LogWarning("Idempotency key collision: {Key} submitted with different payload", idempotencyKey);
                    throw new IdempotencyConflictException(idempotencyKey);
                }

                _logger.LogInformation("Replay request received for idempotency key {Key}. Returning cached response", idempotencyKey);
                var cached = JsonSerializer.Deserialize<TransferResponse>(existingRecord.ResponseBody);
                if (cached != null) return cached;
            }
        }

        // To avoid deadlocks during concurrent bidirectional transfers (A -> B and B -> A),
        // we always lock wallet rows in a fixed, deterministic order by Guid.
        var lockFirstId = request.SourceWalletId.CompareTo(request.DestinationWalletId) < 0
            ? request.SourceWalletId
            : request.DestinationWalletId;

        var lockSecondId = request.SourceWalletId.CompareTo(request.DestinationWalletId) < 0
            ? request.DestinationWalletId
            : request.SourceWalletId;

        await using var transaction = await _dbContext.BeginTransactionAsync(cancellationToken);

        var firstWallet = await _dbContext.Wallets.FirstOrDefaultAsync(w => w.Id == lockFirstId, cancellationToken)
            ?? throw new WalletNotFoundException(lockFirstId);

        var secondWallet = await _dbContext.Wallets.FirstOrDefaultAsync(w => w.Id == lockSecondId, cancellationToken)
            ?? throw new WalletNotFoundException(lockSecondId);

        var source = firstWallet.Id == request.SourceWalletId ? firstWallet : secondWallet;
        var destination = firstWallet.Id == request.DestinationWalletId ? firstWallet : secondWallet;

        // Daily limit check in Nigerian local time (WAT = UTC+1)
        var watDayStartUtc = _clock.GetWatMidnightTodayUtc();
        var spentTodayKobo = await _dbContext.Transactions
            .Where(t => t.WalletId == source.Id &&
                        t.Type == TransactionType.TransferOut &&
                        t.Status == TransactionStatus.Success &&
                        t.CreatedAtUtc >= watDayStartUtc)
            .SumAsync(t => (long?)t.AmountKobo, cancellationToken) ?? 0L;

        if (spentTodayKobo + request.AmountKobo > MaxDailyTransferKobo)
        {
            _logger.LogWarning("Daily transfer limit breached for wallet {WalletId}. Spent: {Spent} kobo, Attempted: {Attempted} kobo, Limit: {Limit} kobo",
                source.Id, spentTodayKobo, request.AmountKobo, MaxDailyTransferKobo);

            throw new DailyLimitExceededException(source.Id, request.AmountKobo, spentTodayKobo, MaxDailyTransferKobo);
        }

        // Atomic balance update
        var sourcePreBalance = source.BalanceKobo;
        var destPreBalance = destination.BalanceKobo;

        source.Debit(request.AmountKobo);
        destination.Credit(request.AmountKobo);

        var sourcePostBalance = source.BalanceKobo;
        var destPostBalance = destination.BalanceKobo;

        var reference = string.IsNullOrWhiteSpace(request.Reference)
            ? $"TRF-{Guid.NewGuid():N}"
            : request.Reference.Trim();

        // Ledger records for both sides of the transfer
        var debitTxn = new Transaction(
            id: Guid.NewGuid(),
            walletId: source.Id,
            type: TransactionType.TransferOut,
            amountKobo: request.AmountKobo,
            balanceAfterKobo: sourcePostBalance,
            reference: reference,
            counterpartyWalletId: destination.Id,
            description: request.Description ?? "P2P Outbound Transfer",
            channel: request.Channel,
            status: TransactionStatus.Success);

        var creditTxn = new Transaction(
            id: Guid.NewGuid(),
            walletId: destination.Id,
            type: TransactionType.TransferIn,
            amountKobo: request.AmountKobo,
            balanceAfterKobo: destPostBalance,
            reference: reference,
            counterpartyWalletId: source.Id,
            description: request.Description ?? "P2P Inbound Transfer",
            channel: request.Channel,
            status: TransactionStatus.Success);

        _dbContext.Transactions.AddRange(debitTxn, creditTxn);

        // Immutable audit log
        var debitAudit = new AuditLog(
            id: Guid.NewGuid(),
            walletId: source.Id,
            operation: "TRANSFER_OUT",
            amountKobo: request.AmountKobo,
            preBalanceKobo: sourcePreBalance,
            postBalanceKobo: sourcePostBalance,
            reference: reference,
            correlationId: correlationId,
            performedBy: performedBy ?? "SYSTEM");

        var creditAudit = new AuditLog(
            id: Guid.NewGuid(),
            walletId: destination.Id,
            operation: "TRANSFER_IN",
            amountKobo: request.AmountKobo,
            preBalanceKobo: destPreBalance,
            postBalanceKobo: destPostBalance,
            reference: reference,
            correlationId: correlationId,
            performedBy: performedBy ?? "SYSTEM");

        _dbContext.AuditLogs.AddRange(debitAudit, creditAudit);

        // Outbox event for downstream integrations
        var eventPayload = JsonSerializer.Serialize(new
        {
            EventId = Guid.NewGuid(),
            EventType = "TransferCompleted",
            TransactionId = debitTxn.Id,
            Reference = reference,
            SourceWalletId = source.Id,
            DestinationWalletId = destination.Id,
            AmountKobo = request.AmountKobo,
            Currency = "NGN",
            TimestampUtc = _clock.UtcNow
        });

        _dbContext.OutboxMessages.Add(new OutboxMessage(Guid.NewGuid(), "TransferCompleted", eventPayload));

        var response = new TransferResponse(
            TransactionId: debitTxn.Id,
            Reference: reference,
            SourceWalletId: source.Id,
            DestinationWalletId: destination.Id,
            AmountKobo: request.AmountKobo,
            SourceBalanceAfterKobo: sourcePostBalance,
            Currency: "NGN",
            CompletedAtUtc: debitTxn.CreatedAtUtc);

        // Cache the response against the idempotency key within the same transaction
        if (!string.IsNullOrWhiteSpace(idempotencyKey) && payloadHash != null)
        {
            var responseJson = JsonSerializer.Serialize(response);
            _dbContext.IdempotencyRecords.Add(new IdempotencyRecord(
                key: idempotencyKey,
                requestHash: payloadHash,
                statusCode: 200,
                responseBody: responseJson,
                ttl: TimeSpan.FromHours(24)));
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        _logger.LogInformation("Transfer {Reference} completed: {Amount} kobo moved from {Source} to {Destination}",
            reference, request.AmountKobo, source.Id, destination.Id);

        return response;
    }

    private static string HashRequestPayload(TransferRequest request)
    {
        var rawString = $"{request.SourceWalletId:N}|{request.DestinationWalletId:N}|{request.AmountKobo}|{request.Reference?.Trim()}";
        var hashBytes = SHA256.HashData(Encoding.UTF8.GetBytes(rawString));
        return Convert.ToHexString(hashBytes);
    }
}
