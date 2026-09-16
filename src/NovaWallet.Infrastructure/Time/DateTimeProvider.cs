namespace NovaWallet.Infrastructure.Time;

using NovaWallet.Application.Common.Interfaces;

public class DateTimeProvider : IDateTimeProvider
{
    private readonly TimeZoneInfo _watZone;

    public DateTimeProvider()
    {
        // Support Windows and Linux/IANA timezone identifiers for Nigeria / West Africa
        try
        {
            _watZone = TimeZoneInfo.FindSystemTimeZoneById("W. Central Africa Standard Time");
        }
        catch (TimeZoneNotFoundException)
        {
            try
            {
                _watZone = TimeZoneInfo.FindSystemTimeZoneById("Africa/Lagos");
            }
            catch
            {
                // Fallback to standard UTC+1 offset
                _watZone = TimeZoneInfo.CreateCustomTimeZone("WAT", TimeSpan.FromHours(1), "West Africa Time", "WAT");
            }
        }
    }

    public DateTime UtcNow => DateTime.UtcNow;

    public DateTime WatNow => TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, _watZone);

    public DateTime GetWatMidnightTodayUtc()
    {
        var localWat = WatNow;
        var midnightWat = new DateTime(localWat.Year, localWat.Month, localWat.Day, 0, 0, 0, DateTimeKind.Unspecified);
        return TimeZoneInfo.ConvertTimeToUtc(midnightWat, _watZone);
    }

    public DateTime GetWatMidnightTomorrowUtc()
    {
        return GetWatMidnightTodayUtc().AddDays(1);
    }
}
