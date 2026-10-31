using System.Globalization;
using EquityBrief.Core.Families;
using EquityBrief.Core.Prices;
using EquityBrief.Core.Time;
using EquityBrief.Tests.Harness;
using EquityBrief.Worker.Dips;
using EquityBrief.Worker.Families;
using EquityBrief.Worker.Indices;
using Xunit;

namespace EquityBrief.Tests.Checks;

// The short-term dip, worked by hand: its 2-session relative strength index, each gate on both sides of its threshold and
// exactly at it, the report window's edges, its exits at the next open and its time limit's close, its order, and its
// night over a constructed S&P 400 giving way to every other family's list and open trades.
// see: The dip lists last among the swing families on every index and gives way to every other family's list and open trades
public partial class FixtureExpectations
{
    // The rows 22.1 adds that this check reaches: section 17's settings and section 18's four.
    internal static readonly string[] DipClaims =
    [
        CheckReach.Key(Scope.LimitsTable, "The short-term dip"),
        CheckReach.Key(Scope.FailureTable, "A member short of the dip's 200 sessions"),
        CheckReach.Key(Scope.FailureTable, "A dip member with no typical move"),
        CheckReach.Key(Scope.FailureTable, "A dip member with no report date on file"),
        CheckReach.Key(Scope.FailureTable, "No next session stored for a dip trade"),
    ];

    [Fact]
    public void TheDipsTwoSessionRsiIsWildersWorkedByHand()
    {
        // Changes of +1, -0.5, -0.5 and -1: the seed's mean gain 0.5 and loss 0.25 read 66.67; smoothed by two, a gain of
        // 0.25 and a loss of 0.375 read 40; then 0.125 and 0.6875 read 15.38.
        var rsi = DipRule.Rsi([10, 11, 10.5, 10, 9]);

        Assert.Null(rsi[0]);
        Assert.Null(rsi[1]);
        Assert.Equal(100 - (100 / 3.0), rsi[2]!.Value, 9);
        Assert.Equal(40.0, rsi[3]!.Value, 9);
        Assert.Equal(100 - (100 / (1 + (0.125 / 0.6875))), rsi[4]!.Value, 9);

        // The same worked figures as the fixture states them, derived by hand and not from a run.
        var expected = Expected("dips");

        Assert.Equal("derived", expected.GetProperty("derivation").GetString());
        Assert.StartsWith("22.1's rule and walk worked by hand", expected.GetProperty("derivedFrom").GetString(), StringComparison.Ordinal);

        var worked = expected.GetProperty("rsi");
        var read = DipRule.Rsi([.. worked.GetProperty("closes").EnumerateArray().Select(close => close.GetDouble())]);
        var stated = worked.GetProperty("values").EnumerateArray().ToArray();

        Assert.Equal(stated.Length, read.Length);

        for (var at = 0; at < stated.Length; at++)
        {
            if (stated[at].ValueKind == System.Text.Json.JsonValueKind.Null)
            {
                Assert.Null(read[at]);
            }
            else
            {
                Assert.Equal(stated[at].GetDouble(), read[at]!.Value, 9);
            }
        }

        // A run no loss has touched reads 100, and a run too short for the window reads none.
        Assert.Equal(100.0, DipRule.Rsi([1, 2, 3])[2]);
        Assert.All(DipRule.Rsi([1, 2]), value => Assert.Null(value));
    }

    // 199 closes of 100 and the night's own: its 200-session average is (19,900 + close) / 200.
    static double[] FlatThen(double close) => [.. Enumerable.Repeat(100.0, 199), close];

    static DipMember Member(IReadOnlyList<double> closes, double? move = 1.5, bool floors = true, IReadOnlyList<int>? reports = null) =>
        new("DX", closes, move, floors, "under the floors", reports ?? [], null, DipRule.NoMarket);

    static bool Passes(DipAnswer answer, string gate) => answer.Gates.Single(one => one.Name == gate).Passed;

    [Fact]
    public void EachOfTheDipsGatesIsWorkedByHandOnBothSidesOfItsThresholdAndExactlyAtIt()
    {
        var any = DipRule.Provisional with { RsiUnder = 101 };

        // The trend: the close above its 200-session average. At 100 the average is 100 and the close is not above it.
        Assert.False(Passes(DipRule.Read(Member(FlatThen(100)), any), DipRule.TrendGate));
        Assert.True(Passes(DipRule.Read(Member(FlatThen(100.5)), any), DipRule.TrendGate));
        Assert.False(Passes(DipRule.Read(Member(FlatThen(99.5)), any), DipRule.TrendGate));

        // The history: 199 closes hold no 200-session average, 200 do.
        var short199 = DipRule.Read(Member([.. Enumerable.Repeat(100.0, 198), 101]), any);
        Assert.False(Passes(short199, DipRule.HistoryGate));
        Assert.Equal(DipRule.ShortHistory, short199.Reason);
        Assert.True(Passes(DipRule.Read(Member(FlatThen(101)), any), DipRule.HistoryGate));

        // The drop: 200 closes of 10, then 11, 10.5, 10 and 9. Flat changes hold no gain or loss, so the night reads a gain
        // of 0.0625 against a loss of 0.6875, an RSI of 8.33; the floor at that figure exactly is not passed.
        double[] drop = [.. Enumerable.Repeat(10.0, 200), 11, 10.5, 10, 9];
        var rsi = DipRule.Rsi(drop)[^1]!.Value;

        Assert.Equal(100 - (100 / (1 + (0.0625 / 0.6875))), rsi, 9);
        Assert.True(Passes(DipRule.Read(Member(drop), DipRule.Provisional), DipRule.DropGate));
        Assert.False(Passes(DipRule.Read(Member(drop), DipRule.Provisional with { RsiUnder = rsi }), DipRule.DropGate));
        Assert.True(Passes(DipRule.Read(Member(drop), DipRule.Provisional with { RsiUnder = rsi + 0.001 }), DipRule.DropGate));
        Assert.False(Passes(DipRule.Read(Member(drop), DipRule.Provisional with { RsiUnder = rsi - 0.001 }), DipRule.DropGate));

        // The typical move: none and nought place no stop; any above nought does.
        Assert.False(Passes(DipRule.Read(Member(FlatThen(101), move: null), any), DipRule.MoveGate));
        Assert.False(Passes(DipRule.Read(Member(FlatThen(101), move: 0), any), DipRule.MoveGate));
        Assert.True(Passes(DipRule.Read(Member(FlatThen(101), move: 0.01), any), DipRule.MoveGate));

        // The floors as the index families read them, and the market setting off passing every member.
        var under = DipRule.Read(Member(FlatThen(101), floors: false), any);
        Assert.Equal((false, DipRule.UnderTheFloors), (Passes(under, DipRule.FloorsGate), under.Reason));
        Assert.True(Passes(under, DipRule.MarketGate));

        // A member passing every gate is bought at its close with its stop four typical moves under it and its time limit.
        var passed = DipRule.Read(Member(FlatThen(101), move: 1.5), any);
        Assert.Equal((true, 101m, 95m, 10), (passed.Passed, passed.Entry, passed.Stop, passed.TimeLimit));
    }

