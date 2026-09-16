using NovaWallet.Application.Common.Interfaces;

namespace NovaWallet.Infrastructure.Time;

public class DateTimeProvider : IDateTimeProvider
{
    private readonly TimeZoneInfo _watTimeZone;

    public DateTimeProvider()
    {
        // Support both Windows ("W. Central Africa Standard Time") and IANA/Linux ("Africa/Lagos")
        try
        {
            _watTimeZone = TimeZoneInfo.FindSystemTimeZoneById("W. Central Africa Standard Time");
        }
        catch (TimeZoneNotFoundException)
        {
            try
            {
                _watTimeZone = TimeZoneInfo.FindSystemTimeZoneById("Africa/Lagos");
            }
            catch
            {
                // Fallback to UTC+1 fixed offset
                _watTimeZone = TimeZoneInfo.CreateCustomTimeZone("WAT", TimeSpan.FromHours(1), "West Africa Time", "West Africa Standard Time");
            }
        }
    }

    public DateTime UtcNow => DateTime.UtcNow;

    public DateTime WatNow => TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, _watTimeZone);

    public DateTime GetWatMidnightTodayUtc()
    {
        var watNow = WatNow;
        var watMidnight = new DateTime(watNow.Year, watNow.Month, watNow.Day, 0, 0, 0, DateTimeKind.Unspecified);
        return TimeZoneInfo.ConvertTimeToUtc(watMidnight, _watTimeZone);
    }

    public DateTime GetWatMidnightTomorrowUtc()
    {
        return GetWatMidnightTodayUtc().AddDays(1);
    }
}
