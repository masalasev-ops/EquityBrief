using System.Globalization;
using System.Text.Json;
using EquityBrief.Core.Ledger;
using EquityBrief.Core.Providers;
using EquityBrief.Core.Time;
using EquityBrief.Tests.Harness;
using EquityBrief.Worker.Ledger;

namespace EquityBrief.Tests.Checks;

// fixture-expectations, 17.3: the archive's daily index and an 8-K's items read as the archive sent them, a filer's
// facts stored a concept a period as first filed, the five business readings worked by hand from the facts filed
// before a session, the night's setups reading them again once the refresh has stored them, and the fixture's night
// refreshing the one member that filed a report and asking nothing for the others.
// see: The SEC's facts are stored as first filed in a table the night reads, and a setup's business readings read those filed before its session
// see: The night refreshes the facts of the members that filed since its last read of the archive's daily index, after the close under its own limit
public partial class FixtureExpectations
{
    [Fact]
    public void TheArchivesDailyIndexAndAnAnnouncementsItemsAreReadAsTheArchiveSentThem()
    {
        var day = new DateOnly(2026, 9, 2);
        var filings = SecEdgarDailyIndex.Parse(File.ReadAllText(Path.Combine(Folder(), "daily-index-20260902.idx")), day);

        // Every line under the column line, the CIK padded to ten digits, the accession read off the file name.
        Assert.Equal(9, filings.Count);
        Assert.Contains(new DailyFiling("0001601046", "Keysight Technologies, Inc.", "10-Q", day, "0001601046-26-000036"), filings);
        Assert.Contains(new DailyFiling("0000789019", "MICROSOFT CORP", "8-K", day, "0001193125-26-380280"), filings);
        Assert.Equal(
            "Archives/edgar/daily-index/2026/QTR3/master.20260902.idx",
            SecEdgarDailyIndex.PathOf(day));
        Assert.Equal("Archives/edgar/daily-index/2026/QTR4/master.20261008.idx", SecEdgarDailyIndex.PathOf(new DateOnly(2026, 10, 8)));

        // An index that is not one refuses rather than reading as a day with no filing.
        Assert.Throws<FormatException>(() => SecEdgarDailyIndex.Parse("<html>not an index</html>", day));
        Assert.Throws<FormatException>(() => SecEdgarDailyIndex.Parse(SecEdgarDailyIndex.Columns + "\n----\n1601046|Keysight|10-Q|2026-09-02|edgar/data/1601046/x.txt\n", day));

        // Each 8-K page's items in their order: Microsoft's carry no results, Apple's amendment none either, and an
        // item numbered past 2.02 is not it.
        Assert.Equal(["7.01", "9.01"], SecEdgarDailyIndex.Items(File.ReadAllText(Path.Combine(Folder(), "filing-page-0001193125-26-380280.htm"))));
        Assert.Equal(["5.02"], SecEdgarDailyIndex.Items(File.ReadAllText(Path.Combine(Folder(), "filing-page-0001140361-26-035325.htm"))));
        Assert.Equal(["2.02", "9.01"], SecEdgarDailyIndex.Items(File.ReadAllText(Path.Combine(Folder(), "filing-types-AAPL-8k.htm"))));
        Assert.True(SecEdgarDailyIndex.CarriesResults(["2.02", "9.01"]));
        Assert.False(SecEdgarDailyIndex.CarriesResults(["7.01", "9.01"]));
        Assert.Empty(SecEdgarDailyIndex.Items("<div class=\"infoHead\">Period of Report</div>"));

        // A day the archive holds no index for is its storage's refusal, which a refusal page of the archive's is not.
        Assert.True(SecEdgarDailyIndex.NonePosted("<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n<Error><Code>AccessDenied</Code><Message>Access Denied</Message></Error>"));
        Assert.False(SecEdgarDailyIndex.NonePosted("<html><title>SEC.gov | Your Request Originates from an Undeclared Automated Tool</title></html>"));
    }

    static ConceptFact Figure(string start, string end, decimal value, string filed, string accession = "0000000001-26-000001", string form = "10-Q") =>
        new(DateOnly.Parse(start, CultureInfo.InvariantCulture), DateOnly.Parse(end, CultureInfo.InvariantCulture), value, DateOnly.Parse(filed, CultureInfo.InvariantCulture), form, accession);

