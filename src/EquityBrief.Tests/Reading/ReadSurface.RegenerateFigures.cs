using System.Globalization;
using System.Text.Json.Nodes;
using EquityBrief.Api.Reading;
using EquityBrief.Core.Facts;
using EquityBrief.Core.Providers;
using EquityBrief.Core.Time;
using EquityBrief.Worker.Fundamentals;
using EquityBrief.Worker.Research;

namespace EquityBrief.Tests.Reading;

// read-surface, the 5.8 correction: a regenerate writes from the company's figures as they stand on the
// day it runs and at most once a name a day, and the facts file and the page read the newest copy of the
// parts that are as of a fetch, an earlier night's page the newest fetched by that night.
// see: A regenerated report is written whole by the paid model from the company's figures as they stand on the day it runs, once a name a day
public partial class ReadSurface
{
    // The recorded answer as the provider gives it on a later day, the trailing multiple moved to 31.25
    // and nothing newly filed, every call counted.
    sealed class LaterFundamentals : IFundamentalsFeed
    {
        readonly IFundamentalsFeed recorded = RecordedFundamentalsFeed.FromFolder(FixtureFolder());

        public int Requests { get; private set; }

        public async Task<CompanyFundamentals> FundamentalsAsync(string ticker, CancellationToken cancellation = default)
        {
            Requests++;

            var fetched = await recorded.FundamentalsAsync(ticker, cancellation);

            return fetched with { Valuation = fetched.Valuation with { TrailingPe = 31.25m } };
        }
    }

    static string? TrailingPe(string payload) =>
        JsonNode.Parse(payload)!["valuation"]?["trailingPe"]?.GetValue<string>();

    static IClock At(string instant) =>
        FixedClock.At(DateTimeOffset.Parse(instant, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal), SessionZones.UnitedStates);

    [Fact]
    public async Task ARegenerateWritesFromTheDaysFiguresOnceADayAndThePageReadsTheNewestCopyFetchedByItsNight()
    {
        using var store = await WithFundamentals();

        var filed = StoredPayload(store, Name);
        var held = TrailingPe(filed);

        Assert.NotNull(held);
        Assert.NotEqual("31.25", held);

        // A pass for the name ran to the end on 2026-09-08 in New York, so a regenerate that day
        // fetches nothing and the page is as it was.
        store.Execute(
            "INSERT INTO run_log (run_id, stage, started_at, ended_at, outcome, rows_written, model_calls, network_requests, spend, detail) " +
            "VALUES ('research-earlier-AAPL', 'research', '2026-09-08T14:00:00Z', '2026-09-08T14:05:00Z', 'ok', 0, 0, 0, '0', '{\"ticker\":\"AAPL\",\"asOf\":\"2026-09-08\"}');");

        var morning = At("2026-09-08T15:00:00Z");
        var sameDay = new LaterFundamentals();
        var refused = await PassFigures.FetchAsync(new FundamentalsFetcher(sameDay, morning, store.DatabaseFile), morning, store.DatabaseFile, Name, regenerate: true, "regenerate-same-day");

        Assert.Null(refused);
        Assert.Equal(0, sameDay.Requests);
        Assert.Equal(held, TrailingPe((await Api(store).FundamentalsAsync(Name))[0].Payload));

        // At 01:00 UTC on 2026-09-10, the evening of 2026-09-09 in New York, no pass has run that day.
        // The regenerate fetches whatever is held and assembles the facts file again, which states the
        // day's multiple, the figure a written section may quote.
        var evening = At("2026-09-10T01:00:00Z");
        var nextDay = new LaterFundamentals();
        var fetched = await PassFigures.FetchAsync(new FundamentalsFetcher(nextDay, evening, store.DatabaseFile), evening, store.DatabaseFile, Name, regenerate: true, "regenerate-next-day");

        Assert.True(fetched is { Fetched: true, RowsWritten: 0 }, "The regenerate did not fetch, or stored a filing the provider did not file.");
        Assert.Equal(1, nextDay.Requests);

        var facts = FactsFile.Read(Rows(store, "SELECT payload FROM facts WHERE ticker = 'AAPL' AND payload != '' ORDER BY session_date DESC LIMIT 1;").Single()[0]);

        Assert.Equal("31.25", facts.Single(fact => fact.Name == "trailing price to earnings").Value);

        // Tonight's page reads the newest copy over the filing, in what it hands the numbers and in the
        // snapshot drawn from it, and the filing's own row is as it was.
        var tonight = await Api(store).FundamentalsAsync(Name);

        Assert.Equal("31.25", TrailingPe(tonight[0].Payload));
        Assert.Equal("31.25", SnapshotRows(NameScreen.Numbers(tonight)).Single(row => row.Key == "trailingPe").Stored);
        Assert.Equal(filed, StoredPayload(store, Name));

        // An earlier night reads the newest copy fetched on or before it by the session date in New York:
        // 2026-09-09 holds the evening's copy, whose date in UTC is the 10th, and 2026-09-08 the first
        // fetch's.
        Assert.Equal("31.25", TrailingPe((await Api(store).FundamentalsAsync(Name, new DateOnly(2026, 9, 9)))[0].Payload));
        Assert.Equal(held, TrailingPe((await Api(store).FundamentalsAsync(Name, new DateOnly(2026, 9, 8)))[0].Payload));

        // A plain pass fetches nothing for a name holding its figures, whatever day it is.
        var plain = new LaterFundamentals();

        Assert.True(await PassFigures.FetchAsync(new FundamentalsFetcher(plain, evening, store.DatabaseFile), evening, store.DatabaseFile, Name, regenerate: false, "plain-pass") is { Fetched: false });
        Assert.Equal(0, plain.Requests);
    }
}
