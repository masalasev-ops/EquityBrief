using System.Net;
using System.Text.RegularExpressions;
using EquityBrief.Api.Reading;
using EquityBrief.Core.Report;
using EquityBrief.Core.Time;

namespace EquityBrief.Tests.Reading;

// read-surface, from the 18.2 correction of 2026-10-21: the revenue estimates the report reads, each kept by a fetch made
// before the quarter's release as the calendar's timing on its report date places it in New York, the quarter's own read
// under either of the provider's words for it, the fiscal year's read in the quarter's place within a week of its end, and
// the instant the stock's estimates are kept from as the store stood on the page's night, read over a constructed store;
// and the chart page's revenue against the estimate the report reads, an implied one named so and why where none is read.
// see: The latest quarter is read line by line against the estimates kept before its release and the quarter a year earlier, a financial company's revenue against none and a fiscal fourth quarter's against its year's estimate less three quarters
public partial class ReadSurface
{
    static readonly DateOnly EstimatesNight = new(2026, 10, 20);

    [Fact]
    public async Task TheReportReadsTheRevenueEstimatesKeptBeforeTheReleaseAndTheYearsWhereTheQuarterEndsIt()
    {
        using var store = new TemporaryStore().Migrated();
        var read = new ReadApi(store.DatabaseFile, FixedClock.At(new DateTimeOffset(2026, 10, 21, 12, 0, 0, TimeSpan.Zero), SessionZones.UnitedStates));

        // EST's quarter to 2026-09-30, reported on 2026-10-14, with three fetches that day: at 00:41 in New York, a
        // millisecond before the open and at the open.
        store.Execute("INSERT INTO reported_quarter (ticker, fetched_at, session_date, period_end, report_date, revenue) VALUES ('EST', '2026-10-14T23:00:00.000Z', '2026-10-14', '2026-09-30', '2026-10-14', '125');");
        store.Execute(
            "INSERT INTO estimate_trend (ticker, fetched_at, period, period_end, revenue_average) VALUES "
            + "('EST', '2026-10-14T04:41:38.890Z', '0q', '2026-09-30', '100'), "
            + "('EST', '2026-10-14T13:29:59.999Z', '0q', '2026-09-30', '110'), "
            + "('EST', '2026-10-14T13:30:00.000Z', '0q', '2026-09-30', '120');");

        async Task<ReportInputs> Timed(string ticker, DateOnly reported, string? timing)
        {
            store.Execute($"DELETE FROM calendar WHERE ticker = '{ticker}';");

            if (timing is not null)
            {
                store.Execute(FormattableString.Invariant($"INSERT INTO calendar (ticker, event_date, kind, timing, detail, observed_at) VALUES ('{ticker}', '{reported:yyyy-MM-dd}', 'earnings', '{timing}', '{{}}', '2026-10-01T21:00:00Z');"));
            }

            return await read.ReportInputsAsync(ticker, null, EstimatesNight);
        }

        // Released before the open, the newest fetch kept before it is the one a millisecond before: 110, and the stock's
        // estimates are kept from its first fetch.
        var before = (await Timed("EST", new DateOnly(2026, 10, 14), "before")).Estimates!;

        Assert.Equal(new RevenueEstimate(110m, new DateOnly(2026, 10, 14)), before.Quarter);
        Assert.Equal(new DateTimeOffset(2026, 10, 14, 13, 30, 0, TimeSpan.Zero), before.Released);
        Assert.Equal(new DateTimeOffset(2026, 10, 14, 4, 41, 38, 890, TimeSpan.Zero), before.KeptFrom);

        // Released after the close, the fetch at the open: 120.
        Assert.Equal(120m, (await Timed("EST", new DateOnly(2026, 10, 14), "after")).Estimates!.Quarter!.Average);

        // No time stated, or no calendar row: the start of the report day, which every fetch follows, so none is read and
        // the quarter's revenue says the day the stock's estimates are kept from.
        foreach (var timing in new[] { "unstated", null })
        {
            var inputs = await Timed("EST", new DateOnly(2026, 10, 14), timing);

            Assert.Null(inputs.Estimates!.Quarter);
            Assert.Equal("not kept: estimates kept from 2026-10-14", QuarterLines.Latest(inputs.Quarters, inputs.Estimates)!.Lines[0].EstimateAbsent);
        }

        // FY4's quarter to 2026-08-31 ends its fiscal year, so the answer files the year's estimate in the quarter's place:
        // the year's 10,000 kept on 2026-10-11, less 2,300 + 2,400 + 2,500 reported, implies 2,800 against 2,940 reported.
        store.Execute(
            "INSERT INTO reported_quarter (ticker, fetched_at, session_date, period_end, report_date, revenue) VALUES "
            + "('FY4', '2026-10-15T23:00:00.000Z', '2026-10-15', '2025-11-30', '2025-12-18', '2300'), "
            + "('FY4', '2026-10-15T23:00:00.000Z', '2026-10-15', '2026-02-28', '2026-03-19', '2400'), "
            + "('FY4', '2026-10-15T23:00:00.000Z', '2026-10-15', '2026-05-31', '2026-06-18', '2500'), "
            + "('FY4', '2026-10-15T23:00:00.000Z', '2026-10-15', '2026-08-31', '2026-10-15', '2940');");
        store.Execute(
            "INSERT INTO estimate_trend (ticker, fetched_at, period, period_end, revenue_average) VALUES "
            + "('FY4', '2026-10-11T01:27:22.778Z', '0y', '2026-09-07', '10000'), "
            + "('FY4', '2026-10-11T01:27:22.778Z', '+1q', '2026-11-30', '2600');");

        var fiscal = await Timed("FY4", new DateOnly(2026, 10, 15), "before");

        Assert.Null(fiscal.Estimates!.Quarter);
        Assert.Equal(new YearEstimate(10_000m, new DateOnly(2026, 9, 7), new DateOnly(2026, 10, 11)), fiscal.Estimates.Year);
        Assert.Equal(
            (2800m, new ImpliedEstimate(10_000m, new DateOnly(2026, 9, 7), new DateOnly(2026, 10, 11), 7200m)),
            QuarterLines.Latest(fiscal.Quarters, fiscal.Estimates)!.Lines[0] is { Estimate: { } estimate, Implied: { } implied } ? (estimate, implied) : default);

        // A year ending a week after the quarter's end is read and one ending eight days after it is not.
        store.Execute("UPDATE estimate_trend SET period_end = '2026-09-08' WHERE ticker = 'FY4' AND period = '0y';");

        Assert.Null((await Timed("FY4", new DateOnly(2026, 10, 15), "before")).Estimates!.Year);

        // NXQ's quarter to 2026-09-30 was fetched before the provider rolled its words, so its one fetch files the quarter as
        // the next one, at 300, beside the quarter before as the current one: the quarter's own estimate is read under
        // either word, and the current quarter's row, ending a quarter before, is not.
        store.Execute("INSERT INTO reported_quarter (ticker, fetched_at, session_date, period_end, report_date, revenue) VALUES ('NXQ', '2026-10-16T23:00:00.000Z', '2026-10-16', '2026-09-30', '2026-10-16', '310');");
        store.Execute(
            "INSERT INTO estimate_trend (ticker, fetched_at, period, period_end, revenue_average) VALUES "
            + "('NXQ', '2026-10-01T04:00:00.000Z', '0q', '2026-06-30', '280'), "
            + "('NXQ', '2026-10-01T04:00:00.000Z', '+1q', '2026-09-30', '300');");

        Assert.Equal(new RevenueEstimate(300m, new DateOnly(2026, 10, 1)), (await Timed("NXQ", new DateOnly(2026, 10, 16), "before")).Estimates!.Quarter);

        // The day the stock's estimates are kept from is read as the store stood on the page's night: KFN's quarter to
        // 2026-06-30, reported on 2026-07-20 and fetched on 2026-10-09, with its first estimate fetch on 2026-10-14, says
        // its estimates are kept from 2026-10-14 tonight and that none is kept for it yet on 2026-10-09's page.
        store.Execute("INSERT INTO reported_quarter (ticker, fetched_at, session_date, period_end, report_date, revenue) VALUES ('KFN', '2026-10-09T23:00:00.000Z', '2026-10-09', '2026-06-30', '2026-07-20', '90');");
        store.Execute("INSERT INTO estimate_trend (ticker, fetched_at, period, period_end, revenue_average) VALUES ('KFN', '2026-10-14T04:40:57.322Z', '0q', '2026-09-30', '95');");

        var tonight = await read.ReportInputsAsync("KFN", null, EstimatesNight);
        var earlier = await read.ReportInputsAsync("KFN", new DateOnly(2026, 10, 9), new DateOnly(2026, 10, 9));

        Assert.Equal("not kept: estimates kept from 2026-10-14", QuarterLines.Latest(tonight.Quarters, tonight.Estimates)!.Lines[0].EstimateAbsent);
        Assert.Null(earlier.Estimates!.KeptFrom);
        Assert.Equal(QuarterLines.NoneKeptYet, QuarterLines.Latest(earlier.Quarters, earlier.Estimates)!.Lines[0].EstimateAbsent);
    }

