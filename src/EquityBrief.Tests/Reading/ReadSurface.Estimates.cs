using EquityBrief.Api.Reading;
using EquityBrief.Core.Report;
using EquityBrief.Core.Time;

namespace EquityBrief.Tests.Reading;

// read-surface, from the 18.2 correction of 2026-10-21: the revenue estimates the report reads, each kept by a fetch made
// before the quarter's release as the calendar's timing on its report date places it in New York, the fiscal year's read in
// the quarter's place within a week of its end, and the instant the stock's estimates are kept from, read over a
// constructed store.
// see: The latest quarter is read line by line against the estimates kept before its release and the quarter a year earlier, a fiscal fourth quarter against its year's estimate less three quarters
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
                store.Execute($"INSERT INTO calendar (ticker, event_date, kind, timing, detail, observed_at) VALUES ('{ticker}', '{reported:yyyy-MM-dd}', 'earnings', '{timing}', '{{}}', '2026-10-01T21:00:00Z');");
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
    }
}
