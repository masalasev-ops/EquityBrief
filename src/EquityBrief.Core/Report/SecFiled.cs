namespace EquityBrief.Core.Report;

// The day a quarter's filing was made, read from what the SEC's archive states for the company and never from the provider,
// whose date can be the quarter's own end or its earnings day. The archive states a period by the day the filing ends it on,
// which can stand a few days from the provider's month end, so the nearest within a week is the quarter's.
// see: A quarter's filing date is read from the SEC's own filings, and where they hold none the page says so
public static class SecFiled
{
    public const string NotOnFile = "Filing date not on file";

    public const int ReachDays = 7;

    public static DateOnly? Of(DateOnly quarterEnd, IReadOnlyDictionary<DateOnly, DateOnly>? firstFiled)
    {
        if (firstFiled is null)
        {
            return null;
        }

        if (firstFiled.TryGetValue(quarterEnd, out var exact))
        {
            return exact;
        }

        return firstFiled
            .Where(pair => Math.Abs(pair.Key.DayNumber - quarterEnd.DayNumber) <= ReachDays)
            .OrderBy(pair => Math.Abs(pair.Key.DayNumber - quarterEnd.DayNumber))
            .ThenBy(pair => pair.Key)
            .Select(pair => (DateOnly?)pair.Value)
            .FirstOrDefault();
    }
}