    [Fact]
    public async Task TheChartPageReadsRevenueAgainstTheEstimateTheReportReadsAnImpliedOneNamedSoAndWhyWhereItReadsNone()
    {
        using var store = UniversesStore();

        // M1's quarter to 2026-08-31 ends its fiscal year: the year's 10,000 kept on 2026-09-10 less 2,300, 2,400 and 2,500
        // reported implies 2,800 against 2,940; M2's quarter to 2026-06-30 holds its own estimate of 500 against 520; M3's
        // holds no estimate fetch at all.
        store.Execute(
            "INSERT INTO reported_quarter (ticker, fetched_at, session_date, period_end, report_date, revenue) VALUES "
            + "('M1', '2026-10-01T23:00:00.000Z', '2026-10-01', '2025-11-30', '2025-12-18', '2300'), "
            + "('M1', '2026-10-01T23:00:00.000Z', '2026-10-01', '2026-02-28', '2026-03-19', '2400'), "
            + "('M1', '2026-10-01T23:00:00.000Z', '2026-10-01', '2026-05-31', '2026-06-18', '2500'), "
            + "('M1', '2026-10-01T23:00:00.000Z', '2026-10-01', '2026-08-31', '2026-09-24', '2940'), "
            + "('M2', '2026-10-01T23:00:00.000Z', '2026-10-01', '2026-06-30', '2026-07-28', '520'), "
            + "('M3', '2026-10-01T23:00:00.000Z', '2026-10-01', '2026-06-30', '2026-07-28', '75');");
        store.Execute(
            "INSERT INTO estimate_trend (ticker, fetched_at, period, period_end, revenue_average) VALUES "
            + "('M1', '2026-09-10T04:00:00.000Z', '0y', '2026-08-31', '10000'), "
            + "('M2', '2026-07-01T04:00:00.000Z', '0q', '2026-06-30', '500');");

        using var host = new Host(store.Root);
        using var client = host.CreateClient();

        async Task<string> Revenue(string ticker) =>
            Regex.Match(
                WebUtility.HtmlDecode(await client.GetStringAsync($"/screens/chart/{ticker}")),
                "<section class=\"grid-group\" data-group=\"earnings\">.*?<dt>Revenue</dt><dd[^>]*>(.*?)</dd>",
                RegexOptions.Singleline).Groups[1].Value;

        Assert.Equal("$2,940 against an implied estimate of $2,800", await Revenue("M1"));
        Assert.Equal("$520 against an estimate of $500", await Revenue("M2"));
        Assert.Equal("$75, its estimate " + QuarterLines.NoneKeptYet, await Revenue("M3"));
    }