    [Fact]
    public void TheDipRefusesAReportInTheDropOrDueOnTheTimeLimitsLastSessionAndNotOneDueTheSessionAfter()
    {
        var setting = DipRule.Provisional with { RsiUnder = 101 };

        bool Refused(int sessions) => !Passes(DipRule.Read(Member(FlatThen(101), reports: [sessions]), setting), DipRule.ReportGate);

        // The three sessions to the listing are the night and the two before it; the time limit's last is the 10th after.
        Assert.False(Refused(-3));
        Assert.True(Refused(-2));
        Assert.True(Refused(0));
        Assert.True(Refused(1));
        Assert.True(Refused(10));
        Assert.False(Refused(11));

        // The edges as the fixture states them.
        var edges = Expected("dips").GetProperty("reportEdges");

        Assert.All(edges.GetProperty("refused").EnumerateArray(), offset => Assert.True(Refused(offset.GetInt32()), offset.ToString()));
        Assert.All(edges.GetProperty("passed").EnumerateArray(), offset => Assert.False(Refused(offset.GetInt32()), offset.ToString()));
        Assert.Equal(DipRule.ReportNear, DipRule.Read(Member(FlatThen(101), reports: [0]), setting).Reason);

        // On the night, a report dated tonight counts nought sessions, one dated the session before the time limit ends
        // counts ahead on the exchange's own calendar, and one on a day the calendar holds back is read at the first
        // session on or after it.
        var night = new DateOnly(2026, 10, 2);
        DateOnly[] calendar = [new(2026, 9, 28), new(2026, 9, 29), new(2026, 9, 30), new(2026, 10, 1), night];
        var (reports, next) = DipFamily.ReportsOf([new(2026, 9, 27), night, new(2026, 10, 16), new(2026, 10, 19)], calendar, calendar.Length - 1, night);

        // 2026-09-27 is a Sunday, read at the Monday after, four sessions back; 2026-10-16 is the tenth session after
        // Friday 2026-10-02 and 2026-10-19 the eleventh.
        Assert.Equal([-4, 0, 10, 11], reports);
        Assert.Equal(new DateOnly(2026, 10, 16), next);

        // Ahead of the night a report dated on a day the exchange is closed is read at the session after it too: Saturday
        // 2026-10-17 at Monday 10-19, the eleventh, which the gate passes, and Saturday 2026-10-03 at Monday 10-05, the
        // first, never as the night.
        Assert.Equal([11, 1], DipFamily.ReportsOf([new(2026, 10, 17), new(2026, 10, 3)], calendar, calendar.Length - 1, night).Reports);
        Assert.False(Refused(11));
    }

    static DipSession Day(int day, double open, double close, double? average) => new(new DateOnly(2026, 10, day), open, close, average);

    [Fact]
    public void EachOfTheDipsExitsIsWorkedByHand()
    {
        // Bought at 99 on the next session's open, its stop 96 from a listing close of 100.
        // A close of 101 above its 5-session average of 99.6 on the second session sells at the third's open, 101.5.
        var exit = DipRule.Walk(100, 96, 10, [Day(5, 99, 98, 99), Day(6, 98.5, 101, 99.6), Day(7, 101.5, 102, 100)]);
        Assert.Equal((new DateOnly(2026, 10, 5), 99.0, new DateOnly(2026, 10, 7), 101.5, DipEnd.Exit, 2), (exit.BoughtOn, exit.Open, exit.EndedOn, exit.Sale, exit.End, exit.Sessions));
        Assert.Equal((101.5 - 100) / 4, exit.ResultAtClose!.Value, 9);

        // Bought at the open, the core's one pricing function restates the result over the risk the close set: (101.5 - 99) / 4.
        Assert.Equal((101.5 - 99) / 4, TradeFillsAtOpen(exit.ResultAtClose.Value, 100, 4, 99), 9);

        // A close of 95.9 under the stop on the first session sells at the second's open.
        var stop = DipRule.Walk(100, 96, 10, [Day(5, 99, 95.9, 99), Day(6, 95, 97, 98)]);
        Assert.Equal((new DateOnly(2026, 10, 6), 95.0, DipEnd.Stop, 1), (stop.EndedOn, stop.Sale, stop.End, stop.Sessions));
        Assert.Equal((95 - 100) / 4.0, stop.ResultAtClose!.Value, 9);

        // With neither on any of its three sessions the time limit sells at the third's own close.
        var limit = DipRule.Walk(100, 96, 3, [Day(5, 99, 98, 99), Day(6, 98, 97.5, 98.5), Day(7, 97.4, 97.2, 98), Day(8, 97, 99, 98)]);
        Assert.Equal((new DateOnly(2026, 10, 7), 97.2, DipEnd.Limit, 3), (limit.EndedOn, limit.Sale, limit.End, limit.Sessions));

        // A trigger on the last stored session waits for its next session's open, and so does a path with no session yet.
        var waiting = DipRule.Walk(100, 96, 10, [Day(5, 99, 98, 99), Day(6, 98.5, 101, 99.6)]);
        Assert.Equal((DipEnd.Open, (DateOnly?)null, 2), (waiting.End, waiting.EndedOn, waiting.Sessions));
        Assert.Equal((DipEnd.Open, (DateOnly?)null), (DipRule.Walk(100, 96, 10, []).End, DipRule.Walk(100, 96, 10, []).BoughtOn));

        // The results and the time limit's sale as the fixture states them.
        var exits = Expected("dips").GetProperty("exits");

        Assert.Equal(exits.GetProperty("exit").GetDouble(), exit.ResultAtClose.Value, 9);
        Assert.Equal(exits.GetProperty("exitAtOpen").GetDouble(), TradeFillsAtOpen(exit.ResultAtClose.Value, 100, 4, 99), 9);
        Assert.Equal(exits.GetProperty("stop").GetDouble(), stop.ResultAtClose.Value, 9);
        Assert.Equal(exits.GetProperty("limitSale").GetDouble(), limit.Sale!.Value, 9);
    }

