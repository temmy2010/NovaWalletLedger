using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using NovaWallet.Application.DTOs;
using NovaWallet.Application.Services;
using NovaWallet.Domain.Entities;
using NovaWallet.Domain.Enums;
using NovaWallet.Infrastructure.Time;
using NovaWallet.Tests.Helpers;

namespace NovaWallet.Tests.Audit;

public class AuditTrailTests
{
    [Fact]
    public async Task EveryBalanceMutation_MustRecordAppendOnlyAuditTrail()
    {
        // Arrange
        using var dbContext = TestDbContextFactory.CreateInMemoryDbContext();
        var dateTimeProvider = new DateTimeProvider();
        var walletService = new WalletService(dbContext, dateTimeProvider, NullLogger<WalletService>.Instance);
        var transferService = new TransferService(dbContext, dateTimeProvider, NullLogger<TransferService>.Instance);

        // 1. Create Wallets
        var sourceWallet = (await walletService.CreateWalletAsync(new CreateWalletRequest("CUST-AUDIT-01"))).Id;
        var destWallet = (await walletService.CreateWalletAsync(new CreateWalletRequest("CUST-AUDIT-02"))).Id;

        // 2. Credit Source (₦50,000 / 5,000,000 kobo)
        await walletService.CreditWalletAsync(sourceWallet, new CreditWalletRequest(5_000_000L, "DEP-01"), "CORR-01", "ADMIN");

        // 3. Transfer from Source to Dest (₦20,000 / 2,000,000 kobo)
        await transferService.TransferFundsAsync(new TransferRequest(sourceWallet, destWallet, 2_000_000L, "TRF-01"), correlationId: "CORR-02", performedBy: "USER-1");

        // Assert - Inspect AuditLogs table directly
        var sourceAudits = await dbContext.AuditLogs
            .Where(a => a.WalletId == sourceWallet)
            .OrderBy(a => a.CreatedAtUtc)
            .ToListAsync();

        sourceAudits.Should().HaveCount(2);

        // First mutation: Credit
        sourceAudits[0].Operation.Should().Be("CREDIT");
        sourceAudits[0].PreBalanceKobo.Should().Be(0L);
        sourceAudits[0].PostBalanceKobo.Should().Be(5_000_000L);
        sourceAudits[0].AmountKobo.Should().Be(5_000_000L);
        sourceAudits[0].CorrelationId.Should().Be("CORR-01");

        // Second mutation: Transfer Out
        sourceAudits[1].Operation.Should().Be("TRANSFER_OUT");
        sourceAudits[1].PreBalanceKobo.Should().Be(5_000_000L);
        sourceAudits[1].PostBalanceKobo.Should().Be(3_000_000L);
        sourceAudits[1].AmountKobo.Should().Be(2_000_000L);
        sourceAudits[1].CorrelationId.Should().Be("CORR-02");

        // Destination audit
        var destAudits = await dbContext.AuditLogs
            .Where(a => a.WalletId == destWallet)
            .ToListAsync();

        destAudits.Should().HaveCount(1);
        destAudits[0].Operation.Should().Be("TRANSFER_IN");
        destAudits[0].PreBalanceKobo.Should().Be(0L);
        destAudits[0].PostBalanceKobo.Should().Be(2_000_000L);
        destAudits[0].AmountKobo.Should().Be(2_000_000L);
    }
}
