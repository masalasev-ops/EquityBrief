using System.Text.Json.Nodes;
using EquityBrief.Api.Reading;
using EquityBrief.Core.Providers;
using EquityBrief.Core.Report;
using EquityBrief.Web.Marks;

namespace EquityBrief.Tests.Reading;

// The filed segment table: a balance date is no quarter, its figures are drawn as money or as a plain count, and a figure
// rounding to nothing carries no sign.
// see: The segment table is the segments whose revenue sums to the company's total within rounding, and the raw table where none do
public partial class ReadSurface
{
    static readonly ReportPeriod TheQuarter = new(3, new DateOnly(2026, 6, 30));
    static readonly ReportPeriod TheYearEnd = new(0, new DateOnly(2025, 12, 31));
    static readonly ReportPeriod TheBalanceDate = new(0, new DateOnly(2026, 6, 30));

    static JsonObject FiledFigure(string concept, string line, string? unit, ReportPeriod period, string? value) => new()
    {
        ["concept"] = concept,
        ["lineItem"] = line,
        ["unit"] = unit,
        ["months"] = period.Months,
        ["ended"] = FormattableString.Invariant($"{period.Ended:yyyy-MM-dd}"),
        ["value"] = value,
    };

    static JsonObject FiledSegments(ReportPeriod[] periods, params JsonObject[] figures) => new()
    {
        ["report"] = "R9.htm",
        ["title"] = "Segments (Details) - USD ($)",
        ["scale"] = 1,
        ["periods"] = new JsonArray([.. periods.Select(period => (JsonNode)new JsonObject { ["months"] = period.Months, ["ended"] = FormattableString.Invariant($"{period.Ended:yyyy-MM-dd}") })]),
        ["consolidated"] = new JsonArray([.. figures]),
        ["groups"] = new JsonArray(),
    };

    static async Task<string> RawSegmentsOf(JsonObject segments)
    {
        using var store = await WithFundamentals(withTheArchive: true);

        var filings = await Api(store).FundamentalsAsync(Name);
        var payload = JsonNode.Parse(filings[0].Payload)!.AsObject();

        payload["segments"] = segments;

        return NameScreen.RawSegments([filings[0] with { Payload = payload.ToJsonString() }])!;
    }

    // AME's shape, by hand from the store: the quarter's Net Sales of 2,044,397,000 beside the balance date of the year's end
    // and of the quarter's end. Both readers read the three months, and the balance date alone is "as at".
    [Fact]
    public async Task ABalanceDateIsNoQuarterToEitherReaderOfTheSegmentTable()
    {
        Assert.Equal(3, SegmentTable.ShortestMonths([0, 3, 6, 0]));
        Assert.Equal(12, SegmentTable.ShortestMonths([0, 12]));
        Assert.Equal(0, SegmentTable.ShortestMonths([0]));
        Assert.Equal(0, SegmentTable.ShortestMonths([]));

        var sales = new SegmentGroup("Electronic Instruments [Member]", "axis=x", [new SegmentFigure("us-gaap_Revenues", "Net Sales", null, TheQuarter, 1_300_000_000m), new SegmentFigure("us-gaap_Assets", "Assets", null, TheYearEnd, 9_000_000_000m)]);
        var other = new SegmentGroup("Electromechanical [Member]", "axis=x", [new SegmentFigure("us-gaap_Revenues", "Net Sales", null, TheQuarter, 744_397_000m)]);
        var read = SegmentTable.Read(new SegmentBreakdown("R9.htm", "Segments (Details) - USD ($)", 1, [TheYearEnd, TheBalanceDate, TheQuarter], [new SegmentFigure("us-gaap_Revenues", "Net Sales", null, TheQuarter, 2_044_397_000m)], [sales, other]));

        Assert.True(read.Clean);
        Assert.Equal((new DateOnly(2026, 6, 30), 3, 2_044_397_000m), (read.Quarter, read.Months, read.Sum));

        var raw = await RawSegmentsOf(FiledSegments(
            [TheYearEnd, TheBalanceDate, TheQuarter],
            FiledFigure("us-gaap_Revenues", "Net Sales", null, TheQuarter, "2044397000"),
            FiledFigure("us-gaap_Assets", "Assets", null, TheYearEnd, "16100000000")));

        Assert.Contains("data-months=\"3\" data-period-end=\"2026-06-30\"", raw, StringComparison.Ordinal);
        Assert.Contains("data-segment-figure=\"2044397000\"", raw, StringComparison.Ordinal);
        Assert.Contains("The 3 months to 2026-06-30", raw, StringComparison.Ordinal);
        Assert.DoesNotContain("As at", raw, StringComparison.Ordinal);
        Assert.DoesNotContain("16100000000", raw, StringComparison.Ordinal);

        Assert.Contains("As at 2026-06-30", await RawSegmentsOf(FiledSegments([TheBalanceDate], FiledFigure("us-gaap_Assets", "Assets", null, TheBalanceDate, "16100000000"))), StringComparison.Ordinal);
    }

