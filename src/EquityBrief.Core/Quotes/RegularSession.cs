using EquityBrief.Core.Bars;

namespace EquityBrief.Core.Quotes;

// The exchange's regular session on a day: from 09:30 to 16:00 in New York, to 13:00 on a day the exchange closes early,
// and none on a day it does not trade, read from the closures table. A day past the table's range has no session here,
// so a quote is never asked on a day the table cannot say the exchange traded.
//
// The early closes sit here rather than in the closures table, which a rule's version pins: a day the exchange closes
// early is still a session, so the night reads none of them, and only the live quote asks when a session ends.
// see: The name page draws a delayed quote in the regular session, asked by a worker job at most every five minutes under a day's cap
public static class RegularSession
{
    public static readonly TimeOnly Opens = new(9, 30);

    public static readonly TimeOnly Closes = new(16, 0);

    public static readonly TimeOnly ClosesEarly = new(13, 0);

    // The days the exchange closes at 13:00, as its own hours and calendars page listed them on 2026-10-10, through the
    // closures table's end.
    public static IReadOnlyCollection<DateOnly> EarlyCloses { get; } = [new(2026, 11, 27), new(2026, 12, 24), new(2027, 11, 26)];

    // The session's open and close on a day as instants, none on a day the exchange does not trade or past the table.
    public static (DateTimeOffset Open, DateTimeOffset Close)? On(DateOnly day, TimeZoneInfo zone)
    {
        if (day < ExchangeClosures.CoveredFrom || day > ExchangeClosures.CoveredThrough || !ExchangeClosures.IsSession(day))
        {
            return null;
        }

        return (At(day, Opens, zone), At(day, EarlyCloses.Contains(day) ? ClosesEarly : Closes, zone));
    }

    // Whether an instant falls inside the session of its own day in New York, from its open to before its close.
    public static bool IsOpen(DateTimeOffset instant, TimeZoneInfo zone) =>
        On(DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(instant, zone).DateTime), zone) is { } session
            && instant >= session.Open
            && instant < session.Close;

    static DateTimeOffset At(DateOnly day, TimeOnly time, TimeZoneInfo zone)
    {
        var local = day.ToDateTime(time, DateTimeKind.Unspecified);

        return new DateTimeOffset(local, zone.GetUtcOffset(local));
    }
}
