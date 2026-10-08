using EquityBrief.Core.Families;
using EquityBrief.Core.Sweep;
using EquityBrief.Worker.Indices;
using EquityBrief.Worker.Sweep;

namespace EquityBrief.Tests.Checks;

// fixture-expectations, 17.1: the sector heavyweights' six settings a design on the S&P 400 and 600, read as the ruling
// registered them. Twelve months' strength skipping the latest and a quarter's sessions worked by hand, each design's
// reading over a constructed sector at the look-back and at twelve months skipping the latest, the floors at their
// edges and what luck passes of six.
// see: The S&P 400's and 600's sector heavyweights are searched over six settings a design registered before the run
public partial class FixtureExpectations
{
    [Fact]
    public void TheHeavyweightsSixAreTheRegisteredSettingsAndLuckPassesAboutOneOfThem()
    {
        // Design (a)'s first is the index's own provisional book, and each of the second to the fifth moves one thing
        // from it: twelve months, the cover, the quarter and the beta; the sixth moves the second's, third's and fourth's.
        var a = HeavyweightSix.DesignA;

        Assert.Equal(6, a.Count);
        Assert.Equal(IndexHeavyweights.Provisional, a[0].Leaders);
        Assert.Equal((false, IndexQuality.Profit, false), (a[0].TwelveOne, a[0].Quality, a[0].Quarterly));
        Assert.Equal((true, IndexQuality.Profit, false), (a[1].TwelveOne, a[1].Quality, a[1].Quarterly));
        Assert.Equal((false, IndexQuality.Cover, false), (a[2].TwelveOne, a[2].Quality, a[2].Quarterly));
        Assert.Equal((false, IndexQuality.Profit, true), (a[3].TwelveOne, a[3].Quality, a[3].Quarterly));
        Assert.Equal(IndexHeavyweights.Provisional with { HighBeta = false }, a[4].Leaders);
        Assert.Equal((true, IndexQuality.Cover, true), (a[5].TwelveOne, a[5].Quality, a[5].Quarterly));
        Assert.All(a.Where(one => one.Place != 5), one => Assert.Equal(IndexHeavyweights.Provisional, one.Leaders));

        // Design (b)'s the same moves from its base, two members an industry in the fifth place.
        var b = HeavyweightSix.DesignB;

        Assert.Equal([1, 1, 1, 1, 2, 1], b.Select(one => one.PerIndustry));
        Assert.Equal([false, true, false, false, false, true], b.Select(one => one.TwelveOne));
        Assert.Equal([false, false, false, true, false, true], b.Select(one => one.Quarterly));
        Assert.Equal([IndexQuality.Profit, IndexQuality.Profit, IndexQuality.Cover, IndexQuality.Profit, IndexQuality.Profit, IndexQuality.Cover], b.Select(one => one.Quality));

        // Of the 256 ways eight years can fall, 37 put six or more above nothing, so luck alone passes six tries times 37
        // over 256 of them, 0.8671875.
        Assert.Equal(37, HeavyweightSweep.LuckPatterns());
        Assert.Equal(6 * 37 / 256.0, HeavyweightSix.Luck);

        // The floors: 300 holdings in six of the eight years meet them, one holding or one year short does not.
        HeavyweightFigures Read(int holdings, int years) => new("constructed", holdings, 0, 0.01, 0.01, 0, null, null, new int[8], new double?[8], years, null, null, null, null, null);

        Assert.True(Read(300, 6).MeetsFloors);
        Assert.False(Read(299, 6).MeetsFloors);
        Assert.False(Read(300, 5).MeetsFloors);
    }

