using Microsoft.Extensions.Logging.Abstractions;
using NovaWallet.Application.DTOs;
using NovaWallet.Application.Services;
using NovaWallet.Domain.Entities;
using NovaWallet.Domain.Enums;
using NovaWallet.Domain.Exceptions;
using NovaWallet.Infrastructure.Time;
using NovaWallet.Tests.Helpers;

namespace NovaWallet.Tests.Unit;

public class IntegerMathPrecisionTests
{
    [Fact]
    public void Wallet_MustPerformExactIntegerMathWithoutFloatingPointDrift()
    {
        var wallet = new Wallet
        {
            Id = Guid.NewGuid(),
            CustomerId = "CUST-MATH-01",
            KycTier = KycTier.Tier1,
            BalanceKobo = 0L
        };

        // Simulating multiple micropayments that would fail with float/double
        // E.g. adding 10 kobo (₦0.10) 100,000 times
        const long microDepositKobo = 10L;
        const int iterations = 100_000;

        for (int i = 0; i < iterations; i++)
        {
            wallet.BalanceKobo += microDepositKobo;
        }

        // Expected: exactly 1,000,000 kobo (₦10,000.00)
        wallet.BalanceKobo.Should().Be(1_000_000L);

        // Debit 333,333 kobo (₦3,333.33)
        wallet.BalanceKobo -= 333_333L;
        wallet.BalanceKobo.Should().Be(666_667L);
    }

    [Fact]
    public async Task TransferService_CannotDebitBelowZero()
    {
        using var db = TestDbContextFactory.CreateInMemoryDbContext();
        var clock = new DateTimeProvider();
        var service = new TransferService(db, clock, NullLogger<TransferService>.Instance);

        var source = new Wallet
        {
            Id = Guid.NewGuid(),
            CustomerId = "CUST-MATH-02",
            KycTier = KycTier.Tier1,
            BalanceKobo = 5_000L, // 5,000 kobo
            IsActive = true
        };
        var dest = new Wallet
        {
            Id = Guid.NewGuid(),
            CustomerId = "CUST-MATH-03",
            KycTier = KycTier.Tier1,
            BalanceKobo = 0L,
            IsActive = true
        };

        db.Wallets.AddRange(source, dest);
        await db.SaveChangesAsync();

        var act = async () => await service.TransferFundsAsync(new TransferRequest
        {
            SourceWalletId = source.Id,
            DestinationWalletId = dest.Id,
            AmountKobo = 5_001L,
            Reference = "OVERDRAW-01"
        });

        await act.Should().ThrowAsync<InsufficientFundsException>()
            .WithMessage("*insufficient funds*");
    }

    [Fact]
    public async Task TransferService_RejectsZeroOrNegativeAmounts()
    {
        using var db = TestDbContextFactory.CreateInMemoryDbContext();
        var clock = new DateTimeProvider();
        var service = new TransferService(db, clock, NullLogger<TransferService>.Instance);

        var sourceId = Guid.NewGuid();
        var destId = Guid.NewGuid();

        var zeroTransfer = async () => await service.TransferFundsAsync(new TransferRequest
        {
            SourceWalletId = sourceId,
            DestinationWalletId = destId,
            AmountKobo = 0L,
            Reference = "ZERO-01"
        });

        var negativeTransfer = async () => await service.TransferFundsAsync(new TransferRequest
        {
            SourceWalletId = sourceId,
            DestinationWalletId = destId,
            AmountKobo = -100L,
            Reference = "NEG-01"
        });

        await zeroTransfer.Should().ThrowAsync<InvalidAmountException>();
        await negativeTransfer.Should().ThrowAsync<InvalidAmountException>();
    }
}
