namespace NovaWallet.Domain.Entities;

public class AuditLog
{
    public Guid Id { get; set; }
    public Guid WalletId { get; set; }
    public string Operation { get; set; } = string.Empty;
    public long AmountKobo { get; set; }
    public long PreBalanceKobo { get; set; }
    public long PostBalanceKobo { get; set; }
    public string? Reference { get; set; }
    public string? CorrelationId { get; set; }
    public string? PerformedBy { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    public AuditLog() { }

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
