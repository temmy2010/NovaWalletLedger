using Microsoft.Extensions.Logging.Abstractions;
using NovaWallet.Application.Common.Interfaces;
using NovaWallet.Application.DTOs;
using NovaWallet.Application.Services;
using NovaWallet.Domain.Entities;
using NovaWallet.Domain.Enums;
using NovaWallet.Domain.Exceptions;
using NovaWallet.Tests.Helpers;

namespace NovaWallet.Tests.Limits;

public class DailyLimitTests
{
    [Fact]
    public async Task Transfer_Exceeding500kDailyLimit_ShouldBeRejected()
    {
        // Arrange
        using var dbContext = TestDbContextFactory.CreateInMemoryDbContext();
        var dateTimeProvider = new MockDateTimeProvider(new DateTime(2026, 9, 16, 12, 0, 0, DateTimeKind.Utc));
        var transferService = new TransferService(dbContext, dateTimeProvider, NullLogger<TransferService>.Instance);

        var sourceWallet = new Wallet
        {
            Id = Guid.NewGuid(),
            CustomerId = "CUST-LIMIT-001",
            KycTier = KycTier.Tier3,
            BalanceKobo = 100_000_000L, // ₦1,000,000 in kobo
            IsActive = true
        };

        var destWallet = new Wallet
        {
            Id = Guid.NewGuid(),
            CustomerId = "CUST-LIMIT-002",
            KycTier = KycTier.Tier3,
            BalanceKobo = 0L,
            IsActive = true
        };

        dbContext.Wallets.AddRange(sourceWallet, destWallet);
        await dbContext.SaveChangesAsync();

        // Transfer 1: ₦300,000 (30,000,000 kobo) - Should Succeed
        var firstTransfer = await transferService.TransferFundsAsync(new TransferRequest
        {
            SourceWalletId = sourceWallet.Id,
            DestinationWalletId = destWallet.Id,
            AmountKobo = 30_000_000L,
            Reference = "LIMIT-TXN-1"
        });

        firstTransfer.Should().NotBeNull();

        // Transfer 2: ₦250,000 (25,000,000 kobo) - Total would be ₦550,000 (> ₦500,000 limit) - MUST FAIL!
        var secondTransferAct = async () => await transferService.TransferFundsAsync(new TransferRequest
        {
            SourceWalletId = sourceWallet.Id,
            DestinationWalletId = destWallet.Id,
            AmountKobo = 25_000_000L,
            Reference = "LIMIT-TXN-2"
        });

        await secondTransferAct.Should().ThrowAsync<DailyLimitExceededException>();

        // Transfer 3: ₦200,000 (20,000,000 kobo) - Total becomes exactly ₦500,000 - Should Succeed
        var thirdTransfer = await transferService.TransferFundsAsync(new TransferRequest
        {
            SourceWalletId = sourceWallet.Id,
            DestinationWalletId = destWallet.Id,
            AmountKobo = 20_000_000L,
            Reference = "LIMIT-TXN-3"
        });

        thirdTransfer.Should().NotBeNull();
    }

    [Fact]
    public async Task Transfer_AfterMidnightWAT_ShouldResetDailyLimit()
    {
        // Arrange
        using var dbContext = TestDbContextFactory.CreateInMemoryDbContext();
        
        // Day 1: 16th September 2026, 22:00 WAT (21:00 UTC)
        var mockTime = new MockDateTimeProvider(new DateTime(2026, 9, 16, 21, 0, 0, DateTimeKind.Utc));
        var transferService = new TransferService(dbContext, mockTime, NullLogger<TransferService>.Instance);

        var sourceWallet = new Wallet
        {
            Id = Guid.NewGuid(),
            CustomerId = "CUST-LIMIT-003",
            KycTier = KycTier.Tier3,
            BalanceKobo = 200_000_000L, // ₦2,000,000 in kobo
            IsActive = true
        };

        var destWallet = new Wallet
        {
            Id = Guid.NewGuid(),
            CustomerId = "CUST-LIMIT-004",
            KycTier = KycTier.Tier3,
            BalanceKobo = 0L,
            IsActive = true
        };

        dbContext.Wallets.AddRange(sourceWallet, destWallet);
        await dbContext.SaveChangesAsync();

        // Exhaust Day 1 Limit (₦500,000 / 50M kobo)
        await transferService.TransferFundsAsync(new TransferRequest
        {
            SourceWalletId = sourceWallet.Id,
            DestinationWalletId = destWallet.Id,
            AmountKobo = 50_000_000L,
            Reference = "DAY1-MAX"
        });

        // Advance Time past Midnight WAT: 17th September 2026, 00:05 WAT (16th Sept 23:05 UTC)
        mockTime.SetUtc(new DateTime(2026, 9, 16, 23, 5, 0, DateTimeKind.Utc));

        // Act - New transfer on Day 2 should succeed because WAT midnight reset has occurred!
        var day2Transfer = await transferService.TransferFundsAsync(new TransferRequest
        {
            SourceWalletId = sourceWallet.Id,
            DestinationWalletId = destWallet.Id,
            AmountKobo = 10_000_000L, // ₦100,000
            Reference = "DAY2-TXN-1"
        });

        // Assert
        day2Transfer.Should().NotBeNull();
        day2Transfer.AmountKobo.Should().Be(10_000_000L);
    }
}

public class MockDateTimeProvider : IDateTimeProvider
{
    private DateTime _utcNow;
    private readonly TimeZoneInfo _watZone;

    public MockDateTimeProvider(DateTime initialUtc)
    {
        _utcNow = initialUtc;
        _watZone = TimeZoneInfo.CreateCustomTimeZone("WAT", TimeSpan.FromHours(1), "West Africa Time", "WAT");
    }

    public void SetUtc(DateTime utc)
    {
        _utcNow = utc;
    }

    public DateTime UtcNow
    {
        get { return _utcNow; }
    }

    public DateTime WatNow
    {
        get { return TimeZoneInfo.ConvertTimeFromUtc(_utcNow, _watZone); }
    }

    public DateTime GetWatMidnightTodayUtc()
    {
        var wat = WatNow;
        var midnight = new DateTime(wat.Year, wat.Month, wat.Day, 0, 0, 0, DateTimeKind.Unspecified);
        return TimeZoneInfo.ConvertTimeToUtc(midnight, _watZone);
    }

    public DateTime GetWatMidnightTomorrowUtc()
    {
        return GetWatMidnightTodayUtc().AddDays(1);
    }
}
