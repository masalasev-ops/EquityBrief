namespace EquityBrief.Core.History;

// One row of the check's marks: a stock's session or stretch of sessions marked unreliable with the reading named, a
// stretch excluded as a block, or a later row lifting one where a source has since given good prices.
public sealed record UnreliableMark(string Ticker, DateOnly First, DateOnly Last, string Reading, string Kind, string MarkedAt);

// The stock-sessions every reader of the every-session table skips: those a mark covers that no later row of the same
// stock, sessions and reading lifts.
// see: A difference of 1% or more the check finds is settled by a second source's closes where one agrees with a side, and otherwise its stock-session is marked unreliable and skipped by every reader of the table
public sealed class MarkedSessions
{
    public const string Unreliable = "unreliable";

    public const string Excluded = "excluded";

    public const string Lifted = "lifted";

    // The reading an excluded stretch names, every reading of it.
    public const string Stretch = "stretch";

    readonly Dictionary<string, List<(DateOnly First, DateOnly Last)>> standing = new(StringComparer.Ordinal);

    public MarkedSessions(IEnumerable<UnreliableMark> marks)
    {
        foreach (var newest in marks
            .GroupBy(mark => (mark.Ticker, mark.First, mark.Last, mark.Reading))
            .Select(group => group.OrderBy(mark => mark.MarkedAt, StringComparer.Ordinal).ThenBy(mark => mark.Kind == Lifted ? 1 : 0).Last())
            .Where(mark => mark.Kind != Lifted))
        {
            if (!standing.TryGetValue(newest.Ticker, out var spans))
            {
                standing[newest.Ticker] = spans = [];
            }

            spans.Add((newest.First, newest.Last));
        }
    }

    public static MarkedSessions None { get; } = new([]);

    public int Count => standing.Values.Sum(spans => spans.Count);

    public bool Skips(string ticker, DateOnly session) =>
        standing.TryGetValue(ticker, out var spans) && spans.Any(span => span.First <= session && session <= span.Last);
}
