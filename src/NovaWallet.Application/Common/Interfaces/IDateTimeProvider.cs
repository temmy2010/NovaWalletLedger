namespace NovaWallet.Application.Common.Interfaces;

public interface IDateTimeProvider
{
    DateTime UtcNow { get; }
    DateTime WatNow { get; }
    DateTime GetWatMidnightTodayUtc();
    DateTime GetWatMidnightTomorrowUtc();
}
