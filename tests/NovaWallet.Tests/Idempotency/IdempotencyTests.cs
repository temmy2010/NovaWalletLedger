using Microsoft.Extensions.Logging.Abstractions;
using NovaWallet.Application.DTOs;
using NovaWallet.Application.Services;
using NovaWallet.Domain.Entities;
using NovaWallet.Domain.Enums;
using NovaWallet.Domain.Exceptions;
using NovaWallet.Infrastructure.Time;
using NovaWallet.Tests.Helpers;

namespace NovaWallet.Tests.Idempotency;

public class IdempotencyTests
{
    [Fact]
    public async Task Transfer_ReplayingExactSameIdempotencyKey_ShouldReturnCachedResultWithoutDoubleDebit()
    {
        // Arrange
        using var dbContext = TestDbContextFactory.CreateInMemoryDbContext();
        var dateTimeProvider = new DateTimeProvider();
        var transferService = new TransferService(dbContext, dateTimeProvider, NullLogger<TransferService>.Instance);

        var sourceWallet = new Wallet(Guid.NewGuid(), "CUST-IDEM-001", KycTier.Tier1);
        sourceWallet.Credit(50_000L); // ₦500.00
        var destWallet = new Wallet(Guid.NewGuid(), "CUST-IDEM-002", KycTier.Tier1);

        dbContext.Wallets.AddRange(sourceWallet, destWallet);
        await dbContext.SaveChangesAsync();

        var request = new TransferRequest
        {
            SourceWalletId = sourceWallet.Id,
            DestinationWalletId = destWallet.Id,
            AmountKobo = 10_000L, // ₦100.00
            Reference = "IDEM-REF-001"
        };

        const string idempotencyKey = "firstbank-idempotency-key-12345";

        // Act - First Attempt
        var firstResponse = await transferService.TransferFundsAsync(request, idempotencyKey);

        // Act - Second Attempt (Replay with same key & payload)
        var replayedResponse = await transferService.TransferFundsAsync(request, idempotencyKey);

        // Assert
        firstResponse.Should().NotBeNull();
        replayedResponse.Should().NotBeNull();

        // Responses should match exactly
        replayedResponse.TransactionId.Should().Be(firstResponse.TransactionId);
        replayedResponse.Reference.Should().Be(firstResponse.Reference);
        replayedResponse.AmountKobo.Should().Be(firstResponse.AmountKobo);

        // Source wallet balance must only have been debited ONCE (50,000 - 10,000 = 40,000)
        var reloadedSource = await dbContext.Wallets.FindAsync(sourceWallet.Id);
        reloadedSource!.BalanceKobo.Should().Be(40_000L, "Funds must NOT be deducted twice on replay");

        var reloadedDest = await dbContext.Wallets.FindAsync(destWallet.Id);
        reloadedDest!.BalanceKobo.Should().Be(10_000L, "Destination must NOT receive double credit on replay");
    }

    [Fact]
    public async Task Transfer_ReusingIdempotencyKeyWithDifferentPayload_ShouldThrowIdempotencyConflictException()
    {
        // Arrange
        using var dbContext = TestDbContextFactory.CreateInMemoryDbContext();
        var dateTimeProvider = new DateTimeProvider();
        var transferService = new TransferService(dbContext, dateTimeProvider, NullLogger<TransferService>.Instance);

        var sourceWallet = new Wallet(Guid.NewGuid(), "CUST-IDEM-003", KycTier.Tier1);
        sourceWallet.Credit(50_000L);
        var destWallet = new Wallet(Guid.NewGuid(), "CUST-IDEM-004", KycTier.Tier1);

        dbContext.Wallets.AddRange(sourceWallet, destWallet);
        await dbContext.SaveChangesAsync();

        const string idempotencyKey = "shared-idempotency-key-999";

        var initialRequest = new TransferRequest
        {
            SourceWalletId = sourceWallet.Id,
            DestinationWalletId = destWallet.Id,
            AmountKobo = 5_000L,
            Reference = "REF-A"
        };

        var conflictingRequest = new TransferRequest
        {
            SourceWalletId = sourceWallet.Id,
            DestinationWalletId = destWallet.Id,
            AmountKobo = 15_000L, // Different amount
            Reference = "REF-B"
        };

        // Act - First Request Succeeds
        await transferService.TransferFundsAsync(initialRequest, idempotencyKey);

        // Act - Second Request with different payload throws exception
        var act = async () => await transferService.TransferFundsAsync(conflictingRequest, idempotencyKey);

        // Assert
        await act.Should().ThrowAsync<IdempotencyConflictException>()
            .WithMessage($"*{idempotencyKey}*");
    }
}
