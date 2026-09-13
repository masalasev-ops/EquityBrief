using System.Globalization;
using System.Net;
using System.Text.RegularExpressions;
using EquityBrief.Api.Reading;
using EquityBrief.Core.Spending;
using EquityBrief.Tests.Checks;
using EquityBrief.Web.App;
using EquityBrief.Web.Marks;
using EquityBrief.Worker.Research;
using Microsoft.Data.Sqlite;

namespace EquityBrief.Tests.Reading;

// read-surface, 6.7: research paused on the name page, what research spent in
// tonight's header, and the paid calls priced on the run page, each read back off the
// markup against the run log by a query of the test's own.
public partial class ReadSurface
{
    // A spend row as the spend cap writes one, or as any stage would.
    static void Spend(TemporaryStore store, string runId, string stage, string startedAt, string spend, string outcome = "ok") =>
        Insert(
            store,
            "INSERT INTO run_log (run_id, stage, started_at, ended_at, outcome, rows_written, model_calls, network_requests, spend, detail) " +
            $"VALUES ('{runId}', '{stage}', '{startedAt}', '{startedAt}', '{outcome}', 0, 1, 1, '{spend}', '{{}}');");

    static decimal Summed(TemporaryStore store, string where)
    {
        using var connection = new SqliteConnection($"Data Source={store.DatabaseFile}");
        connection.Open();

        using var command = connection.CreateCommand();
        command.CommandText = $"SELECT spend FROM run_log WHERE {where};";

        var total = 0m;
        using var reader = command.ExecuteReader();

        while (reader.Read())
        {
            total += decimal.Parse(reader.GetString(0), CultureInfo.InvariantCulture);
        }

        return total;
    }

    static async Task<string> NamePageAt(TemporaryStore store, string ticker, DateTimeOffset now)
    {
        var api = Api(store);
        var verdict = NameScreen.Spend(await api.SpentRowsAsync(SpendLedger.MonthStart(now), now.AddSeconds(1)), SpendCaps.Default, now);

        return NameScreen.Region(
            new SinglePageApp(),
            new MarkRenderer(),
            ticker,
            await api.BarsAsync(ticker, DateOnly.MinValue, DateOnly.MaxValue),
            await api.IndicatorsAsync(ticker, DateOnly.MinValue, DateOnly.MaxValue),
            await api.LevelsAsync(ticker),
            await api.ProfileAsync(ticker),
            await api.LadderAsync(ticker),
            await api.NextEventAsync(ticker, DateOnly.MinValue),
            await api.MovesAsync(ticker),
            await api.FundamentalsAsync(ticker),
            sections: await api.SectionStatesAsync(ticker, DateOnly.MaxValue),
            staleness: await api.StalenessAsync(ticker),
            written: await api.WrittenSectionsAsync(ticker),
            prosePass: await api.NewestProsePassAsync(ticker),
            spend: verdict);
    }

    [Fact]
    public void TheReadSurfaceNamesThePaidCallStageAndOutcomeTheSpendCapWrites()
    {
        Assert.Equal(SpendCap.Stage, ReadApi.PaidCallStage);
        Assert.Equal(SpendCap.Paid, ReadApi.PaidOutcome);
    }

    [Fact]
    public async Task ANamePausedAtACapSaysResearchIsPausedAndWhenItResumesReadBackOffTheMarkup()
    {
        using var store = await FixtureReplay.ReplayedAsync();

        var now = DateTimeOffset.Parse("2026-09-15T20:00:00Z", CultureInfo.InvariantCulture);

        // Below both caps: no pause is drawn.
        Spend(store, "earlier", "research call: The two cases", "2026-09-15T14:00:00Z", "6.00");

        Assert.DoesNotContain("class=\"research-paused\"", await NamePageAt(store, "KEYS", now), StringComparison.Ordinal);

        // At the day cap, counted off the store by a query of the test's own.
        Spend(store, "later", "research call: The short version", "2026-09-15T18:30:00Z", "4.00");

        Assert.Equal(10.00m, Summed(store, "started_at >= '2026-09-15T00:00:00Z' AND started_at < '2026-09-16T00:00:00Z'"));

        var region = await NamePageAt(store, "KEYS", now);
        var line = Regex.Match(region, "<p class=\"research-paused\" data-cap=\"([a-z]+)\" data-resumes-at=\"([^\"]+)\">([^<]*)</p>");

        Assert.True(line.Success);
        Assert.Equal(SpendVerdict.DayCap, line.Groups[1].Value);
        Assert.Equal("2026-09-16T00:00:00Z", line.Groups[2].Value);
        Assert.Equal(
            "research is paused: today's spend of $10.00 has reached the $10.00 day cap, and it resumes at 2026-09-16 00:00 UTC",
            WebUtility.HtmlDecode(line.Groups[3].Value));

        // The month on its own, from earlier days, resumes at the first of the next month.
        using var month = await FixtureReplay.ReplayedAsync();

        Spend(month, "first", "research call: The two cases", "2026-09-02T10:00:00Z", "50.00");

        var paused = Regex.Match(await NamePageAt(month, "KEYS", now), "<p class=\"research-paused\" data-cap=\"([a-z]+)\" data-resumes-at=\"([^\"]+)\">");

        Assert.Equal(SpendVerdict.MonthCap, paused.Groups[1].Value);
        Assert.Equal("2026-10-01T00:00:00Z", paused.Groups[2].Value);
    }

