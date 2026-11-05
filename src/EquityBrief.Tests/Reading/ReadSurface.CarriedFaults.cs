using EquityBrief.Api.Reading;
using EquityBrief.Core.Ledger;
using EquityBrief.Core.Providers;
using EquityBrief.Core.Report;
using EquityBrief.Web.Marks;
using Microsoft.Data.Sqlite;

namespace EquityBrief.Tests.Reading;

// The faults phase 18's sign-off carried and those it put in phase 22's, each worked by hand from the page it was read on.
public partial class ReadSurface
{
    static readonly ReportPeriod ThreeMonths = new(3, new DateOnly(2026, 6, 30));

    static SegmentFigure Revenue(decimal? value) => new("us-gaap_RevenueFromContractWithCustomerExcludingAssessedTax", "Revenue", null, ThreeMonths, value);

    static SegmentBreakdown ATable(decimal consolidated, params SegmentGroup[] groups) =>
        new("R9.htm", "Segment Information (Details) - USD ($)", 1_000_000, [ThreeMonths], [Revenue(consolidated)], groups);

    static SegmentGroup AGroup(string label, decimal? revenue) => new(label, "axis=x", [Revenue(revenue)]);

    // ABNB's table by hand: one group labelled the axis's singular, holding the company's 3,608,000,000. It names no business
    // of its own, so no row and no bar stands, and the line says what the table does state. BKNG's and OMC's labels carry the
    // axis twice, the singular read from either place.
    // see: A company reporting one segment is told so with its revenue and its quarter and the table as filed stays beneath
    [Fact]
    public void ACompanyReportingOneSegmentIsToldSoWithItsNameItsRevenueAndItsQuarter()
    {
        var marks = new MarkRenderer();

        foreach (var label in new[] { "Reportable Segment", "Reportable Segment | Operating Segments", "Operating Segments | Reportable Segment [Member]" })
        {
            var read = SegmentTable.Read(ATable(3_608_000_000m, AGroup(label, 3_608_000_000m)));

            Assert.False(read.Clean);
            Assert.Empty(read.Rows);
            Assert.Equal(("Reportable Segment", 3_608_000_000m), read.OneSegment);

            var drawn = marks.SegmentsTable(read, "USD");

            Assert.Contains("This company reports one business segment: Reportable Segment, with revenue of $3.6B in the quarter to 2026-06-30.</p>", drawn, StringComparison.Ordinal);
            Assert.DoesNotContain("states no segment's revenue", drawn, StringComparison.Ordinal);
            Assert.DoesNotContain("data-segments=\"raw\"", drawn, StringComparison.Ordinal);
            Assert.Contains(SegmentTable.OneSegmentReads, marks.ShareBars(read), StringComparison.Ordinal);
        }

        // A sentence is added to a table of one named row, whose row still stands: AZO's Auto Parts.
        var named = SegmentTable.Read(ATable(4_840_950_000m, AGroup("Auto Parts", 4_840_950_000m)));

        Assert.True(named.Clean);
        Assert.Contains("This company reports one business segment: Auto Parts, with revenue of $4.8B in the quarter to 2026-06-30.</p><div class=\"tbl-wrap\"><table class=\"segment-table\"", marks.SegmentsTable(named, "USD"), StringComparison.Ordinal);
    }

