using NovaWallet.Domain.Enums;

namespace NovaWallet.Application.DTOs;

public record WalletDto(
    Guid Id,
    string CustomerId,
    long BalanceKobo,
    string Currency,
    KycTier KycTier,
    bool IsActive,
    DateTime CreatedAtUtc
);

public record BalanceResponse(
    Guid WalletId,
    string CustomerId,
    long BalanceKobo,
    string Currency,
    decimal FormattedNaira,
    DateTime AsOfUtc
);

public record CreateWalletRequest(
    string CustomerId,
    KycTier KycTier = KycTier.Tier1,
    string? Bvn = null,
    string? Nin = null
);

public record CreditWalletRequest(
    long AmountKobo,
    string? Reference = null,
    string? CounterpartyBankCode = null,
    string? SessionId = null,
    string? Description = null,
    string Channel = "NIP"
);

public record CreditWalletResponse(
    Guid TransactionId,
    Guid WalletId,
    long AmountKobo,
    long BalanceAfterKobo,
    string Currency,
    string Reference,
    DateTime CompletedAtUtc
);

public record TransferRequest(
    Guid SourceWalletId,
    Guid DestinationWalletId,
    long AmountKobo,
    string? Reference = null,
    string? Description = null,
    string Channel = "API"
);

public record TransferResponse(
    Guid TransactionId,
    string Reference,
    Guid SourceWalletId,
    Guid DestinationWalletId,
    long AmountKobo,
    long SourceBalanceAfterKobo,
    string Currency,
    DateTime CompletedAtUtc
);

public record StatementQuery(
    int PageNumber = 1,
    int PageSize = 20,
    DateTime? FromDateUtc = null,
    DateTime? ToDateUtc = null,
    TransactionType? Type = null
);

public record StatementResponse(
    Guid WalletId,
    int PageNumber,
    int PageSize,
    int TotalCount,
    int TotalPages,
    IReadOnlyList<TransactionDto> Items
);

public record TransactionDto(
    Guid Id,
    Guid WalletId,
    TransactionType Type,
    long AmountKobo,
    long BalanceAfterKobo,
    string Currency,
    string Reference,
    Guid? CounterpartyWalletId,
    string? Description,
    string Channel,
    TransactionStatus Status,
    DateTime CreatedAtUtc
);

public record AuditLogDto(
    Guid Id,
    Guid WalletId,
    string Operation,
    long AmountKobo,
    long PreBalanceKobo,
    long PostBalanceKobo,
    string? Reference,
    string? CorrelationId,
    string? PerformedBy,
    DateTime CreatedAtUtc
);
