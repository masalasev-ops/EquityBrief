namespace EquityBrief.Core.Bars;

// A gap, being a session the exchange traded and one name's series does not
// hold.
public sealed record Gap(string Ticker, DateOnly SessionDate);

// Which sessions the exchange traded, and which of them a name is missing.
//
// Detection is against the calendar and never against the rows themselves. A
// run of stored dates is self-consistent whatever is missing from it: a store
// holding four days of a five-day week looks exactly like a store holding four
// days of a four-day week, so a series cannot be asked whether it is complete.
// see: A gap is a session the exchange traded and the store does not hold
//
// The calendar is observed rather than fetched. A session the exchange traded
// is one that some name has a bar for, so the union of session dates across the
// names in hand is the calendar, and no provider call is added to a night to
// get it. That is exact once there is more than one name and it is stated
// rather than assumed: over a single series the union is the series, so nothing
// can be found missing from it, and the caller is told so rather than handed a
// clean answer.
public static class TradingCalendar
{
    // The sessions the exchange traded, over the series given. Every date any
    // name holds, in order.
    public static IReadOnlyList<DateOnly> Sessions(
        IEnumerable<IReadOnlyCollection<DateOnly>> series) =>
    [
        .. series.SelectMany(dates => dates).Distinct().OrderBy(date => date),
    ];

    // The sessions one name is missing from the calendar, between its own first
    // and last stored session and nowhere else.
    //
    // Interior only, and that is the whole rule. A name that joined the index in
    // March has no bars for February and that is not a gap, it is a shorter
    // history. A name whose last session is Friday has no bar for Monday until
    // Monday's night runs. What is a gap is a hole with stored sessions on both
    // sides of it, because that is a session the name traded through and the
    // store does not hold.
    public static IReadOnlyList<DateOnly> MissingFrom(
        IReadOnlyCollection<DateOnly> held,
        IReadOnlyList<DateOnly> calendar)
    {
        if (held.Count == 0)
        {
            return [];
        }

        var first = held.Min();
        var last = held.Max();
        var stored = held.ToHashSet();

        return
        [
            .. calendar
                .Where(session => session >= first && session <= last && !stored.Contains(session))
                .OrderBy(session => session),
        ];
    }

    // Every gap across a set of named series, each name against the calendar the
    // set itself defines.
    public static IReadOnlyList<Gap> GapsIn(
        IReadOnlyDictionary<string, IReadOnlyCollection<DateOnly>> series)
    {
        var calendar = Sessions(series.Values);

        return
        [
            .. series
                .OrderBy(entry => entry.Key, StringComparer.Ordinal)
                .SelectMany(entry => MissingFrom(entry.Value, calendar)
                    .Select(session => new Gap(entry.Key, session))),
        ];
    }

    // Whether the calendar can find anything at all. Over one series the union
    // is that series, so every date it holds is a date it holds and nothing can
    // be missing. A caller that asked anyway would get a clean answer meaning
    // "nothing was compared", which is the shape of a check that passes by
    // having no population.
    public static bool CanDetect(IReadOnlyDictionary<string, IReadOnlyCollection<DateOnly>> series) =>
        series.Count > 1;
}
