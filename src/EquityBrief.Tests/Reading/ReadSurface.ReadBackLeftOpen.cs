using System.Net;
using System.Text.RegularExpressions;
using EquityBrief.Api.Reading;
using EquityBrief.Core.Report;
using EquityBrief.Tests.Checks;
using EquityBrief.Tests.Harness;
using EquityBrief.Web.Marks;
using Xunit;

namespace EquityBrief.Tests.Reading;

// What the PR 640 read-back left open. Every expected value is written by hand from the operator's order, before the code ran.
// see: A quarter's filing date is read from the SEC's own filings, and where they hold none the page says so
// see: A company whose one segment is filed under a placeholder name is told so with no name
// see: A name filed as one run of joined capitals is split at its capitals for the reader, and a company's own joined names keep their spelling
// see: The proof reads a results release newer than the filed quarter as passing where the page says that quarter is awaited, and a story the labeller has not reached as waiting
// see: A member with no dated event shows its next report as an estimate where one exists
public partial class ReadSurface
{
    internal static string[] ReadBackRows =>
    [
        CheckReach.Key("15.9 Name", "A quarter's filing date"),
        CheckReach.Key("15.9 Name", "A segment filed under a placeholder name"),
        CheckReach.Key("15.9 Name", "A name filed in joined capitals"),
        CheckReach.Key("15.9 Name", "A next report estimated"),
        CheckReach.Key("15.9 Name", "The proof's awaited quarter and waiting story"),
    ];

    [Fact]
    public void AQuartersFilingDayIsTheSecsForTheNearestPeriodWithinAWeek()
    {
        var filed = new Dictionary<DateOnly, DateOnly>
        {
            [new DateOnly(2026, 6, 27)] = new DateOnly(2026, 7, 31),
            [new DateOnly(2026, 3, 28)] = new DateOnly(2026, 5, 1),
            [new DateOnly(2026, 3, 25)] = new DateOnly(2026, 4, 20),
            [new DateOnly(2025, 12, 31)] = new DateOnly(2026, 2, 20),
        };

        Assert.Equal(new DateOnly(2026, 2, 20), SecFiled.Of(new DateOnly(2025, 12, 31), filed));
        Assert.Equal(new DateOnly(2026, 7, 31), SecFiled.Of(new DateOnly(2026, 6, 30), filed));
        Assert.Equal(new DateOnly(2026, 5, 1), SecFiled.Of(new DateOnly(2026, 3, 31), filed));
        Assert.Null(SecFiled.Of(new DateOnly(2025, 9, 30), filed));
        Assert.Null(SecFiled.Of(new DateOnly(2025, 12, 23), filed));
        Assert.Null(SecFiled.Of(new DateOnly(2025, 12, 31), null));
        Assert.Equal(new DateOnly(2026, 2, 20), SecFiled.Of(new DateOnly(2026, 1, 7), filed));
        Assert.Equal("Filing date not on file", SecFiled.NotOnFile);
    }