    // A company whose newest company fetch on or before the page's night files the sector Financials reads its revenue
    // against no estimate on the report's quarter table and the chart page alike, each saying the estimate and the filing
    // measure it on different bases, and a fetch made after the page's night is not read, over the universes' store.
    [Fact]
    public async Task AFinancialCompanysRevenueIsDrawnAgainstNoEstimateOnTheReportAndTheChartPage()
    {
        using var store = UniversesStore();

        // M1 and M2 each report 520 for the quarter against its own estimate of 500. M2 files the sector Financials from its
        // first fetch; M1 files Energy then and Financials only from a fetch after the earlier page's night.
        store.Execute(
            "INSERT INTO reported_quarter (ticker, fetched_at, session_date, period_end, report_date, revenue) VALUES "
            + "('M1', '2026-10-01T23:00:00.000Z', '2026-10-01', '2026-06-30', '2026-07-28', '520'), "
            + "('M2', '2026-10-01T23:00:00.000Z', '2026-10-01', '2026-06-30', '2026-07-28', '520');");
        store.Execute(
            "INSERT INTO estimate_trend (ticker, fetched_at, period, period_end, revenue_average) VALUES "
            + "('M1', '2026-07-01T04:00:00.000Z', '0q', '2026-06-30', '500'), ('M2', '2026-07-01T04:00:00.000Z', '0q', '2026-06-30', '500');");
        store.Execute(
            "INSERT INTO company (ticker, fetched_at, sector, industry) VALUES "
            + "('M2', '2026-09-15T23:00:00.000Z', 'Financials', 'Banks'), "
            + "('M1', '2026-09-15T23:00:00.000Z', 'Energy', 'Oil, Gas & Consumable Fuels'), ('M1', '2026-10-03T23:00:00.000Z', 'Financials', 'Banks');");

        // The earlier page reads M1's first fetch and not the later one, so its revenue is read against 500; the newest
        // night's page reads the later one and none.
        var read = new ReadApi(store.DatabaseFile, FixedClock.At(new DateTimeOffset(2026, 10, 21, 12, 0, 0, TimeSpan.Zero), SessionZones.UnitedStates));
        var earlier = await read.ReportInputsAsync("M1", new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 1));
        var tonight = await read.ReportInputsAsync("M1", null, EstimatesNight);

