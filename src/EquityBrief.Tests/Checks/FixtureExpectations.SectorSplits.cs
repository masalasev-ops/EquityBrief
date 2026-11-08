using EquityBrief.Core.Candidates;
using EquityBrief.Core.Families;
using EquityBrief.Core.History;
using EquityBrief.Tests.Harness;
using EquityBrief.Worker.History;
using Xunit;

namespace EquityBrief.Tests.Checks;

// fixture-expectations, 23.9: the map read by hand, Real Estate and Communication Services under their older sectors before
// their own; each split's rule on both sides of its line; one split's figures worked by hand over a constructed store; the
// variant's test on both sides of an era; no store row written; and no source a rule's version pins among those it writes.
// see: The sector splits read a sector as it stood off the funds' industry headings through a stated map, and the filed GICS sector after the schedules end
public partial class FixtureExpectations
{
    internal static string[] SectorSplitsClaims =>
    [
        CheckReach.Key(Scope.LimitsTable, "The sector splits"),
        CheckReach.Key(Scope.LimitsTable, "The sector map"),
    ];

    internal static string[] SectorSplitsRows => [.. ComponentAccess.SectorSplitsRows, .. SectorSplitsClaims, .. Reading.ReadSurface.NewsMoodRows];

    [Fact]
    public void TheMapReadsEachHeadingThroughItsKeysInOrderAndTheOlderSectorsBeforeTheirOwn()
    {
        Assert.Equal((GicsSectors.InformationTechnology, (string?)null), SectorMap.Of("Semiconductors & Semiconductor Equipment"));
        Assert.Equal(GicsSectors.Financials, SectorMap.Of("MORTGAGE REAL ESTATE INVESTMENT")!.Value.Sector);
        Assert.Equal(GicsSectors.ConsumerStaples, SectorMap.Of("Food &Staples Retailing")!.Value.Sector);
        Assert.Equal(GicsSectors.ConsumerDiscretionary, SectorMap.Of("INTERNET & CATALOG RETAIL")!.Value.Sector);
        Assert.Equal(GicsSectors.Industrials, SectorMap.Of("ELECTRICAL COMPONENTS & EQUIPMENT")!.Value.Sector);
        Assert.Equal(GicsSectors.Utilities, SectorMap.Of("ELECTRIC")!.Value.Sector);
        Assert.Equal(GicsSectors.Utilities, SectorMap.Of("GAS")!.Value.Sector);
        Assert.Equal(GicsSectors.Energy, SectorMap.Of("OIL & GAS SERVICES")!.Value.Sector);
        Assert.Null(SectorMap.Of("SPACE TOURISM"));

        Assert.Equal(GicsSectors.Financials, SectorMap.AsItStood("REAL ESTATE INVESTMENT TRUSTS", new DateOnly(2016, 9, 16)));
        Assert.Equal(GicsSectors.RealEstate, SectorMap.AsItStood("REAL ESTATE INVESTMENT TRUSTS", new DateOnly(2016, 9, 19)));
        Assert.Equal(GicsSectors.ConsumerDiscretionary, SectorMap.AsItStood("MEDIA", new DateOnly(2018, 9, 21)));
        Assert.Equal(GicsSectors.InformationTechnology, SectorMap.AsItStood("INTERNET", new DateOnly(2018, 9, 21)));
        Assert.Equal(GicsSectors.InformationTechnology, SectorMap.AsItStood("TELECOMMUNICATIONS", new DateOnly(2018, 9, 21)));
        Assert.Equal(GicsSectors.CommunicationServices, SectorMap.AsItStood("MEDIA", new DateOnly(2018, 9, 24)));
        Assert.Equal("XLC", SectorMap.FundOf(GicsSectors.CommunicationServices));
    }