    // 21.16: a dip trade an approved exit of the menu stands in its sell dial's place is walked under that exit, as the
    // engines walk the dip under one, worked by hand.
    // see: Every change the engines propose is one its family's night reads, so each is put to the operator
    [Fact]
    public void ADipTradeIsWalkedUnderTheExitOfTheMenuItsRuleNamesWorkedByHand()
    {
        var target = EquityBrief.Core.Loop.ExitMenu.Swing.First(one => one.Kind == EquityBrief.Core.Loop.ExitKind.Target && one.K == 1.5);
        DipPath Own() => DipRule.Walk(100, 96, 10, [Day(5, 99, 98, 99)]);

        // From a listing close of 100 and a stop of 96, a risk of 4 two typical moves deep, a target at 1.5 risks stands at
        // 106: the second session's close of 106.5 reaches it, sold at the third's open of 107, 1.75 risks over the
        // listing close; the first session's close of 101.5 is above the 5-session average the sell dial reads, which the
        // menu's exit does not read.
        var reached = DipRule.WalkUnder(100, 96, 10, 2, target, [Day(5, 99, 101.5, 99), Day(6, 102, 106.5, 100), Day(7, 107, 108, 101)], Own);

        Assert.Equal((new DateOnly(2026, 10, 5), 99.0, new DateOnly(2026, 10, 7), 107.0, DipEnd.Exit, 2), (reached.BoughtOn, reached.Open, reached.EndedOn, reached.Sale, reached.End, reached.Sessions));
        Assert.Equal(1.75, reached.ResultAtClose!.Value, 9);

        // A close of 95.5 under the stop on the first session is sold at the second's open of 95, a stop.
        var stopped = DipRule.WalkUnder(100, 96, 10, 2, target, [Day(5, 99, 95.5, 99), Day(6, 95, 97, 98)], Own);

        Assert.Equal((new DateOnly(2026, 10, 6), 95.0, DipEnd.Stop, 1), (stopped.EndedOn, stopped.Sale, stopped.End, stopped.Sessions));
        Assert.Equal(-1.25, stopped.ResultAtClose!.Value, 9);

        // Reaching neither over a time limit of three, the third session's close of 104 sells it, the limit.
        var limited = DipRule.WalkUnder(100, 96, 3, 2, target, [Day(5, 99, 101, 99), Day(6, 101, 103, 100), Day(7, 103, 104, 101), Day(8, 104, 110, 102)], Own);

        Assert.Equal((new DateOnly(2026, 10, 7), 104.0, DipEnd.Limit, 3), (limited.EndedOn, limited.Sale, limited.End, limited.Sessions));
        Assert.Equal(1.0, limited.ResultAtClose!.Value, 9);

        // A trigger on the last stored session waits for its next session's open.
        Assert.Equal((DipEnd.Open, (DateOnly?)null), (DipRule.WalkUnder(100, 96, 10, 2, target, [Day(5, 99, 106.5, 99)], Own).End, DipRule.WalkUnder(100, 96, 10, 2, target, [Day(5, 99, 106.5, 99)], Own).EndedOn));

        // An exit moving half is one the plan cannot be walked under, and the trade is left to its sell dial's own walk.
        var half = EquityBrief.Core.Loop.ExitMenu.Swing.First(one => one.Kind is EquityBrief.Core.Loop.ExitKind.HalfTarget or EquityBrief.Core.Loop.ExitKind.HalfBreakEven);

        Assert.Equal(Own(), DipRule.WalkUnder(100, 96, 10, 2, half, [Day(5, 99, 98, 99)], Own));
    }

    static double TradeFillsAtOpen(double atClose, double close, double risk, double open) =>
        EquityBrief.Core.Ledger.TradeFills.AtOpen(atClose, close, risk, open)!.Value;

    [Fact]
    public void TheDipsFiveSessionAverageExitReadsTheCloseToItAndTheFourBefore()
    {
        // Closes 10, 11, 12, 13, 14, 15 and 9: after a listing on the third, the fourth session's average is (10 + 11 + 12 +
        // 13) / 4 short of five and none, the fifth's (10 + 11 + 12 + 13 + 14) / 5 = 12, the sixth's 13 and the seventh's 12.6.
        var bars = new (DateOnly, double, double)[] { (new(2026, 10, 1), 10, 10), (new(2026, 10, 2), 11, 11), (new(2026, 10, 3), 12, 12), (new(2026, 10, 4), 13, 13), (new(2026, 10, 5), 14, 14), (new(2026, 10, 6), 15, 15), (new(2026, 10, 7), 9, 9) };
        var sessions = DipRule.Sessions(bars, 2, 5, [.. bars.Select(bar => bar.Item1)]);

        Assert.Equal([null, 12.0, 13.0, 12.6], sessions.Select(session => session.Average));
    }

    [Fact]
    public void ADipTradeWaitsWhereItsStocksBarsSkipASessionTheIndexTraded()
    {
        // The index traded every day from 2026-10-01 to 10-07; the stock was listed on 10-02.
        DateOnly[] calendar = [.. Enumerable.Range(1, 7).Select(day => new DateOnly(2026, 10, day))];
        (DateOnly, double, double) Bar(int day, double close) => (new DateOnly(2026, 10, day), close, close);

        // Its bars skip 10-03, the session after its listing: the walk reads no session, so the trade is not bought on
        // 10-04's open, which reading the next stored bar would do.
        Assert.Empty(DipRule.Sessions([Bar(1, 10), Bar(2, 9), Bar(4, 9.5), Bar(5, 10)], 1, 5, calendar));
        Assert.Null(DipRule.Walk(9, 8, 10, DipRule.Sessions([Bar(1, 10), Bar(2, 9), Bar(4, 9.5), Bar(5, 10)], 1, 5, calendar)).BoughtOn);

        // Its bars skip 10-05: the walk reads 10-03 and 10-04 and stops there, so the close of 11 above its 2-session
        // average of 9.95 on 10-04 waits for 10-05's open rather than selling at 10-06's, 10-03's close of 8.9 under its
        // average of 8.95 and over the stop of 8 having sold nothing.
        var skipped = DipRule.Sessions([Bar(1, 10), Bar(2, 9), Bar(3, 8.9), Bar(4, 11), Bar(6, 11.5), Bar(7, 12)], 1, 2, calendar);

        Assert.Equal([new DateOnly(2026, 10, 3), new DateOnly(2026, 10, 4)], skipped.Select(session => session.Session));

        var held = DipRule.Walk(9, 8, 10, skipped);

        Assert.Equal((new DateOnly(2026, 10, 3), DipEnd.Open, (DateOnly?)null), (held.BoughtOn, held.End, held.EndedOn));

        // A calendar the stock's bars hold every session of reads each of them, and the same trade sells at 10-05's open.
        var whole = DipRule.Sessions([Bar(1, 10), Bar(2, 9), Bar(3, 8.9), Bar(4, 11), Bar(5, 11.5), Bar(6, 11), Bar(7, 12)], 1, 2, calendar);

        Assert.Equal(5, whole.Count);
        Assert.Equal((new DateOnly(2026, 10, 5), DipEnd.Exit), (DipRule.Walk(9, 8, 10, whole).EndedOn, DipRule.Walk(9, 8, 10, whole).End));
    }

