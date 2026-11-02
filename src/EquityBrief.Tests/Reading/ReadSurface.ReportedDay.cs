using System.Text.Json.Nodes;
using EquityBrief.Api.Reading;
using EquityBrief.Core.Cards;
using EquityBrief.Tests.Harness;
using Microsoft.Data.Sqlite;

namespace EquityBrief.Tests.Reading;

// Where a company's reports stand: a quarter's reported day read from its stored row and then the calendar, and the
// provider's expected day only where neither says, on the numbers' row, the line beneath them and the proof.
// see: A quarter reported before its filing is stored is labelled as the release's, its reported day and next report read from its stored row and then the calendar and its release quoted where it says what it reported
public partial class ReadSurface
{
    // Section 17's row of the report page's day limits, which this check reaches.
    internal static readonly string[] PageDayLimitRows = [CheckReach.Key(Scope.LimitsTable, "Report page's day limits")];

    // Each worked by hand from what the store held on the night of 2026-10-30, the name it came from beside it.
    [Fact]
    public void AQuartersReportedDayIsItsStoredRowsThenTheCalendarsAndTheProvidersOnlyWhereNeitherSays()
    {
        var night = new DateOnly(2026, 10, 30);
        var september = new DateOnly(2026, 9, 30);
        var june = new DateOnly(2026, 6, 30);

        static (DateOnly, DateOnly?) Row(DateOnly quarter, DateOnly reported) => (quarter, reported);

        ReportedDay Read(string? expected, (DateOnly, DateOnly?)[] stored, params DateOnly[] calendar) =>
            ReportedDay.Of(expected is null ? null : "2026-09-30", expected, night, stored, calendar);

        // MSFT: the provider expects 2026-11-04, and the quarter stands reported on 2026-10-28; its old calendar days say nothing.
        Assert.Equal(new ReportedDay(september, new DateOnly(2026, 10, 28), null), Read("2026-11-04", [Row(september, new DateOnly(2026, 10, 28))], new DateOnly(2026, 1, 28)));

        // TSLA: the provider's day has passed and is not the day stored.
        Assert.Equal(new ReportedDay(september, new DateOnly(2026, 10, 21), null), Read("2026-10-28", [Row(september, new DateOnly(2026, 10, 21))]));

        // KEY: reported, and the calendar dates the next.
        Assert.Equal(new ReportedDay(september, new DateOnly(2026, 10, 20), new DateOnly(2027, 1, 20)), Read("2026-10-20", [Row(september, new DateOnly(2026, 10, 20))], new DateOnly(2027, 1, 20)));

        // AME: the provider's day has passed, no row is stored and the calendar dates the report after the night, so it has not
        // reported; its calendar day before the quarter's end is the quarter before's.
        Assert.Equal(new ReportedDay(null, null, new DateOnly(2026, 11, 3)), Read("2026-10-29", [Row(june, new DateOnly(2026, 8, 4))], new DateOnly(2026, 8, 4), new DateOnly(2026, 11, 3)));

        // WY: no row stored, the calendar's day on or before the night is the reported day and its later one the next.
        Assert.Equal(new ReportedDay(september, new DateOnly(2026, 10, 29), new DateOnly(2027, 1, 22)), Read("2026-10-29", [], new DateOnly(2026, 10, 29), new DateOnly(2027, 1, 22)));

        // DLR: the calendar's day stands over the provider's.
        Assert.Equal(new ReportedDay(september, new DateOnly(2026, 10, 29), null), Read("2026-10-22", [], new DateOnly(2026, 10, 29)));

        // At their edges: a row and a calendar day on the night itself are reported; a row a day after it is not.
        Assert.Equal(new ReportedDay(september, night, null), Read("2026-11-04", [Row(september, night)]));
        Assert.Equal(new ReportedDay(september, night, null), Read("2026-11-04", [], night));
        Assert.Equal(new ReportedDay(null, null, new DateOnly(2026, 11, 4)), Read("2026-11-04", [Row(september, night.AddDays(1))]));

        // A calendar day on the quarter's own last day is before its report, and the night itself is no next report.
        Assert.Equal(new ReportedDay(null, null, new DateOnly(2026, 11, 4)), Read("2026-11-04", [], september));
        Assert.Equal(new ReportedDay(september, night, new DateOnly(2026, 10, 31)), Read("2026-11-04", [], night, night.AddDays(1)));

        // A stored quarter later than the one the provider expects is the one reported, and of two the newest.
        Assert.Equal(new ReportedDay(september, new DateOnly(2026, 10, 21), null), ReportedDay.Of("2026-06-30", "2026-07-20", night, [Row(june, new DateOnly(2026, 7, 21)), Row(september, new DateOnly(2026, 10, 21))], []));

        // A stored row with no report day says nothing.
        Assert.Equal(new ReportedDay(null, null, new DateOnly(2026, 11, 4)), Read("2026-11-04", [(september, null)]));

        // Neither says: the provider's day, as reported where the night has passed it and as the next where not.
        Assert.Equal(new ReportedDay(september, new DateOnly(2026, 10, 29), null), Read("2026-10-29", []));
        Assert.Equal(new ReportedDay(null, null, new DateOnly(2026, 11, 4)), Read("2026-11-04", []));

        // RLI: the filing names no quarter, so only the calendar or a day still ahead is the next, and nothing is reported.
        Assert.Equal(new ReportedDay(null, null, new DateOnly(2027, 1, 20)), ReportedDay.Of(null, null, night, [Row(september, new DateOnly(2026, 10, 19))], [new DateOnly(2027, 1, 20)]));
        Assert.Equal(new ReportedDay(null, null, null), ReportedDay.Of(null, null, night, [Row(september, new DateOnly(2026, 10, 19))], []));
        Assert.Equal(new ReportedDay(null, null, new DateOnly(2026, 11, 4)), ReportedDay.Of(null, "2026-11-04", night, [], []));
        Assert.Equal(new ReportedDay(null, null, null), ReportedDay.Of(null, "2026-10-29", night, [], []));

        // Two calendar days after the quarter's end and on or before the night: the latest is the day it reported.
        Assert.Equal(new ReportedDay(september, new DateOnly(2026, 10, 28), null), ReportedDay.Of("2026-09-30", null, night, [], [new DateOnly(2026, 10, 7), new DateOnly(2026, 10, 28)]));

        // No night known: the provider's day as it stands.
        Assert.Equal(new ReportedDay(null, null, new DateOnly(2026, 11, 4)), ReportedDay.Of("2026-09-30", "2026-11-04", null, [Row(september, new DateOnly(2026, 10, 28))], [new DateOnly(2027, 1, 20)]));
    }

