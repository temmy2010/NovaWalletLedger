namespace NovaWallet.Application.DTOs;

using NovaWallet.Domain.Enums;

public class WalletDto
{
    public Guid Id { get; set; }
    public string CustomerId { get; set; } = string.Empty;
    public long BalanceKobo { get; set; }
    public string Currency { get; set; } = "NGN";
    public KycTier KycTier { get; set; }
    public bool IsActive { get; set; }
    public DateTime CreatedAtUtc { get; set; }
}

public class BalanceResponse
{
    public Guid WalletId { get; set; }
    public string CustomerId { get; set; } = string.Empty;
    public long BalanceKobo { get; set; }
    public string Currency { get; set; } = "NGN";
    public decimal FormattedNaira { get; set; }
    public DateTime AsOfUtc { get; set; }
}

public class CreateWalletRequest
{
    public string CustomerId { get; set; } = string.Empty;
    public KycTier KycTier { get; set; } = KycTier.Tier1;
    public string? Bvn { get; set; }
    public string? Nin { get; set; }

    public CreateWalletRequest() { }

    public CreateWalletRequest(string customerId, KycTier kycTier = KycTier.Tier1, string? bvn = null, string? nin = null)
    {
        CustomerId = customerId;
        KycTier = kycTier;
        Bvn = bvn;
        Nin = nin;
    }
}

public class CreditWalletRequest
{
    public long AmountKobo { get; set; }
    public string? Reference { get; set; }
    public string? CounterpartyBankCode { get; set; }
    public string? SessionId { get; set; }
    public string? Description { get; set; }
    public string Channel { get; set; } = "NIP";

    public CreditWalletRequest() { }

    public CreditWalletRequest(long amountKobo, string? reference = null, string? counterpartyBankCode = null, string? sessionId = null, string? description = null, string channel = "NIP")
    {
        AmountKobo = amountKobo;
        Reference = reference;
        CounterpartyBankCode = counterpartyBankCode;
        SessionId = sessionId;
        Description = description;
        Channel = channel;
    }
}

public class CreditWalletResponse
{
    public Guid TransactionId { get; set; }
    public Guid WalletId { get; set; }
    public long AmountKobo { get; set; }
    public long BalanceAfterKobo { get; set; }
    public string Currency { get; set; } = "NGN";
    public string Reference { get; set; } = string.Empty;
    public DateTime CompletedAtUtc { get; set; }
}

public class TransferRequest
{
    public Guid SourceWalletId { get; set; }
    public Guid DestinationWalletId { get; set; }
    public long AmountKobo { get; set; }
    public string? Reference { get; set; }
    public string? Description { get; set; }
    public string Channel { get; set; } = "API";

    public TransferRequest() { }

    public TransferRequest(Guid sourceWalletId, Guid destinationWalletId, long amountKobo, string? reference = null, string? description = null, string channel = "API")
    {
        SourceWalletId = sourceWalletId;
        DestinationWalletId = destinationWalletId;
        AmountKobo = amountKobo;
        Reference = reference;
        Description = description;
        Channel = channel;
    }
}

public class TransferResponse
{
    public Guid TransactionId { get; set; }
    public string Reference { get; set; } = string.Empty;
    public Guid SourceWalletId { get; set; }
    public Guid DestinationWalletId { get; set; }
    public long AmountKobo { get; set; }
    public long SourceBalanceAfterKobo { get; set; }
    public string Currency { get; set; } = "NGN";
    public DateTime CompletedAtUtc { get; set; }
}

public class StatementQuery
{
    public int PageNumber { get; set; } = 1;
    public int PageSize { get; set; } = 20;
    public DateTime? FromDateUtc { get; set; }
    public DateTime? ToDateUtc { get; set; }
    public TransactionType? Type { get; set; }
}

public class StatementResponse
{
    public Guid WalletId { get; set; }
    public int PageNumber { get; set; }
    public int PageSize { get; set; }
    public int TotalCount { get; set; }
    public int TotalPages { get; set; }
    public List<TransactionDto> Items { get; set; } = new List<TransactionDto>();
}

public class TransactionDto
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
    public DateTime CreatedAtUtc { get; set; }
}

public class AuditLogDto
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
    public DateTime CreatedAtUtc { get; set; }
}
