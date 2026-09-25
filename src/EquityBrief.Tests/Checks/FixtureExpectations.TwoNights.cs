using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using EquityBrief.Api.Reading;
using EquityBrief.Core.Providers;
using EquityBrief.Core.Time;
using EquityBrief.Worker.Bars;
using EquityBrief.Worker.Calendar;
using EquityBrief.Worker.Facts;
using EquityBrief.Worker.Filter;
using EquityBrief.Worker.Indicators;
using EquityBrief.Worker.Ladders;
using EquityBrief.Worker.Levels;
using EquityBrief.Worker.Membership;
using EquityBrief.Worker.Moves;
using EquityBrief.Worker.Shortlist;
using EquityBrief.Worker.Swings;
using EquityBrief.Worker.Volume;

namespace EquityBrief.Tests.Checks;

// The fixture's second night: the session before the fixture's own, replayed from the committed
// year of bars and followed by the fixture's night, so an earlier night's listings, bands and plans
// stand in the store beside a newer night's.
//
// The earlier night is replayed with the last session held back from the backfill, and the
// fixture's night arrives the way a live night's does, as one session's bulk file, built here from
// the same committed bars. No model is asked anything on either night, so no recording is keyed on
// a prompt the earlier night would write.
public partial class FixtureExpectations
{
    internal static readonly DateOnly EarlierNight = new(2026, 9, 3);

    // The evening of each night, after the close in New York, so each stage reads the night's own
    // session off the clock as a scheduled night does.
    static readonly DateTimeOffset EarlierEvening = new(2026, 9, 3, 21, 10, 0, TimeSpan.Zero);
    static readonly DateTimeOffset FixtureEvening = new(2026, 9, 4, 21, 10, 0, TimeSpan.Zero);

    internal static async Task<TemporaryStore> WithTwoNights()
    {
        var store = new TemporaryStore().Migrated();
        var backfill = FixedClock.At(Instant, SessionZones.UnitedStates);

        await new MembershipLoader(
            RecordedIndexMembershipFeed.FromFile(Path.Combine(Folder(), "index-constituents.json")),
            backfill,
            store.DatabaseFile).LoadAsync(Index, "two-nights-membership");

        await new Backfill(
            new HeldBack(RecordedHistoricalBarFeed.FromFolder(Folder()), EarlierNight),
            backfill,
            store.DatabaseFile).RunAsync(Index, "two-nights-backfill");

        await ComputedNight(store, FixedClock.At(EarlierEvening, SessionZones.UnitedStates), EarlierEvening, "earlier");

        var fixture = FixedClock.At(FixtureEvening, SessionZones.UnitedStates);

        await new BarFetcher(new RecordedBulkPriceFeed(BulkFor(FixtureNight)), fixture, store.DatabaseFile)
            .RunAsync(Index, "two-nights-fetch");

        await ComputedNight(store, fixture, FixtureEvening, "fixture");

        return store;
    }

    // The stages a night runs between the fetch and the list, in the order the listings chain
    // takes them.
    static async Task ComputedNight(TemporaryStore store, IClock clock, DateTimeOffset startedAt, string night)
    {
        await new IndicatorEngine(clock, store.DatabaseFile).RunAsync($"two-nights-{night}-indicators");
        await new SwingFinder(clock, store.DatabaseFile).RunAsync($"two-nights-{night}-swings");
        await new VolumeProfileBuilder(clock, store.DatabaseFile).RunAsync($"two-nights-{night}-profile");
        await new LevelBuilder(clock, store.DatabaseFile).RunAsync($"two-nights-{night}-levels");

        await new CalendarFetcher(
            RecordedEarningsCalendarFeed.FromFolder(Folder()),
            clock,
            store.DatabaseFile).RunAsync(Index, CalendarSession, $"two-nights-{night}-calendar");

        await new LadderBuilder(clock, store.DatabaseFile).RunAsync(Index, $"two-nights-{night}-ladders");
        await new MoveAnnotator(clock, store.DatabaseFile).RunAsync($"two-nights-{night}-moves");
        await new SwingReader(clock, store.DatabaseFile).RunAsync(Index, $"two-nights-{night}-swing-readings");
        await new FactsAssembler(clock, store.DatabaseFile).RunAsync($"two-nights-{night}-facts");
        await new ChangeDetector(clock, store.DatabaseFile).RunAsync($"two-nights-{night}-changes");
        await new ShortlistBuilder(clock, store.DatabaseFile).RunAsync(Index, $"two-nights-{night}-listings", startedAt);
        await new SwingFilter(clock, store.DatabaseFile).RunAsync(Index, $"two-nights-{night}-swing-filter");
    }

    // One session's bulk file for the exchange, from each captured name's committed bar on it, in
    // the shape the provider's bulk endpoint sends.
    static string BulkFor(DateOnly session)
    {
        var day = session.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var rows = new List<Dictionary<string, object>>();

        foreach (var path in Directory.GetFiles(Folder(), "bars-*.json").Order(StringComparer.Ordinal))
        {
            var ticker = Path.GetFileNameWithoutExtension(path)["bars-".Length..];

            foreach (var bar in JsonDocument.Parse(File.ReadAllText(path)).RootElement.EnumerateArray())
            {
                if (bar.GetProperty("date").GetString() != day)
                {
                    continue;
                }

                var row = new Dictionary<string, object>(StringComparer.Ordinal)
                {
                    ["code"] = ticker,
                    ["exchange_short_name"] = "US",
                };

                foreach (var field in bar.EnumerateObject())
                {
                    row[field.Name] = field.Value.Clone();
                }

                rows.Add(row);
            }
        }

        Assert.True(rows.Count > 0, $"no committed bar is on {day}.");

        return JsonSerializer.Serialize(rows);
    }

