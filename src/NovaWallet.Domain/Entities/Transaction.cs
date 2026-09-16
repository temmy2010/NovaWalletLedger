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

    public Transaction() { }

    public Transaction(
        Guid id,
        Guid walletId,
        TransactionType type,
        long amountKobo,
        long balanceAfterKobo,
        string reference,
        Guid? counterpartyWalletId = null,
        string? description = null,
        string channel = "API",
        TransactionStatus status = TransactionStatus.Success)
    {
        Id = id;
        WalletId = walletId;
        Type = type;
        AmountKobo = amountKobo;
        BalanceAfterKobo = balanceAfterKobo;
        Currency = "NGN";
        Reference = reference;
        CounterpartyWalletId = counterpartyWalletId;
        Description = description;
        Channel = channel;
        Status = status;
        CreatedAtUtc = DateTime.UtcNow;
    }
}
