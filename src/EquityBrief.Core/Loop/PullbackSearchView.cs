namespace EquityBrief.Core.Loop;

// One line of the pullbacks' search as the Improvement loop page draws it: its setting in words and as its line, its picks
// and edge over the benchmark on 2005 to 2018 and from 2019, its adjusted p-value and its verdict with why.
public sealed record PullbackSearchRow(string Words, string Line, int EarlyPicks, double? EarlyEdge, int LatePicks, double? LateEdge, double? Adjusted, string Verdict, string Why);

// The newest search on one index: the day it ran, how many lines it tested and how many passed or came near, and its lines
// best on 2005 to 2018 first, the setting it chose among them.
// see: The pullbacks' search walks the swing filter's dials as grammar lines, chosen on 2005 to 2018 and judged together from 2019
public sealed record PullbackSearchView(string Index, DateOnly Day, int Lines, int Passed, int NearMisses, IReadOnlyList<PullbackSearchRow> Best)
{
    // The source the search's lines are written to the notebook under, a search over the long history as the long search's
    // are, its own told apart by the filter its lines name.
    public const string Source = "a search over the long history";

    public const string LineHolds = "% · filter %";

    // The most lines the card draws, best on the early years first.
    public const int Drawn = 10;

    // A search's lines best on the early years first: the highest edge there, the more picks and then the line breaking a
    // tie, as the search chose; lines with no result there last.
    public static IReadOnlyList<PullbackSearchRow> Ordered(IEnumerable<PullbackSearchRow> rows) =>
    [
        .. rows
            .OrderBy(row => row.EarlyEdge is null)
            .ThenByDescending(row => row.EarlyEdge)
            .ThenByDescending(row => row.EarlyPicks)
            .ThenBy(row => row.Line, StringComparer.Ordinal),
    ];
}
