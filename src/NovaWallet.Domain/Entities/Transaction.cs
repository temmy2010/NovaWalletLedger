namespace NovaWallet.Domain.Entities;

using NovaWallet.Domain.Enums;

public class Transaction
{
    public Guid Id { get; private set; }
    public Guid WalletId { get; private set; }
    public TransactionType Type { get; private set; }
    public long AmountKobo { get; private set; }
    public long BalanceAfterKobo { get; private set; }
    public string Currency { get; private set; } = "NGN";
    public string Reference { get; private set; } = string.Empty;
    public Guid? CounterpartyWalletId { get; private set; }
    public string? Description { get; private set; }
    public string Channel { get; private set; } = "API";
    public TransactionStatus Status { get; private set; } = TransactionStatus.Success;
    public DateTime CreatedAtUtc { get; private set; }

    private Transaction() { }

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