    [Fact]
    public void ADipMemberMissingASessionOrWhoseSeriesIsSuspectHasNothingReadFromItsClosesAndItsRowNamesWhy()
    {
        var any = DipRule.Provisional with { RsiUnder = 101 };

        // A member every gate passes on whole closes is refused at its history once its stored year misses a session:
        // no average, no relative strength index and no typical move read, so no stop is placed.
        Assert.True(DipRule.Read(Member(FlatThen(101)), any).Passed);

        var gapped = DipRule.Read(Member(FlatThen(101)) with { Gap = new DateOnly(2026, 9, 30) }, any);
        var history = gapped.Gates.Single(gate => gate.Name == DipRule.HistoryGate);

        Assert.Equal((false, DipRule.Gapped, (double?)null, (decimal?)null, (decimal?)null), (gapped.Passed, gapped.Reason, gapped.Rsi, gapped.Entry, gapped.Stop));
        Assert.Equal((false, "its stored year missing the session of 2026-09-30, so nothing is read across it", "2026-09-30"), (history.Passed, history.Reason, history.Values["gap"]));
        Assert.Equal(
            [(DipRule.TrendGate, "its average not read"), (DipRule.DropGate, "its 2-session RSI not read"), (DipRule.MoveGate, "its typical move not read")],
            gapped.Gates.Where(gate => !gate.Passed && gate.Name != DipRule.HistoryGate).Select(gate => (gate.Name, gate.Reason)));

        // A series marked suspect is refused the same way, its row naming the mark.
        var suspect = DipRule.Read(Member(FlatThen(101)) with { Suspect = true }, any);

        Assert.Equal((false, DipRule.SuspectSeries, "yes"), (suspect.Passed, suspect.Reason, suspect.Gates.Single(gate => gate.Name == DipRule.HistoryGate).Values["suspect"]));
        Assert.Null(suspect.Rsi);

        // Each history reason is the history gate's, so the funnel counts a gapped or suspect member there.
        Assert.Equal([2, 2, 2], new[] { DipRule.ShortHistory, DipRule.Gapped, DipRule.SuspectSeries }.Select(DipRule.GateOf));
        Assert.Equal(
            [("market", 3), ("floors", 3), ("history", 1), ("trend", 1), ("drop", 1), ("report", 1), ("typical move", 1)],
            EquityBrief.Worker.Cards.RuleCards.DipFunnel([(true, null), (false, DipRule.Gapped), (false, DipRule.SuspectSeries)]));
    }

    [Fact]
    public void TheDipListsTheLowestTwoSessionRsiFirstAndThenByTicker()
    {
        static DipAnswer Passing(string ticker, double rsi) => new(ticker, [], 10m, 9m, 10, rsi, null);

        Assert.Equal(["C", "A", "B"], DipRule.Ranked([Passing("B", 5), Passing("C", 3), Passing("A", 5), new DipAnswer("D", [], null, null, null, 1, DipRule.NoDrop)]).Select(answer => answer.Ticker));
    }

    [Fact]
    public void NoSourceARulesVersionPinsNamesTheDipSoNoExistingRulesVersionMovesBecauseOfIt()
    {
        // The dip's code and its tables: its rule, its step, its four tables and its words among the families.
        string[] dip = ["DipRule", "DipFamily", "dip_night", "dip_result", "dip_pick", "dip_trade", "SetupFamilies.Dips", "PageOrder"];

        // Every source an evaluator pins, its own and the shared evaluation's, and the hooks', but from 22.3 the dip's own
        // rules', which pin the files its step runs through.
        // see: A dip rule is registered as the family on its index and kept by the dip's own step alone
        var pinned = EquityBrief.Core.Candidates.CandidateEvaluators.All
            .Where(evaluator => evaluator is not EquityBrief.Core.Candidates.DipCandidate)
            .SelectMany(evaluator => evaluator.OwnSources)
            .Concat(EquityBrief.Core.Candidates.CandidateEvaluator.EvaluationSources)
            .Concat(EquityBrief.Core.Loop.RuleHooks.Sources)
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        Assert.True(pinned.Length >= 30, $"Read {pinned.Length} pinned source(s), expected at least 30.");
        Assert.Contains("src/EquityBrief.Worker/Indices/IndexFamilies.cs", pinned);
        Assert.Contains("src/EquityBrief.Worker/Families/FamilyEvaluator.cs", pinned);

        var naming = pinned
            .Where(path => dip.Any(word => File.ReadAllText(Path.Combine(Repository.Root, path)).Contains(word, StringComparison.Ordinal)))
            .ToArray();

        Assert.Empty(naming);

        // The reader is shown to find a pinned source naming the dip.
        Assert.Contains(dip, word => "var ranked = DipRule.Ranked(answers);".Contains(word, StringComparison.Ordinal));
    }

    const string DipNightSession = "2026-10-02";

    // A year of bars ending on the night, a calendar day a session: closes rising from 80 by 0.16 a session to the session
    // before the drop, then falling by the given amount on each of the last two, each bar's range 2 about its close, a
    // million shares traded a session, which clears the S&P 400's floors at about $100.
    static IReadOnlyList<FamilyBar> DipYear(double drop)
    {
        var night = DateOnly.ParseExact(DipNightSession, "yyyy-MM-dd", CultureInfo.InvariantCulture);

        return
        [
            .. Enumerable.Range(0, 252).Select(at =>
            {
                var close = at < 250 ? 80 + (0.16 * at) : 80 + (0.16 * 249) - (drop * (at - 249));
                var price = Statistic.ToPrice(close);

                return new FamilyBar(night.AddDays(at - 251), price + 1m, price - 1m, price, 1_000_000);
            }),
        ];
    }

    // The S&P 400 on the constructed night. DA to DF fall by 4, 3.5, 3, 2.5, 2 and 1.5 a session over the last two, so
    // their 2-session RSIs order them DA first; DH falls by 5 and holds a trade on an S&P 500 card from the week before; DL
    // falls by 4.5 and the S&P 400's breakout listed it tonight; DR falls by 3 with a report on the night; DN does not fall.
    static TemporaryStore DipStore()
    {
        var store = new TemporaryStore().Migrated();
        var members = new (string Ticker, double Drop)[] { ("DA", 4), ("DB", 3.5), ("DC", 3), ("DD", 2.5), ("DE", 2), ("DF", 1.5), ("DH", 5), ("DL", 4.5), ("DR", 3), ("DN", -0.16) };

        foreach (var (ticker, drop) in members)
        {
            StoreYear(store, ticker, DipYear(drop));
            store.Execute($"INSERT INTO membership (index_code, ticker, joined, \"left\", observed_at) VALUES ('MID', '{ticker}', '2024-01-02', NULL, '2024-01-02T00:00:00Z');");
        }

        store.Execute($"INSERT INTO calendar (ticker, event_date, kind, timing, detail, observed_at) VALUES ('DR', '{DipNightSession}', 'earnings', 'after', '', '2026-09-01T00:00:00Z');");
        store.Execute("INSERT INTO family_night (session_date, families) VALUES ('2026-09-28', '[\"pullback\",\"breakout\",\"drift\"]');");
        store.Execute("INSERT INTO family_pick (session_date, ticker, family, state, place, also, held_family, held_night) VALUES ('2026-09-28', 'DH', 'breakout', 'listed', 1, '[]', NULL, NULL);");
        store.Execute($"INSERT INTO index_family_pick (index_code, session_date, ticker, family, state, place, also) VALUES ('MID', '{DipNightSession}', 'DL', 'breakout', 'listed', 1, '[]');");

        return store;
    }

