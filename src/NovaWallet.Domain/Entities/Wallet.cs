namespace NovaWallet.Domain.Entities;

using NovaWallet.Domain.Enums;

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
}