    // A clean table drawn from a filing says which quarter it reports, on the page, AME's shape by hand summing to its total.
    [Fact]
    public async Task ACleanFiledTableNamesTheQuarterItReportsOnThePage()
    {
        using var store = await WithFundamentals(withTheArchive: true);
        using var host = new Host(store.Root);
        using var client = host.CreateClient();

        var table = FiledSegments([TheBalanceDate, TheQuarter], FiledFigure("us-gaap_Revenues", "Net Sales", null, TheQuarter, "2044397000"));

        table["groups"] = new JsonArray(
            new JsonObject { ["label"] = "Electronic Instruments [Member]", ["dimension"] = "axis=x", ["figures"] = new JsonArray(FiledFigure("us-gaap_Revenues", "Net Sales", null, TheQuarter, "1300000000")) },
            new JsonObject { ["label"] = "Electromechanical [Member]", ["dimension"] = "axis=x", ["figures"] = new JsonArray(FiledFigure("us-gaap_Revenues", "Net Sales", null, TheQuarter, "744397000")) });

        store.Execute($"UPDATE fundamentals SET payload = json_set(payload, '$.segments', json('{table.ToJsonString()}')) WHERE ticker = '{Name}';");

        var page = await client.GetStringAsync($"/screens/name/{Name}");

        Assert.Contains("<div class=\"sub segments-filed-quarter\" data-quarter=\"2026-06-30\">The quarter to 2026-06-30, from the filing</div>", page, StringComparison.Ordinal);
    }

    // CSCO's "Revenue 63325000000 $" reads as money, the XBRL's own names for a count go, and a unit a reader can read stays.
    [Fact]
    public async Task AFiledFiguresDollarUnitIsMoneyAndTheXbrlsOwnNamesForACountGo()
    {
        var raw = await RawSegmentsOf(FiledSegments(
            [TheQuarter],
            FiledFigure("us-gaap_Revenues", "Revenue", "$", TheQuarter, "63325000000"),
            FiledFigure("x_Plain", "Unstated", null, TheQuarter, "2000000"),
            FiledFigure("x_Count", "Number of operating segments", "Integer", TheQuarter, "3"),
            FiledFigure("x_Reportable", "Number of reportable segments", "Reportable_segment", TheQuarter, "1"),
            FiledFigure("x_Named", "Number of markets", "market", TheQuarter, "2")));

        Assert.Contains("<td class=\"num\" data-segment-figure=\"63325000000\">$63.3B</td>", raw, StringComparison.Ordinal);
        Assert.Contains("<td class=\"num\" data-segment-figure=\"2000000\">$2.0M</td>", raw, StringComparison.Ordinal);
        Assert.Contains("<td class=\"num\" data-segment-figure=\"3\">3</td>", raw, StringComparison.Ordinal);
        Assert.Contains("<td class=\"num\" data-segment-figure=\"1\">1</td>", raw, StringComparison.Ordinal);
        Assert.Contains("<td class=\"num\" data-segment-figure=\"2\">2 market</td>", raw, StringComparison.Ordinal);
    }