    // A year of bars ending on the night at the given close, a calendar day a session: closes rising over the year to 80
    // cents above it and falling 40 cents a session over the last two, each bar's range 5 cents about its close, and a
    // hundred shares traded a session, under every index's dollar volume floor.
    static IReadOnlyList<FamilyBar> CheapYear(decimal last)
    {
        var night = DateOnly.ParseExact(DipNightSession, "yyyy-MM-dd", CultureInfo.InvariantCulture);
        var top = last + 0.80m;

        return
        [
            .. Enumerable.Range(0, 252).Select(at =>
            {
                var close = at < 250 ? Math.Round(top * (0.4m + (0.6m * at / 249m)), 2) : top - (0.40m * (at - 249));

                return new FamilyBar(night.AddDays(at - 251), close + 0.05m, close - 0.05m, close, 100);
            }),
        ];
    }

    // The least close of $5 the dip reads on the S&P 500 as on the S&P 400 and 600, with no dollar volume floor there: two
    // S&P 500 members falling out of the same uptrend to a close of 4.99 and of 5.00 on a few hundred dollars a session,
    // the first stopped at the floors and the second passing every gate.
    [Fact]
    public async Task OnTheSAndP500TheDipReadsTheLeastCloseOfFiveDollarsAndNoDollarVolumeFloor()
    {
        using var store = new TemporaryStore().Migrated();

        foreach (var (ticker, close) in new[] { ("DU", 4.99m), ("DV", 5.00m) })
        {
            StoreYear(store, ticker, CheapYear(close));
            store.Execute($"INSERT INTO membership (index_code, ticker, joined, \"left\", observed_at) VALUES ('GSPC', '{ticker}', '2024-01-02', NULL, '2024-01-02T00:00:00Z');");
        }

        var clock = FixedClock.At(new DateTimeOffset(2026, 10, 2, 23, 40, 0, TimeSpan.Zero), SessionZones.UnitedStates);

        await new DipFamily(clock, store.DatabaseFile).RunAsync("night-dip");

        Assert.Equal(
            [$"DU|0|{DipRule.UnderTheFloors}", "DV|1|null"],
            FamilyRows(store, "SELECT ticker, passed, reason FROM dip_result WHERE index_code = 'GSPC' ORDER BY ticker;"));
    }

    [Fact]
    public async Task TheDipsNightStoresEveryMembersAnswerAndGivesWayToEveryOtherFamilysListAndOpenTrades()
    {
        using var store = DipStore();
        var clock = FixedClock.At(new DateTimeOffset(2026, 10, 2, 23, 40, 0, TimeSpan.Zero), SessionZones.UnitedStates);
        var outcome = await new DipFamily(clock, store.DatabaseFile).RunAsync("night-dip");
        var mid = outcome.Indices.Single(index => index.Index == "MID");

        // Ten members read, eight passing: every one but DR, whose report falls on the night, and DN, which did not fall.
        Assert.Equal((10, 8), (mid.Members, mid.Passed));
        Assert.Equal(
            [
                "DA|1|3|null", "DB|1|4|null", "DC|1|5|null", "DD|1|6|null", "DE|1|7|null", "DF|1|8|null", "DH|1|1|null", "DL|1|2|null",
                $"DN|0|null|{DipRule.NoDrop}", $"DR|0|null|{DipRule.ReportNear}",
            ],
            FamilyRows(store, "SELECT ticker, passed, place, reason FROM dip_result WHERE index_code = 'MID' ORDER BY ticker;"));

        // The list gives way: DH held by its S&P 500 trade, DL listed tonight under the breakout, then the first five of the
        // rest listed and DF past them.
        Assert.Equal(
            [
                $"DA|{FamilyList.Listed}|1|null|null", $"DB|{FamilyList.Listed}|2|null|null", $"DC|{FamilyList.Listed}|3|null|null",
                $"DD|{FamilyList.Listed}|4|null|null", $"DE|{FamilyList.Listed}|5|null|null", $"DF|{FamilyList.PastFive}|null|null|null",
                $"DH|{FamilyList.OpenTrade}|null|null|GSPC", $"DL|{FamilyList.UnderAnother}|null|breakout|null",
            ],
            FamilyRows(store, "SELECT ticker, state, place, listed_under, held_index FROM dip_pick WHERE index_code = 'MID' ORDER BY ticker;"));

        // The five listed kept as trades under the provisional rule, bought on the plan at the night's close, held at most
        // ten sessions, each stop four typical moves under the close.
        Assert.Equal(
            ["DA|1|10|4", "DB|2|10|4", "DC|3|10|4", "DD|4|10|4", "DE|5|10|4"],
            FamilyRows(store, $"SELECT ticker, place, time_limit, risk_moves FROM dip_trade WHERE rule = '{DipFamily.ProvisionalRule}' AND index_code = 'MID' ORDER BY ticker;"));
        Assert.Equal((5, 1, 1), (mid.Listed, mid.HeldByATrade, mid.UnderAnother));
        Assert.All(
            FamilyRows(store, "SELECT entry, stop, end_reason FROM dip_trade WHERE index_code = 'MID';"),
            row => Assert.True(row.Split('|') is [var entry, var stop, "open"] && decimal.Parse(stop, CultureInfo.InvariantCulture) < decimal.Parse(entry, CultureInfo.InvariantCulture), row));

        // No other family's row is written: the families' own tables hold what the store was given and nothing more.
        Assert.Equal(["1"], FamilyRows(store, "SELECT COUNT(*) FROM index_family_pick;"));
        Assert.Equal(["0"], FamilyRows(store, "SELECT COUNT(*) FROM index_family_trade;"));
    }