        Assert.Equal(("Energy", "Financials"), (earlier.Sector, tonight.Sector));
        Assert.Equal(500m, QuarterLines.Latest(earlier.Quarters, earlier.Estimates, earlier.Sector)!.Lines[0].Estimate);
        Assert.Equal(QuarterLines.OtherBasis, QuarterLines.Latest(tonight.Quarters, tonight.Estimates, tonight.Sector)!.Lines[0].EstimateAbsent);

        using var host = new Host(store.Root);
        using var client = host.CreateClient();

        foreach (var ticker in new[] { "M1", "M2" })
        {
            var chart = WebUtility.HtmlDecode(await client.GetStringAsync($"/screens/chart/{ticker}"));
            var name = WebUtility.HtmlDecode(await client.GetStringAsync($"/screens/name/{ticker}"));

            Assert.Equal(
                "$520, its estimate " + QuarterLines.OtherBasis,
                Regex.Match(chart, "<section class=\"grid-group\" data-group=\"earnings\">.*?<dt>Revenue</dt><dd[^>]*>(.*?)</dd>", RegexOptions.Singleline).Groups[1].Value);
            Assert.Equal(
                QuarterLines.OtherBasis,
                Regex.Match(name, "<tr data-line=\"Revenue\">.*?data-estimate=\"absent\">([^<]*)</td>", RegexOptions.Singleline).Groups[1].Value);
        }
    }
}