    // One is read as one: the jobs' line on the news, and the growth tile's analysts.
    [Fact]
    public void OneStoryAndOneAnalystReadAsOne()
    {
        var one = new NewsView(Name, new DateOnly(2026, 9, 4), [], 1, 1, 0, 0, 0, 0, "deepseek-flash", "deepseek", null, null);
        JobProfile[] profiles = [new("deepseek", "deepseek-flash", false)];
        var empty = new HashSet<string>(StringComparer.Ordinal);

        Assert.Equal("labelled 1 of the 1 story the page draws", MarkRenderer.ModelJobs([], one, null, profiles, empty).Single().Detail);
        Assert.Equal("no label stands for the 1 story the page draws", MarkRenderer.ModelJobs([], one with { Model = null, Unlabelled = 1 }, null, profiles, empty).Single().Detail);
        Assert.Equal("labelled 0 of the 2 stories the page draws", MarkRenderer.ModelJobs([], one with { All = 2, Unlabelled = 2 }, null, profiles, empty).Single().Detail);

        var through = new DateOnly(2026, 12, 31);
        var tile = new EquityBrief.Core.Tiles.GrowthTile(EquityBrief.Core.Tiles.GrowthTile.Consensus, 5.0, through, 1, new DateOnly(2026, 9, 4));
        var marks = new MarkRenderer();
        var tiles = new EquityBrief.Web.App.TilesView(null, tile, null, null, null, false);

        Assert.Contains("for the year to 2026-12-31, 1 analyst, as fetched on 2026-09-04", marks.LeadTiles(tiles), StringComparison.Ordinal);
        Assert.Contains("for the year to 2026-12-31, 2 analysts, as fetched on 2026-09-04", marks.LeadTiles(tiles with { Growth = tile with { Analysts = 2 } }), StringComparison.Ordinal);

        var spans = marks.TipRanksRatings(Name, new EquityBrief.Core.Analysts.TipRanksView(DateTimeOffset.UnixEpoch, new TipRanksAnswer(null, 1, [new TipRanksCounts("last90Days", 1, 1, 0, 0)], [])));

        Assert.Contains("data-analysts=\"1\">90 days: 1 analyst, 1 buy", spans, StringComparison.Ordinal);
    }

    // ARES by hand: 1.88, 2.44, 3.08, 3.72 and 5.324 over 2021 to 2025, five payments in 2025 against four in 2024, so the run
    // stops at 2025 with a count of 0 and says why; a year that fell or held says nothing, and no figure moves.
    // see: A dividend's run of raises says where it stopped because two years pay a different number of payments
    [Fact]
    public void ARunOfRaisesStoppedByADifferentNumberOfPaymentsSaysSo()
    {
        var night = new DateOnly(2026, 10, 30);

        static KeptDividend[] Paid(int year, decimal total, int payments) =>
            [.. Enumerable.Range(1, payments).Select(month => new KeptDividend(new DateOnly(year, month * 2, 10), total / payments))];

        KeptDividend[] ares = [.. Paid(2021, 1.88m, 4), .. Paid(2022, 2.44m, 4), .. Paid(2023, 3.08m, 4), .. Paid(2024, 3.72m, 4), .. Paid(2025, 5.324m, 5)];
        var read = DividendSafety.Of(5.32m, 100m, 8.00m, [], ares, [], [], night)!;

        Assert.Equal(0, read.RaisedInARow);
        Assert.Equal("not compared past 2025: 5 payments against 4", DividendSafety.RunStopped(read.Years));

        var row = new MarkRenderer().DividendTable(read, 100m, live: false, "USD");

        Assert.Contains("<span data-raised=\"0\">0</span> <small class=\"raised-stopped\">not compared past 2025: 5 payments against 4</small>", row, StringComparison.Ordinal);

        // The run counted back to a year paying otherwise, three raises before it, and one payment said as one.
        KeptDividend[] counted = [.. Paid(2021, 1.00m, 1), .. Paid(2022, 2.00m, 4), .. Paid(2023, 3.00m, 4), .. Paid(2024, 4.00m, 4), .. Paid(2025, 5.00m, 4)];
        var run = DividendSafety.Of(5.00m, 100m, 8.00m, [], counted, [], [], night)!;

        Assert.Equal(3, run.RaisedInARow);
        Assert.Equal("not compared past 2022: 4 payments against 1", DividendSafety.RunStopped(run.Years));
        Assert.Equal("not compared past 2025: 1 payment against 4", DividendSafety.RunStopped([new(2024, 4m, 4, true, null), new(2025, 5m, 1, true, null)]));

        // A year that fell, a year held level, a run reaching the first year kept and a gap between years say nothing.
        KeptDividend[] fell = [.. Paid(2024, 4.00m, 4), .. Paid(2025, 3.00m, 4)];
        KeptDividend[] whole = [.. Paid(2023, 3.00m, 4), .. Paid(2024, 4.00m, 4), .. Paid(2025, 5.00m, 4)];

        Assert.Null(DividendSafety.RunStopped(DividendSafety.Of(3.00m, 100m, 8.00m, [], fell, [], [], night)!.Years));
        Assert.Null(DividendSafety.RunStopped(DividendSafety.Of(5.00m, 100m, 8.00m, [], whole, [], [], night)!.Years));
        Assert.Null(DividendSafety.RunStopped([new(2022, 4m, 4, true, null), new(2025, 5m, 5, true, null)]));
        Assert.DoesNotContain("raised-stopped", new MarkRenderer().DividendTable(DividendSafety.Of(5.00m, 100m, 8.00m, [], whole, [], [], night)!, 100m, live: false, "USD"), StringComparison.Ordinal);
    }

