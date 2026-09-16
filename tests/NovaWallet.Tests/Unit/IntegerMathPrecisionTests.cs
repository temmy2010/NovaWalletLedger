using NovaWallet.Domain.Entities;
using NovaWallet.Domain.Enums;
using NovaWallet.Domain.Exceptions;

namespace NovaWallet.Tests.Unit;

public class IntegerMathPrecisionTests
{
    [Fact]
    public void Wallet_MustPerformExactIntegerMathWithoutFloatingPointDrift()
    {
        var wallet = new Wallet(Guid.NewGuid(), "CUST-MATH-01", KycTier.Tier1);

        // Simulating multiple micropayments that would fail with float/double
        // E.g. adding 10 kobo (₦0.10) 100,000 times
        const long microDepositKobo = 10L;
        const int iterations = 100_000;

        for (int i = 0; i < iterations; i++)
        {
            wallet.Credit(microDepositKobo);
        }

        // Expected: exactly 1,000,000 kobo (₦10,000.00)
        wallet.BalanceKobo.Should().Be(1_000_000L);

        // Debit 333,333 kobo (₦3,333.33)
        wallet.Debit(333_333L);
        wallet.BalanceKobo.Should().Be(666_667L);
    }

    [Fact]
    public void Wallet_CannotBeDebitedBelowZero()
    {
        var wallet = new Wallet(Guid.NewGuid(), "CUST-MATH-02", KycTier.Tier1);
        wallet.Credit(5_000L); // 5,000 kobo

        var act = () => wallet.Debit(5_001L);

        act.Should().Throw<InsufficientFundsException>()
            .WithMessage("*insufficient funds*");
    }

    [Fact]
    public void Wallet_RejectsZeroOrNegativeAmounts()
    {
        var wallet = new Wallet(Guid.NewGuid(), "CUST-MATH-03", KycTier.Tier1);

        var creditZero = () => wallet.Credit(0L);
        var creditNegative = () => wallet.Credit(-100L);
        var debitZero = () => wallet.Debit(0L);
        var debitNegative = () => wallet.Debit(-100L);

        creditZero.Should().Throw<InvalidAmountException>();
        creditNegative.Should().Throw<InvalidAmountException>();
        debitZero.Should().Throw<InvalidAmountException>();
        debitNegative.Should().Throw<InvalidAmountException>();
    }
}
