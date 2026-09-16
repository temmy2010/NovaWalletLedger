namespace NovaWallet.Domain.Exceptions;

public abstract class DomainException : Exception
{
    protected DomainException(string message) : base(message) { }
}

public class WalletNotFoundException : DomainException
{
    public Guid WalletId { get; }

    public WalletNotFoundException(Guid walletId) 
        : base($"Wallet with ID '{walletId}' was not found.")
    {
        WalletId = walletId;
    }
}

public class InsufficientFundsException : DomainException
{
    public Guid WalletId { get; }
    public long RequestedKobo { get; }
    public long AvailableKobo { get; }

    public InsufficientFundsException(Guid walletId, long requestedKobo, long availableKobo)
        : base($"Wallet '{walletId}' has insufficient funds. Requested: {requestedKobo} kobo, Available: {availableKobo} kobo.")
    {
        WalletId = walletId;
        RequestedKobo = requestedKobo;
        AvailableKobo = availableKobo;
    }
}

public class DailyLimitExceededException : DomainException
{
    public Guid WalletId { get; }
    public long RequestedKobo { get; }
    public long DailySpentKobo { get; }
    public long DailyLimitKobo { get; }

    public DailyLimitExceededException(Guid walletId, long requestedKobo, long dailySpentKobo, long dailyLimitKobo)
        : base($"Transfer of {requestedKobo} kobo exceeds the remaining daily limit for wallet '{walletId}'. Total spent today: {dailySpentKobo} kobo, Limit: {dailyLimitKobo} kobo (WAT midnight reset).")
    {
        WalletId = walletId;
        RequestedKobo = requestedKobo;
        DailySpentKobo = dailySpentKobo;
        DailyLimitKobo = dailyLimitKobo;
    }
}

public class IdempotencyConflictException : DomainException
{
    public string Key { get; }

    public IdempotencyConflictException(string key)
        : base($"Idempotency key '{key}' has already been used with a different request payload.")
    {
        Key = key;
    }
}

public class InvalidAmountException : DomainException
{
    public long AmountKobo { get; }

    public InvalidAmountException(long amountKobo)
        : base($"Amount must be a positive integer greater than zero. Provided: {amountKobo} kobo.")
    {
        AmountKobo = amountKobo;
    }
}

public class WalletInactiveException : DomainException
{
    public Guid WalletId { get; }

    public WalletInactiveException(Guid walletId)
        : base($"Wallet '{walletId}' is inactive or frozen.")
    {
        WalletId = walletId;
    }
}

public class SameWalletTransferException : DomainException
{
    public SameWalletTransferException() 
        : base("Sender and recipient wallet cannot be the same.")
    {
    }
}
