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
    private const long MaxDailyTransferKobo = 50_000_000L; // ₦500,000 in kobo

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
        {
            throw new SameWalletTransferException();
        }

        if (request.AmountKobo <= 0)
        {
            throw new InvalidAmountException(request.AmountKobo);
        }

        // 1. Check Idempotency Key
        string? payloadHash = null;
        if (!string.IsNullOrWhiteSpace(idempotencyKey))
        {
            payloadHash = HashRequestPayload(request);

            IdempotencyRecord? existingRecord = await _dbContext.IdempotencyRecords
                .AsNoTracking()
                .FirstOrDefaultAsync(r => r.Key == idempotencyKey, cancellationToken);

            if (existingRecord != null)
            {
                if (existingRecord.RequestHash != payloadHash)
                {
                    _logger.LogWarning("Idempotency conflict for key {Key}: payload differs from original request.", idempotencyKey);
                    throw new IdempotencyConflictException(idempotencyKey);
                }

                _logger.LogInformation("Idempotent replay for key {Key}. Returning cached response.", idempotencyKey);
                TransferResponse? cachedResponse = JsonSerializer.Deserialize<TransferResponse>(existingRecord.ResponseBody);
                if (cachedResponse != null)
                {
                    return cachedResponse;
                }
            }
        }

        // 2. Deadlock Prevention: Sort wallet IDs before locking rows
        Guid lockFirstId = request.SourceWalletId.CompareTo(request.DestinationWalletId) < 0
            ? request.SourceWalletId
            : request.DestinationWalletId;

        Guid lockSecondId = request.SourceWalletId.CompareTo(request.DestinationWalletId) < 0
            ? request.DestinationWalletId
            : request.SourceWalletId;

        await using IDbTransactionScope transaction = await _dbContext.BeginTransactionAsync(cancellationToken);

        Wallet? firstWallet = await _dbContext.Wallets.FirstOrDefaultAsync(w => w.Id == lockFirstId, cancellationToken);
        if (firstWallet == null)
        {
            throw new WalletNotFoundException(lockFirstId);
        }

        Wallet? secondWallet = await _dbContext.Wallets.FirstOrDefaultAsync(w => w.Id == lockSecondId, cancellationToken);
        if (secondWallet == null)
        {
            throw new WalletNotFoundException(lockSecondId);
        }

        Wallet source = firstWallet.Id == request.SourceWalletId ? firstWallet : secondWallet;
        Wallet destination = firstWallet.Id == request.DestinationWalletId ? firstWallet : secondWallet;

        if (!source.IsActive)
        {
            throw new WalletInactiveException(source.Id);
        }

        if (!destination.IsActive)
        {
            throw new WalletInactiveException(destination.Id);
        }

        if (source.BalanceKobo < request.AmountKobo)
        {
            throw new InsufficientFundsException(source.Id, request.AmountKobo, source.BalanceKobo);
        }

        // 3. Daily Limit Check (WAT Midnight Reset)
        DateTime watDayStartUtc = _clock.GetWatMidnightTodayUtc();
        long spentTodayKobo = await _dbContext.Transactions
            .Where(t => t.WalletId == source.Id &&
                        t.Type == TransactionType.TransferOut &&
                        t.Status == TransactionStatus.Success &&
                        t.CreatedAtUtc >= watDayStartUtc)
            .SumAsync(t => (long?)t.AmountKobo, cancellationToken) ?? 0L;

        if (spentTodayKobo + request.AmountKobo > MaxDailyTransferKobo)
        {
            _logger.LogWarning("Daily limit exceeded for wallet {WalletId}. Spent: {Spent} kobo, Attempted: {Attempted} kobo, Limit: {Limit} kobo",
                source.Id, spentTodayKobo, request.AmountKobo, MaxDailyTransferKobo);

            throw new DailyLimitExceededException(source.Id, request.AmountKobo, spentTodayKobo, MaxDailyTransferKobo);
        }

        // 4. Atomic Balance Updates
        long sourcePreBalance = source.BalanceKobo;
        long destPreBalance = destination.BalanceKobo;

        source.BalanceKobo -= request.AmountKobo;
        source.UpdatedAtUtc = _clock.UtcNow;

        destination.BalanceKobo += request.AmountKobo;
        destination.UpdatedAtUtc = _clock.UtcNow;

        long sourcePostBalance = source.BalanceKobo;
        long destPostBalance = destination.BalanceKobo;

        string reference = string.IsNullOrWhiteSpace(request.Reference)
            ? $"TRF-{Guid.NewGuid():N}"
            : request.Reference.Trim();

        // 5. Ledger Transactions
        var debitTransaction = new Transaction
        {
            Id = Guid.NewGuid(),
            WalletId = source.Id,
            Type = TransactionType.TransferOut,
            AmountKobo = request.AmountKobo,
            BalanceAfterKobo = sourcePostBalance,
            Reference = reference,
            CounterpartyWalletId = destination.Id,
            Description = request.Description ?? "P2P Outbound Transfer",
            Channel = request.Channel,
            Status = TransactionStatus.Success,
            CreatedAtUtc = _clock.UtcNow
        };

        var creditTransaction = new Transaction
        {
            Id = Guid.NewGuid(),
            WalletId = destination.Id,
            Type = TransactionType.TransferIn,
            AmountKobo = request.AmountKobo,
            BalanceAfterKobo = destPostBalance,
            Reference = reference,
            CounterpartyWalletId = source.Id,
            Description = request.Description ?? "P2P Inbound Transfer",
            Channel = request.Channel,
            Status = TransactionStatus.Success,
            CreatedAtUtc = _clock.UtcNow
        };

        _dbContext.Transactions.AddRange(debitTransaction, creditTransaction);

        // 6. Immutable Audit Trail
        var debitAudit = new AuditLog
        {
            Id = Guid.NewGuid(),
            WalletId = source.Id,
            Operation = "TRANSFER_OUT",
            AmountKobo = request.AmountKobo,
            PreBalanceKobo = sourcePreBalance,
            PostBalanceKobo = sourcePostBalance,
            Reference = reference,
            CorrelationId = correlationId,
            PerformedBy = performedBy ?? "SYSTEM",
            CreatedAtUtc = _clock.UtcNow
        };

        var creditAudit = new AuditLog
        {
            Id = Guid.NewGuid(),
            WalletId = destination.Id,
            Operation = "TRANSFER_IN",
            AmountKobo = request.AmountKobo,
            PreBalanceKobo = destPreBalance,
            PostBalanceKobo = destPostBalance,
            Reference = reference,
            CorrelationId = correlationId,
            PerformedBy = performedBy ?? "SYSTEM",
            CreatedAtUtc = _clock.UtcNow
        };

        _dbContext.AuditLogs.AddRange(debitAudit, creditAudit);

        // 7. Transactional Outbox Pattern
        string eventPayload = JsonSerializer.Serialize(new
        {
            EventId = Guid.NewGuid(),
            EventType = "TransferCompleted",
            TransactionId = debitTransaction.Id,
            Reference = reference,
            SourceWalletId = source.Id,
            DestinationWalletId = destination.Id,
            AmountKobo = request.AmountKobo,
            Currency = "NGN",
            TimestampUtc = _clock.UtcNow
        });

        var outboxMessage = new OutboxMessage
        {
            Id = Guid.NewGuid(),
            EventType = "TransferCompleted",
            Payload = eventPayload,
            CreatedAtUtc = _clock.UtcNow
        };

        _dbContext.OutboxMessages.Add(outboxMessage);

        var response = new TransferResponse
        {
            TransactionId = debitTransaction.Id,
            Reference = reference,
            SourceWalletId = source.Id,
            DestinationWalletId = destination.Id,
            AmountKobo = request.AmountKobo,
            SourceBalanceAfterKobo = sourcePostBalance,
            Currency = "NGN",
            CompletedAtUtc = debitTransaction.CreatedAtUtc
        };

        // 8. Persist Idempotency Record
        if (!string.IsNullOrWhiteSpace(idempotencyKey) && payloadHash != null)
        {
            string responseJson = JsonSerializer.Serialize(response);
            var record = new IdempotencyRecord
            {
                Key = idempotencyKey.Trim(),
                RequestHash = payloadHash,
                StatusCode = 200,
                ResponseBody = responseJson,
                CreatedAtUtc = _clock.UtcNow,
                ExpiresAtUtc = _clock.UtcNow.AddHours(24)
            };

            _dbContext.IdempotencyRecords.Add(record);
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        _logger.LogInformation("Transfer {Reference} of {Amount} kobo from {Source} to {Dest} succeeded.",
            reference, request.AmountKobo, source.Id, destination.Id);

        return response;
    }

    private static string HashRequestPayload(TransferRequest request)
    {
        string rawString = $"{request.SourceWalletId:N}|{request.DestinationWalletId:N}|{request.AmountKobo}|{request.Reference?.Trim()}";
        byte[] hashBytes = SHA256.HashData(Encoding.UTF8.GetBytes(rawString));
        return Convert.ToHexString(hashBytes);
    }
}