    [Fact]
    public void AFilersFactsAreStoredAConceptAPeriodAsFirstFiled()
    {
        var facts = new Dictionary<string, IReadOnlyList<ConceptFact>>(StringComparer.Ordinal)
        {
            ["Revenues"] =
            [
                // One quarter stated twice: the earlier filing is the one stored, whatever the later says.
                Figure("2026-04-01", "2026-06-30", 100m, "2026-08-01", "a-1"),
                Figure("2026-04-01", "2026-06-30", 120m, "2027-08-01", "a-2"),
                // Nine months and a year are kept; six months are not a span the rule reads.
                Figure("2025-10-01", "2026-06-30", 290m, "2026-08-01", "a-1"),
                Figure("2025-10-01", "2026-09-30", 400m, "2026-11-01", "a-3", "10-K"),
                Figure("2026-01-01", "2026-06-30", 190m, "2026-08-01", "a-1"),
                // A period ending before the first stored is left out, and one ending on it kept.
                Figure("2014-10-01", "2014-12-31", 50m, "2015-02-01", "a-0"),
                Figure("2014-10-02", "2015-01-01", 51m, "2015-02-01", "a-0"),
            ],
            // A concept outside the six measures is not stored.
            ["Assets"] = [Figure("2026-04-01", "2026-06-30", 999m, "2026-08-01")],
        };

        var stored = FiledFacts.Stored(facts);

        Assert.Equal(
            [
                ("2014-10-02", "2015-01-01", 51m, "a-0"),
                ("2025-10-01", "2026-06-30", 290m, "a-1"),
                ("2026-04-01", "2026-06-30", 100m, "a-1"),
                ("2025-10-01", "2026-09-30", 400m, "a-3"),
            ],
            stored.Select(row => (row.Start.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), row.End.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), row.Value, row.Accession)).ToArray());
        Assert.All(stored, row => Assert.Equal("Revenues", row.Concept));
        Assert.Equal(new DateOnly(2015, 1, 1), FiledFacts.FirstPeriodEnd);
        Assert.Equal(16, FiledFacts.Concepts.Count);
    }

    // A filer's quarters as filed: revenue, gross profit and operating income for the quarter to 2026-06-30, the
    // quarter before and the same quarters a year before, and two fiscal years of net income and cash from operations.
    static IReadOnlyList<FiledFactRow> Business(string newestFiled)
    {
        FiledFactRow Row(string concept, string start, string end, decimal value, string filed) =>
            new(concept, DateOnly.Parse(start, CultureInfo.InvariantCulture), DateOnly.Parse(end, CultureInfo.InvariantCulture), value, DateOnly.Parse(filed, CultureInfo.InvariantCulture), "10-Q", "x");

        return
        [
            Row("Revenues", "2025-04-01", "2025-06-30", 100m, "2025-08-01"),
            Row("Revenues", "2025-01-01", "2025-03-31", 100m, "2025-05-01"),
            Row("Revenues", "2026-01-01", "2026-03-31", 110m, "2026-05-01"),
            Row("Revenues", "2026-04-01", "2026-06-30", 125m, newestFiled),
            Row("GrossProfit", "2025-04-01", "2025-06-30", 40m, "2025-08-01"),
            Row("GrossProfit", "2026-04-01", "2026-06-30", 55m, newestFiled),
            Row("OperatingIncomeLoss", "2025-04-01", "2025-06-30", 10m, "2025-08-01"),
            Row("OperatingIncomeLoss", "2026-04-01", "2026-06-30", 25m, newestFiled),
            Row("NetIncomeLoss", "2024-01-01", "2024-12-31", 20m, "2025-02-15"),
            Row("NetIncomeLoss", "2025-01-01", "2025-12-31", 40m, "2026-02-15"),
            Row("NetCashProvidedByUsedInOperatingActivities", "2025-01-01", "2025-12-31", 50m, "2026-02-15"),
        ];
    }

    [Fact]
    public void ABusinessReadingIsWorkedByHandFromTheFactsFiledBeforeItsSession()
    {
        var facts = Business("2026-08-03");

        // The session after the newest quarter was filed: its revenue 125 over the year before's 100 is a growth of
        // 0.25; the quarter before grew 110 over 100, 0.10, so the growth moved 0.15; the gross margin 55 over 125,
        // 0.44, against 40 over 100, 0.40; the operating margin 25 over 125, 0.20, against 10 over 100, 0.10; and the
        // newest year's cash 50 over its income 40, 1.25.
        var after = FiledFacts.Read(facts, new DateOnly(2026, 8, 4));

        Assert.Equal(0.25, after.RevenueGrowth!.Value, 12);
        Assert.Equal(0.15, after.GrowthChange!.Value, 12);
        Assert.Equal(0.04, after.GrossMarginChange!.Value, 12);
        Assert.Equal(0.10, after.OperatingMarginChange!.Value, 12);
        Assert.Equal(1.25, after.CashOverIncome!.Value, 12);

        // On the session the quarter was filed it is not read yet: the newest read is the quarter to 2026-03-31, 110
        // over its year before's 100, a growth of 0.10, while the quarter before it and its gross profit and operating
        // income are not stated, so the change and both margins read none; the year's cash is read.
        var on = FiledFacts.Read(facts, new DateOnly(2026, 8, 3));

        Assert.Equal(0.10, on.RevenueGrowth!.Value, 12);
        Assert.Null(on.GrowthChange);
        Assert.Null(on.GrossMarginChange);
        Assert.Null(on.OperatingMarginChange);
        Assert.Equal(1.25, on.CashOverIncome!.Value, 12);

        // A year whose income is nothing or less reads no cash over it, and a filer with no facts reads none at all.
        var loss = FiledFacts.Read([.. facts.Where(fact => fact.Concept != "NetIncomeLoss" || fact.End.Year != 2025), facts.Single(fact => fact.Concept == "NetIncomeLoss" && fact.End.Year == 2025) with { Value = 0m }], new DateOnly(2026, 8, 4));

        Assert.Null(loss.CashOverIncome);
        Assert.Equal(new BusinessReading(null, null, null, null, null), FiledFacts.Read([], new DateOnly(2026, 8, 4)));
    }

    [Fact]
    public async Task TheNightsSetupsReadTheirBusinessReadingsAgainOnceTheRefreshHasStoredTheFacts()
    {
        using var store = new TemporaryStore().Migrated();

        store.Execute("INSERT INTO company (ticker, fetched_at, cik) VALUES ('AAA', '2026-08-01T23:40:00.000Z', '0000000101');");

        foreach (var (source, session) in new[] { ("night", "2026-08-04"), ("history", "2026-08-04"), ("night", "2026-08-03") })
        {
            store.Execute(
                "INSERT INTO setup (index_code, family, ticker, session_date, rule, live_pass, entry, stop, cap, end, settled, source, pin) "
                + $"VALUES ('GSPC', '{(source == "night" ? "pullback" : "breakout")}', 'AAA', '{session}', 'the live rule', 0, '10', '9', 21, 'open', 0, '{source}', '{LedgerReadings.Version}');");
        }

        // The night's setup on the session read none before the refresh stored the filer's quarters, filed before it.
        static string Day(DateOnly day) => day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

        foreach (var fact in Business("2026-08-03"))
        {
            store.Execute(
                "INSERT INTO filed_fact (cik, concept, period_start, period_end, dollars, filed, form, accession, run_id) VALUES ("
                + $"'0000000101', '{fact.Concept}', '{Day(fact.Start)}', '{Day(fact.End)}', '{fact.Value.ToString(CultureInfo.InvariantCulture)}', '{Day(fact.Filed)}', 'x', 'x', 'refresh');");
        }

        var read = await new SetupLedger(FixedClock.At(new DateTimeOffset(2026, 8, 4, 23, 30, 0, TimeSpan.Zero), SessionZones.UnitedStates), store.DatabaseFile)
            .BusinessAgainAsync(new DateOnly(2026, 8, 4), ["AAA"]);

        // The night's row on the session read again, at the readings worked by hand above; the history's row and the
        // night's row of another session left as they were.
        Assert.Equal(1, read);
        Assert.Equal(
            ["2026-08-03|night|", "2026-08-04|history|", "2026-08-04|night|0.25|0.15|1.25"],
            Query(store, "SELECT session_date || '|' || source || '|' || COALESCE(ROUND(revenue_growth, 6) || '|' || ROUND(growth_change, 6) || '|' || ROUND(cash_over_income, 6), '') FROM setup ORDER BY session_date, source;"));
    }

    [Fact]
    public async Task TheFixturesNightRefreshesTheOneMemberThatFiledAReportAndAsksNothingForTheOthers()
    {
        var expected = Expected("filings");

        Assert.Equal("derived", expected.GetProperty("derivation").GetString());
        Assert.Equal(["filed_fact", "filed_fact_pull", "filing_day"], expected.GetProperty("tables").EnumerateArray().Select(table => table.GetString()!).ToArray());

        using var store = await FixtureReplay.ReplayedAsync();

        // The days read, the one the archive posted none for, and the night's own not yet posted, on the step's row.
        Assert.Equal(
            expected.GetProperty("daysRead").EnumerateArray().Select(day => day.GetString()!).ToArray(),
            Query(store, "SELECT day FROM filing_day ORDER BY day;"));
        Assert.Equal([expected.GetProperty("dayNotPosted").GetString()!], Query(store, "SELECT day FROM filing_day WHERE posted = 0;"));
        Assert.Contains($"{expected.GetProperty("notYetPosted").GetString()} not yet posted", Query(store, $"SELECT detail FROM run_log WHERE run_id = 'replay-filings' AND stage = '{FilingsRefresher.Stage}';").Single(), StringComparison.Ordinal);
        Assert.Equal([expected.GetProperty("documents").GetInt32().ToString(CultureInfo.InvariantCulture)], Query(store, "SELECT network_requests FROM run_log WHERE run_id = 'replay-filings';"));

        // The announcements whose pages were asked, the two members' 8-Ks of the week, each a capture of the fixture.
        var pages = expected.GetProperty("pagesAsked").EnumerateArray().Select(page => page.GetString()!).ToArray();

        Assert.Contains(FormattableString.Invariant($"{pages.Length} 8-K page(s)"), Query(store, "SELECT detail FROM run_log WHERE run_id = 'replay-filings';").Single(), StringComparison.Ordinal);
        Assert.All(pages, accession => Assert.True(File.Exists(Path.Combine(Folder(), $"filing-page-{accession}.htm")), accession));

        // Keysight alone refreshed, its filer the one its quarters' company names.
        Assert.Equal(["0001601046"], Query(store, "SELECT cik FROM filed_fact_pull;"));
        Assert.Equal(["0001601046"], Query(store, "SELECT DISTINCT cik FROM filed_fact;"));
        Assert.Equal(expected.GetProperty("refreshed").EnumerateArray().Select(ticker => ticker.GetString()!).ToArray(), Query(store, "SELECT ticker FROM company WHERE cik = '0001601046' GROUP BY ticker;"));

        // Its facts worked out here from the capture with this test's own reading: each dollar figure of a measure's
        // concept for a quarter, nine months or a year ending on or after 2015-01-01, the earliest filing of each period.
        using var capture = JsonDocument.Parse(File.ReadAllText(Path.Combine(Folder(), "company-facts-KEYS.json")));
        var worked = new SortedSet<string>(StringComparer.Ordinal);

        foreach (var concept in capture.RootElement.GetProperty("facts").GetProperty("us-gaap").EnumerateObject().Where(concept => FiledFacts.Concepts.Contains(concept.Name)))
        {
            var byPeriod = new Dictionary<(string Start, string End), (string Filed, string Accession, decimal Value)>();

            foreach (var fact in concept.Value.GetProperty("units").GetProperty("USD").EnumerateArray().Where(fact => fact.TryGetProperty("start", out _)))
            {
                var start = fact.GetProperty("start").GetString()!;
                var end = fact.GetProperty("end").GetString()!;
                var days = DateOnly.Parse(end, CultureInfo.InvariantCulture).DayNumber - DateOnly.Parse(start, CultureInfo.InvariantCulture).DayNumber + 1;

                if (string.CompareOrdinal(end, "2015-01-01") < 0 || !(days is >= 80 and <= 100 or >= 260 and <= 285 or >= 350 and <= 380))
                {
                    continue;
                }

                var candidate = (fact.GetProperty("filed").GetString()!, fact.GetProperty("accn").GetString()!, fact.GetProperty("val").GetDecimal());

                if (!byPeriod.TryGetValue((start, end), out var held) || string.CompareOrdinal(candidate.Item1 + candidate.Item2, held.Filed + held.Accession) < 0)
                {
                    byPeriod[(start, end)] = candidate;
                }
            }

            foreach (var ((start, end), (filed, accession, value)) in byPeriod)
            {
                worked.Add($"{concept.Name}|{start}|{end}|{filed}|{accession}|{value.ToString(CultureInfo.InvariantCulture)}");
            }
        }

        Assert.True(worked.Count >= 4, $"Worked {worked.Count} fact(s) from the capture, expected at least 4.");
        Assert.Equal(
            [.. worked],
            Query(store, "SELECT concept || '|' || period_start || '|' || period_end || '|' || filed || '|' || accession || '|' || dollars FROM filed_fact ORDER BY 1;"));

        // And the others the expectation names were never asked for.
        foreach (var ticker in expected.GetProperty("notAskedFor").EnumerateArray().Select(name => name.GetString()!))
        {
            Assert.Empty(Query(store, $"SELECT f.cik FROM filed_fact_pull f JOIN company c ON c.cik = f.cik WHERE c.ticker = '{ticker}';"));
        }
    }
}
