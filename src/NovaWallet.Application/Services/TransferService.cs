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

namespace NovaWallet.Application.Services;

public class TransferService : ITransferService
{
    private const long DailyLimitKobo = 50_000_000L; // ₦500,000.00 in kobo
    private readonly IApplicationDbContext _dbContext;
    private readonly IDateTimeProvider _dateTimeProvider;
    private readonly ILogger<TransferService> _logger;

    public TransferService(
        IApplicationDbContext dbContext,
        IDateTimeProvider dateTimeProvider,
        ILogger<TransferService> logger)
    {
        _dbContext = dbContext;
        _dateTimeProvider = dateTimeProvider;
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

        // 1. Idempotency Check
        string? requestPayloadHash = null;
        if (!string.IsNullOrWhiteSpace(idempotencyKey))
        {
            requestPayloadHash = ComputePayloadHash(request);

            var existingIdempotency = await _dbContext.IdempotencyRecords
                .AsNoTracking()
                .FirstOrDefaultAsync(r => r.Key == idempotencyKey, cancellationToken);

            if (existingIdempotency != null)
            {
                if (existingIdempotency.RequestHash != requestPayloadHash)
                {
                    _logger.LogWarning("Idempotency conflict for key {Key}. Stored hash does not match current payload.", idempotencyKey);
                    throw new IdempotencyConflictException(idempotencyKey);
                }

                _logger.LogInformation("Idempotent replay detected for key {Key}. Returning cached response.", idempotencyKey);
                var cachedResponse = JsonSerializer.Deserialize<TransferResponse>(existingIdempotency.ResponseBody);
                if (cachedResponse != null)
                    return cachedResponse;
            }
        }

        // 2. Deadlock-Free Sorted Resource Acquisition
        // By always acquiring wallet rows in consistent ID order, we eliminate deadlocks
        // when bidirectional cross-transfers (A -> B and B -> A) execute concurrently.
        var firstId = request.SourceWalletId.CompareTo(request.DestinationWalletId) < 0
            ? request.SourceWalletId
            : request.DestinationWalletId;

        var secondId = request.SourceWalletId.CompareTo(request.DestinationWalletId) < 0
            ? request.DestinationWalletId
            : request.SourceWalletId;

        await using var transactionScope = await _dbContext.BeginTransactionAsync(cancellationToken);

        var firstWallet = await _dbContext.Wallets.FirstOrDefaultAsync(w => w.Id == firstId, cancellationToken);
        var secondWallet = await _dbContext.Wallets.FirstOrDefaultAsync(w => w.Id == secondId, cancellationToken);

        if (firstWallet == null)
            throw new WalletNotFoundException(firstId);

        if (secondWallet == null)
            throw new WalletNotFoundException(secondId);

        var sourceWallet = firstWallet.Id == request.SourceWalletId ? firstWallet : secondWallet;
        var destinationWallet = firstWallet.Id == request.DestinationWalletId ? firstWallet : secondWallet;

        // 3. Enforce Daily Limit (WAT Midnight Reset)
        var watStartOfDayUtc = _dateTimeProvider.GetWatMidnightTodayUtc();
        var dailySpentKobo = await _dbContext.Transactions
            .Where(t => t.WalletId == sourceWallet.Id &&
                        t.Type == TransactionType.TransferOut &&
                        t.Status == TransactionStatus.Success &&
                        t.CreatedAtUtc >= watStartOfDayUtc)
            .SumAsync(t => (long?)t.AmountKobo, cancellationToken) ?? 0L;

        if (dailySpentKobo + request.AmountKobo > DailyLimitKobo)
        {
            _logger.LogWarning("Daily limit exceeded for wallet {WalletId}. Spent: {Spent} kobo, Req: {Req} kobo, Limit: {Limit} kobo.",
                sourceWallet.Id, dailySpentKobo, request.AmountKobo, DailyLimitKobo);

            throw new DailyLimitExceededException(sourceWallet.Id, request.AmountKobo, dailySpentKobo, DailyLimitKobo);
        }

        // 4. Atomic Balance Mutation
        long sourcePreBalance = sourceWallet.BalanceKobo;
        long destPreBalance = destinationWallet.BalanceKobo;

        // Debit verifies sufficient funds and ensures non-negative balance
        sourceWallet.Debit(request.AmountKobo);
        destinationWallet.Credit(request.AmountKobo);

        long sourcePostBalance = sourceWallet.BalanceKobo;
        long destPostBalance = destinationWallet.BalanceKobo;

        // 5. Generate Ledger Transactions
        string reference = string.IsNullOrWhiteSpace(request.Reference)
            ? $"TRF-{Guid.NewGuid():N}"
            : request.Reference;

        var sourceTransaction = new Transaction(
            id: Guid.NewGuid(),
            walletId: sourceWallet.Id,
            type: TransactionType.TransferOut,
            amountKobo: request.AmountKobo,
            balanceAfterKobo: sourcePostBalance,
            reference: reference,
            counterpartyWalletId: destinationWallet.Id,
            description: request.Description ?? "P2P Transfer Out",
            channel: request.Channel,
            status: TransactionStatus.Success);

        var destinationTransaction = new Transaction(
            id: Guid.NewGuid(),
            walletId: destinationWallet.Id,
            type: TransactionType.TransferIn,
            amountKobo: request.AmountKobo,
            balanceAfterKobo: destPostBalance,
            reference: reference,
            counterpartyWalletId: sourceWallet.Id,
            description: request.Description ?? "P2P Transfer In",
            channel: request.Channel,
            status: TransactionStatus.Success);

        _dbContext.Transactions.AddRange(sourceTransaction, destinationTransaction);

        // 6. Immutable Append-Only Audit Trail
        var sourceAudit = new AuditLog(
            id: Guid.NewGuid(),
            walletId: sourceWallet.Id,
            operation: "TRANSFER_OUT",
            amountKobo: request.AmountKobo,
            preBalanceKobo: sourcePreBalance,
            postBalanceKobo: sourcePostBalance,
            reference: reference,
            correlationId: correlationId,
            performedBy: performedBy ?? "SYSTEM");

        var destinationAudit = new AuditLog(
            id: Guid.NewGuid(),
            walletId: destinationWallet.Id,
            operation: "TRANSFER_IN",
            amountKobo: request.AmountKobo,
            preBalanceKobo: destPreBalance,
            postBalanceKobo: destPostBalance,
            reference: reference,
            correlationId: correlationId,
            performedBy: performedBy ?? "SYSTEM");

        _dbContext.AuditLogs.AddRange(sourceAudit, destinationAudit);

        // 7. Transactional Outbox Pattern: Publish TransferCompleted domain event
        var outboxPayload = JsonSerializer.Serialize(new
        {
            EventId = Guid.NewGuid(),
            EventType = "TransferCompleted",
            TransactionId = sourceTransaction.Id,
            Reference = reference,
            SourceWalletId = sourceWallet.Id,
            DestinationWalletId = destinationWallet.Id,
            AmountKobo = request.AmountKobo,
            Currency = "NGN",
            TimestampUtc = _dateTimeProvider.UtcNow
        });

        var outboxMessage = new OutboxMessage(Guid.NewGuid(), "TransferCompleted", outboxPayload);
        _dbContext.OutboxMessages.Add(outboxMessage);

        var response = new TransferResponse(
            TransactionId: sourceTransaction.Id,
            Reference: reference,
            SourceWalletId: sourceWallet.Id,
            DestinationWalletId: destinationWallet.Id,
            AmountKobo: request.AmountKobo,
            SourceBalanceAfterKobo: sourcePostBalance,
            Currency: "NGN",
            CompletedAtUtc: sourceTransaction.CreatedAtUtc);

        // 8. Persist Idempotency Record
        if (!string.IsNullOrWhiteSpace(idempotencyKey) && requestPayloadHash != null)
        {
            var responseJson = JsonSerializer.Serialize(response);
            var idempotencyRecord = new IdempotencyRecord(
                key: idempotencyKey,
                requestHash: requestPayloadHash,
                statusCode: 200,
                responseBody: responseJson,
                timeToLive: TimeSpan.FromHours(24));

            _dbContext.IdempotencyRecords.Add(idempotencyRecord);
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
        await transactionScope.CommitAsync(cancellationToken);

        _logger.LogInformation("Transfer completed successfully. Ref: {Reference}, Amount: {AmountKobo} kobo from {Source} to {Dest}.",
            reference, request.AmountKobo, sourceWallet.Id, destinationWallet.Id);

        return response;
    }

    private static string ComputePayloadHash(TransferRequest request)
    {
        var rawData = $"{request.SourceWalletId:N}|{request.DestinationWalletId:N}|{request.AmountKobo}|{request.Reference?.Trim() ?? string.Empty}";
        var bytes = Encoding.UTF8.GetBytes(rawData);
        var hash = SHA256.HashData(bytes);
        return Convert.ToHexString(hash);
    }
}
