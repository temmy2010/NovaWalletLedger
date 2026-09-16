namespace NovaWallet.Domain.Entities;

using NovaWallet.Domain.Enums;
using NovaWallet.Domain.Exceptions;

public class Wallet
{
    public Guid Id { get; private set; }
    public string CustomerId { get; private set; } = string.Empty;
    public long BalanceKobo { get; private set; }
    public string Currency { get; private set; } = "NGN";
    public KycTier KycTier { get; private set; } = KycTier.Tier1;
    public string? Bvn { get; private set; }
    public string? Nin { get; private set; }
    public bool IsActive { get; private set; } = true;
    public DateTime CreatedAtUtc { get; private set; }
    public DateTime UpdatedAtUtc { get; private set; }

    private Wallet() { } // EF Core

    public Wallet(Guid id, string customerId, KycTier kycTier = KycTier.Tier1, string? bvn = null, string? nin = null)
    {
        if (string.IsNullOrWhiteSpace(customerId))
            throw new ArgumentException("CustomerId cannot be empty", nameof(customerId));

        Id = id;
        CustomerId = customerId.Trim();
        BalanceKobo = 0; // Wallets always start at zero balance
        Currency = "NGN";
        KycTier = kycTier;
        Bvn = bvn;
        Nin = nin;
        IsActive = true;
        CreatedAtUtc = DateTime.UtcNow;
        UpdatedAtUtc = DateTime.UtcNow;
    }

    public void Credit(long amountKobo)
    {
        if (amountKobo <= 0)
            throw new InvalidAmountException(amountKobo);

        if (!IsActive)
            throw new WalletInactiveException(Id);

        BalanceKobo += amountKobo;
        UpdatedAtUtc = DateTime.UtcNow;
    }

    public void Debit(long amountKobo)
    {
        if (amountKobo <= 0)
            throw new InvalidAmountException(amountKobo);

        if (!IsActive)
            throw new WalletInactiveException(Id);

        if (BalanceKobo < amountKobo)
            throw new InsufficientFundsException(Id, amountKobo, BalanceKobo);

        BalanceKobo -= amountKobo;
        UpdatedAtUtc = DateTime.UtcNow;
    }
}
