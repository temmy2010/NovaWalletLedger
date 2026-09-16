namespace NovaWallet.Domain.Entities;

public class IdempotencyRecord
{
    public string Key { get; set; } = string.Empty;
    public string RequestHash { get; set; } = string.Empty;
    public int StatusCode { get; set; }
    public string ResponseBody { get; set; } = string.Empty;
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime ExpiresAtUtc { get; set; }

    public IdempotencyRecord() { }

    public IdempotencyRecord(string key, string requestHash, int statusCode, string responseBody, TimeSpan ttl)
    {
        if (string.IsNullOrWhiteSpace(key))
            throw new ArgumentException("Idempotency key is required.", nameof(key));

        Key = key.Trim();
        RequestHash = requestHash;
        StatusCode = statusCode;
        ResponseBody = responseBody;
        CreatedAtUtc = DateTime.UtcNow;
        ExpiresAtUtc = DateTime.UtcNow.Add(ttl);
    }
}
