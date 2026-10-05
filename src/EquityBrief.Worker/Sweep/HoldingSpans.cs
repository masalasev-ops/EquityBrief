namespace EquityBrief.Worker.Sweep;

// Membership as it stood, read off a fund's quarter-end snapshots: a name is a member from the first snapshot holding it
// to the last, joining on the first's quarter end and leaving the day after the last's, so it is a member on both sides
// of every quarter end its snapshots straddle and on the quarter end itself. A name the first snapshot holds is read from
// the history's start, since nothing says when it joined, and a name the newest snapshot holds that is a member today is
// left open; one the newest holds that is not a member today left the day after it, and one a member today that no
// snapshot holds joined the day after the newest. A name its fund let go and took back is read as held between, the
// stretch it was away being no quarter end any snapshot saw. Each of those is a reading the history's figures state.
// see: Membership as it stood is rebuilt from the funds' quarterly holdings filed with the SEC, matched by ISIN and then by name
public static class HoldingSpans
{
    public static IReadOnlyDictionary<string, (DateOnly? Joined, DateOnly? Left)> From(
        IReadOnlyCollection<DateOnly> periods,
        IEnumerable<(DateOnly Period, string Ticker)> held,
        IReadOnlySet<string> today)
    {
        if (periods.Count == 0)
        {
            return new Dictionary<string, (DateOnly?, DateOnly?)>(StringComparer.Ordinal);
        }

        var first = periods.Min();
        var newest = periods.Max();
        var spans = held
            .GroupBy(one => one.Ticker, StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group =>
                {
                    var from = group.Min(one => one.Period);
                    var to = group.Max(one => one.Period);

                    return (
                        Joined: from == first ? (DateOnly?)null : from,
                        Left: to == newest && today.Contains(group.Key) ? (DateOnly?)null : to.AddDays(1));
                },
                StringComparer.Ordinal);

        foreach (var ticker in today.Where(ticker => !spans.ContainsKey(ticker)))
        {
            spans[ticker] = (newest.AddDays(1), null);
        }

        return spans;
    }
}