    // The shapes that are not one segment keep what they said: the plural says nothing of how many, DD's and IFF's; a group
    // holding no revenue for the quarter is GILD's and the wording is true of it; LOW's one group not summing to the company's
    // says so and is told nothing of a single segment; two named segments are a table.
    [Fact]
    public void OnlyTheAxissSingularHoldingTheCompanysRevenueReadsAsOneSegment()
    {
        var plural = SegmentTable.Read(ATable(1_819_000_000m, AGroup("Operating Segments", 1_819_000_000m)));

        Assert.Null(plural.OneSegment);
        Assert.Equal("the table states no segment's revenue for the quarter", plural.Raw);

        var none = SegmentTable.Read(ATable(1_819_000_000m, AGroup("Reportable Segment", null)));

        Assert.Null(none.OneSegment);
        Assert.Equal("the table states no segment's revenue for the quarter", none.Raw);

        var short_ = SegmentTable.Read(ATable(25_956_000_000m, AGroup("Reportable Segment", 24_015_000_000m)));

        Assert.Null(short_.OneSegment);
        Assert.Equal("the segments' revenue does not sum to the company's within rounding", short_.Raw);

        // The axis beside a word naming no segment of its own is no segment either, whatever it holds.
        var eliminated = SegmentTable.Read(ATable(1_000_000_000m, AGroup("Reportable Segment | Intersegment Eliminations", 1_000_000_000m)));

        Assert.Null(eliminated.OneSegment);
        Assert.Equal("the table states no segment's revenue for the quarter", eliminated.Raw);

        var two = SegmentTable.Read(ATable(3_000_000_000m, AGroup("Stores", 2_000_000_000m), AGroup("Online", 1_000_000_000m)));

        Assert.True(two.Clean);
        Assert.Null(two.OneSegment);
        Assert.Equal(2, two.Rows.Count);
        Assert.DoesNotContain("one business segment", new MarkRenderer().SegmentsTable(two, "USD"), StringComparison.Ordinal);
    }

    // ADBE's table by hand: the analysts' quarter ends on the filing's 2026-08-28, where the provider labels it 2026-08-31, and
    // the row keeps the label in its attribute for what matches on it. A row no filing states draws the label.
    // see: The analysts' table and the figures checked draw a quarter's end as the filing states it
    [Fact]
    public void TheAnalystsTableDrawsTheFilingsDateAndKeepsTheLabelToMatchOn()
    {
        var row = new ConsensusRow(EquityBrief.Core.Quarters.EstimatePeriod.CurrentQuarter, new DateOnly(2026, 8, 31), 5.1m, 5m, 5.2m, 4.5m, 13.3, 20, null, null, null, null, null, null);
        var reading = new AnalystReading(new DateOnly(2026, 11, 3), [row with { DrawnEnd = new DateOnly(2026, 8, 28) }, row with { Period = EquityBrief.Core.Quarters.EstimatePeriod.NextQuarter, PeriodEnd = new DateOnly(2026, 11, 30) }], [], new Dictionary<string, IReadOnlyList<TrendPoint>>(), [], null, null, null);

        var drawn = new MarkRenderer().ConsensusTable(reading, "USD");

        Assert.Contains("data-period-end=\"2026-08-31\"><td>This quarter to 2026-08-28</td>", drawn, StringComparison.Ordinal);
        Assert.Contains("data-period-end=\"2026-11-30\"><td>Next quarter to 2026-11-30</td>", drawn, StringComparison.Ordinal);
        Assert.DoesNotContain("to 2026-08-31", drawn, StringComparison.Ordinal);

        // The page hands each its stated end: ADBE's by the filing's end within a week of the label, and a quarter no filing
        // states keeps its label.
        var stated = new StatedQuarters([new DateOnly(2026, 8, 28)]);
        var labelled = new AnalystReading(new DateOnly(2026, 11, 3), [row, row with { PeriodEnd = new DateOnly(2026, 11, 30) }], [], new Dictionary<string, IReadOnlyList<TrendPoint>>(), [], null, null, null);

        Assert.Equal([new DateOnly(2026, 8, 28), new DateOnly(2026, 11, 30)], [.. NameScreen.AnalystsAsFiled(labelled, stated)!.Consensus.Select(consensus => consensus.DrawnEnd!.Value)]);
        Assert.Equal([new DateOnly(2026, 8, 31), new DateOnly(2026, 11, 30)], [.. NameScreen.AnalystsAsFiled(labelled, stated)!.Consensus.Select(consensus => consensus.PeriodEnd)]);
        Assert.Null(NameScreen.AnalystsAsFiled(null, stated));

        var conflict = new FigureConflict("Gross profit", new DateOnly(2026, 8, 31), 6_000_000_000m, "GrossProfit", new DateOnly(2026, 9, 22), 6_100_000_000m);

        Assert.Equal(new DateOnly(2026, 8, 28), Assert.Single(NameScreen.ConflictsAsFiled([conflict], stated)).PeriodEnd);
    }

