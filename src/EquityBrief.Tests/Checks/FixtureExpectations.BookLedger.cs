using EquityBrief.Core.Families;
using EquityBrief.Core.Ledger;
using EquityBrief.Core.Prices;
using EquityBrief.Core.Readings;
using EquityBrief.Core.Sweep;
using EquityBrief.Worker.Indices;
using EquityBrief.Worker.Ledger;
using EquityBrief.Worker.Sweep;

namespace EquityBrief.Tests.Checks;

// fixture-expectations, 21.4: the two book families' setups in the ledger, worked by hand over a constructed history. A
// quality setup is laid for each member whose scorecard clears six in nine on a reshuffle session, the first of a
// calendar month, and on no other session; a Fundamentals setup for each member whose newest quarter's filing was first
// read on the session and leaves its scorecard at six in nine or better. Each is held as a book: the quality setup to the
// close of the first later reshuffle its scorecard fails the floor at, the Fundamentals setup to the close of the session a
// filing read anew leaves it under five in nine, either to its cap of a year, against every member of the index held the
// same way; and the point-in-time check rebuilds each with every reading the same.
// see: A book family's setup is each member a reshuffle or a filing leaves at the ledger's gate, held while it clears the hold and never past a year against an equal-weighted hold of the index
public partial class FixtureExpectations
{
    // Three hundred and forty weekdays from the first of July 2026: AAA closing at 100 plus its session's place, BBB at
    // 300 less it and CCC at 200 throughout, so a path's result and the members' mean over it are worked from the places.
    static (List<DateOnly> Calendar, SweepHistoryInputs Inputs, LedgerMarket Market) BookHistory()
    {
        var calendar = new List<DateOnly>();

        for (var day = new DateOnly(2026, 7, 1); calendar.Count < 340; day = day.AddDays(1))
        {
            if (day.DayOfWeek is not DayOfWeek.Saturday and not DayOfWeek.Sunday)
            {
                calendar.Add(day);
            }
        }

        SweepName Member(string ticker, Func<int, decimal> close) =>
            new(
                ticker,
                [.. calendar.Select((day, at) => new SweepBar(day, close(at) + 1, close(at) - 1, close(at), 1_000_000, close(at), close(at)))],
                [(new DateOnly(2020, 1, 2), null)],
                []);

        var inputs = new SweepHistoryInputs(calendar[^1], [.. calendar], [Member("AAA", at => 100 + at), Member("BBB", at => 300 - at), Member("CCC", _ => 200)], 0, 0, 0, 0, "constructed");
        var market = new LedgerMarket(new Dictionary<string, IReadOnlyList<(DateOnly Session, double Close)>>(StringComparer.Ordinal)
        {
            [LedgerMarket.Index] = [.. calendar.Select(day => (day, 4000.0))],
        });

        return (calendar, inputs, market);
    }

    // The filer every check passes on, filed as the scorecard's own tests state it, its newest quarter the six months to
    // 2026-06-30 filed on 2026-08-05; and the same filer with one more filing on 2026-11-05, the three months to
    // 2026-09-30 stating net income alone, after which no measure's four quarters can be read and its card clears nothing.
    static Dictionary<string, IReadOnlyList<FiledFactRow>> BookFacts() => new(StringComparer.Ordinal)
    {
        ["AAA"] = [.. NineFacts(), NineFact("NetIncomeLoss", new(2026, 7, 1), new(2026, 9, 30), 10m, new(2026, 11, 5))],
        ["BBB"] = NineFacts(),
    };

    // The scorecard's constructed filer for the night test over a store, which lives in another partial class.
    internal static IReadOnlyList<FiledFactRow> NineFactsForTheLedger() => NineFacts();

