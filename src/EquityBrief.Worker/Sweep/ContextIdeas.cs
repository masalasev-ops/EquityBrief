using EquityBrief.Core.Families;
using EquityBrief.Core.Sweep;

namespace EquityBrief.Worker.Sweep;

// One context idea read on a frozen family: its key and rule, its figures, its test against the family as frozen,
// and for an idea reading the revenue, how many of its trades read a growth and how many of those a quarter first
// filed after their buy.
public sealed record ContextReading(string Key, string Rule, IdeaFigures Figures, IdeaTest Test, int RevenueRead = 0, int FiledAfterTheBuy = 0);

// What the context run read: the drift as frozen, how many of its listings read a revenue growth, and its two ideas;
// and the pullback's base, its idea, and the base's first three a night in the list's own order beside it.
public sealed record ContextIdeasRead(
    string DriftFrozen,
    IdeaFigures DriftBase,
    int DriftListings,
    int DriftListingsWithRevenue,
    IReadOnlyList<ContextReading> Drift,
    IdeaFigures PullbackBase,
    IReadOnlyList<ContextReading> Pullback,
    IdeaFigures PullbackFirstThree)
{
    public int Passes => Drift.Count(one => one.Test.Passes) + Pullback.Count(one => one.Test.Passes);

    // What luck alone passes of a family's tries: an idea with no effect is better in at least six of eight years with
    // two of the last three in 34 of the 256 patterns of years, before the other tests.
    public static double Luck(int tries) => 1.0 * tries * SweepIdeas.LuckPatterns() / (1 << SweepFigures.Years);
}

// The context checks over the history, each added alone to its family as frozen and judged against it, and nothing
// they show frozen or registered: the drift's revenue growth as a filter and as an order, judged by the year tests,
// the test without the five largest and a thousand trades with no floor of nights; and the pullback's order by how
// far its RSI fell from its high session, its first three kept, judged by the pullback's own test. No model reads
// any of it, and no news is read.
// see: The context checks are read on the frozen families one at a time, and nothing they show is frozen or registered
// see: A drift print's revenue growth is its quarter's revenue as first filed against the same quarter a year before
// see: The drift's context ideas are judged by the year tests, the test without the five largest and a thousand trades with no floor of nights
// see: The pullback's RSI-fall order keeps the night's first three by how far its RSI fell from its high session
public static class ContextIdeas
{
    public static IReadOnlyList<FamilyIdea> Drift { get; } =
    [
        new("r1", "only a print whose quarter's revenue grew on the same quarter a year before, both as first filed", IdeaKind.Selection, IdeaExit.Base, FamilySweep.PerNight, null, false, RevenueUse.Grew),
        new("r2", "only the night's first three, the largest growth of the print's quarter's revenue first", IdeaKind.Selection, IdeaExit.Base, SweepIdeas.BestOf, null, false, RevenueUse.Order),
    ];

    public const string PullbackKey = "o1";

    public const string PullbackRule = "only the night's first three, the furthest the RSI fell from the pullback's high session to the night first";

    public static IdeaRule PullbackOrder { get; } = SweepIdeas.BaseRule with { PerNight = SweepIdeas.BestOf, Order = IdeaOrder.RsiFall };

    public static IdeaRule PullbackFirstThree { get; } = SweepIdeas.BaseRule with { PerNight = SweepIdeas.BestOf };

    // A drift idea against the drift as frozen, held to no floor of nights.
    public static IdeaTest DriftTest(IdeaFigures idea, IdeaFigures frozen) => SweepIdeas.Test(idea, frozen, onTotals: false, nightFloor: false);

    // The pullback's idea against its base, by the pullback's own test.
    public static IdeaTest PullbackTest(IdeaFigures idea, IdeaFigures asBase) => SweepIdeas.Test(idea, asBase, onTotals: false);

    // The drift as frozen and its two ideas over the history, the market series and each name's revenue as filed.
    public static (string Frozen, IdeaFigures Base, int Listings, int WithRevenue, IReadOnlyList<ContextReading> Ideas) ReadDrift(
        SweepHistoryInputs inputs,
        IReadOnlyList<SweepMarketSeries> market,
        IReadOnlyDictionary<string, IReadOnlyList<FiledRevenue>> revenue,
        Action<string>? progress = null)
    {
        var calendar = inputs.Sessions;
        var sessionAt = calendar.Select((session, index) => (session, index)).ToDictionary(pair => pair.session, pair => pair.index);
        var firstScored = Array.FindIndex(calendar, session => session >= SweepColumns.FirstScored);
        var nights = calendar.Length - firstScored;
        var series = new SweepSeries[inputs.Names.Count];

        Parallel.For(0, inputs.Names.Count, name => series[name] = SweepColumns.Series(inputs.Names[name], sessionAt));

        var sessions = SweepColumns.Sessions(series, calendar);
        var members = SweepBenchmark.On(series, calendar.Length);

        int YearOf(int session) => calendar[session].Year - SweepColumns.FirstScored.Year;

        progress?.Invoke("reading the drift's listings");

        var adapter = FamilySweepRunner.For(DriftRule.Name, series, sessions, members, firstScored, calendar);
        var frozen = FamilyIdeas.Frozen(DriftRule.Name, adapter.Grid);
        var breadth = SweepIdeas.BreadthAboveTheSlowAverage(series, members, calendar.Length);
        var (highs, lows) = SweepIdeas.HighsAndLows(series, members, calendar.Length);
        var index = SweepIdeas.OnCalendar(market.FirstOrDefault(one => one.Series == "GSPC"), calendar);
        var vix = SweepIdeas.OnCalendar(market.FirstOrDefault(one => one.Series == "VIX"), calendar);
        var switches = SweepIdeas.Switches(breadth, highs, lows, index, vix);
        var replay = new FamilyIdeaReplay(adapter, frozen, series, members, switches, YearOf, nights, calendar, revenue);
        var asFrozen = replay.Evaluate("as frozen", null);

        ContextReading[] ideas =
        [
            .. Drift.Select(idea =>
            {
                var figures = replay.Evaluate(idea.Key, idea);
                var read = replay.RevenueRead.GetValueOrDefault(idea.Key);

                return new ContextReading(idea.Key, idea.Rule, figures, DriftTest(figures, asFrozen), read.Read, read.FiledAfterTheBuy);
            }),
        ];

        return (adapter.Grid.Key(frozen), asFrozen, replay.Listings, replay.ListingsWithRevenue, ideas);
    }

    // The pullback's base and its idea over the history and the market series, with the base's first three a night
    // in the list's own order.
    public static (IdeaFigures Base, IReadOnlyList<ContextReading> Ideas, IdeaFigures FirstThree) ReadPullback(
        SweepHistoryInputs inputs,
        IReadOnlyList<SweepMarketSeries> market,
        Action<string>? progress = null)
    {
        var (replay, _) = SweepIdeasRunner.Read(inputs, market, progress);
        var asBase = replay.Evaluate("base", SweepIdeas.BaseRule);
        var figures = replay.Evaluate(PullbackKey, PullbackOrder);

        return (asBase, [new ContextReading(PullbackKey, PullbackRule, figures, PullbackTest(figures, asBase))], replay.Evaluate("c", PullbackFirstThree));
    }
}
