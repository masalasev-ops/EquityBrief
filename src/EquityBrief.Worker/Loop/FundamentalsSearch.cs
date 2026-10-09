using System.Globalization;
using EquityBrief.Core.Configuration;
using EquityBrief.Core.Families;
using EquityBrief.Core.Sweep;
using EquityBrief.Core.Time;
using EquityBrief.Worker.Cards;
using EquityBrief.Worker.Sweep;

namespace EquityBrief.Worker.Loop;

// What one setting of the fundamentals-first family's search came to over the history: its trades with an edge, the
// mean edge after each trade's round trip against the same plan on every member that session, each year's mean edge
// and trades, the years it stood above nothing and of the last three how many, and whether it passed.
public sealed record FundamentalsFigures(FundamentalsSetting Setting, int Trades, double? Edge, IReadOnlyList<(int Year, int Trades, double? Edge)> Years, int YearsBetter, int RecentBetter, bool Passed);

// The fundamentals-first family's search on one index by hand: the 27 settings registered before it ran, each the
// pullback's base listings over the history as it stood, kept where the index's floors and the family's parts pass on
// the listing's session, the business read from the facts filed before it as first filed, walked five a night with one
// open trade a stock under the pullback's own exit, each trade after its round trip and against the same plan on every
// member that session. A setting passes the family floors, its trades and the years it stands above nothing, and the
// ideas' test of the latest years; the report states what luck alone passes of 27. It reads the store and writes
// nothing, and nothing it finds is frozen or registered.
// see: The fundamentals-first family buys an improving business in an uptrend at the pullback's buy point
// see: No family on any index is set aside or hidden by a test result without the operator's word
public static class FundamentalsSearch
{
    public const string Verb = "sweep-fundamentals";

    // A setting's figures over the walked trades, the years from the first scored.
    public static FundamentalsFigures Figures(FundamentalsSetting setting, IReadOnlyList<(DateOnly Entry, double Edge)> trades)
    {
        var years = Enumerable.Range(0, SweepFigures.Years)
            .Select(at =>
            {
                var year = SweepColumns.FirstScored.Year + at;
                var own = trades.Where(trade => trade.Entry.Year == year).Select(trade => trade.Edge).ToArray();

                return (Year: year, Trades: own.Length, Edge: own.Length > 0 ? own.Average() : default(double?));
            })
            .ToArray();
        var better = years.Count(year => year.Edge > 0);
        var recent = years.TakeLast(SweepIdeas.RecentYears).Count(year => year.Edge > 0);
        var passed = trades.Count >= FamilySweep.TradeFloor && better >= FamilySweep.YearsBeating && recent >= SweepIdeas.RecentYearsBetter;

        return new FundamentalsFigures(setting, trades.Count, trades.Count > 0 ? trades.Average(trade => trade.Edge) : null, years, better, recent, passed);
    }

    // What luck alone passes of the settings read: each the share of the ways eight years can fall that pass the yearly test.
    public static double Luck(int settings) => settings * SweepIdeas.LuckPatterns() / Math.Pow(2, SweepFigures.Years);

    public static async Task<int> RunAsync(IClock clock, string databaseFile, string dataRoot, string index, TextWriter output, CancellationToken cancellation = default)
    {
        if (!WalkForwardTester.Indices.Contains(index, StringComparer.Ordinal))
        {
            output.WriteLine($"{Verb}: name an index with '--index', GSPC, MID or SML");

            return 2;
        }

        if (NightLock.Holder(dataRoot) is { } holder)
        {
            output.WriteLine($"{Verb}: the night holds the store ({holder}); run it once the night has finished");

            return 2;
        }

        if (SweepRunner.InTheNightsWindow(clock.UtcNow, WalkForwardTester.Expected))
        {
            output.WriteLine(FormattableString.Invariant($"{Verb}: a run started now would reach the night's window, which begins at {SweepRunner.PauseFrom:hh\\:mm} UTC on a weekday; run it after the night"));

            return 2;
        }

        var history = new SweepHistory(databaseFile);
        var through = await history.NewestSessionAsync(cancellation);
        var large = index == WalkForwardTester.LargeIndex;
        var inputs = large
            ? await history.ReadAsync(through, output.WriteLine, cancellation)
            : await history.ReadAsync(through, output.WriteLine, cancellation, index: index, asItStood: true);

        if (inputs.Names.Count == 0)
        {
            output.WriteLine($"{Verb}: the store holds no member of the {DecisionCards.NameOf(index)}; pull them first with 'history-pull --members --index {index}', then their history");

            return 2;
        }

        var read = await WalkForwardTester.ReadAsync(index, history, through, inputs, cancellation);
        var rule = RuleWalk.Pullback(read, await history.MarketAsync(through, cancellation), output.WriteLine);
        var readings = await LoopReadings.ReadAsync(read, databaseFile, through, cancellation);

        output.WriteLine(FormattableString.Invariant($"{Verb}: the fundamentals-first family on the {DecisionCards.NameOf(index)} over {rule.Listings.Count} pullback listing(s) through {through:yyyy-MM-dd}, {FundamentalsRule.Grid.Count} settings registered before the run"));

        var figures = new List<FundamentalsFigures>();

        foreach (var setting in FundamentalsRule.Grid)
        {
            bool Keeps(int at)
            {
                var listing = rule.Listings[at];
                var ticker = read.Ticker(listing.Name);

                return FundamentalsRule.FailsOn(setting, readings.Of(listing.Name, listing.Bar, listing.Session), read.Income.GetValueOrDefault(ticker) ?? [], read.Calendar[listing.Session]) is null;
            }

            IReadOnlyList<(DateOnly, double)> trades = [.. rule.Keeping(Keeps).Where(one => one.Edge is not null).Select(one => (read.Calendar[one.Entry], one.Edge!.Value))];

            figures.Add(Figures(setting, trades));
        }

        foreach (var one in figures)
        {
            output.WriteLine(Line(one));
        }

        var passing = figures.Where(one => one.Passed).ToArray();

        output.WriteLine(FormattableString.Invariant($"{Verb}: {passing.Length} of {figures.Count} setting(s) passed the family floors, {FamilySweep.TradeFloor} trades and {FamilySweep.YearsBeating} of {SweepFigures.Years} years above nothing with {SweepIdeas.RecentYearsBetter} of the last {SweepIdeas.RecentYears}; luck alone passes about {Luck(figures.Count).ToString("0.0", CultureInfo.InvariantCulture)} of {figures.Count}; nothing is frozen or registered"));

        return 0;
    }

    // A setting's line: its key, its trades and mean edge, its years above nothing, and each year's edge.
    public static string Line(FundamentalsFigures one) =>
        FormattableString.Invariant($"  {one.Setting.Key}: {(one.Passed ? "passed" : "did not pass")}; {one.Trades} trade(s), a mean edge of {(one.Edge is { } edge ? edge.ToString("+0.000;-0.000", CultureInfo.InvariantCulture) : "none")} risks; better in {one.YearsBetter} of {SweepFigures.Years} years, {one.RecentBetter} of the last {SweepIdeas.RecentYears}; ")
        + string.Join(", ", one.Years.Select(year => FormattableString.Invariant($"{year.Year} {(year.Edge is { } held ? held.ToString("+0.00;-0.00", CultureInfo.InvariantCulture) : "none")} over {year.Trades}")));
}
