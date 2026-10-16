namespace EquityBrief.Core.Ledger;

// Where the open a trade is bought at stood against the plan its listing night's close set.
public static class FillPast
{
    public const string Stop = "stop";

    public const string Target = "target";
}

// One member of a benchmark's plan on the listing night: whether its path under the plan has ended, its close there, the
// risk its plan put up at that close, the close itself for a book's size cut, and the open of its next session, none
// where the store holds none.
public readonly record struct FillMember(bool Ended, double Close, double Risk, double? Open);

// A trade's fill: the session after its listing, the open it is bought at, where that open stood against its plan, and
// its result, benchmark and edge at the open in the risk the close set, with how many members the benchmark's move
// averaged.
public sealed record TradeFill(DateOnly On, double Fill, string? Past, double? Result, double? Benchmark, int Members, double? Edge);

// The one pricing function every trade's entry at the next session's open is worked out by: the fill step's over the
// stored trades, the ledger's, the rule records' replays, the monthly tests' and the next-open check's. The stop, the
// target, the trail, the cap and the exits stay where the close set them, and no exit reads the buy, so a trade bought
// at the open ends at the close it ended at bought at the close; its result at the open is that result less the gap
// over the risk the close set, which a position is sized from the night before. An open past the stop or the target is
// still bought and still ends where the plan ends, and says which it was. A book's holding states its result as a
// fraction of the sum its close put to it, so its gap is over that close. The benchmark moves the same way, each member
// by its own gap over its own risk.
// see: A trade's entry is priced at the next session's open by a fill step of its own, and no rule's version moves because of it
public static class TradeFills
{
    // What a page says of a trade whose next session is not stored yet.
    public const string Waiting = "buys at the next open";

    // Why a trade holds no fill.
    public const string NoNextSession = "no next session";

    public const string NoOpen = "no open on its next session";

    public const string NoRisk = "no risk at its close";

    public static string? PastOf(double open, double stop, double? target) =>
        open <= stop ? FillPast.Stop : target is { } aimed && open >= aimed ? FillPast.Target : null;

    // A result bought at the close restated bought at the open, over the risk the close set.
    public static double? AtOpen(double? atClose, double close, double risk, double open) =>
        atClose is { } result && risk > 0 ? result - ((open - close) / risk) : null;

    // The benchmark at the open: the benchmark at the close less the mean of its ended members' own gaps, each over its
    // own risk; none where no ended member holds an open, and the members it averaged.
    public static (double? Mean, int Members) Benchmark(double? atClose, IReadOnlyList<FillMember> members)
    {
        if (atClose is not { } mean || double.IsNaN(mean))
        {
            return (null, 0);
        }

        var sum = 0.0;
        var count = 0;

        foreach (var member in members)
        {
            if (member.Ended && member.Open is { } open && open > 0 && member.Risk > 0)
            {
                sum += (open - member.Close) / member.Risk;
                count++;
            }
        }

        return count > 0 ? (mean - (sum / count), count) : (null, 0);
    }

    // A swing trade's fill, none where the open is not held or the plan put up no risk. A plan whose stop is not stored
    // yet, a scored plan still open before its risk is written, is bought at the open with nothing read against its range
    // and no result until it is.
    public static TradeFill? Swing(DateOnly on, double close, double? stop, double? target, double? open, double? resultAtClose, double? benchmarkAtClose, IReadOnlyList<FillMember> members) =>
        stop is not { } floor
            ? open is { } price && price > 0 ? new TradeFill(on, price, null, null, null, 0, null) : null
            : Price(on, close, close - floor, PastOf(open ?? 0, floor, target), open, resultAtClose, benchmarkAtClose, members);

    // A book's holding's fill: no stop and no target to open past.
    public static TradeFill? Holding(DateOnly on, double close, double? open, double? resultAtClose, double? benchmarkAtClose, IReadOnlyList<FillMember> members) =>
        Price(on, close, close, null, open, resultAtClose, benchmarkAtClose, members);

    static TradeFill? Price(DateOnly on, double close, double risk, string? past, double? open, double? resultAtClose, double? benchmarkAtClose, IReadOnlyList<FillMember> members)
    {
        if (open is not { } price || !(price > 0) || !(risk > 0))
        {
            return null;
        }

        var result = AtOpen(resultAtClose, close, risk, price);
        var (benchmark, count) = Benchmark(benchmarkAtClose, members);

        return new TradeFill(on, price, past, result, benchmark, count, result is { } made && benchmark is { } against ? made - against : null);
    }
}
