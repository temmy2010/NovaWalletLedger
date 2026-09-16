namespace NovaWallet.Domain.Entities;

using NovaWallet.Domain.Enums;
using NovaWallet.Domain.Exceptions;

public class Wallet
{
    public Guid Id { get; set; }
    public string CustomerId { get; set; } = string.Empty;
    public long BalanceKobo { get; set; }
    public string Currency { get; set; } = "NGN";
    public KycTier KycTier { get; set; } = KycTier.Tier1;
    public string? Bvn { get; set; }
    public string? Nin { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;

    public Wallet() { }

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