    // INDV's night by hand: a stock holding no bar on the night still asks for what follows the night, and a stock whose bars
    // reach the night reads as it did.
    // see: A name's next dated event is the first after the night, and the earnings soon reason alone counts the night's own
    [Fact]
    public void ANamesNextDatedEventIsAskedForAfterTheNightWhereItsLastBarIsBeforeIt()
    {
        var night = new DateOnly(2026, 11, 3);

        Assert.Equal(new DateOnly(2026, 11, 4), NameScreen.NextEventFrom(new DateOnly(2026, 11, 2), night));
        Assert.Equal(new DateOnly(2026, 11, 4), NameScreen.NextEventFrom(night, night));
        Assert.Equal(new DateOnly(2026, 11, 4), NameScreen.NextEventFrom(null, night));
        Assert.Equal(new DateOnly(2026, 11, 3), NameScreen.NextEventFrom(new DateOnly(2026, 11, 2), null));
        Assert.Equal(DateOnly.MinValue, NameScreen.NextEventFrom(null, null));
    }

    // FDXF's page: a held stock with no next report on the calendar, whose news holds a story labelled results. The story is
    // no calendar row, so the rule reads no report to place and does not fail the page.
    // see: The calendar and the plan's block around the next report list it declared or estimated and place it against the hold
    [Fact]
    public void TheProofReadsAResultsStoryInTheNewsAsNoReportOnTheCalendar()
    {
        const string Held = "<section class=\"held-card\" data-family=\"drift\">";
        const string Story = "<li class=\"news-row\" data-article=\"a1\" data-state=\"labelled\" data-tabs=\"all\" data-kind=\"results\" data-direction=\"up\">";

        Assert.Equal(ReportProof.Unexamined, VerdictOf(ReportProof.Evaluate("FDXF", Held + Story), "next-report-hold"));

        // A calendar row of the same kind standing unplaced still fails.
        Assert.Equal(ReportProof.Failed, VerdictOf(ReportProof.Evaluate("FDXF", Held + Story + "<tr data-date=\"2027-01-20\" data-kind=\"results\" data-declared=\"yes\">"), "next-report-hold"));
    }