    // A change rounding to nothing carries no sign and is drawn in ink: DHI's and LOW's change on a year, ESNT's share of
    // revenue, CPRI's median and ECL's against its estimate, each to the places it is drawn at.
    // see: A figure that rose or fell is drawn green or red with its sign on every page
    [Fact]
    public async Task AFigureRoundingToNothingCarriesNoSign()
    {
        Assert.Equal(("0.0%", "0.0%", "-0.1%", "0.1%", "75.0%"), (Figures.Percent(-0.0004m), Figures.Percent(0.0004m), Figures.Percent(-0.001m), Figures.Percent(0.0005m), Figures.Percent(0.749753m)));

        using var store = await WithFundamentals(withTheArchive: true);

        var filings = await Api(store).FundamentalsAsync(Name);
        var payload = JsonNode.Parse(filings[0].Payload)!.AsObject();

        payload["growth"]!["revenue"] = "0.0003";
        payload["growth"]!["netIncome"] = "-0.0003";

        var numbers = NameScreen.Numbers([filings[0] with { Payload = payload.ToJsonString() }]);

        Assert.Contains("<span class=\"flat\">0.0%</span> on a year earlier", numbers, StringComparison.Ordinal);
        Assert.DoesNotContain("+0.0%", numbers, StringComparison.Ordinal);
        Assert.DoesNotContain("-0.0%", numbers, StringComparison.Ordinal);

        var marks = new MarkRenderer();
        var table = marks.SegmentsTable(
            new SegmentReading("R9.htm", new DateOnly(2026, 6, 30), 3, 200_000_000m, 200_000_000m, [new SegmentRow("Gains", -80_000m, null, null, null, null, null, -0.04), new SegmentRow("Premiums", 200_080_000m, null, null, null, null, null, 100.04)], null),
            "USD");

        Assert.Contains(">0.0%</td></tr>", table, StringComparison.Ordinal);
        Assert.DoesNotContain("-0.0%", table, StringComparison.Ordinal);

        var peers = marks.PeerTable(PeerValuation.Of("Apparel", "OWN", [
            new PeerValue("AAA", 10.0, 1.0, -0.04, false),
            new PeerValue("BBB", 11.0, 1.0, -0.04, false),
            new PeerValue("CCC", 12.0, 1.0, -0.04, false),
            new PeerValue("OWN", 13.0, 1.0, -0.04, false),
        ])!);

        Assert.Contains("0.0% of 4", peers, StringComparison.Ordinal);
        Assert.DoesNotContain("-0.0% of 4", peers, StringComparison.Ordinal);

        var lead = MarkRenderer.ShortVersionLead(null, new LatestQuarter(
            new DateOnly(2026, 6, 30),
            null,
            null,
            [new QuarterLine(QuarterLines.Revenue, 1_000_000_000m, 1_000_000_000m, 0m, 0.04, 1_000_000_000m, -0.04, null, null, false, null, null, null)]));

        Assert.Contains(", 0.0% against its estimate and 0.0% on a year earlier", lead, StringComparison.Ordinal);
    }
}