    [Fact]
    public async Task TheDipWalksAStoredTradeToItsBuyAndItsExitAndGivesWayToAnIndexFamilysAndItsOwnOpenTrades()
    {
        using var store = DipStore();
        var clock = FixedClock.At(new DateTimeOffset(2026, 10, 2, 23, 40, 0, TimeSpan.Zero), SessionZones.UnitedStates);

        // DN, rising 0.16 a session, listed on 2026-09-28 at its close of 119.52 with its stop at 113.52: bought at
        // 09-29's open of 119.68, whose close of 119.68 is above its 5-session average of 119.36, so it sells at 09-30's
        // open of 119.84, one session held, (119.84 - 119.68) / 6 risks restated at the fill.
        store.Execute("INSERT INTO dip_trade (rule, index_code, ticker, session_date, place, entry, stop, time_limit, exit_average, risk_moves, end_reason) VALUES ('provisional', 'MID', 'DN', '2026-09-28', 1, '119.52', '113.52', 10, 5, 4.0, 'open');");
        // DC, listed on 09-30 at 119.84 with its stop at 114: bought at 10-01's open of 116.84, its close of 113.84 on 10-02
        // under the stop selling at an open the store does not hold yet, so it stays open and holds DC back tonight.
        store.Execute("INSERT INTO dip_trade (rule, index_code, ticker, session_date, place, entry, stop, time_limit, exit_average, risk_moves, end_reason) VALUES ('provisional', 'MID', 'DC', '2026-09-30', 1, '119.84', '114', 10, 5, 4.0, 'open');");
        // DB holds an open trade under the S&P 400's pullback from 09-30.
        store.Execute("INSERT INTO index_family_trade (index_code, family, ticker, session_date, place, entry, stop, target, trail, cap) VALUES ('MID', 'pullback', 'DB', '2026-09-30', 1, '119.84', '114', '130', NULL, 63);");

        await new DipFamily(clock, store.DatabaseFile).RunAsync("night-dip");

        Assert.Equal(
            ["2026-09-29|119.68|2026-09-30|119.84|exit|1"],
            FamilyRows(store, "SELECT bought_on, fill, ended_on, sale, end_reason, sessions FROM dip_trade WHERE ticker = 'DN';"));
        Assert.Equal((119.84 - 119.68) / 6, double.Parse(Assert.Single(FamilyRows(store, "SELECT result FROM dip_trade WHERE ticker = 'DN';")), CultureInfo.InvariantCulture), 6);
        Assert.NotEqual(["null"], FamilyRows(store, "SELECT cost FROM dip_trade WHERE ticker = 'DN';"));
        Assert.Equal(["2026-10-01|116.84|null|open"], FamilyRows(store, "SELECT bought_on, fill, ended_on, end_reason FROM dip_trade WHERE ticker = 'DC';"));

        // DB held back by the pullback's trade on its index and DC by the dip's own, each naming its holder.
        Assert.Equal(
            [$"DB|{FamilyList.OpenTrade}|pullback|MID|2026-09-30", $"DC|{FamilyList.OpenTrade}|dip|MID|2026-09-30"],
            FamilyRows(store, "SELECT ticker, state, held_family, held_index, held_night FROM dip_pick WHERE index_code = 'MID' AND ticker IN ('DB', 'DC') ORDER BY ticker;"));

        // Run again, the night stores the same walk and the same list.
        var first = FamilyRows(store, "SELECT ticker, state, place FROM dip_pick WHERE index_code = 'MID' ORDER BY ticker;");

        await new DipFamily(clock, store.DatabaseFile).RunAsync("night-dip-again");

        Assert.Equal(first, FamilyRows(store, "SELECT ticker, state, place FROM dip_pick WHERE index_code = 'MID' ORDER BY ticker;"));
        Assert.Equal(["2026-09-29|119.68|2026-09-30|119.84|exit|1"], FamilyRows(store, "SELECT bought_on, fill, ended_on, sale, end_reason, sessions FROM dip_trade WHERE ticker = 'DN';"));
    }

    [Fact]
    public async Task AFailureInOneIndexsPartOfTheDipIsNamedOnItsRowAndTheOtherIndicesGoOn()
    {
        // The S&P 600's one member holds a bar whose close the store cannot read, so the S&P 600's part fails.
        using var store = DipStore();

        StoreYear(store, "SX", DipYear(4));
        store.Execute("INSERT INTO membership (index_code, ticker, joined, \"left\", observed_at) VALUES ('SML', 'SX', '2024-01-02', NULL, '2024-01-02T00:00:00Z');");
        store.Execute($"UPDATE bar SET close = 'unreadable' WHERE ticker = 'SX' AND session_date = '{DipNightSession}';");

        var clock = FixedClock.At(new DateTimeOffset(2026, 10, 2, 23, 40, 0, TimeSpan.Zero), SessionZones.UnitedStates);
        var outcome = await new DipFamily(clock, store.DatabaseFile).RunAsync("night-dip");

        Assert.NotNull(outcome.Indices.Single(index => index.Index == "SML").Fault);
        Assert.Null(outcome.Indices.Single(index => index.Index == "MID").Fault);

        // Its night row names the failure and holds no answer, while the S&P 400's part stored its eight passes.
        Assert.Equal(["SML|0|0|0"], FamilyRows(store, "SELECT index_code, members, passed, fault IS NULL FROM dip_night WHERE index_code = 'SML';"));
        Assert.Empty(FamilyRows(store, "SELECT ticker FROM dip_result WHERE index_code = 'SML';"));
        Assert.Equal(["MID|10|8|1"], FamilyRows(store, "SELECT index_code, members, passed, fault IS NULL FROM dip_night WHERE index_code = 'MID';"));

        // The stage's row is not computed, naming the index's failure, and the night goes on.
        Assert.Equal([DipFamily.NotComputed], FamilyRows(store, $"SELECT outcome FROM run_log WHERE stage = '{DipFamily.Stage}';"));
        Assert.Contains("SML: not computed tonight", Assert.Single(FamilyRows(store, $"SELECT detail FROM run_log WHERE stage = '{DipFamily.Stage}';")), StringComparison.Ordinal);
    }

    [Fact]
    public void ADipStopTheTypicalMovesWouldPlaceAtOrUnderNothingIsRefusedAtTheTypicalMove()
    {
        var any = DipRule.Provisional with { RsiUnder = 101 };

        // A close of 101 with a typical move of 25.25 puts the stop four moves under at nought, and one of 25.24 at 0.04.
        var nought = DipRule.Read(Member(FlatThen(101), move: 25.25), any);

        Assert.Equal((false, DipRule.StopUnderNothing, (decimal?)null), (nought.Passed, nought.Reason, nought.Stop));
        Assert.Equal(6, DipRule.GateOf(DipRule.StopUnderNothing));
        Assert.True(DipRule.Read(Member(FlatThen(101), move: 25.24), any).Passed);
    }

