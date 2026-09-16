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
}
