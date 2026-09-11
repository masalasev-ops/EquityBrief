namespace EquityBrief.Core.Bars;

// The weekdays the exchange did not trade, and so which sessions a store should
// hold between two nights.
//
// Pinned rather than observed, and that is the point of it. The trading
// calendar in this namespace observes the calendar from the union of the dates
// the names hold, which is exact for one name's hole and blind to a hole every
// name shares: a session the whole index missed, because a night did not run,
// is a session no name holds and so a session the observed calendar says the
// exchange never traded. Only a calendar that comes from somewhere other than the
// store can see that one.
// see: A session the night finds missing is fetched in bulk before tonight's
//
// Full-day closures of the New York Stock Exchange, early closes excluded since
// an early close is still a session. Checked when written against the operator's
// store, whose 252 sessions from 2025-09-10 to 2026-09-10 are exactly the
// weekdays in that span less the closures listed here. The years after that span
// follow the exchange's published rules and have not been observed.
//
// It covers a stated range and refuses to answer past it rather than guessing,
// because a closure missing from the table reads as a session the night failed to
// fetch, and a day the table wrongly holds as a session is one the night asks the
// provider for and gets nothing.
public static class ExchangeClosures
{
    public static readonly DateOnly CoveredFrom = new(2025, 1, 1);

    public static readonly DateOnly CoveredThrough = new(2027, 12, 31);

    static readonly HashSet<DateOnly> Closed =
    [
        new(2025, 1, 1), new(2025, 1, 9), new(2025, 1, 20), new(2025, 2, 17), new(2025, 4, 18),
        new(2025, 5, 26), new(2025, 6, 19), new(2025, 7, 4), new(2025, 9, 1), new(2025, 11, 27),
        new(2025, 12, 25),

        new(2026, 1, 1), new(2026, 1, 19), new(2026, 2, 16), new(2026, 4, 3), new(2026, 5, 25),
        new(2026, 6, 19), new(2026, 7, 3), new(2026, 9, 7), new(2026, 11, 26), new(2026, 12, 25),

        new(2027, 1, 1), new(2027, 1, 18), new(2027, 2, 15), new(2027, 3, 26), new(2027, 5, 31),
        new(2027, 6, 18), new(2027, 7, 5), new(2027, 9, 6), new(2027, 11, 25), new(2027, 12, 24),
    ];

    public static IReadOnlyCollection<DateOnly> All => Closed;

    static bool Weekday(DateOnly day) => day.DayOfWeek is not DayOfWeek.Saturday and not DayOfWeek.Sunday;

    // Whether the exchange traded on a day inside the table's range.
    public static bool IsSession(DateOnly day)
    {
        Covering(day);

        return Weekday(day) && !Closed.Contains(day);
    }

    // The sessions strictly between two dates, in order.
    //
    // A weekend asks nothing of the table, so a Friday and the Monday after it
    // answer with nothing wherever they fall. Only a weekday between them has to
    // be inside the range.
    public static IReadOnlyList<DateOnly> SessionsBetween(DateOnly after, DateOnly before)
    {
        var sessions = new List<DateOnly>();

        for (var day = after.AddDays(1); day < before; day = day.AddDays(1))
        {
            if (Weekday(day) && IsSession(day))
            {
                sessions.Add(day);
            }
        }

        return sessions;
    }

    static void Covering(DateOnly day)
    {
        if (day < CoveredFrom || day > CoveredThrough)
        {
            throw new InvalidOperationException(
                "The exchange closure table covers " + CoveredFrom.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture) +
                " to " + CoveredThrough.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture) + " and " +
                day.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture) +
                " is outside it. A weekday the table cannot place is refused rather than guessed, because " +
                "a closure read as a session is a day asked for and never answered, and a session read " +
                "as a closure is a missed session nobody fetches. Extend the table from the exchange's " +
                "published closures.");
        }
    }
}
