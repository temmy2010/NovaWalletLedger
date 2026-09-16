using System.Collections.Concurrent;
using Microsoft.Extensions.Logging.Abstractions;
using NovaWallet.Application.DTOs;
using NovaWallet.Application.Services;
using NovaWallet.Domain.Entities;
using NovaWallet.Domain.Enums;
using NovaWallet.Domain.Exceptions;
using NovaWallet.Infrastructure.Time;
using NovaWallet.Tests.Helpers;

namespace NovaWallet.Tests.Concurrency;

public class ConcurrentTransferTests
{
    [Fact]
    public async Task ConcurrentTransfers_UnderHighContention_ShouldNeverAllowNegativeBalanceOrDoubleSpend()
    {
        // Arrange
        using var dbContext = TestDbContextFactory.CreateInMemoryDbContext();
        var dateTimeProvider = new DateTimeProvider();
        var logger = NullLogger<TransferService>.Instance;

        var sourceWalletId = Guid.NewGuid();
        var destWalletId = Guid.NewGuid();

        var sourceWallet = new Wallet(sourceWalletId, "CUST-RACE-001", KycTier.Tier1);
        const long initialBalanceKobo = 10_000L; // ₦100.00
        sourceWallet.Credit(initialBalanceKobo);

        var destWallet = new Wallet(destWalletId, "CUST-RACE-002", KycTier.Tier1);

        dbContext.Wallets.AddRange(sourceWallet, destWallet);
        await dbContext.SaveChangesAsync();

        const int totalConcurrentRequests = 50;
        const long transferAmountKobo = 500L; // ₦5.00
        // Total requested = 50 * 500 = 25,000 kobo. But source only has 10,000 kobo!
        // Exactly 20 transfers must succeed (20 * 500 = 10,000), 30 must fail.

        var successfulTransfers = new ConcurrentBag<TransferResponse>();
        var failedTransfers = new ConcurrentBag<Exception>();

        // Act - Dispatch 50 concurrent transfer requests across ThreadPool threads
        var tasks = Enumerable.Range(0, totalConcurrentRequests).Select(async i =>
        {
            try
            {
                // Each thread gets its own scoped service / DbContext instance connected to the same shared DB
                var transferService = new TransferService(dbContext, dateTimeProvider, logger);
                var request = new TransferRequest(
                    SourceWalletId: sourceWalletId,
                    DestinationWalletId: destWalletId,
                    AmountKobo: transferAmountKobo,
                    Reference: $"RACE-TXN-{i:D3}",
                    Description: $"Race condition test #{i}");

                var result = await transferService.TransferFundsAsync(request);
                successfulTransfers.Add(result);
            }
            catch (Exception ex)
            {
                failedTransfers.Add(ex);
            }
        });

        await Task.WhenAll(tasks);

        // Reload fresh state from DB
        var finalSource = await dbContext.Wallets.FindAsync(sourceWalletId);
        var finalDest = await dbContext.Wallets.FindAsync(destWalletId);

        // Assert - Financial Invariants
        finalSource.Should().NotBeNull();
        finalDest.Should().NotBeNull();

        // 1. Balance must NEVER go negative
        finalSource!.BalanceKobo.Should().BeGreaterThanOrEqualTo(0, "Balance must never drop below zero under any interleaving");
        finalSource.BalanceKobo.Should().Be(0L, "All 10,000 kobo should have been exhausted cleanly");

        // 2. Exact count of successful vs failed transfers
        successfulTransfers.Count.Should().Be(20, "Only 20 transfers of 500 kobo can be satisfied from 10,000 kobo");
        failedTransfers.Count.Should().Be(30, "The remaining 30 attempts must be rejected due to insufficient funds");

        foreach (var failure in failedTransfers)
        {
            failure.Should().BeOfType<InsufficientFundsException>();
        }

        // 3. Destination balance must match exact successful transfer sum
        finalDest!.BalanceKobo.Should().Be(10_000L);

        // 4. Conservation of Money invariant (Total Before == Total After)
        (finalSource.BalanceKobo + finalDest.BalanceKobo).Should().Be(initialBalanceKobo, "Ledger must strictly conserve money");
    }

    [Fact]
    public async Task ConcurrentBidirectionalTransfers_ShouldBeDeadlockFree()
    {
        // Arrange
        using var dbContext = TestDbContextFactory.CreateInMemoryDbContext();
        var dateTimeProvider = new DateTimeProvider();
        var logger = NullLogger<TransferService>.Instance;

        var walletAId = Guid.NewGuid();
        var walletBId = Guid.NewGuid();

        var walletA = new Wallet(walletAId, "CUST-BI-A", KycTier.Tier2);
        walletA.Credit(100_000L); // ₦1,000.00

        var walletB = new Wallet(walletBId, "CUST-BI-B", KycTier.Tier2);
        walletB.Credit(100_000L); // ₦1,000.00

        dbContext.Wallets.AddRange(walletA, walletB);
        await dbContext.SaveChangesAsync();

        const int operationsPerDirection = 20;
        const long amountKobo = 1_000L;

        // Act - 20 concurrent A -> B transfers and 20 concurrent B -> A transfers simultaneously
        var tasksAtoB = Enumerable.Range(0, operationsPerDirection).Select(async i =>
        {
            var transferService = new TransferService(dbContext, dateTimeProvider, logger);
            return await transferService.TransferFundsAsync(new TransferRequest(
                SourceWalletId: walletAId,
                DestinationWalletId: walletBId,
                AmountKobo: amountKobo,
                Reference: $"A-TO-B-{i}"));
        });

        var tasksBtoA = Enumerable.Range(0, operationsPerDirection).Select(async i =>
        {
            var transferService = new TransferService(dbContext, dateTimeProvider, logger);
            return await transferService.TransferFundsAsync(new TransferRequest(
                SourceWalletId: walletBId,
                DestinationWalletId: walletAId,
                AmountKobo: amountKobo,
                Reference: $"B-TO-A-{i}"));
        });

        // Execute all 40 concurrent operations without deadlock
        var allResults = await Task.WhenAll(tasksAtoB.Concat(tasksBtoA));

        // Reload fresh state
        var finalA = await dbContext.Wallets.FindAsync(walletAId);
        var finalB = await dbContext.Wallets.FindAsync(walletBId);

        // Assert
        allResults.Length.Should().Be(40);
        finalA!.BalanceKobo.Should().Be(100_000L, "Net balance should remain 100,000 kobo after symmetric transfers");
        finalB!.BalanceKobo.Should().Be(100_000L, "Net balance should remain 100,000 kobo after symmetric transfers");
        (finalA.BalanceKobo + finalB.BalanceKobo).Should().Be(200_000L);
    }
}