    [Fact]
    public void ABookFamilysSetupIsLaidAtAReshuffleOrAFilingAndHeldToTheFirstLaterOneItsGateFailsAtOrItsCapAgainstTheIndexHeldTheSameWay()
    {
        var (calendar, inputs, market) = BookHistory();
        var sessionAt = calendar.Select((day, at) => (day, at)).ToDictionary(pair => pair.day, pair => pair.at);
        var series = inputs.Names.Select(name => SweepColumns.Series(name, sessionAt)).ToArray();
        var sessions = SweepColumns.Sessions(series, inputs.Sessions);
        var members = SweepBenchmark.On(series, calendar.Count);
        var (highs, lows) = SweepIdeas.HighsAndLows(series, members, calendar.Count);
        var closes = series.Select(one => one.Bars.Select(bar => Statistic.FromPrice(bar.Close)).ToArray()).ToArray();
        var opens = series.Select(one => one.Bars.Select(bar => Statistic.FromPrice(bar.Open)).ToArray()).ToArray();
        var facts = BookFacts();

        LedgerContext Context(QualitySettings? quality = null, ImprovingSweepSetting? improving = null) =>
            new(NoIncome, NoSectors, highs, lows, facts)
            {
                Ranks = new FactorRanks(series, members),
                QualityLive = quality ?? QualityRule.Starting,
                ImprovingLive = improving ?? ImprovingGrid.Starting,
            };

        IReadOnlyList<FamilySetups> On(int at, LedgerContext context) => LedgerSetups.On(IndexFamilies.LargeIndex, series, sessions, members, calendar, at, market, context);

        // The reshuffles: the calendar's first session, then the first of each month, 2026-08-03 a Monday after the 31st
        // of July, 2026-09-01 a Tuesday after Monday the 31st of August, and not the session after either.
        var september = calendar.IndexOf(new DateOnly(2026, 9, 1));
        var december = calendar.IndexOf(new DateOnly(2026, 12, 1));

        Assert.True(BookSetups.Reshuffles(calendar, 0));
        Assert.True(BookSetups.Reshuffles(calendar, calendar.IndexOf(new DateOnly(2026, 8, 3))));
        Assert.True(BookSetups.Reshuffles(calendar, september));
        Assert.False(BookSetups.Reshuffles(calendar, calendar.IndexOf(new DateOnly(2026, 8, 4))));
        Assert.False(BookSetups.Reshuffles(calendar, september + 1));
        Assert.Equal((DayOfWeek.Tuesday, new DateOnly(2026, 8, 31)), (calendar[september].DayOfWeek, calendar[september - 1]));

        // On the September reshuffle AAA's and BBB's cards clear six in nine, nine of nine, and CCC, with no filer, clears
        // nothing: two quality setups, each bought at the session's close with no stop and the year's cap, in ticker order.
        var context = Context();
        var atSeptember = On(september, context);
        var quality = atSeptember.Single(family => family.Family == QualityRule.Name);

        Assert.Equal((9, 9), (context.Scorecard(series[0], calendar[september]).Passed, context.Scorecard(series[0], calendar[september]).Applied));
        Assert.Equal(["AAA", "BBB"], quality.Rows.Select(row => row.Ticker));
        Assert.Equal(3, quality.Members);
        Assert.All(quality.Rows, row => Assert.Equal((0.0, (double?)null, (double?)null, BookSetups.Cap, QualityRule.Starting.Words), (row.Anchor.Stop, row.Anchor.Target, row.Anchor.Trail, row.Anchor.Cap, row.Rule)));
        Assert.Equal(100.0 + september, quality.Rows[0].Anchor.Entry);
        Assert.Equal([.. LedgerSetups.Families], atSeptember.Select(family => family.Family));

        // The session after the reshuffle lays no quality setup and still counts the members.
        var afterReshuffle = On(september + 1, context).Single(family => family.Family == QualityRule.Name);

        Assert.Equal((0, 3), (afterReshuffle.Rows.Count, afterReshuffle.Members));

        // The live pass under the live rule's setting: the starting setting asks a valuation at or below the median, and
        // with no earnings history behind the close none is read, so it fails; with the valuation off it passes a nine of
        // nine; and with the trend on, the 200-session average not yet held, it fails.
        Assert.All(quality.Rows, row => Assert.False(row.LivePass));
        Assert.All(On(september, Context(quality: QualityRule.Starting with { Valuation = QualityValuation.Off })).Single(family => family.Family == QualityRule.Name).Rows, row => Assert.True(row.LivePass));
        Assert.All(On(september, Context(quality: QualityRule.Starting with { Valuation = QualityValuation.Off, Trend = true })).Single(family => family.Family == QualityRule.Name).Rows, row => Assert.False(row.LivePass));

        // AAA's path from the September reshuffle: held through October's and November's reshuffles, nine of nine at each,
        // and sold at the close of December's, where its filing of the fifth of November has left a card no measure's four
        // quarters can be read on; by hand, December's place less September's sessions on, the close 100 plus December's
        // place over 100 plus September's, less one, stored as a rebalance. The benchmark is the three members held the same
        // sessions: AAA's return, BBB's, 300 less December's place over 300 less September's less one, and CCC's nothing,
        // averaged. Each is bought at the next session's open, a point over AAA's close and a point under BBB's, so the
        // result and the members' mean at the open are the close's less those gaps over the buy, CCC's gap nothing.
        var cardAtDecember = context.Scorecard(series[0], calendar[december]);

        Assert.True(cardAtDecember.Applied < FundamentalScorecard.FewestRead);
        Assert.Equal((9, 9), (context.Scorecard(series[0], calendar[calendar.IndexOf(new DateOnly(2026, 11, 2))]).Passed, context.Scorecard(series[0], calendar[calendar.IndexOf(new DateOnly(2026, 11, 2))]).Applied));
        // Every later reshuffle fails it from December on, each listed, and the path ends at the first.
        var notHeld = LedgerSetups.BookNotHeld(QualityRule.Name, series[0], calendar, september, context);

        Assert.Equal(december, notHeld[0]);
        Assert.All(notHeld, later => Assert.True(BookSetups.Reshuffles(calendar, later) && later > december - 1 && later <= september + BookSetups.Cap));

        var benchmarks = new Dictionary<(int At, int Sessions), (BenchmarkReading Reading, IReadOnlyList<FillMember> Members)>();
        var sold = SetupLedger.BookRead(QualityRule.Name, series, 0, quality.Rows[0].Anchor, closes, opens, members, calendar, september, september, context, null, benchmarks);
        var held = december - september;
        var aaa = ((100.0 + december) / (100.0 + september)) - 1;
        var bbb = ((300.0 - december) / (300.0 - september)) - 1;
        var gapAaa = 1 / (100.0 + september);
        var gapBbb = -1 / (300.0 - september);

        Assert.Equal((held, SetupEnds.Rebalance), (sold.Outcome.Sessions, sold.Outcome.End));
        Assert.Equal(aaa - gapAaa, sold.Outcome.Result!.Value, 9);
        Assert.Equal(((aaa + bbb) / 3) - ((gapAaa + gapBbb) / 3), sold.Benchmark.Mean!.Value, 9);
        Assert.Equal((3, 3, true), (sold.Benchmark.Ended, sold.Benchmark.Entered, sold.Benchmark.Settled));
        Assert.Equal(100.0 + september + 1, sold.Fill!.Value);

        // BBB's card never fails, so its path ends at the cap, a year of sessions on, with the same members over it.
        Assert.Empty(LedgerSetups.BookNotHeld(QualityRule.Name, series[1], calendar, september, context));

        var capped = SetupLedger.BookRead(QualityRule.Name, series, 1, quality.Rows[1].Anchor, closes, opens, members, calendar, september, september, context, null, benchmarks);

        Assert.Equal((BookSetups.Cap, SetupEnds.Cap), (capped.Outcome.Sessions, capped.Outcome.End));
        Assert.Equal((((300.0 - september - BookSetups.Cap) / (300.0 - september)) - 1) - gapBbb, capped.Outcome.Result!.Value, 9);

        // A setup near the calendar's end is open, with no result and its benchmark unsettled: AAA's on the third session
        // from the end, no reshuffle between it and the end; and BBB, whose constructed close has fallen under nothing by
        // then, is a buy with no close and places no trade.
        var open = SetupLedger.BookRead(QualityRule.Name, series, 0, quality.Rows[0].Anchor with { Session = calendar[^3], Entry = closes[0][^3] }, closes, opens, members, calendar, calendar.Count - 3, calendar.Count - 3, context, null, benchmarks);

        Assert.Equal((null, SetupEnds.Open, false), (open.Outcome.Result, open.Outcome.End, open.Benchmark.Settled));
        Assert.True(closes[1][^3] < 0);
        Assert.Equal(SetupEnds.None, SetupLedger.BookRead(QualityRule.Name, series, 1, quality.Rows[1].Anchor with { Session = calendar[^3], Entry = closes[1][^3] }, closes, opens, members, calendar, calendar.Count - 3, calendar.Count - 3, context, null, benchmarks).Outcome.End);

        // The Fundamentals family: AAA's and BBB's filing of the fifth of August is first read on Thursday the sixth, the
        // session after it, where each card clears six in nine, so both are setups that session and neither the session
        // after; a filing on the session's own day is read on the next. CCC files nothing.
        var august = calendar.IndexOf(new DateOnly(2026, 8, 6));
        var improving = On(august, context).Single(family => family.Family == ImprovingRule.Name);

        Assert.Equal(DayOfWeek.Thursday, calendar[august].DayOfWeek);
        Assert.True(BookSetups.FiledAnew(context.FilingDays("AAA"), calendar, august));
        Assert.False(BookSetups.FiledAnew(context.FilingDays("AAA"), calendar, august + 1));
        Assert.False(BookSetups.FiledAnew([calendar[august]], calendar, august));
        Assert.True(BookSetups.FiledAnew([calendar[august]], calendar, august + 1));
        Assert.False(BookSetups.FiledAnew([calendar[0]], calendar, 0));
        Assert.Equal(["AAA", "BBB"], improving.Rows.Select(row => row.Ticker));
        Assert.Empty(On(august + 1, context).Single(family => family.Family == ImprovingRule.Name).Rows);
        Assert.Empty(On(august - 1, context).Single(family => family.Family == ImprovingRule.Name).Rows);
        Assert.All(improving.Rows, row => Assert.Equal((0.0, BookSetups.Cap, ImprovingGrid.Starting.Words), (row.Anchor.Stop, row.Anchor.Cap, row.Rule)));

        // Its live pass under a setting asking a score of eight alone passes a nine of nine; one asking a flag first fails
        // where no release is read.
        Assert.All(On(august, Context(improving: new ImprovingSweepSetting(8, false, false, false, 7, 126))).Single(family => family.Family == ImprovingRule.Name).Rows, row => Assert.True(row.LivePass));
        Assert.All(On(august, Context(improving: new ImprovingSweepSetting(8, true, false, false, 7, 126))).Single(family => family.Family == ImprovingRule.Name).Rows, row => Assert.False(row.LivePass));

        // AAA's path from the sixth of August: its filing of the fifth of November is first read on Friday the sixth,
        // where its card clears nothing, under five in nine, so it is sold at that close, stored as a filing; BBB files
        // nothing more and runs to the cap.
        var november = calendar.IndexOf(new DateOnly(2026, 11, 6));

        Assert.Equal(DayOfWeek.Friday, calendar[november].DayOfWeek);
        Assert.Equal([november], LedgerSetups.BookNotHeld(ImprovingRule.Name, series[0], calendar, august, context));
        Assert.Empty(LedgerSetups.BookNotHeld(ImprovingRule.Name, series[1], calendar, august, context));

        var filed = SetupLedger.BookRead(ImprovingRule.Name, series, 0, improving.Rows[0].Anchor, closes, opens, members, calendar, august, august, context, null, benchmarks);

        Assert.Equal((november - august, SetupEnds.Filing), (filed.Outcome.Sessions, filed.Outcome.End));
        Assert.Equal((((100.0 + november) / (100.0 + august)) - 1) - (1 / (100.0 + august)), filed.Outcome.Result!.Value, 9);
        Assert.Equal(SetupEnds.Cap, SetupLedger.BookRead(ImprovingRule.Name, series, 1, improving.Rows[1].Anchor, closes, opens, members, calendar, august, august, context, null, benchmarks).Outcome.End);

        // The point-in-time check rebuilds each book setup from the history cut at its session with every reading the same.
        foreach (var (family, at, row) in new[] { (QualityRule.Name, september, quality.Rows[0]), (ImprovingRule.Name, august, improving.Rows[0]) })
        {
            var (rebuilt, readings) = LedgerPointInTime.Setup(IndexFamilies.LargeIndex, LedgerPointInTime.Cut(inputs, calendar[at]), LedgerPointInTime.Cut(market, calendar[at]), NoIncome, NoSectors, facts, family, "AAA");

            Assert.True(rebuilt);
            Assert.Empty(LedgerPointInTime.Differences(row.Readings, readings!));
        }

        // CCC, with no filer, is no book setup on any of those sessions.
        Assert.DoesNotContain("CCC", atSeptember.SelectMany(family => family.Rows).Select(row => row.Ticker).Where(ticker => ticker == "CCC"));
        Assert.Equal((false, (double?[]?)null), LedgerPointInTime.Setup(IndexFamilies.LargeIndex, LedgerPointInTime.Cut(inputs, calendar[september]), market, NoIncome, NoSectors, facts, QualityRule.Name, "CCC") is var (heldCcc, readingsCcc) ? (heldCcc && readingsCcc is not null, readingsCcc) : default);
        Assert.Equal([QualityRule.Name, ImprovingRule.Name], BookSetups.Families);
        Assert.True(BookSetups.IsBook(HeavyweightRule.Name));
        Assert.False(BookSetups.IsBook(DipRule.Name));
        Assert.Equal(SetupEnds.Filing, HeavyweightPaths.Replay([100, 101, 102], 0, [1], sold: SetupEnds.Filing).End);
    }
}
