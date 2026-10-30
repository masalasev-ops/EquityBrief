using EquityBrief.Core.Insiders;
using EquityBrief.Core.Ledger;
using EquityBrief.Core.Prices;
using EquityBrief.Core.Readings;
using EquityBrief.Core.Sweep;
using EquityBrief.Tests.Harness;
using EquityBrief.Worker.Ledger;
using EquityBrief.Worker.Sweep;

namespace EquityBrief.Tests.Checks;

// fixture-expectations, 21.4: the five readings the operator chose at 21.2, worked by hand at a constructed session
// through the ledger's one readings function, each at a figure and none where its source holds nothing for the session.
// see: Phase 21 makes the existing families choose better and the monthly loop test every family on every index, built to completion before any new work with no model judging a stock and nothing retired without the operator's word
public partial class FixtureExpectations
{
    // The row 21.4's catalogue part adds that this check reaches: section 17's row for the five readings.
    internal static readonly string[] ChosenReadingClaims =
    [
        CheckReach.Key(Scope.LimitsTable, "The readings the operator chose"),
    ];

    [Fact]
    public void TheFiveReadingsTheOperatorChoseAreWorkedByHandAtAConstructedSessionAndNoneWhereTheirSourceHoldsNothing()
    {
        // Three hundred and twenty sessions of one member whose close saws from 100 up to 149 and back every fifty, over
        // an index series flat at 100 from the day it starts, so the close over the index is the close itself over a hundred
        // and the relative strength's high is the close over the highest close of the 252 sessions to it.
        var calendar = Enumerable.Range(0, 320).Select(at => new DateOnly(2025, 1, 6).AddDays(at)).ToArray();
        var bars = calendar.Select((session, at) =>
        {
            var close = 100m + (at % 50);

            return new SweepBar(session, close + 1m, close - 1m, close, 1_000_000, close, close);
        }).ToArray();
        var sessionAt = calendar.Select((session, at) => (session, at)).ToDictionary(pair => pair.session, pair => pair.at);
        var series = new[] { SweepColumns.Series(new SweepName("AAA", bars, [(null, null)], [calendar[70]]), sessionAt) };
        var sessions = SweepColumns.Sessions(series, calendar);
        var members = SweepBenchmark.On(series, calendar.Length);
        var closes = series[0].Bars.Select(one => Statistic.FromPrice(one.Close)).ToArray();
        var (highs, lows) = SweepIdeas.HighsAndLows(series, members, calendar.Length);

        LedgerMarket Market(int from) => new(new Dictionary<string, IReadOnlyList<(DateOnly, double)>>(StringComparer.Ordinal)
        {
            [LedgerMarket.Index] = [.. calendar.Skip(from).Select(session => (session, 100.0))],
        });

        double?[] Read(
            int bar,
            LedgerMarket market,
            IReadOnlyDictionary<string, IReadOnlyList<InsiderRow>>? insiders = null,
            InsiderCoverage? spans = null,
            IReadOnlyDictionary<string, LedgerActions>? actions = null,
            IReadOnlyDictionary<string, IReadOnlyList<LedgerTrend>>? trends = null,
            IReadOnlyDictionary<string, IReadOnlyList<LedgerShortInterest>>? shortInterest = null,
            IReadOnlyDictionary<string, IReadOnlyList<FiledFactRow>>? facts = null) =>
            LedgerSetups.Readings("GSPC", 0, series[0], closes, bar, sessions[bar], members, calendar, bar, market,
                new LedgerContext(new Dictionary<string, IReadOnlyList<FiledIncome>>(), new Dictionary<string, string?>(), highs, lows, facts)
                {
                    Insiders = insiders,
                    InsiderSpans = spans ?? InsiderCoverage.None,
                    Actions = actions,
                    Trends = trends,
                    ShortInterest = shortInterest,
                });

        static double? Of(double?[] readings, string column) => readings[LedgerReadings.IndexOf(column)];

        const int At = 299;
        var session = calendar[At];

        // The insiders: three distinct buyers filed inside the 21 sessions to the session, the oldest on the window's first
        // day and one of them twice; a fourth filed the day before the window, a seller inside it and a buyer filed on the
        // session itself, which a reading of the session does not see.
        InsiderRow Filing(string owner, int filedBefore, string code) =>
            new($"f-{owner}-{filedBefore}", calendar[At - filedBefore], "4", null, null, owner, "An insider", false, true, "Officer", false, 1,
                calendar[At - filedBefore - 1], code, 100m, 50m, code == InsiderReadings.Purchase, null, true);

        var insiders = new Dictionary<string, IReadOnlyList<InsiderRow>>(StringComparer.Ordinal)
        {
            ["AAA"] =
            [
                Filing("1001", 5, InsiderReadings.Purchase),
                Filing("1001", 2, InsiderReadings.Purchase),
                Filing("1002", 10, InsiderReadings.Purchase),
                Filing("1003", 20, InsiderReadings.Purchase),
                Filing("1004", 21, InsiderReadings.Purchase),
                Filing("1005", 3, InsiderReadings.Sale),
                Filing("1006", 0, InsiderReadings.Purchase),
            ],
        };
        var spans = new InsiderCoverage([(calendar[0], calendar[^1])]);

        // Yahoo's answer asked on the session: two upgrades inside the 21 sessions before it, the older on the window's
        // first day, one upgrade the day before the window, a downgrade inside it, a downgrade dated on the session itself,
        // which the window ends before, and a word that is neither.
        var actions = new Dictionary<string, LedgerActions>(StringComparer.Ordinal)
        {
            ["AAA"] = new(session,
            [
                (calendar[At - 1], LedgerContext.Upgrade),
                (calendar[At - 21], LedgerContext.Upgrade),
                (calendar[At - 22], LedgerContext.Upgrade),
                (calendar[At - 7], LedgerContext.Downgrade),
                (session, LedgerContext.Downgrade),
                (calendar[At - 3], "main"),
            ]),
        };

        // Three quarters fetches: the newest on or before the session states 1.10 against 1.00 thirty days before, and
        // the one after the session is not read.
        var trends = new Dictionary<string, IReadOnlyList<LedgerTrend>>(StringComparer.Ordinal)
        {
            ["AAA"] = [new(calendar[At - 40], 2.00m, 1.60m), new(calendar[At - 3], 1.10m, 1.00m), new(calendar[At + 1], 5m, 1m)],
        };

        // FINRA's newest report before the session, a million shares short, over the ten million the newest cover filed
        // before it states; a report settled on the session is not yet read.
        var shortInterest = new Dictionary<string, IReadOnlyList<LedgerShortInterest>>(StringComparer.Ordinal)
        {
            ["AAA"] = [new(calendar[At - 10], 1_000_000m), new(session, 9m)],
        };
        var facts = new Dictionary<string, IReadOnlyList<FiledFactRow>>(StringComparer.Ordinal)
        {
            ["AAA"] = [new FiledFactRow(FiledFacts.SharesOutstanding[0], calendar[At - 30], calendar[At - 30], 10_000_000m, calendar[At - 25], "10-Q", "q-1")],
        };

        var whole = Read(At, Market(0), insiders, spans, actions, trends, shortInterest, facts);

        // By hand: the close at the session is 149, the highest of its fifty, so the ratio is at its high of the 252 to it;
        // three buyers; two upgrades less one downgrade; 1.10 over 1.00 less one; a million over ten million.
        Assert.Equal(149m, bars[At].Close);
        Assert.Equal(1.0, Of(whole, "relative_strength_high"));
        Assert.Equal(3.0, Of(whole, "insider_cluster_buyers"));
        Assert.Equal(1.0, Of(whole, "rating_actions_net"));
        Assert.Equal(0.1, Of(whole, "estimate_revision_month")!.Value, 12);
        Assert.Equal(0.1, Of(whole, "short_interest_share")!.Value, 12);

        // Under a high: at the 281st session the close is 130 and the highest of the 252 to it is 149; at the 252nd the
        // first figure, 101 over 149, and none one session before it.
        Assert.Equal(130m, bars[280].Close);
        Assert.Equal(130.0 / 149.0, Of(Read(280, Market(0)), "relative_strength_high")!.Value, 12);
        Assert.Equal(101.0 / 149.0, Of(Read(251, Market(0)), "relative_strength_high")!.Value, 12);
        Assert.Null(Of(Read(250, Market(0)), "relative_strength_high"));

        // An index series starting on the 41st session: none while the 252 to the session reach a day it lacks, and the
        // first figure once they do not, 141 over 149.
        Assert.Null(Of(Read(290, Market(40)), "relative_strength_high"));
        Assert.Equal(141m, bars[291].Close);
        Assert.Equal(141.0 / 149.0, Of(Read(291, Market(40)), "relative_strength_high")!.Value, 12);

        // Each none where its source holds nothing: no index series, no insiders, no answer, no fetch and no report.
        var none = Read(At, LedgerMarket.None);

        Assert.All(
            new[] { "relative_strength_high", "insider_cluster_buyers", "rating_actions_net", "estimate_revision_month", "short_interest_share" },
            column => Assert.Null(Of(none, column)));

        // And none where the source holds something but not for the session: an insider history not holding the window's
        // days, an answer asked before the session, a first fetch after it or one stating no earlier figure, and a report
        // with no cover to read the count from.
        Assert.Null(Of(Read(At, Market(0), insiders, new InsiderCoverage([(calendar[0], calendar[At - 10])])), "insider_cluster_buyers"));
        Assert.Null(Of(Read(At, Market(0), actions: new Dictionary<string, LedgerActions>(StringComparer.Ordinal) { ["AAA"] = actions["AAA"] with { AskedOn = calendar[At - 1] } }), "rating_actions_net"));
        Assert.Null(Of(Read(At, Market(0), trends: new Dictionary<string, IReadOnlyList<LedgerTrend>>(StringComparer.Ordinal) { ["AAA"] = [new(calendar[At + 1], 1.10m, 1.00m)] }), "estimate_revision_month"));
        Assert.Null(Of(Read(At, Market(0), trends: new Dictionary<string, IReadOnlyList<LedgerTrend>>(StringComparer.Ordinal) { ["AAA"] = [new(calendar[At - 3], 1.10m, null)] }), "estimate_revision_month"));
        Assert.Null(Of(Read(At, Market(0), shortInterest: shortInterest), "short_interest_share"));
    }
}