    [Fact]
    public void TwelveMonthsStrengthSkippingTheLatestAndAQuartersSessionsAreWorkedByHand()
    {
        // Closes of 100 plus the session's place: on session 280 the return from 28 to 259, 359 over 128 less one, on 273
        // from 21 to 252, and on 252 from the first to 231.
        var closes = Enumerable.Range(0, 300).Select(at => 100.0 + at).ToArray();

        Assert.Equal((359.0 / 128.0) - 1.0, HeavyweightSix.TwelveOne(closes, 280));
        Assert.Equal((352.0 / 121.0) - 1.0, HeavyweightSix.TwelveOne(closes, 273));
        Assert.Equal((331.0 / 100.0) - 1.0, HeavyweightSix.TwelveOne(closes, 252));

        // One session short of twelve months reads none, as does a missing close at either end.
        Assert.Null(HeavyweightSix.TwelveOne(closes, 251));

        var missing = (double[])closes.Clone();

        missing[259] = double.NaN;
        missing[29] = double.NaN;

        Assert.Null(HeavyweightSix.TwelveOne(missing, 280));
        Assert.Null(HeavyweightSix.TwelveOne(missing, 281));
        Assert.NotNull(HeavyweightSix.TwelveOne(missing, 282));

        // The months from 2019-01-02 over 400 weekdays from 2018: a quarterly book reads January's, April's and July's
        // first sessions and none between them.
        var calendar = Weekdays(new DateOnly(2018, 1, 1), 400);
        var first = Array.IndexOf(calendar, new DateOnly(2019, 1, 2));
        var months = HeavyweightSweep.Rebalances(calendar, first, HeavyweightPeriod.Month);

        Assert.Equal(
            [new DateOnly(2019, 1, 2), new DateOnly(2019, 4, 1), new DateOnly(2019, 7, 1)],
            HeavyweightSix.Quarters(calendar, months).Select(session => calendar[session]));
        Assert.Equal(7, months.Count);
        Assert.Same(months, HeavyweightSix.RebalancesOf(HeavyweightSix.DesignA[0], calendar, months));
    }

