using NovaWallet.Application.DTOs;

namespace NovaWallet.Application.Interfaces;

public interface IWalletService
{
    Task<WalletDto> CreateWalletAsync(CreateWalletRequest request, CancellationToken cancellationToken = default);
    Task<BalanceResponse> GetBalanceAsync(Guid walletId, CancellationToken cancellationToken = default);
    Task<CreditWalletResponse> CreditWalletAsync(Guid walletId, CreditWalletRequest request, string? correlationId = null, string? performedBy = null, CancellationToken cancellationToken = default);
}

public interface ITransferService
{
    Task<TransferResponse> TransferFundsAsync(TransferRequest request, string? idempotencyKey = null, string? correlationId = null, string? performedBy = null, CancellationToken cancellationToken = default);
}

public interface IStatementService
{
    Task<StatementResponse> GetStatementAsync(Guid walletId, StatementQuery query, CancellationToken cancellationToken = default);
}