    // Four filings of a stock whose provider dates its quarters at their own ends: the SEC states the newest and the oldest.
    [Fact]
    public void TheFiledColumnDrawsTheSecsDayAndWhereItStatesNoneSaysSoAndTheProvidersDateNowhere()
    {
        const string Payload = "{{\"periodEnd\":\"{0}\",\"quarter\":{{\"revenue\":\"100\",\"netIncome\":\"10\"}},\"currency\":\"USD\"}}";
        IReadOnlyList<FilingRow> filings =
        [
            new("OZK", new DateOnly(2026, 9, 30), string.Format(System.Globalization.CultureInfo.InvariantCulture, Payload, "2026-09-30"), "{}"),
            new("OZK", new DateOnly(2026, 6, 30), string.Format(System.Globalization.CultureInfo.InvariantCulture, Payload, "2026-06-30"), "{}"),
            new("OZK", new DateOnly(2026, 3, 31), string.Format(System.Globalization.CultureInfo.InvariantCulture, Payload, "2026-03-31"), "{}"),
        ];
        var sec = new Dictionary<DateOnly, DateOnly>
        {
            [new DateOnly(2026, 9, 30)] = new DateOnly(2026, 11, 6),
            [new DateOnly(2026, 3, 31)] = new DateOnly(2026, 5, 7),
        };

        var drawn = NameScreen.Numbers(filings, secFiled: sec);
        var cells = Regex.Matches(drawn, "<td data-filed=\"[^\"]*\">(.*?)</td></tr>").Select(match => match.Groups[1].Value).ToArray();

        Assert.Equal(["2026-11-06", "<span class=\"degraded\">Filing date not on file</span>", "2026-05-07"], cells);
        Assert.Equal(new DateOnly(2026, 11, 6), NameScreen.FiledShown(filings, sec));
        Assert.Null(NameScreen.FiledShown(filings, new Dictionary<DateOnly, DateOnly>()));

        // Where the store holds no answer from the SEC at all, no provider's date is drawn in its place.
        Assert.Equal(["<span class=\"degraded\">Filing date not on file</span>", "<span class=\"degraded\">Filing date not on file</span>", "<span class=\"degraded\">Filing date not on file</span>"],
            Regex.Matches(NameScreen.Numbers(filings), "<td data-filed=\"[^\"]*\">(.*?)</td></tr>").Select(match => match.Groups[1].Value).ToArray());

        // The quarters a reading read draw it as well.
        var says = new MarkRenderer().NumbersSay(
            new NumbersSayView("steady", "Steady", null, [], [new QuarterFiled(new DateOnly(2026, 6, 30), null, new DateOnly(2026, 7, 20))]),
            string.Empty);

        Assert.Contains("<td class=\"num\"><span class=\"degraded\">Filing date not on file</span></td>", says, StringComparison.Ordinal);
        Assert.DoesNotContain(">not filed<", says, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ThePageDrawsTheDayTheStoresSecFactsStateAndTheNightsPageNothingTheyStatedAfterIt()
    {
        using var store = await WithFundamentals();

        var filings = await Api(store).FundamentalsAsync(Name);
        var periods = Regex.Matches(NameScreen.Numbers(filings), "<tr data-period-end=\"([^\"]+)\"").Select(match => match.Groups[1].Value).ToArray();

        Assert.Equal(NameScreen.QuartersShown, periods.Length);

        // The filer's facts through the company row the night stored, for the newest quarter alone and filed on a day of the
        // test's choosing, under a form that counts, and an 8-K stating a later day that does not.
        store.Execute($"INSERT INTO company (ticker, fetched_at, cik) VALUES ('{Name}', '2026-09-08T21:00:00Z', '0000000042');");
        store.Execute(
            "INSERT INTO filed_fact (cik, concept, period_start, period_end, dollars, filed, form, accession, run_id) VALUES " +
            $"('0000000042', 'Revenues', '2026-01-01', '{periods[0]}', '5', '2026-08-15', '10-Q', 'a-1', 'r'), " +
            $"('0000000042', 'NetIncomeLoss', '2026-01-01', '{periods[1]}', '5', '2026-08-15', '8-K', 'a-2', 'r'), " +
            // A later report stating the newest quarter again, as a year's comparatives do: the first filing's day stands.
            $"('0000000042', 'NetIncomeLoss', '2026-01-01', '{periods[0]}', '5', '2026-11-01', '10-K', 'a-3', 'r'), " +
            // A report stating a period ahead of it, as PMT's of 2021 state 2026's: no filing of that period.
            $"('0000000042', 'Revenues', '2019-01-01', '{periods[2]}', '5', '2020-01-01', '10-Q', 'a-4', 'r');");

        var days = await Api(store).SecFiledDaysAsync(Name);
        var drawn = NameScreen.Numbers(filings, secFiled: days);

        Assert.Single(days);
        Assert.Contains(">2026-08-15</td></tr>", drawn, StringComparison.Ordinal);
        Assert.Equal(NameScreen.QuartersShown - 1, Regex.Matches(drawn, "<span class=\"degraded\">Filing date not on file</span></td></tr>").Count);

        // A page for a night before that day draws what the store held that night.
        Assert.Empty(await Api(store).SecFiledDaysAsync(Name, new DateOnly(2026, 8, 14)));
        Assert.Single(await Api(store).SecFiledDaysAsync(Name, new DateOnly(2026, 8, 15)));
    }

    // The cards dated by the filing, read off the page: where the SEC states nothing for the newest quarter they say so, and
    // where it states a day they draw it.
    [Fact]
    public async Task TheCardsDatedByTheFilingDrawTheSecsDayOrSaySo()
    {
        using var store = await WithFundamentals();
        using var host = new Host(store.Root);
        using var client = host.CreateClient();

        var newest = (await Api(store).FundamentalsAsync(Name))[0];
        var period = Regex.Match(newest.Payload, "\"periodEnd\":\"([^\"]+)\"").Groups[1].Value;

        var without = await client.GetStringAsync($"/screens/name/{Name}");

        Assert.Contains("<div class=\"lbl\">The latest quarter</div><div class=\"dl\"><span class=\"dl-k\">Filing date</span><b>not on file</b>", without, StringComparison.Ordinal);
        Assert.Contains("<div class=\"lbl\">Segments</div><div class=\"dl\"><span class=\"dl-k\">Filing date</span><b>not on file</b>", without, StringComparison.Ordinal);
        Assert.DoesNotContain(FormattableString.Invariant($"<span class=\"dl-k\">Filed</span><b>{newest.FilingDate:yyyy-MM-dd}</b>"), without, StringComparison.Ordinal);

        store.Execute($"INSERT INTO company (ticker, fetched_at, cik) VALUES ('{Name}', '2026-09-08T21:00:00Z', '0000000042');");
        store.Execute($"INSERT INTO filed_fact (cik, concept, period_start, period_end, dollars, filed, form, accession, run_id) VALUES ('0000000042', 'Revenues', '2026-01-01', '{period}', '5', '2026-08-15', '10-Q', 'a-1', 'r');");

        var with = await client.GetStringAsync($"/screens/name/{Name}");

        Assert.Contains("<div class=\"lbl\">The latest quarter</div><div class=\"dl\"><span class=\"dl-k\">Filed</span><b>2026-08-15</b>", with, StringComparison.Ordinal);
        Assert.Contains("<div class=\"lbl\">Segments</div><div class=\"dl\"><span class=\"dl-k\">Filed</span><b>2026-08-15</b>", with, StringComparison.Ordinal);
    }

    // The quarters a night's readings read draw the SEC's day too, and a reason left out for an old table names none where it states none.
    [Fact]
    public void TheQuartersAReadingReadAndAnOldTablesReasonDrawTheSecsDay()
    {
        var readings = EquityBrief.Core.Quarters.QuarterReadings.Of(FixtureExpectations.Quarters(4), null, null) with { ReadFrom = new DateOnly(2026, 6, 30) };
        var reading = new FundamentalReadingRow("BTSG", new DateOnly(2026, 11, 2), readings.State, new DateOnly(2026, 6, 30), "2026-10-11T01:29:16.095Z", null, readings.ToJson());
        var ends = System.Text.Json.JsonDocument.Parse(readings.ToJson());
        QuarterDatesRow[] quarters = [.. FixtureExpectations.Quarters(4).Select(quarter => new QuarterDatesRow(quarter.PeriodEnd, new DateOnly(2000, 1, 1), new DateOnly(2000, 1, 2)))];
        var read = NameScreen.WhatTheNumbersSay(reading, quarters, secFiled: quarters.ToDictionary(quarter => quarter.PeriodEnd, _ => new DateOnly(2026, 8, 15))).Quarters;

        Assert.NotEmpty(read);
        Assert.All(read, quarter => Assert.Equal(new DateOnly(2026, 8, 15), quarter.FilingDate));
        Assert.All(NameScreen.WhatTheNumbersSay(reading, quarters).Quarters, quarter => Assert.Null(quarter.FilingDate));

        Assert.Equal("the table ends on 2020-09-30, more than 200 days before this filing, so it is left out until the company next files", OldFilings.Why("the table ends on", new DateOnly(2020, 9, 30), null));
        Assert.Equal("the table ends on 2020-09-30, more than 200 days before this filing of 2026-08-05, so it is left out until the company next files", OldFilings.Why("the table ends on", new DateOnly(2020, 9, 30), new DateOnly(2026, 8, 5)));
    }

    [Fact]
    public void AOneSegmentCompanyFiledUnderAPlaceholderIsToldSoWithNoNameAndANamedBusinessKeepsItsName()
    {
        var marks = new MarkRenderer();

        foreach (var name in new[] { "Reportable Segment", "Single Segment", "Segment", "Operating Segment", "Single Reportable Segment", "reportable segment" })
        {
            Assert.True(SegmentTable.IsPlaceholder(name), name);

            var line = marks.SegmentsTable(
                new SegmentReading("R1.htm", new DateOnly(2026, 6, 30), 0, null, null, [], SegmentTable.OneSegmentReads) { OneSegment = (name, 3_608_000_000m) },
                "USD");

            Assert.Contains(">This company reports one business segment, with revenue of $3.6B in the quarter to 2026-06-30.</p>", line, StringComparison.Ordinal);
        }

        Assert.False(SegmentTable.IsPlaceholder("Auto Parts"));
        Assert.Contains(
            ">This company reports one business segment: Auto Parts, with revenue of $4.8B in the quarter to 2026-06-30.</p>",
            marks.SegmentsTable(new SegmentReading("R1.htm", new DateOnly(2026, 6, 30), 0, null, null, [], SegmentTable.OneSegmentReads) { OneSegment = ("Auto Parts", 4_800_000_000m) }, "USD"),
            StringComparison.Ordinal);
    }

    [Fact]
    public void AJoinedNameIsSplitAtItsCapitalsWithNoAbbreviationExpandedAndACompanysOwnSpellingKept()
    {
        Assert.Equal("Home Building Ops", FieldWords.Spaced("HomeBuildingOps"));
        Assert.Equal("Intl Vehicle Pmts", FieldWords.Spaced("IntlVehiclePmts"));
        Assert.Equal("President, Intl Vehicle Pmts", FieldWords.Spaced("President, IntlVehiclePmts"));
        Assert.Equal("Gen Counsel", FieldWords.Spaced("GenCounsel"));
        Assert.Equal("Chief Corp Aff Off", FieldWords.Spaced("ChiefCorpAffOff"));
        Assert.Equal("Sr VP", FieldWords.Spaced("SrVP"));

        foreach (var name in new[] { "YouTube", "PepsiCo", "QuickBooks", "PayPal", "IoT", "SaaS", "McDonald", "UnitedHealthcare", "ExxonMobil", "DoorDash", "NextEra", "UniFirst", "DownREIT", "NVIDIA", "iPhone", "Auto Parts" })
        {
            Assert.Equal(name, FieldWords.Spaced(name));
        }

        // On a page: the cell split, its element keeping the value as stored.
        var marks = new MarkRenderer();
        var table = marks.SegmentsTable(
            new SegmentReading(
                "R1.htm",
                new DateOnly(2026, 6, 30),
                2,
                100m,
                100m,
                [new SegmentRow("HomeBuildingOps", 60m, null, null, null, null, null, 60), new SegmentRow("YouTube", 40m, null, null, null, null, null, 40)],
                null),
            "USD");

        Assert.Contains("data-segment=\"HomeBuildingOps\"><td>Home Building Ops</td>", table, StringComparison.Ordinal);
        Assert.Contains("data-segment=\"YouTube\"><td>YouTube</td>", table, StringComparison.Ordinal);

        // The table as filed: the operator's own example is one of its group headings, and its element keeps the value as stored.
        const string Filed = "{\"periodEnd\":\"2026-06-30\",\"currency\":\"USD\",\"segments\":{\"report\":\"R1.htm\",\"periods\":[{\"months\":3,\"ended\":\"2026-06-30\"}],\"consolidated\":[{\"lineItem\":\"Revenues\",\"months\":3,\"ended\":\"2026-06-30\",\"value\":\"5\",\"unit\":null}],\"groups\":[{\"label\":\"HomeBuildingOps\",\"figures\":[{\"lineItem\":\"Revenues\",\"months\":3,\"ended\":\"2026-06-30\",\"value\":\"5\",\"unit\":null}]}]}}";
        var raw = NameScreen.RawSegments([new FilingRow("DHI", new DateOnly(2026, 7, 30), Filed, "{}")]);

        Assert.Contains("<tr class=\"segment-group\" data-segment=\"HomeBuildingOps\"><th colspan=\"2\">Home Building Ops</th></tr>", raw, StringComparison.Ordinal);

        // The bars beside the table, a one-segment line naming a business and an insider's title read the same way.
        Assert.Contains(">Home Building Ops</text>", marks.ShareBars(new SegmentReading("R1.htm", new DateOnly(2026, 6, 30), 2, 100m, 100m, [new SegmentRow("HomeBuildingOps", 60m, null, null, null, null, null, 60), new SegmentRow("YouTube", 40m, null, null, null, null, null, 40)], null)), StringComparison.Ordinal);
        Assert.Contains(
            ">This company reports one business segment: Home Building Ops, with revenue of $4.8B in the quarter to 2026-06-30.</p>",
            marks.SegmentsTable(new SegmentReading("R1.htm", new DateOnly(2026, 6, 30), 0, null, null, [], SegmentTable.OneSegmentReads) { OneSegment = ("HomeBuildingOps", 4_800_000_000m) }, "USD"),
            StringComparison.Ordinal);

        var day = new DateOnly(2026, 10, 1);
        var totals = new EquityBrief.Core.Insiders.InsiderTotals(0m, 1000m, 0, 1);
        var trades = marks.InsiderTrades(new EquityBrief.Core.Insiders.InsiderView([new EquityBrief.Core.Insiders.InsiderTradeLine("A Person", "President, IntlVehiclePmts", day, day, false, 100m, 10m, 1000m, false)], totals, totals, 0, 0, 0, 0, null));

        Assert.Contains("<td>A Person</td><td>President, Intl Vehicle Pmts</td>", trades, StringComparison.Ordinal);
    }

    [Fact]
    public void AMemberWithNoDatedEventShowsItsNextReportAsAnEstimateWhereOneExists()
    {
        var estimated = NameScreen.FactStrip("AA", 40m, null, null, [], [], nextEstimated: (new DateOnly(2026, 1, 22), new DateOnly(2027, 1, 21)));

        Assert.Contains("<dt>Next dated event</dt><dd>not on file; the next report estimated about 2027-01-21 <small>from its report of 2026-01-22 a year on</small></dd>", estimated, StringComparison.Ordinal);
        Assert.Contains("data-next-event=\"none\"", estimated, StringComparison.Ordinal);

        Assert.Contains("<dt>Next dated event</dt><dd>not on file</dd>", NameScreen.FactStrip("HOS", 6m, null, null, [], []), StringComparison.Ordinal);

        // A dated event is the event, whatever is estimated.
        Assert.Contains("<dt>Next dated event</dt><dd>2026-11-20</dd>", NameScreen.FactStrip("AA", 40m, new DateOnly(2026, 11, 20), null, [], [], nextEstimated: (new DateOnly(2026, 1, 22), new DateOnly(2027, 1, 21))), StringComparison.Ordinal);
    }

    [Fact]
    public void TheProofPassesAnAwaitedQuarterAndAWaitingStoryAndFailsTheMissingLineAndAStoryLeftUnlabelled()
    {
        const string Card = "<section class=\"card spined fund\" id=\"quarter\"><div class=\"spine\"><div class=\"dl\"><span class=\"dl-k\">Filed</span><b>2026-08-05</b></div></div><div class=\"main\"><h2>The quarter to 2026-06-30, line by line</h2></div></section>";
        const string Unfiled = "<section class=\"card spined fund\" id=\"quarter\"><div class=\"spine\"><div class=\"dl\"><span class=\"dl-k\">Filing date</span><b>not on file</b></div></div><div class=\"main\"><h2>The quarter to 2026-06-30, line by line</h2></div></section>";
        const string Release = "<p class=\"guidance release-opening\" data-accepted=\"2026-10-27\">A line.</p>";
        var awaited = MarkRenderer.QuarterWaitLine(QuarterWaits.Of(new DateOnly(2026, 10, 27), new DateOnly(2026, 6, 30), new DateOnly(2026, 10, 27), new DateOnly(2026, 10, 27), [], []));

        Assert.Equal(ReportProof.Passed, VerdictOf(ReportProof.Evaluate("BA", Card + awaited + Release), "newest-quarter"));
        Assert.Equal(ReportProof.Passed, VerdictOf(ReportProof.Evaluate("BA", Unfiled + awaited + Release), "newest-quarter"));
        Assert.Equal(ReportProof.Failed, VerdictOf(ReportProof.Evaluate("BA", Card + Release), "newest-quarter"));
        Assert.Equal(ReportProof.Failed, VerdictOf(ReportProof.Evaluate("BA", Unfiled + Release), "newest-quarter"));
        Assert.Equal(ReportProof.Passed, VerdictOf(ReportProof.Evaluate("BA", Card), "newest-quarter"));

        const string News = "<div class=\"news-region\" data-ticker=\"ROP\" data-articles=\"2\" data-all=\"2\" data-positive=\"1\" data-negative=\"0\" data-neutral=\"0\" data-opinion=\"0\" data-unlabelled=\"1\" data-left=\"0\">";
        const string Labelled = "<li class=\"news-row\" data-article=\"a\" data-state=\"labelled\" data-tabs=\"all positive\">";
        const string Waiting = "<li class=\"news-row\" data-article=\"b\" data-state=\"unlabelled\" data-tabs=\"all\">";
        const string Left = "<li class=\"news-row\" data-article=\"c\" data-state=\"unreadable\" data-tabs=\"all\">";

        var waiting = ReportProof.Evaluate("ROP", News + Labelled + Waiting).Verdicts.Single(verdict => verdict.Rule == "news-labelled");

        Assert.Equal(ReportProof.Passed, waiting.Verdict);
        Assert.Equal("1 admitted story waits for the labeller", waiting.Why);

        var left = ReportProof.Evaluate("ROP", News + Labelled + Left).Verdicts.Single(verdict => verdict.Rule == "news-labelled");

        Assert.Equal(ReportProof.Failed, left.Verdict);
        Assert.Equal("the labeller read 1 admitted story and left it unlabelled", left.Why);
    }
}