    // The numbers' row and the line beneath them say what the standing reads, and a filing naming no next report draws the
    // calendar's day or the estimate, "not filed" standing only where neither is held.
    [Fact]
    public async Task TheNumbersDrawTheStoredReportedDayAndTheCalendarsNextReportAndNotFiledOnlyWhereNeitherIsHeld()
    {
        using var store = await WithFundamentals(withTheArchive: true);

        var filings = await Api(store).FundamentalsAsync(Name);
        var estimated = JsonNode.Parse(StoredPayload(store, Name))!["estimated"]!;
        var reportDate = DateOnly.ParseExact(estimated["reportDate"]!.GetValue<string>(), "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
        var periodEnd = DateOnly.ParseExact(estimated["periodEnd"]!.GetValue<string>(), "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
        var onTheDay = new ConceptFigures(null, reportDate, null, null);
        var before = new ConceptFigures(null, reportDate.AddDays(-1), null, null);

        // The provider's day still ahead, and the quarter stored as reported a week before it: the page says that day, with
        // no consensus for a quarter it has reported.
        var stored = NameScreen.Numbers(filings, before, null, null, [new QuarterDatesRow(periodEnd, null, reportDate.AddDays(-7))]);

        Assert.Contains(FormattableString.Invariant($"<p class=\"estimate\" data-estimated-quarter=\"{periodEnd:yyyy-MM-dd}\" data-reported=\"{reportDate.AddDays(-7):yyyy-MM-dd}\">The quarter to {periodEnd:yyyy-MM-dd} was reported on {reportDate.AddDays(-7):yyyy-MM-dd}, and the next report is not announced."), stored, StringComparison.Ordinal);
        Assert.Contains(FormattableString.Invariant($"<small>the quarter to {periodEnd:yyyy-MM-dd} was reported on {reportDate.AddDays(-7):yyyy-MM-dd}</small>"), stored, StringComparison.Ordinal);
        Assert.DoesNotContain("is expected on", stored, StringComparison.Ordinal);
        Assert.DoesNotContain("a share, for the quarter to", stored, StringComparison.Ordinal);

        // The provider's day passed and the calendar dating the report later: not reported, and the calendar's day with its
        // consensus for the quarter.
        var later = reportDate.AddDays(5);
        var dated = NameScreen.Numbers(filings, onTheDay, null, null, null, [later]);

        Assert.DoesNotContain("was reported on", dated, StringComparison.Ordinal);
        Assert.Contains(FormattableString.Invariant($"<dd data-reportDate=\"{later:yyyy-MM-dd}\"><b>{later:yyyy-MM-dd}</b>"), dated, StringComparison.Ordinal);
        Assert.Contains(FormattableString.Invariant($"and is expected on {later:yyyy-MM-dd}, with a consensus estimate"), dated, StringComparison.Ordinal);
        Assert.Contains(FormattableString.Invariant($"a share, for the quarter to {periodEnd:yyyy-MM-dd}"), dated, StringComparison.Ordinal);

        // A calendar day after the quarter's end and on or before the night is the reported day, and its later one the next.
        var reported = reportDate.AddDays(-1);
        var both = NameScreen.Numbers(filings, onTheDay, null, null, null, [reported, later]);

        Assert.Contains(FormattableString.Invariant($"was reported on {reported:yyyy-MM-dd}, and the next report is on {later:yyyy-MM-dd}, as the calendar dates it.</p>"), both, StringComparison.Ordinal);
        Assert.Contains(FormattableString.Invariant($"<dd data-reportDate=\"{later:yyyy-MM-dd}\"><b>{later:yyyy-MM-dd}</b><small>as the calendar dates it</small><small>the quarter to {periodEnd:yyyy-MM-dd} was reported on {reported:yyyy-MM-dd}</small></dd>"), both, StringComparison.Ordinal);

        // A filing that names no next report.
        var payload = JsonNode.Parse(filings[0].Payload)!.AsObject();

        payload.Remove("estimated");

        FilingRow[] unfiled = [filings[0] with { Payload = payload.ToJsonString() }, .. filings.Skip(1)];
        var estimate = (new DateOnly(2026, 1, 21), new DateOnly(2027, 1, 20));

        var onTheCalendar = NameScreen.Numbers(unfiled, onTheDay, null, estimate, null, [later]);

        Assert.Contains(FormattableString.Invariant($"<dd data-reportDate=\"{later:yyyy-MM-dd}\"><b>{later:yyyy-MM-dd}</b>"), onTheCalendar, StringComparison.Ordinal);
        Assert.DoesNotContain("not filed", onTheCalendar, StringComparison.Ordinal);

        var onAnEstimate = NameScreen.Numbers(unfiled, onTheDay, null, estimate);

        Assert.Contains("<dd data-reportDate=\"unfiled\" data-estimated=\"2027-01-20\"><b>not announced</b><small class=\"estimated\">estimated about 2027-01-20, from its report of 2026-01-21 a year on</small></dd>", onAnEstimate, StringComparison.Ordinal);
        Assert.Equal(ReportProof.Passed, VerdictOf(ReportProof.Evaluate(Name, onAnEstimate), "next-report-estimated"));

        var neither = NameScreen.Numbers(unfiled, onTheDay);

        Assert.Contains("<dd class=\"degraded\" data-reportDate=\"absent\">not filed</dd>", neither, StringComparison.Ordinal);

        // The filing's line on a consensus stays true of a filing naming none.
        Assert.Contains("data-absent=\"estimate\"", onTheCalendar, StringComparison.Ordinal);
    }

    // The proof holds a next report estimated clear of the newest report the page draws, CRH's shape failing: reported on
    // 2026-10-29 and estimated about 2026-11-04, 6 days on, where 46 days on passes and 45 does not.
    // see: A next report not on file is estimated from the first report a year back falling clear of the newest report known, and a pick's card restates its fill once the open prints
    [Fact]
    public void TheProofFailsANextReportEstimatedWithinFortyFiveDaysOfTheNewestReportThePageDraws()
    {
        static string Line(string reported) => $"<p class=\"estimate\" data-estimated-quarter=\"2026-09-30\" data-reported=\"{reported}\">";
        static string Row(string estimated, string how = "reported") => $"<dd data-reportDate=\"{how}\" data-estimated=\"{estimated}\">";
        static string Card(string day) => $"id=\"quarter\"><div class=\"spine\"><span class=\"dl-k\">Filed</span><b>{day}</b>";

        Assert.Equal(ReportProof.Failed, VerdictOf(ReportProof.Evaluate(Name, Line("2026-10-29") + Row("2026-11-04")), "next-report-clear"));
        Assert.Equal(ReportProof.Failed, VerdictOf(ReportProof.Evaluate(Name, Line("2026-10-29") + Row("2026-12-13")), "next-report-clear"));
        Assert.Equal(ReportProof.Passed, VerdictOf(ReportProof.Evaluate(Name, Line("2026-10-29") + Row("2026-12-14")), "next-report-clear"));
        Assert.Equal(ReportProof.Passed, VerdictOf(ReportProof.Evaluate(Name, Line("2026-10-29") + Row("2027-02-17")), "next-report-clear"));

        // A row naming no reported line is read against the day the latest quarter draws, the newer of the two where both are drawn.
        Assert.Equal(ReportProof.Failed, VerdictOf(ReportProof.Evaluate(Name, Card("2026-10-29") + Row("2026-11-04", "unfiled")), "next-report-clear"));
        Assert.Equal(ReportProof.Failed, VerdictOf(ReportProof.Evaluate(Name, Card("2026-10-29") + Line("2026-07-30") + Row("2026-11-04")), "next-report-clear"));

        // No estimate, or no day the company reported, leaves nothing to read.
        Assert.Equal(ReportProof.Unexamined, VerdictOf(ReportProof.Evaluate(Name, Line("2026-10-29")), "next-report-clear"));
        Assert.Equal(ReportProof.Unexamined, VerdictOf(ReportProof.Evaluate(Name, Row("2026-11-04")), "next-report-clear"));
    }

    // The page reads the calendar's report days from the store and hands them to the numbers: the night of the fixture's store
    // is 2026-09-04, and the provider expects 2026-10-29, as the calendar's own next day does until an earlier one is stored.
    [Fact]
    public async Task ThePageReadsItsNextReportFromTheCalendarsStoredDays()
    {
        using var store = await WithFundamentals(withTheArchive: true);
        using var host = new Host(store.Root);
        using var client = host.CreateClient();

        using (var connection = new SqliteConnection($"Data Source={store.DatabaseFile}"))
        {
            connection.Open();

            using var command = connection.CreateCommand();

            command.CommandText = $"INSERT INTO calendar (ticker, event_date, kind, timing, detail, observed_at) VALUES ('{Name}', '2026-09-20', 'earnings', 'before', '{{}}', '2026-10-01T21:00:00Z');";
            command.ExecuteNonQuery();
        }

        var page = await client.GetStringAsync($"/screens/name/{Name}");

        Assert.Contains("<dt>Next report</dt><dd data-reportDate=\"2026-09-20\">", page, StringComparison.Ordinal);

        // And the quarters of the fetch the night's reading read: the provider's quarter stored as reported the day before
        // the night, which the page then says.
        using (var connection = new SqliteConnection($"Data Source={store.DatabaseFile}"))
        {
            connection.Open();

            using var command = connection.CreateCommand();

            var readings = EquityBrief.Core.Quarters.QuarterReadings.Of(EquityBrief.Tests.Checks.FixtureExpectations.Quarters(4), null, null);

            command.CommandText =
                $"INSERT INTO fundamental_reading (ticker, session_date, state, read_from, fetched_at, awaited, readings) VALUES ('{Name}', '2026-09-04', '{readings.State}', NULL, '2026-09-04T21:00:00.000Z', NULL, $readings);"
                + $"INSERT INTO reported_quarter (ticker, fetched_at, session_date, period_end, report_date, revenue) VALUES ('{Name}', '2026-09-04T21:00:00.000Z', '2026-09-04', '2026-09-30', '2026-09-03', '1');";
            command.Parameters.AddWithValue("$readings", readings.ToJson());
            command.ExecuteNonQuery();
        }

        var reported = await client.GetStringAsync($"/screens/name/{Name}");

        Assert.Contains("<small>the quarter to 2026-09-30 was reported on 2026-09-03</small>", reported, StringComparison.Ordinal);
    }

    // The page hands the estimate the day the calendar says the company reported, which no stored quarter holds: by hand, on
    // the fixture's night of 2026-09-04 a report of 2025-09-10 a year on is 2026-09-09, 8 days after the calendar's report of
    // 2026-09-01, so the estimate is a year on from 2025-12-10, 2026-12-09. A day after the night is none the page knows.
    // see: A next report not on file is estimated from the first report a year back falling clear of the newest report known, and a pick's card restates its fill once the open prints
    [Fact]
    public async Task ThePageEstimatesTheNextReportClearOfTheDayTheCalendarSaysItReported()
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
                + $"UPDATE fundamentals SET payload = json_set(payload, '$.estimated.periodEnd', '2026-06-30') WHERE ticker = '{Name}';"
                + $"UPDATE fundamentals_snapshot SET payload = json_set(payload, '$.estimated.periodEnd', '2026-06-30') WHERE ticker = '{Name}';"
                + $"INSERT INTO calendar (ticker, event_date, kind, timing, detail, observed_at) VALUES ('{Name}', '2026-09-01', 'earnings', 'before', '{{}}', '2026-10-01T21:00:00Z');"
                + $"INSERT INTO fundamental_reading (ticker, session_date, state, read_from, fetched_at, awaited, readings) VALUES ('{Name}', '2026-09-04', '{readings.State}', NULL, '2026-09-04T21:00:00.000Z', NULL, $readings);"
                + $"INSERT INTO reported_quarter (ticker, fetched_at, session_date, period_end, report_date, revenue) VALUES ('{Name}', '2026-09-04T21:00:00.000Z', '2026-09-04', '2025-03-31', '2025-06-10', '1');"
                + $"INSERT INTO reported_quarter (ticker, fetched_at, session_date, period_end, report_date, revenue) VALUES ('{Name}', '2026-09-04T21:00:00.000Z', '2026-09-04', '2025-06-30', '2025-09-10', '1');"
                + $"INSERT INTO reported_quarter (ticker, fetched_at, session_date, period_end, report_date, revenue) VALUES ('{Name}', '2026-09-04T21:00:00.000Z', '2026-09-04', '2025-09-30', '2025-12-10', '1');";
            command.Parameters.AddWithValue("$readings", readings.ToJson());
            command.ExecuteNonQuery();
        }

        var page = await client.GetStringAsync($"/screens/name/{Name}");

        Assert.Contains("data-reportDate=\"reported\" data-estimated=\"2026-12-09\"", page, StringComparison.Ordinal);
        Assert.Contains("from its report of 2025-12-10 a year on", page, StringComparison.Ordinal);
        Assert.DoesNotContain("about 2026-09-09", page, StringComparison.Ordinal);
    }

