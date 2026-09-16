namespace NovaWallet.Tests.Concurrency;

using System.Collections.Concurrent;
using Microsoft.Extensions.Logging.Abstractions;
using NovaWallet.Application.DTOs;
using NovaWallet.Application.Services;
using NovaWallet.Domain.Entities;
using NovaWallet.Domain.Enums;
using NovaWallet.Domain.Exceptions;
using NovaWallet.Infrastructure.Time;
using NovaWallet.Tests.Helpers;

public class ConcurrentTransferTests
{
    [Fact]
    public async Task ConcurrentTransfers_UnderHighContention_ShouldNeverAllowNegativeBalanceOrDoubleSpend()
    {
        using var db = TestDbContextFactory.CreateInMemoryDbContext();
        var clock = new DateTimeProvider();
        var logger = NullLogger<TransferService>.Instance;

        var sourceId = Guid.NewGuid();
        var destId = Guid.NewGuid();

        var source = new Wallet(sourceId, "CUST-RACE-001", KycTier.Tier1);
        const long startingBalanceKobo = 10_000L; // ₦100.00
        source.Credit(startingBalanceKobo);

        var dest = new Wallet(destId, "CUST-RACE-002", KycTier.Tier1);

        db.Wallets.AddRange(source, dest);
        await db.SaveChangesAsync();

        // 50 concurrent requests of ₦5 (500 kobo) each. Total attempted = 25,000 kobo.
        // With 10,000 kobo available, exactly 20 should succeed and 30 must fail.
        const int totalThreads = 50;
        const long amountPerTransferKobo = 500L;

        var successes = new ConcurrentBag<TransferResponse>();
        var failures = new ConcurrentBag<Exception>();

        var tasks = Enumerable.Range(0, totalThreads).Select(async i =>
        {
            try
            {
                var service = new TransferService(db, clock, logger);
                var res = await service.TransferFundsAsync(new TransferRequest
                {
                    SourceWalletId = sourceId,
                    DestinationWalletId = destId,
                    AmountKobo = amountPerTransferKobo,
                    Reference = $"RACE-{i:D3}"
                });

                successes.Add(res);
            }
            catch (Exception ex)
            {
                failures.Add(ex);
            }
        });

        await Task.WhenAll(tasks);

        var finalSource = await db.Wallets.FindAsync(sourceId);
        var finalDest = await db.Wallets.FindAsync(destId);

        finalSource.Should().NotBeNull();
        finalDest.Should().NotBeNull();

        // Invariants:
        finalSource!.BalanceKobo.Should().Be(0L, "All 10,000 kobo should be depleted with zero negative balance");
        finalDest!.BalanceKobo.Should().Be(10_000L, "Destination should receive exactly 10,000 kobo");
        (finalSource.BalanceKobo + finalDest.BalanceKobo).Should().Be(startingBalanceKobo, "No money created or lost");

        successes.Count.Should().Be(20);
        failures.Count.Should().Be(30);

        foreach (var failure in failures)
        {
            failure.Should().BeOfType<InsufficientFundsException>();
        }
    }

    [Fact]
    public async Task ConcurrentBidirectionalTransfers_ShouldBeDeadlockFree()
    {
        using var db = TestDbContextFactory.CreateInMemoryDbContext();
        var clock = new DateTimeProvider();
        var logger = NullLogger<TransferService>.Instance;

        var walletAId = Guid.NewGuid();
        var walletBId = Guid.NewGuid();

        var walletA = new Wallet(walletAId, "CUST-A", KycTier.Tier2);
        walletA.Credit(100_000L); // ₦1,000.00

        var walletB = new Wallet(walletBId, "CUST-B", KycTier.Tier2);
        walletB.Credit(100_000L); // ₦1,000.00

        db.Wallets.AddRange(walletA, walletB);
        await db.SaveChangesAsync();

        const int batchSize = 20;
        const long transferAmountKobo = 1_000L;

        // 20 concurrent A -> B transfers and 20 concurrent B -> A transfers fired together
        var tasksAtoB = Enumerable.Range(0, batchSize).Select(async i =>
        {
            var service = new TransferService(db, clock, logger);
            return await service.TransferFundsAsync(new TransferRequest(walletAId, walletBId, transferAmountKobo, $"A-B-{i}"));
        });

        var tasksBtoA = Enumerable.Range(0, batchSize).Select(async i =>
        {
            var service = new TransferService(db, clock, logger);
            return await service.TransferFundsAsync(new TransferRequest(walletBId, walletAId, transferAmountKobo, $"B-A-{i}"));
        });

        var allResults = await Task.WhenAll(tasksAtoB.Concat(tasksBtoA));

        var freshA = await db.Wallets.FindAsync(walletAId);
        var freshB = await db.Wallets.FindAsync(walletBId);

        allResults.Length.Should().Be(40);
        freshA!.BalanceKobo.Should().Be(100_000L);
        freshB!.BalanceKobo.Should().Be(100_000L);
    }
}