    // BTSG's night by hand: it reported on 2026-10-30, the night of 2026-11-02 read its quarters at step fundamental-readings
    // and fetched the new one after the close step, so its reading names the fetch of 2026-10-11 whose newest report is
    // 2026-07-31. The page of that night names the report of 2026-10-30, and the page of the night before the fetch does not
    // reach forward to it; the calendar holds no row, so the stored quarter alone can say.
    // see: A page names a company's newest report from the night its quarter is stored, which the night's readings ran before
    [Fact]
    public async Task APageNamesTheNewestReportFromTheNightItsQuarterIsStored()
    {
        using var store = await WithFundamentals(withTheArchive: true);
        using var host = new Host(store.Root);
        using var client = host.CreateClient();

        using (var connection = new SqliteConnection($"Data Source={store.DatabaseFile}"))
        {
            connection.Open();

            using var command = connection.CreateCommand();

            var readings = EquityBrief.Core.Quarters.QuarterReadings.Of(EquityBrief.Tests.Checks.FixtureExpectations.Quarters(4), null, null);

            command.CommandText =
                $"DELETE FROM calendar WHERE ticker = '{Name}';"
                + $"INSERT INTO fundamental_reading (ticker, session_date, state, read_from, fetched_at, awaited, readings) VALUES ('{Name}', '2026-09-04', '{readings.State}', NULL, '2026-08-01T21:00:00.000Z', NULL, $readings);"
                + $"INSERT INTO reported_quarter (ticker, fetched_at, session_date, period_end, report_date, revenue) VALUES ('{Name}', '2026-08-01T21:00:00.000Z', '2026-07-31', '2026-06-30', '2026-07-31', '1');"
                + $"INSERT INTO reported_quarter (ticker, fetched_at, session_date, period_end, report_date, revenue) VALUES ('{Name}', '2026-09-05T01:33:02.400Z', '2026-09-04', '2026-06-30', '2026-07-31', '1');"
                + $"INSERT INTO reported_quarter (ticker, fetched_at, session_date, period_end, report_date, revenue) VALUES ('{Name}', '2026-09-05T01:33:02.400Z', '2026-09-04', '2026-09-30', '2026-09-03', '1');";
            command.Parameters.AddWithValue("$readings", readings.ToJson());
            command.ExecuteNonQuery();
        }

        var read = Api(store);

        Assert.Equal([new DateOnly(2026, 9, 30), new DateOnly(2026, 6, 30)], [.. (await read.QuartersToTheNightAsync(Name, new DateOnly(2026, 9, 4))).Select(quarter => quarter.PeriodEnd)]);
        Assert.Equal([new DateOnly(2026, 6, 30)], [.. (await read.QuartersToTheNightAsync(Name, new DateOnly(2026, 9, 3))).Select(quarter => quarter.PeriodEnd)]);

        var page = await client.GetStringAsync($"/screens/name/{Name}");

        Assert.Contains("<small>the quarter to 2026-09-30 was reported on 2026-09-03</small>", page, StringComparison.Ordinal);
    }

    // BTSG's filing expected no quarter, so the filing's reading named no day and the calendar went unread: the newest of the
    // stored report days and the calendar's on or before the night, as Tonight's cards read them, a later one not counted.
    // see: A page names a company's newest report from the night its quarter is stored, which the night's readings ran before
    [Fact]
    public void TheDayAPageKnowsACompanyReportedIsTheNewestOfItsStoredQuartersAndTheCalendar()
    {
        FilingRow[] filings = [new("BTSG", new DateOnly(2026, 10, 30), "{\"periodEnd\":\"2026-09-30\"}", "test")];
        var night = new DateOnly(2026, 11, 2);
        QuarterDatesRow[] stored = [new(new DateOnly(2026, 9, 30), new DateOnly(2026, 10, 30), new DateOnly(2026, 10, 30)), new(new DateOnly(2026, 6, 30), new DateOnly(2026, 7, 31), new DateOnly(2026, 7, 31))];

        Assert.Equal(new DateOnly(2026, 10, 30), NameScreen.ReportedOnOf(filings, night, stored, []));
        Assert.Equal(new DateOnly(2026, 10, 30), NameScreen.ReportedOnOf(filings, night, stored[1..], [new DateOnly(2026, 7, 31), new DateOnly(2026, 10, 30)]));
        Assert.Equal(new DateOnly(2026, 7, 31), NameScreen.ReportedOnOf(filings, new DateOnly(2026, 10, 29), stored, [new DateOnly(2026, 10, 30)]));
        Assert.Equal(new DateOnly(2026, 10, 30), NameScreen.ReportedOnOf([], night, stored, []));
        Assert.Null(NameScreen.ReportedOnOf([], night, [], []));
    }