    [Fact]
    public async Task BothFixtureNightsAreDrawnInTheOrderWorkedByHand()
    {
        // The two nights' rows, read against what the listings expectation works by hand: each
        // name's fired reasons, the plan its listing kept, the ratio that plan computes or its reason
        // for none, and each night's order through the route that draws it. The earlier night is
        // drawn from its own plans, which the expectation shows by a name whose ratio moved.
        // owes: An earlier night's order on tonight's list asserted from the fixture
        // see: Tonight's list breaks a tie in fired count by the plan's reward to risk, and a row with none is drawn after every row with one and says why
        var expected = Expected("listings").GetProperty("twoNights");

        Assert.Equal("derived", expected.GetProperty("derivation").GetString());

        using var store = await WithTwoNights();

        var api = new ReadApi(store.DatabaseFile, FixedClock.At(Instant, SessionZones.UnitedStates));

        using var host = new Reading.ReadSurface.Host(store.Root);
        using var client = host.CreateClient();

        var nights = expected.GetProperty("nights").EnumerateObject().ToArray();

        Assert.Equal([EarlierNight, FixtureNight], [.. nights.Select(night => DateOnly.ParseExact(night.Name, "yyyy-MM-dd", CultureInfo.InvariantCulture))]);

        foreach (var night in nights)
        {
            var on = DateOnly.ParseExact(night.Name, "yyyy-MM-dd", CultureInfo.InvariantCulture);
            var listings = (await api.ListingsAsync(on)).ToDictionary(listing => listing.Ticker, StringComparer.Ordinal);
            var rows = night.Value.GetProperty("rows");

            Assert.Equal(
                [.. listings.Keys.Order(StringComparer.Ordinal)],
                [.. rows.EnumerateObject().Select(row => row.Name).Order(StringComparer.Ordinal)]);

            foreach (var row in rows.EnumerateObject())
            {
                var listing = listings[row.Name];
                var fired = JsonDocument.Parse(listing.Reasons).RootElement.EnumerateArray()
                    .Where(reason => reason.GetProperty("fired").GetBoolean())
                    .Select(reason => reason.GetProperty("name").GetString()!)
                    .Order(StringComparer.Ordinal)
                    .ToArray();

                Assert.Equal([.. row.Value.GetProperty("fired").EnumerateArray().Select(reason => reason.GetString()!).Order(StringComparer.Ordinal)], fired);
                Assert.Equal(fired.Length, listing.FiredCount);

                if (!row.Value.TryGetProperty("rewardToRisk", out var ratio))
                {
                    continue;
                }

                var plan = JsonDocument.Parse(listing.PlanAtListing).RootElement;

                foreach (var price in new[] { "entryLow", "entryHigh", "stop", "firstTradedTarget" })
                {
                    Assert.Equal(row.Value.GetProperty(price).ToString(), plan.GetProperty(price).ToString());
                }

                var (rewardToRisk, why) = TonightScreen.FirstTranche(listing.PlanAtListing);

                if (ratio.GetString() == "none")
                {
                    Assert.Null(rewardToRisk);
                    Assert.Equal(row.Value.GetProperty("why").GetString(), why);
                }
                else
                {
                    Assert.Equal(decimal.Parse(ratio.GetString()!, CultureInfo.InvariantCulture), rewardToRisk);
                }
            }

            // The order the route draws, against the order the expectation derives by hand.
            var page = await client.GetStringAsync($"/screens/tonight/{night.Name}");
            var drawn = Regex.Matches(page, "<tr data-ticker=\"([^\"]+)\" data-fired-count=").Select(match => match.Groups[1].Value).ToArray();

            Assert.Equal([.. night.Value.GetProperty("order").EnumerateArray().Select(ticker => ticker.GetString()!)], drawn);

            // And each drawn row carries the ratio the expectation works, so a row cannot stand in
            // the right place with another night's figure on it.
            foreach (var ticker in drawn)
            {
                var figure = rows.GetProperty(ticker).GetProperty("rewardToRisk").GetString()!;
                var at = Regex.Match(page, $"<tr data-ticker=\"{ticker}\".*?</tr>", RegexOptions.Singleline).Value;
                var carried = Regex.Match(at, "data-reward-to-risk=\"([^\"]+)\"").Groups[1].Value;

                Assert.Equal(figure, figure == "none" ? carried : decimal.Parse(carried, CultureInfo.InvariantCulture).ToString("0.0000", CultureInfo.InvariantCulture));
            }
        }

        // The earlier night's walk is that night's list: KEYS follows MSFT on it, where the newer
        // night puts it first.
        var walked = await client.GetStringAsync("/screens/name/KEYS/" + EarlierNight.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));

        Assert.Contains("data-previous=\"MSFT\" data-next=\"none\"", walked, StringComparison.Ordinal);
        Assert.Contains(
            "data-previous=\"none\" data-next=\"AAPL\"",
            await client.GetStringAsync("/screens/name/KEYS/" + FixtureNight.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)),
            StringComparison.Ordinal);
    }

    // The committed bars up to a session and none after it.
    sealed class HeldBack(IHistoricalBarFeed inner, DateOnly through) : IHistoricalBarFeed
    {
        public int Requests => inner.Requests;

        public async Task<IReadOnlyList<ProviderBar>> BarsAsync(
            string ticker,
            DateOnly from,
            DateOnly to,
            CancellationToken cancellationToken = default) =>
            [.. (await inner.BarsAsync(ticker, from, to, cancellationToken)).Where(bar => bar.SessionDate <= through)];
    }
}
