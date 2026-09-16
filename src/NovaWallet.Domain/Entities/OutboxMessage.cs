namespace NovaWallet.Domain.Entities;

public class OutboxMessage
{
    public Guid Id { get; set; }
    public string EventType { get; set; } = string.Empty;
    public string Payload { get; set; } = string.Empty;
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? ProcessedAtUtc { get; set; }
    public string? Error { get; set; }
    public int RetryCount { get; set; }

    public OutboxMessage() { }

    public OutboxMessage(Guid id, string eventType, string payload)
    {
        Id = id;
        EventType = eventType;
        Payload = payload;
        CreatedAtUtc = DateTime.UtcNow;
    }

    public void MarkProcessed()
    {
        ProcessedAtUtc = DateTime.UtcNow;
        Error = null;
    }

    public void MarkFailed(string error)
    {
        RetryCount++;
        Error = error;
    }
}
