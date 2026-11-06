namespace EquityBrief.Core.Chart;

// The operator's word on one session of a stock's pulled history, after the check against its filings: where its history starts,
// a print that is not the market's, or a move checked and kept.
public sealed record SessionMark(DateOnly Session, string Kind, string Says);

// A pulled row stands whatever its mark says. A session a stock's history starts on leaves the sessions before it out of every
// page's read of the pull, a bad print is skipped by the moves the check reads and a small mark on the chart, and a move checked
// and kept is a line and no flag.
// see: The operator's word on a session of the pull is a mark beside it and no pulled row moves, and only the chart's reads and the chart check read it until phase 23's restart
public static class SessionMarking
{
    public const string StartsHere = "starts here";
    public const string BadPrint = "bad print";
    public const string CheckedAndKept = "checked and kept";

    // From 23.1 the session a company's history ends on, set by rule from its filings, and on the operator's word only where no
    // such filing is found.
    // see: A stock's history ends by rule on its company's last regular session read from its Form 25 or the 8-K completing its merger
    public const string HistoryEnds = "history ends";

    public static IReadOnlyList<string> Kinds { get; } = [StartsHere, BadPrint, CheckedAndKept, HistoryEnds];

    // A read of a stock's pulled rows from the session its history starts on, $ticker bound.
    public const string FromItsStart = "session_date >= COALESCE((SELECT MAX(mark.session_date) FROM session_mark AS mark WHERE mark.ticker = $ticker AND mark.kind = 'starts here'), '')";
}