    [Fact]
    public void EachDesignReadsItsLeadersAtTheLookBackOrAtTwelveMonthsSkippingTheLatest()
    {
        // Two Energy companies of ten shares over 400 weekdays from 2018: A rising half a point a session from 100 to
        // 21 sessions before the first scored, then flat at 220.5; B at 100 until then, then rising ten a session to 310.
        var calendar = Weekdays(new DateOnly(2018, 1, 1), 400);
        var first = Array.IndexOf(calendar, new DateOnly(2019, 1, 2));
        var turn = first - HeavyweightSix.SkippedMonth;

        SweepName Name(string ticker, Func<int, decimal> close) => new(
            ticker,
            [.. calendar.Select((day, at) => new SweepBar(day, close(at), close(at), close(at), 1_000, close(at), close(at)))],
            [(null, null)],
            []);

        decimal A(int at) => 100m + (0.5m * Math.Min(at, turn));
        decimal B(int at) => at <= turn ? 100m : 100m + (10m * Math.Min(at - turn, HeavyweightSix.SkippedMonth));

        var inputs = new SweepHistoryInputs(calendar[^1], calendar, [Name("A", A), Name("B", B)], 0, 0, 0, 0, "constructed");
        var history = new HeavyweightHistory(
            new Dictionary<string, (string? Cik, string? Sector)> { ["A"] = ("0000000001", GicsSectors.Energy), ["B"] = ("0000000002", GicsSectors.Energy) },
            new Dictionary<string, IReadOnlyList<Core.Families.FiledCount>>
            {
                ["A"] = [new(new DateOnly(2017, 12, 31), new DateOnly(2018, 1, 15), 10m, new DateOnly(2019, 6, 28))],
                ["B"] = [new(new DateOnly(2017, 12, 31), new DateOnly(2018, 1, 15), 10m, new DateOnly(2019, 6, 28))],
            },
            new Dictionary<string, IReadOnlyList<Core.Families.FiledSplit>>(),
            [],
            []);
        var months = HeavyweightSweep.Rebalances(calendar, first, HeavyweightPeriod.Month);
        var (tape, sessions) = HeavyweightSweep.Lay(inputs, history, null, months.ToHashSet(), first);
        var names = tape.Tickers.Select((ticker, at) => (ticker, at)).ToDictionary(pair => pair.ticker, pair => pair.at, StringComparer.Ordinal);
        var cleared = new List<(int Name, int Session, IndexQuality Quality)>();

        bool Clears(int name, int session, IndexQuality quality)
        {
            lock (cleared)
            {
                cleared.Add((name, session, quality));
            }

            return true;
        }

        // Over the 251 sessions to the first scored A rose 220.5 over 105.5 and B 310 over 100, so B leads the sector's
        // mean and is bought; over twelve months skipping the latest, from 10 to 241, A rose 220.5 over 105 and B
        // nothing, so A leads. Both pass the trend gate and neither reads a beta, the fifth's floor being off.
        string Leaders(HeavyweightRebalance rebalance) => string.Join(",", rebalance.Buys.Select(name => tape.Tickers[name]));

        var looked = HeavyweightSix.DesignA[4];
        var twelve = looked with { TwelveOne = true };

        Assert.Equal("B", Leaders(HeavyweightSix.ReadA(tape, sessions[first], looked, names, Clears)));
        Assert.Equal("A", Leaders(HeavyweightSix.ReadA(tape, sessions[first], twelve, names, Clears)));
        Assert.Equal(["B,A"], HeavyweightSix.ReadA(tape, sessions[first], twelve, names, Clears).Sectors.Select(sector => string.Join(",", sector.Cut.Select(name => tape.Tickers[name]))));

        // Each member is asked at the setting's own quality, and one the quality refuses stands in no reading: with A
        // refused B is the sector alone, its lead over itself nothing, and nothing is bought.
        Assert.All(cleared, one => Assert.Equal(IndexQuality.Profit, one.Quality));

        var refused = HeavyweightSix.ReadA(tape, sessions[first], twelve, names, (name, _, _) => tape.Tickers[name] == "B");

        Assert.Empty(refused.Buys);
        Assert.Equal(["B"], refused.Sectors.Select(sector => string.Join(",", sector.Cut.Select(name => tape.Tickers[name]))));

        // Design (b) in one leading industry: over 63 sessions B's own return, 310 over 100, beats A's, 220.5 over 199.5;
        // over twelve months skipping the latest A's beats B's; two an industry buys both, each against its sector's
        // members in the index.
        IReadOnlyList<string> Leading(int from, int to) => ["Oil"];
        string? IndustryOf(int name) => "Oil";
        string? SectorOf(int name) => GicsSectors.Energy;

        var baseB = HeavyweightSix.DesignB[0];

        Assert.Equal("B", Leaders(HeavyweightSix.ReadB(tape, first, baseB, Leading, IndustryOf, SectorOf, Clears)));
        Assert.Equal("A", Leaders(HeavyweightSix.ReadB(tape, first, HeavyweightSix.DesignB[1], Leading, IndustryOf, SectorOf, Clears)));
        Assert.Equal("B,A", Leaders(HeavyweightSix.ReadB(tape, first, HeavyweightSix.DesignB[4], Leading, IndustryOf, SectorOf, Clears)));
        Assert.Equal(["A,B"], HeavyweightSix.ReadB(tape, first, HeavyweightSix.DesignB[4], Leading, IndustryOf, SectorOf, Clears).Sectors.Select(sector => string.Join(",", sector.Cut.Select(name => tape.Tickers[name]))));

        // No industry leading buys nothing, and a window reaching before the history buys nothing.
        Assert.Empty(HeavyweightSix.ReadB(tape, first, baseB, (_, _) => [], IndustryOf, SectorOf, Clears).Buys);
        Assert.Empty(HeavyweightSix.ReadB(tape, HeavyweightSix.BaseWindow - 1, baseB, Leading, IndustryOf, SectorOf, Clears).Buys);
    }
}