    // The fund up over 5 sessions where its close stands above the one 5 before; above its 20-day average where its close
    // stands over the mean of the 20 to it; a sector's third by its place among those ranked; and a 63-day return.
    [Fact]
    public void EachSplitsRuleReadsBothSidesOfItsLine()
    {
        double?[] rising = [.. Enumerable.Range(0, 70).Select(at => (double?)(100 + at))];
        double?[] falling = [.. Enumerable.Range(0, 70).Select(at => (double?)(200 - at))];

        Assert.Equal(SectorSplits.Up, SectorSplits.Change(rising, 10));
        Assert.Equal(SectorSplits.Down, SectorSplits.Change(falling, 10));
        Assert.Null(SectorSplits.Change(rising, 4));
        Assert.Equal(SectorSplits.Above, SectorSplits.Average(rising, 25));
        Assert.Equal(SectorSplits.Below, SectorSplits.Average(falling, 25));
        Assert.Null(SectorSplits.Average(rising, 18));
        Assert.Null(SectorSplits.Average([.. rising.Select((close, at) => at == 20 ? null : close)], 25));
        Assert.Equal((163.0 / 100) - 1, SectorSplits.Return(rising, 63)!.Value, 12);

        Assert.Equal([SectorSplits.Top, SectorSplits.Top, SectorSplits.Top, SectorSplits.Top, SectorSplits.Middle, SectorSplits.Middle, SectorSplits.Middle, SectorSplits.Middle, SectorSplits.Bottom, SectorSplits.Bottom, SectorSplits.Bottom], Enumerable.Range(0, 11).Select(place => SectorSplits.Third(place, 11)));

        // Three settled pullbacks, two at target, edges 0.3, -0.1 and 0.4, and one not settled: 3 trades, 2 at target, 0.2.
        var side = SectorSplits.Side(
        [
            new SplitSetup("GSPC", "pullback", "2010 to 2014", true, true, 0.3, null, SectorSplits.Below, null),
            new SplitSetup("GSPC", "pullback", "2010 to 2014", true, false, -0.1, null, SectorSplits.Below, null),
            new SplitSetup("GSPC", "pullback", "2010 to 2014", true, true, 0.4, null, SectorSplits.Below, null),
            new SplitSetup("GSPC", "pullback", "2010 to 2014", false, false, null, null, SectorSplits.Below, null),
        ]);

        Assert.Equal((3, 2), (side.Trades, side.Targets));
        Assert.Equal(0.2, side.Edge!.Value, 12);
    }

    // The variant holds where every era holds 50 trades a side with the side below standing above; one era with the side
    // below at or under the side above, or 49 trades on a side, and it does not.
    [Fact]
    public void ThePullbackVariantHoldsOnlyWhereEveryEraHoldsItWithFiftyTradesASide()
    {
        (string, SplitSide, SplitSide) Era(string era, int below, double belowEdge, int above, double aboveEdge) =>
            (era, new SplitSide(below, 0, belowEdge), new SplitSide(above, 0, aboveEdge));

        Assert.True(SectorSplits.VariantHolds([Era("2005 to 2009", 50, 0.2, 50, 0.1), Era("2010 to 2014", 80, 0.1, 90, 0.0)]));
        Assert.False(SectorSplits.VariantHolds([Era("2005 to 2009", 50, 0.2, 50, 0.1), Era("2010 to 2014", 80, 0.1, 90, 0.1)]));
        Assert.False(SectorSplits.VariantHolds([Era("2005 to 2009", 49, 0.2, 50, 0.1), Era("2010 to 2014", 80, 0.1, 90, 0.0)]));
        Assert.False(SectorSplits.VariantHolds([]));
    }