    [Fact]
    public async Task TheDipsPicksGetACardEachAndItsRuleItsNightsRowWithItsFunnelOffTheGateEachMemberFailedFirst()
    {
        using var store = DipStore();
        var clock = FixedClock.At(new DateTimeOffset(2026, 10, 2, 23, 40, 0, TimeSpan.Zero), SessionZones.UnitedStates);

        // The S&P 400's night as the index families stored it, its breadth of 42 per cent under the floor.
        store.Execute($"INSERT INTO index_family_night (index_code, session_date, members, breadth, market_open, settings, rebalanced) VALUES ('MID', '{DipNightSession}', 10, 0.42, 0, '{IndexFamilies.Settings("MID")}', 0);");

        await new DipFamily(clock, store.DatabaseFile).RunAsync("night-dip");
        await new EquityBrief.Worker.Cards.DecisionCards(clock, store.DatabaseFile).RunAsync("night-dip-cards");
        await new EquityBrief.Worker.Cards.RuleCards(clock, store.DatabaseFile).RunAsync("night-dip-rules", [], new DateTimeOffset(2026, 10, 2, 23, 0, 0, TimeSpan.Zero));

        // A card for each of the five the dip listed, under its own word, and none for a stock it held back.
        Assert.Equal(
            ["DA|dip|1", "DB|dip|2", "DC|dip|3", "DD|dip|4", "DE|dip|5"],
            FamilyRows(store, "SELECT ticker, family, place FROM decision_card WHERE index_code = 'MID' AND family = 'dip' ORDER BY place;"));

        // Each card's market line states the index's breadth the night read, a check the dip does not read.
        Assert.All(
            FamilyRows(store, "SELECT lines FROM decision_card WHERE index_code = 'MID' AND family = 'dip';"),
            lines =>
            {
                using var stored = System.Text.Json.JsonDocument.Parse(lines);
                var market = Assert.Single(stored.RootElement.EnumerateArray(), line => line.GetProperty("name").GetString() == EquityBrief.Core.Cards.CardLines.MarketName).GetProperty("words").GetString()!;

                Assert.Contains("S&P 400's breadth is 42", market, StringComparison.Ordinal);
                Assert.Contains("a check this rule does not read", market, StringComparison.Ordinal);
                Assert.DoesNotContain("breadth was not read", market, StringComparison.Ordinal);
            });

        // Its provisional rule's night: five listed, the ten members read reaching the market setting, which every one
        // passes, then the floors, the history and the trend, which every one passes; DN stopping at the drop and DR at the
        // report, so eight pass every gate.
        Assert.Equal(["provisional|1|5"], FamilyRows(store, "SELECT rule, evaluated, listed FROM rule_night WHERE index_code = 'MID' AND family = 'dip';"));
        Assert.Equal(
            [("market", 10), ("floors", 10), ("history", 10), ("trend", 10), ("drop", 9), ("report", 8), ("typical move", 8)],
            EquityBrief.Core.Cards.RuleRows.ReadGates(FamilyRows(store, "SELECT gates FROM rule_night WHERE index_code = 'MID' AND family = 'dip';").Single()));
        Assert.Equal(10, EquityBrief.Core.Cards.RuleRows.ReachedOf(FamilyRows(store, "SELECT gates FROM rule_night WHERE index_code = 'MID' AND family = 'dip';").Single()));

        // The funnel as the fixture states it.
        Assert.Equal(
            [.. Expected("dips").GetProperty("funnel").EnumerateArray().Select(step => (step[0].GetString()!, step[1].GetInt32()))],
            EquityBrief.Core.Cards.RuleRows.ReadGates(FamilyRows(store, "SELECT gates FROM rule_night WHERE index_code = 'MID' AND family = 'dip';").Single()));

        // DA's card holds no report on file, since the stored calendar holds none for it, and says so in its checklist.
        Assert.Contains("no report on file in the stored calendar", FamilyRows(store, "SELECT lines FROM decision_card WHERE index_code = 'MID' AND family = 'dip' AND ticker = 'DA';").Single(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task AStockTheDipHoldsIsListedByAnOlderFamilyAsBeforeAndNoOtherListReadsTheDipsTrades()
    {
        // IA, which the S&P 400's breakout lists first on the constructed night, holding an open dip trade from 2026-09-30.
        using var store = IndexStore();

        store.Execute("INSERT INTO dip_trade (rule, index_code, ticker, session_date, place, entry, stop, time_limit, exit_average, risk_moves, end_reason) VALUES ('provisional', 'MID', 'IA', '2026-09-30', 1, '100', '94', 10, 5, 4.0, 'open');");

        var clock = FixedClock.At(new DateTimeOffset(2026, 10, 2, 23, 40, 0, TimeSpan.Zero), SessionZones.UnitedStates);

        await new IndexFamilies(clock, store.DatabaseFile).RunAsync("night-test");

        Assert.Contains($"IA|{FamilyList.Listed}|1", FamilyRows(store, "SELECT ticker, state, place FROM index_family_pick WHERE index_code = 'MID' AND family = 'breakout';"));

        // The S&P 500's lister reads no dip trade as one holding the stock.
        await using var connection = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={store.DatabaseFile}");
        await connection.OpenAsync();

        Assert.Empty(await FamilyLister.OpenTradesAsync(connection, new DateOnly(2026, 10, 2), ["IA"], CancellationToken.None));
    }

    // Section 18's four rows for the dip, each induced.

    [Fact]
    public async Task ADipMemberHoldingFewerThanItsAveragesSessionsIsNotReadAndItsRowNamesTheSessionsStored()
    {
        // DS holds the last 150 of a falling year, so its 200-session average reads nothing on the night. DG falls as DA
        // does but its stored year misses 2026-09-26, a session every other member holds, and DB's series is marked suspect.
        using var store = DipStore();

        StoreYear(store, "DS", [.. DipYear(4).Skip(102)]);
        store.Execute("INSERT INTO membership (index_code, ticker, joined, \"left\", observed_at) VALUES ('MID', 'DS', '2024-01-02', NULL, '2024-01-02T00:00:00Z');");
        StoreYear(store, "DG", [.. DipYear(4).Where((bar, at) => at != 245)]);
        store.Execute("INSERT INTO membership (index_code, ticker, joined, \"left\", observed_at) VALUES ('MID', 'DG', '2024-01-02', NULL, '2024-01-02T00:00:00Z');");
        store.Execute("INSERT INTO series_state (ticker, state, reason, checked_at) VALUES ('DB', 'suspect', 'its refetch failed', '2026-10-02T23:00:00Z');");

        var clock = FixedClock.At(new DateTimeOffset(2026, 10, 2, 23, 40, 0, TimeSpan.Zero), SessionZones.UnitedStates);

        await new DipFamily(clock, store.DatabaseFile).RunAsync("night-dip");
        await new EquityBrief.Worker.Cards.RuleCards(clock, store.DatabaseFile).RunAsync("night-dip-rules", [], new DateTimeOffset(2026, 10, 2, 23, 0, 0, TimeSpan.Zero));

        Assert.Equal([$"DS|0|{DipRule.ShortHistory}"], FamilyRows(store, "SELECT ticker, passed, reason FROM dip_result WHERE index_code = 'MID' AND ticker = 'DS';"));

        var history = FamilyRule.GatesOf(FamilyRows(store, "SELECT gates FROM dip_result WHERE index_code = 'MID' AND ticker = 'DS';").Single()).Single(gate => gate.Name == DipRule.HistoryGate);

        Assert.Equal((false, "150 sessions stored, fewer than the 200 its average reads", "150"), (history.Passed, history.Reason, history.Values["sessions"]));

        // DG is refused at its history with the session named and no 2-session RSI stored, so it is neither listed nor kept;
        // DB is refused naming its mark.
        Assert.Equal(
            [$"DB|0|null|{DipRule.SuspectSeries}", $"DG|0|null|{DipRule.Gapped}"],
            FamilyRows(store, "SELECT ticker, passed, rsi, reason FROM dip_result WHERE index_code = 'MID' AND ticker IN ('DB', 'DG') ORDER BY ticker;"));
        Assert.Equal(
            "its stored year missing the session of 2026-09-26, so nothing is read across it",
            FamilyRule.GatesOf(FamilyRows(store, "SELECT gates FROM dip_result WHERE index_code = 'MID' AND ticker = 'DG';").Single()).Single(gate => gate.Name == DipRule.HistoryGate).Reason);
        Assert.Empty(FamilyRows(store, "SELECT ticker FROM dip_pick WHERE index_code = 'MID' AND ticker IN ('DB', 'DG');"));
        Assert.Empty(FamilyRows(store, "SELECT ticker FROM dip_trade WHERE index_code = 'MID' AND ticker IN ('DB', 'DG');"));

        // The funnel counts all three at the history gate: twelve reach the market setting and the floors, nine the history.
        var funnel = EquityBrief.Core.Cards.RuleRows.ReadGates(FamilyRows(store, "SELECT gates FROM rule_night WHERE index_code = 'MID' AND family = 'dip';").Single());

        Assert.Equal([("market", 12), ("floors", 12), ("history", 9)], funnel.Take(3));
    }

    [Fact]
    public void ADipMemberWithNoTypicalMovePlacesNoStopAndIsNotListed()
    {
        var any = DipRule.Provisional with { RsiUnder = 101 };

        foreach (double? move in new double?[] { null, 0 })
        {
            var answer = DipRule.Read(Member(FlatThen(101), move: move), any);

            Assert.Equal((DipRule.NoMove, (decimal?)null, (decimal?)null), (answer.Reason, answer.Entry, answer.Stop));
            Assert.Empty(DipRule.Ranked([answer]));
            Assert.All(answer.Gates.Where(gate => gate.Name != DipRule.MoveGate), gate => Assert.True(gate.Passed, gate.Name));
        }
    }

    [Fact]
    public async Task ADipMemberWithNoReportDateOnFilePassesItsReportGateAndItsCardSaysNoneIsOnFile()
    {
        using var store = DipStore();
        var clock = FixedClock.At(new DateTimeOffset(2026, 10, 2, 23, 40, 0, TimeSpan.Zero), SessionZones.UnitedStates);

        await new DipFamily(clock, store.DatabaseFile).RunAsync("night-dip");
        await new EquityBrief.Worker.Cards.DecisionCards(clock, store.DatabaseFile).RunAsync("night-dip-cards");

        // DA has no date in the stored calendar: its report gate passes on no report, the next none on file.
        var report = FamilyRule.GatesOf(FamilyRows(store, "SELECT gates FROM dip_result WHERE index_code = 'MID' AND ticker = 'DA';").Single()).Single(gate => gate.Name == DipRule.ReportGate);

        Assert.Equal((true, string.Empty, DipRule.NoReportOnFile), (report.Passed, report.Values["reports"], report.Values["next"]));
        Assert.Contains("no report on file in the stored calendar", FamilyRows(store, "SELECT lines FROM decision_card WHERE index_code = 'MID' AND family = 'dip' AND ticker = 'DA';").Single(), StringComparison.Ordinal);

        // DR's date is on file on the night itself, which the gate reads and refuses.
        var refused = FamilyRule.GatesOf(FamilyRows(store, "SELECT gates FROM dip_result WHERE index_code = 'MID' AND ticker = 'DR';").Single()).Single(gate => gate.Name == DipRule.ReportGate);

        Assert.Equal((false, "0"), (refused.Passed, refused.Values["reports"]));
    }

    [Fact]
    public async Task ADipBuyOrSaleWhoseNextSessionIsNotStoredWaitsForIt()
    {
        // On the night the five listed are kept unbought, since the store holds no session after the night.
        using var store = DipStore();
        var clock = FixedClock.At(new DateTimeOffset(2026, 10, 2, 23, 40, 0, TimeSpan.Zero), SessionZones.UnitedStates);

        await new DipFamily(clock, store.DatabaseFile).RunAsync("night-dip");

        Assert.All(
            FamilyRows(store, "SELECT bought_on, fill, end_reason, sessions FROM dip_trade WHERE index_code = 'MID';"),
            row => Assert.Equal($"null|null|{DipEnd.Open}|null", row));

        // A sale its trigger set on the last stored session waits for the next session's open, the trade held.
        var held = DipRule.Walk(100, 96, 10, [Day(5, 99, 95.9, 99)]);

        Assert.Equal((DipEnd.Open, (DateOnly?)null, (double?)null), (held.End, held.EndedOn, held.Sale));

        // DW, listed on 2026-09-30, holds no bar for 2026-10-01, a session every other member of the index holds: its
        // trade waits unbought on the night of 2026-10-02 rather than buying at that night's open.
        using var skipping = DipStore();

        StoreYear(skipping, "DW", [.. DipYear(-0.16).Where((bar, at) => at != 250)]);
        skipping.Execute("INSERT INTO membership (index_code, ticker, joined, \"left\", observed_at) VALUES ('MID', 'DW', '2024-01-02', NULL, '2024-01-02T00:00:00Z');");
        skipping.Execute("INSERT INTO dip_trade (rule, index_code, ticker, session_date, place, entry, stop, time_limit, exit_average, risk_moves, end_reason) VALUES ('provisional', 'MID', 'DW', '2026-09-30', 1, '119.84', '114', 10, 5, 4.0, 'open');");

        await new DipFamily(clock, skipping.DatabaseFile).RunAsync("night-dip");

        Assert.Equal(["null|null|open"], FamilyRows(skipping, "SELECT bought_on, fill, end_reason FROM dip_trade WHERE index_code = 'MID' AND ticker = 'DW';"));
    }
}
