namespace NovaWallet.Domain.Entities;

using NovaWallet.Domain.Enums;

public class Transaction
{
    public Guid Id { get; set; }
    public Guid WalletId { get; set; }
    public TransactionType Type { get; set; }
    public long AmountKobo { get; set; }
    public long BalanceAfterKobo { get; set; }
    public string Currency { get; set; } = "NGN";
    public string Reference { get; set; } = string.Empty;
    public Guid? CounterpartyWalletId { get; set; }
    public string? Description { get; set; }
    public string Channel { get; set; } = "API";
    public TransactionStatus Status { get; set; } = TransactionStatus.Success;
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}