    // The awaited quarter's line where the night fetched the quarter whole after its readings ran, its filing stored: BTSG's
    // "is awaited" beside a page drawing that quarter. The reading's own quarters stay the table of what it read.
    // see: A page names a company's newest report from the night its quarter is stored, which the night's readings ran before
    [Fact]
    public void TheAwaitedLineMovesOnWhereTheNightFetchedTheQuarterAfterItsReadingsRan()
    {
        var readings = EquityBrief.Core.Quarters.QuarterReadings.Of(EquityBrief.Tests.Checks.FixtureExpectations.Quarters(4), null, null) with
        {
            Awaited = new DateOnly(2026, 9, 30),
            ReadFrom = new DateOnly(2026, 6, 30),
        };
        var reading = new FundamentalReadingRow("BTSG", new DateOnly(2026, 11, 2), readings.State, new DateOnly(2026, 6, 30), "2026-10-11T01:29:16.095Z", new DateOnly(2026, 9, 30), readings.ToJson());
        QuarterDatesRow[] read = [new(new DateOnly(2026, 6, 30), new DateOnly(2026, 7, 31), new DateOnly(2026, 7, 31))];
        QuarterDatesRow[] newest = [new(new DateOnly(2026, 9, 30), new DateOnly(2026, 10, 30), new DateOnly(2026, 10, 30)), .. read];

        const string After = "The quarter to 2026-09-30 was reported on 2026-10-30 and reached the store after these were read, so these read the quarter to 2026-06-30 until the next night.";

        Assert.Equal("reported after", EquityBrief.Core.Quarters.NumbersSay.PatternOf(After));
        Assert.Contains(After, NameScreen.WhatTheNumbersSay(reading, read, null, newest).Sentences);
        Assert.Contains("The quarter to 2026-09-30 is awaited, so these read the quarter to 2026-06-30.", NameScreen.WhatTheNumbersSay(reading, read, null, read).Sentences);
        Assert.Contains("The quarter to 2026-09-30 is awaited, so these read the quarter to 2026-06-30.", NameScreen.WhatTheNumbersSay(reading, read).Sentences);

        // Its filing not stored, the release's line, as it read.
        QuarterDatesRow[] released = [new(new DateOnly(2026, 9, 30), null, new DateOnly(2026, 10, 30)), .. read];

        Assert.Contains("The quarter to 2026-09-30 was reported on 2026-10-30 in its results release and its filing is not stored yet, so these read the quarter to 2026-06-30.", NameScreen.WhatTheNumbersSay(reading, read, null, released).Sentences);
    }

    // ADAM's return on assets of -0.03% and MTX's year before: each rounds to nothing, and the format's third section draws it
    // bare where -0.1% keeps its sign.
    [Fact]
    public void APercentageRoundingToNothingIsDrawnWithNoSign()
    {
        Assert.Equal("0.0%", Figures.Share(-0.0003));
        Assert.Equal("0.0%", Figures.Share(0.0003));
        Assert.Equal("-0.1%", Figures.Share(-0.001));
        Assert.Equal("1.6%", Figures.Share(0.0159));
        Assert.Equal("0.0%", Figures.Percent(-0.0003m));
        Assert.Equal("-1,234.5%", Figures.Percent(-12.345m));

        // What the numbers say, written by a formatter of its own: a margin of -0.03% and a growth before of 0.02%.
        var readings = EquityBrief.Core.Quarters.QuarterReadings.Of(EquityBrief.Tests.Checks.FixtureExpectations.Quarters(4), null, null) with
        {
            Trajectory = new EquityBrief.Core.Quarters.TrajectoryReading(
                [
                    new(new DateOnly(2026, 6, 30), 0.1m, 0.0002m, -0.0003m, -0.05m),
                    new(new DateOnly(2026, 3, 31), 0.05m, null, 0.01m, 0.02m),
                ],
                1,
                null),
        };
        var said = EquityBrief.Core.Quarters.NumbersSay.Sentences(readings);

        Assert.Contains("The newer quarter's growth was faster than the 0.0% the same quarter showed a year before.", said);
        Assert.Contains("The operating margin was 0.0% against -5.0% a year earlier, wider for 1 quarter in a row.", said);

        Assert.Equal(ReportProof.Failed, VerdictOf(ReportProof.Evaluate("ADAM", "<p>Return on assets was -0.0%, down.</p>"), "signed-zero"));
        Assert.Equal(ReportProof.Failed, VerdictOf(ReportProof.Evaluate("ADAM", "<p>Sales <b>+0.0%</b> on a year.</p>"), "signed-zero"));
        Assert.Equal(ReportProof.Passed, VerdictOf(ReportProof.Evaluate("ADAM", "<p>Return on assets was 0.0%, down from -10.0% and +20.0%, a 3-0.0% typo aside.</p>"), "signed-zero"));
    }

