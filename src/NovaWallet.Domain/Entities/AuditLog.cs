namespace NovaWallet.Domain.Entities;

public class AuditLog
{
    public Guid Id { get; private set; }
    public Guid WalletId { get; private set; }
    public string Operation { get; private set; } = string.Empty; // e.g. "CREDIT", "TRANSFER_OUT", "TRANSFER_IN"
    public long AmountKobo { get; private set; }
    public long PreBalanceKobo { get; private set; }
    public long PostBalanceKobo { get; private set; }
    public string? Reference { get; private set; }
    public string? CorrelationId { get; private set; }
    public string? PerformedBy { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }

    private AuditLog() { }

    public AuditLog(
        Guid id,
        Guid walletId,
        string operation,
        long amountKobo,
        long preBalanceKobo,
        long postBalanceKobo,
        string? reference = null,
        string? correlationId = null,
        string? performedBy = null)
    {
        Id = id;
        WalletId = walletId;
        Operation = operation;
        AmountKobo = amountKobo;
        PreBalanceKobo = preBalanceKobo;
        PostBalanceKobo = postBalanceKobo;
        Reference = reference;
        CorrelationId = correlationId;
        PerformedBy = performedBy;
        CreatedAtUtc = DateTime.UtcNow;
    }
}