    // The proof fails each shape the store showed on the night of 2026-10-30 and passes the same page as it is now drawn.
    [Fact]
    public void TheProofReadsTheNextReportAndTheReportedDayAgainstTheLatestQuarterAndTheCalendar()
    {
        static string Row(string day, string words = "") => $"<div><dt>Next report</dt><dd data-reportDate=\"{day}\"><b>{day}</b>{words}</dd></div>";
        static string Around(string day, string declared) => $"<p class=\"next-report-around\" data-date=\"{day}\" data-declared=\"{declared}\">";
        static string Verdict(string page) => VerdictOf(ReportProof.Evaluate(Name, page), "report-day");

        const string Latest = "<section id=\"quarter\"><div class=\"spine\"><span class=\"dl-k\">Reported</span><b>2026-10-28</b></div><h2>The quarter to 2026-09-30, line by line</h2></section>";
        const string Filed = "<section id=\"quarter\"><div class=\"spine\"><span class=\"dl-k\">Filed</span><b>2026-08-04</b></div><h2>The quarter to 2026-06-30, line by line</h2></section>";
        const string NotAnnounced = "<div><dt>Next report</dt><dd data-reportDate=\"reported\" data-estimated=\"2027-01-27\"><b>not announced</b></dd></div>";

        // MSFT: a next report naming the quarter drawn as reported, or an earlier one; the quarter after it passes, and a
        // quarter drawn from its filing is not read as reported.
        Assert.Equal(ReportProof.Failed, Verdict(Latest + Row("2026-11-04", "<small>consensus 4.69 a share, for the quarter to 2026-09-30</small>")));
        Assert.Equal(ReportProof.Failed, Verdict(Latest + Row("2026-11-04", "<small>consensus 4.69 a share, for the quarter to 2026-06-30</small>")));
        Assert.Equal(ReportProof.Passed, Verdict(Latest + Row("2027-01-27", "<small>consensus 4.69 a share, for the quarter to 2026-12-31</small>")));
        Assert.Equal(ReportProof.Passed, Verdict(Filed + Row("2026-11-03", "<small>consensus 2.10 a share, for the quarter to 2026-06-30</small>")));

        // TSLA: the line beneath the numbers saying another day than the latest quarter draws; a line of another quarter is
        // not read against it.
        Assert.Equal(ReportProof.Failed, Verdict(Latest + NotAnnounced + "<p class=\"estimate\" data-estimated-quarter=\"2026-09-30\" data-reported=\"2026-10-21\">"));
        Assert.Equal(ReportProof.Passed, Verdict(Latest + NotAnnounced + "<p class=\"estimate\" data-estimated-quarter=\"2026-09-30\" data-reported=\"2026-10-28\">"));
        Assert.Equal(ReportProof.Passed, Verdict(Latest + NotAnnounced + "<p class=\"estimate\" data-estimated-quarter=\"2026-06-30\" data-reported=\"2026-10-21\">"));

        // KEY and AME: not announced, or another day, where the calendar dates it; an estimate beside not announced passes.
        Assert.Equal(ReportProof.Failed, Verdict(NotAnnounced + Around("2027-01-20", "yes")));
        Assert.Equal(ReportProof.Passed, Verdict(NotAnnounced + Around("2027-01-27", "no")));
        Assert.Equal(ReportProof.Failed, Verdict(Row("2026-11-04") + Around("2026-11-03", "yes")));
        Assert.Equal(ReportProof.Passed, Verdict(Row("2026-11-03") + Around("2026-11-03", "yes")));

        // RLI: not filed where the calendar lists one, dated or estimated, and passing where it lists none.
        const string NotFiled = "<div><dt>Next report</dt><dd class=\"degraded\" data-reportDate=\"absent\">not filed</dd></div>";

        Assert.Equal(ReportProof.Failed, Verdict(NotFiled + Around("2027-01-20", "no")));
        Assert.Equal(ReportProof.Passed, Verdict(NotFiled));

        // No row is unexamined.
        Assert.Equal(ReportProof.Unexamined, Verdict(Latest));
    }
}