    [Fact]
    public async Task TonightsHeaderStatesWhatResearchSpentOnTheNightAndInItsMonthBesideTheCaps()
    {
        using var store = new TemporaryStore().Migrated();

        var night = new DateOnly(2026, 9, 8);

        // Two calls on the night, one earlier in its month, one the next day and one the
        // month before, which the header must not count.
        Spend(store, "a", "research call: The two cases", "2026-09-08T19:00:00Z", "0.0001395");
        Spend(store, "b", "research call: The short version", "2026-09-08T23:59:59Z", "0.25");
        Spend(store, "c", "research call: The two cases", "2026-09-02T09:00:00Z", "1.50");
        Spend(store, "d", "research call: The two cases", "2026-09-09T00:00:00Z", "3.00");
        Spend(store, "e", "research call: The two cases", "2026-08-31T23:00:00Z", "7.00");

        var api = Api(store);
        var (from, to) = TonightScreen.SpendWindow(night);
        var spent = TonightScreen.Spend(night, await api.SpentRowsAsync(from, to), SpendCaps.Default);

        var header = new MarkRenderer().NightHeader(night, 503, 4, "00:03:10", null, spent);
        var line = Regex.Match(header, "<p class=\"night-spend\" data-spent-day=\"([^\"]+)\" data-spent-month=\"([^\"]+)\" data-day-cap=\"([^\"]+)\" data-month-cap=\"([^\"]+)\">([^<]*)</p>");

        Assert.True(line.Success);

        Assert.Equal(
            Summed(store, "started_at >= '2026-09-08T00:00:00Z' AND started_at < '2026-09-09T00:00:00Z'"),
            decimal.Parse(line.Groups[1].Value, CultureInfo.InvariantCulture));
        Assert.Equal(
            Summed(store, "started_at >= '2026-09-01T00:00:00Z' AND started_at < '2026-09-09T00:00:00Z'"),
            decimal.Parse(line.Groups[2].Value, CultureInfo.InvariantCulture));
        Assert.Equal(SpendCaps.DefaultDay, decimal.Parse(line.Groups[3].Value, CultureInfo.InvariantCulture));
        Assert.Equal(SpendCaps.DefaultMonth, decimal.Parse(line.Groups[4].Value, CultureInfo.InvariantCulture));

        // Stated in advance, so a sum that read everything would not agree with itself.
        Assert.Equal(0.2501395m, decimal.Parse(line.Groups[1].Value, CultureInfo.InvariantCulture));
        Assert.Equal(1.7501395m, decimal.Parse(line.Groups[2].Value, CultureInfo.InvariantCulture));
        Assert.Contains("research spent $0.25 of the $10.00 day cap on 2026-09-08", WebUtility.HtmlDecode(line.Groups[5].Value), StringComparison.Ordinal);

        Assert.DoesNotContain("data-spend=\"absent\"", header, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TheRunPageCountsThePaidCallsCarryingARecordedCostAndWhatTheyCost()
    {
        using var store = new TemporaryStore().Migrated();

        Spend(store, "a", "research call: The two cases", "2026-09-08T19:00:00Z", "0.0001395");
        Spend(store, "a", "research call: The risks, each with what would confirm it", "2026-09-08T19:01:00Z", "0.0002");
        Spend(store, "b", "research call: The short version", "2026-09-10T19:00:00Z", "0.00002835");

        // A call a cap refused, a call that failed and a stage that is not a paid call
        // are not priced calls, whatever their spend column reads.
        Spend(store, "c", "research call: The two cases", "2026-09-11T19:00:00Z", "0", outcome: "paused");
        Spend(store, "d", "research call: The two cases", "2026-09-11T19:05:00Z", "0", outcome: "unavailable");
        Spend(store, "e", "facts", "2026-09-11T21:10:00Z", "0");

        var priced = RunScreen.Priced(await Api(store).PaidCallSpendsAsync());
        var header = new MarkRenderer().OperationalHeader(new DateOnly(2026, 9, 11), [], priced);

        // A night with no stage still states nothing of calls, because that header
        // returns early; the priced line is drawn under a night's stages.
        Assert.DoesNotContain("priced-calls", header, StringComparison.Ordinal);

        var api = Api(store);
        var stages = RunScreen.Stages(await api.RunLogAsync(new DateOnly(2026, 9, 11)));
        var drawn = new MarkRenderer().OperationalHeader(new DateOnly(2026, 9, 11), stages, priced);
        var line = Regex.Match(drawn, "<p class=\"priced-calls\" data-calls=\"(\\d+)\" data-passes=\"(\\d+)\" data-spend=\"([^\"]+)\">([^<]*)</p>");

        Assert.True(line.Success);

        // Three answered calls over two passes, stated in advance: the passes are the
        // runs the calls were made under, which is what the operating row counts.
        Assert.Equal("3", line.Groups[1].Value);
        Assert.Equal("2", line.Groups[2].Value);
        Assert.Equal(
            Summed(store, "stage LIKE 'research call:%' AND outcome = 'ok'"),
            decimal.Parse(line.Groups[3].Value, CultureInfo.InvariantCulture));
        Assert.Equal(0.00036785m, decimal.Parse(line.Groups[3].Value, CultureInfo.InvariantCulture));
    }
}