    // FCN's and FDXF's cards by hand, as their nights stored them, and every other reading whose name is a code name; the
    // card's own words are the ones the stored row holds with the name put into words.
    // see: A page draws every field in the reader's words, and the proof fails a page drawing a field's code name
    [Fact]
    public void AStoredFieldsCodeNameReadsInTheReadersWords()
    {
        Assert.Equal(
            "the pick holds no reading tonight of the close against its 252-day high, which the match reads",
            FieldWords.Words("the pick holds no high_ratio reading tonight, which the match reads"));
        Assert.Equal(
            "the pick holds no reading tonight of the close against its 200-day average, which the match reads",
            FieldWords.Words("the pick holds no close_over_long reading tonight, which the match reads"));
        Assert.Contains("No past setups were matched: the pick holds no reading tonight of the close against its 200-day average, which the match reads.</p>", MarkRenderer.NoPastSetups("the pick holds no close_over_long reading tonight, which the match reads"), StringComparison.Ordinal);
        Assert.Equal("also requires relative strength at or above 61.5", FieldWords.Words("also requires rsi at or above 61.5"));
        Assert.Equal("the 2-day relative strength", FieldWords.Words("rsi_two"));

        // The longer name reads whole, and a word holding a name inside it, a file's among them, is left alone.
        Assert.Equal("the VIX against its usual level over a year", FieldWords.Words("vix_over_median"));
        Assert.Equal("rsi_two.htm, vixen and the prsi", FieldWords.Words("rsi_two.htm, vixen and the prsi"));

        // Every reading the ledger holds under a name with more than one word has the reader's words, and the table names no
        // field the ledger does not hold.
        string[] columns = [.. LedgerReadings.All.Select(reading => reading.Column)];

        Assert.Empty(columns.Where(column => column.Contains('_', StringComparison.Ordinal)).Except(FieldWords.Fields.Select(field => field.Field)));
        Assert.Empty(FieldWords.Fields.Select(field => field.Field).Except(columns));
        Assert.Equal(FieldWords.Fields.Count, FieldWords.Fields.Select(field => field.Field).Distinct().Count());
        Assert.All(FieldWords.Fields, field => Assert.Null(FieldWords.Drawn("<p>" + field.Words + "</p>")));

        Assert.Equal("downtrend from averages 1, nights the new label holds 2", FieldWords.Settings("{\"downtrendFromAverages\": 1, \"nightsTheNewLabelHolds\": 2}"));

        // TSN's and APOG's raw tables, and BWXT's eliminations: a tag read as words, and a share rounding to nothing bare.
        Assert.Equal("Income Statement Location [Axis]: cost of revenue | Pork", FieldWords.FilerLabel("Income Statement Location [Axis]: us-gaap:CostOfRevenue | Pork"));
        Assert.Equal("Reportable Segment", FieldWords.FilerLabel("Reportable Segment"));

        // LVS's own tag, the flag FCX, LYV and PLTR draw as a line, and DHI's segment under a shorter run of capitals, its own name.
        Assert.Equal("Balance [Axis]: corporate expense", FieldWords.FilerLabel("Balance [Axis]: lvs:CorporateExpense"));
        Assert.Equal("Number of reportable segments not disclosed flag", FieldWords.FilerLabel("NumberOfReportableSegmentsNotDisclosedFlag"));
        Assert.Equal("Northwest | HomeBuildingOps", FieldWords.FilerLabel("Northwest | HomeBuildingOps"));

        var bars = new MarkRenderer().ShareBars(new SegmentReading("R9.htm", ThreeMonths.Ended, 3, 100m, 100m, [new SegmentRow("Eliminations", -0.01m, null, null, null, null, null, -0.03), new SegmentRow("Stores", 100.01m, null, null, null, null, null, 100.03)], null));

        Assert.Contains("Eliminations: 0.0% of revenue</title>", bars, StringComparison.Ordinal);
        Assert.Null(FieldWords.SignedZero(bars));
    }