    // One split worked over a constructed store: a software maker on the S&P 400 whose fund, XLK, rises every session, so on
    // both setups' sessions the fund stands above its 20-day average; two settled breakouts, one at target, their edges 1.0
    // and -0.5 risks less the published round trip at the price, which the table gives at a company of no count.
    [Fact]
    public async Task OneSplitIsWorkedOverAConstructedStore()
    {
        using var store = new TemporaryStore().Migrated();
        var days = Enumerable.Range(0, 60).Select(at => new DateOnly(2006, 1, 2).AddDays(at)).Where(day => day.DayOfWeek is not DayOfWeek.Saturday and not DayOfWeek.Sunday).ToArray();

        string Bars(string series) => string.Join(", ", days.Select((day, at) => FormattableString.Invariant($"('{series}', '{day:yyyy-MM-dd}', '{100 + at}', '{100 + at}', '{100 + at}', '{100 + at}', 'r')")));

        store.Execute($"INSERT INTO long_market_bar (series, session_date, open, high, low, close, run_id) VALUES {Bars("GSPC")}, {Bars("XLK")};");
        store.Execute("INSERT INTO long_span (index_code, ticker, joined, \"left\", read_from, run_id) VALUES ('MID', 'AAA', '2005-12-30', NULL, 'fund', 'r');");
        store.Execute("INSERT INTO long_holding (index_code, period, holding, name, industry, shares, value_usd, ticker, matched_by, stood_between, checked, left_as, left_read, run_id) VALUES ('MID', '2005-12-31', 0, 'Aaa Software', 'SOFTWARE', '1', '1', 'AAA', 'name', NULL, NULL, NULL, NULL, 'r');");

        string Setup(DateOnly day, string end, double result, double edge) =>
            FormattableString.Invariant($"('MID', 'breakout', 'AAA', '{day:yyyy-MM-dd}', 'r', 0, '20', '19', 10, {result}, {edge}, '{end}', 1, 'history', 'p', '{end}', 1, 'r')");

        store.Execute("INSERT INTO long_setup (index_code, family, ticker, session_date, rule, live_pass, entry, stop, cap, result, edge, end, settled, source, pin, last_end, last_settled, run_id) VALUES "
            + Setup(days[30], "target", 2.0, 1.0) + ", " + Setup(days[35], "stop", -1.0, -0.5) + ";");

        var lines = await SectorSplitsReader.MeasureAsync(store.DatabaseFile);
        var trip = Core.Readings.TradeCost.InRisk(null, 20m, 19m, 22m) + Core.Readings.TradeCost.InRisk(null, 20m, 19m, 19m);
        var expected = ((1.0 + -0.5) / 2) - (trip / 2);

        Assert.Contains(lines, line => line == FormattableString.Invariant($"MID breakout by its fund against its 20-day average, 2005 to 2009: above 2 trades, 50% at target, edge after costs {expected:0.000}; below 0 trades, none at target, edge after costs none; none read 0"));
    }

    // The measure reads the store and writes nothing: every touch it declares is a read, and it holds no method that writes.
    [Fact]
    public void TheSectorSplitsWriteNoStoreRow()
    {
        Assert.All(SectorSplitsReader.Access.Stores, touch => Assert.Equal(Core.Components.Touch.Read, touch.Touch));
        Assert.DoesNotContain(typeof(SectorSplitsReader).GetMethods(), method => method.Name.Contains("Write", StringComparison.Ordinal));

        var source = File.ReadAllText(Path.Combine(Repository.Root, "src", "EquityBrief.Worker", "History", "SectorSplitsReader.cs"));

        Assert.DoesNotMatch(@"\b(INSERT|UPDATE|DELETE)\b", source);
    }

    // Every shipped source 23.9 writes or changes, none of them one a rule's version pins, so no rule's code version moves.
    internal static readonly string[] SectorSplitsSources =
    [
        "src/EquityBrief.Api/Program.cs",
        "src/EquityBrief.Api/Reading/ReadApi.NewsMood.cs",
        "src/EquityBrief.Core/History/SectorMap.cs",
        "src/EquityBrief.Core/History/SectorSplits.cs",
        "src/EquityBrief.Web/App/SinglePageApp.History.cs",
        "src/EquityBrief.Web/App/SinglePageApp.NewsMood.cs",
        "src/EquityBrief.Worker/History/SectorSplitsReader.cs",
        "src/EquityBrief.Worker/Program.cs",
    ];

    [Fact]
    public void NoSourceTheSectorSplitsWriteIsOneARulesVersionPins()
    {
        var pinned = CandidateEvaluators.All
            .SelectMany(evaluator => evaluator.OwnSources)
            .Concat(CandidateEvaluator.EvaluationSources)
            .Concat(Core.Loop.RuleHooks.Sources)
            .Concat(LedgerSources.Select(path => Path.GetRelativePath(Repository.Root, path).Replace('\\', '/')))
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        Assert.True(pinned.Length >= 30, $"Read {pinned.Length} pinned source(s), expected at least 30.");
        Assert.All(SectorSplitsSources, path => Assert.True(File.Exists(Path.Combine(Repository.Root, path)), path));
        Assert.Empty(SectorSplitsSources.Intersect(pinned, StringComparer.Ordinal));
        Assert.Contains("src/EquityBrief.Worker/Sweep/SweepHistory.cs", pinned);
    }
}