    // TSN's raw table by hand: the group's heading reads the tag in words, and its attribute keeps what the filing states.
    // see: A page draws every field in the reader's words, and the proof fails a page drawing a field's code name
    [Fact]
    public async Task ARawTablesHeadingReadsAFilersTagInWords()
    {
        var table = FiledSegments([TheQuarter], FiledFigure("us-gaap_Revenues", "Net Sales", null, TheQuarter, "2044397000"));
        const string Label = "Income Statement Location [Axis]: us-gaap:CostOfRevenue";

        table["groups"] = new System.Text.Json.Nodes.JsonArray(new System.Text.Json.Nodes.JsonObject { ["label"] = Label, ["dimension"] = "axis=x", ["figures"] = new System.Text.Json.Nodes.JsonArray(FiledFigure("us-gaap_CostOfRevenue", "Cost", null, TheQuarter, "100"), FiledFigure("us-gaap_Flag", "NumberOfReportableSegmentsNotDisclosedFlag", null, TheQuarter, "100")) });

        var raw = await RawSegmentsOf(table);

        Assert.Contains($"data-segment=\"{Label}\"><th colspan=\"2\">Income Statement Location [Axis]: cost of revenue</th>", raw, StringComparison.Ordinal);
        Assert.Contains("<td>Number of reportable segments not disclosed flag</td>", raw, StringComparison.Ordinal);
        Assert.Null(FieldWords.Drawn(raw));
    }

    // The proof's check over each kind of code name a page drew, with what a reader may be shown beside them: a name in an
    // attribute, a script or a style is not drawn, a company's own capitals are its name, and a filing's file name is a name
    // the filer gave its document.
    // see: A page draws every field in the reader's words, and the proof fails a page drawing a field's code name
    [Theory]
    [InlineData("<p>No past setups were matched: the pick holds no high_ratio reading tonight.</p>", "high_ratio")]
    [InlineData("<text>The bars (macd_hist) are the distance between the two lines</text>", "macd_hist")]
    [InlineData("<text>Trend momentum (macd, the solid line)</text>", "macd")]
    [InlineData("<title>Relative strength over 14 sessions (rsi14)</title>", "rsi14")]
    [InlineData("<td>$1.2B, first filed 2026-09-02 as GrossProfit</td>", "GrossProfit")]
    [InlineData("<td>as us-gaap_Revenues</td>", "us-gaap_Revenues")]
    [InlineData("<th>Balance [Axis]: lvs:CorporateExpense</th>", "lvs:CorporateExpense")]
    [InlineData("<td>NumberOfReportableSegmentsNotDisclosedFlag</td>", "NumberOfReportableSegmentsNotDisclosedFlag")]
    public void TheProofFailsAPageDrawingAFieldsCodeName(string drawn, string name)
    {
        var page = ReportProof.Evaluate("FCN", drawn);

        Assert.Equal(ReportProof.Failed, VerdictOf(page, "field-names"));
        Assert.Contains(name, page.Verdicts.Single(verdict => verdict.Rule == "field-names").Why!, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("<tr data-name=\"macd_hist\" data-concept=\"GrossProfit\"><td>Gross profit</td></tr>")]
    [InlineData("<script>var high_ratio = 1;</script><style>.close_over_long { color: red; }</style><p>Words</p>")]
    [InlineData("<p>Results release, btsg-ex99_1.htm and press_release.htm, published by McKesson and AbbVie</p>")]
    [InlineData("<p>GroupPresident IntlVehiclePmts sold, Northwest | HomeBuildingOps, and Note: The quarter, at 10:30, from https://www.sec.gov/Archives</p>")]
    public void TheProofPassesAPageDrawingNoFieldsCodeName(string drawn) =>
        Assert.Equal(ReportProof.Passed, VerdictOf(ReportProof.Evaluate("FCN", drawn), "field-names"));
}
