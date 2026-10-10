using System.Globalization;
using System.Text.Json;
using EquityBrief.Core.Bars;
using EquityBrief.Core.Time;
using EquityBrief.Worker.Indicators;

namespace EquityBrief.Tests.Bars;

// The chart's averages over the sessions before the store's year, read from the pulled history at the store's scale,
// worked by hand over constructed stores.
// see: The chart's averages are read over the sessions before the store's year from the pulled history at the store's scale, by a step only the chart reads
public class ChartAveragerTests
{
    // The exchange's sessions from a day, as many as asked for.
    static IReadOnlyList<DateOnly> SessionsFrom(DateOnly first, int count)
    {
        var sessions = new List<DateOnly>();

        for (var day = first; sessions.Count < count; day = day.AddDays(1))
        {
            if (ExchangeClosures.IsSession(day))
            {
                sessions.Add(day);
            }
        }

        return sessions;
    }

    static string Day(DateOnly session) => session.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    // A store holding a name's year of bars, the session numbered n closing at 100 + n, and a pull of the sessions before
    // and the year's first, closing at the bar store's close times the scale given.
    static (TemporaryStore Store, IReadOnlyList<DateOnly> Year, IReadOnlyList<DateOnly> Before) Stored(decimal scale, bool pull = true)
    {
        var store = new TemporaryStore().Migrated();
        var all = SessionsFrom(new DateOnly(2025, 1, 2), 199 + 252);
        var before = all.Take(199).ToArray();
        var year = all.Skip(199).ToArray();

        for (var at = 0; at < year.Length; at++)
        {
            var close = (100 + at).ToString(CultureInfo.InvariantCulture);

            store.Execute($"INSERT INTO bar (ticker, session_date, open, high, low, close, volume, source, observed_at, raw_close) VALUES ('AAA', '{Day(year[at])}', '{close}', '{close}', '{close}', '{close}', 1000, 'bulk', '2026-10-09T21:10:00Z', '{close}');");
        }

        if (pull)
        {
            // The pull's sessions before the year close at 50 + n for the session numbered n back from the year, on the
            // pull's own scale, and it holds the year's first session too.
            foreach (var (session, back) in before.Reverse().Select((session, at) => (session, at + 1)))
            {
                var close = ((50m + back) * scale).ToString(CultureInfo.InvariantCulture);

                store.Execute($"INSERT INTO pulled_bar (ticker, session_date, open, high, low, close, raw_close, volume, pull) VALUES ('AAA', '{Day(session)}', '{close}', '{close}', '{close}', '{close}', '{close}', 1000, 'history-pull-a');");
            }

            var first = (100m * scale).ToString(CultureInfo.InvariantCulture);

            store.Execute($"INSERT INTO pulled_bar (ticker, session_date, open, high, low, close, raw_close, volume, pull) VALUES ('AAA', '{Day(year[0])}', '{first}', '{first}', '{first}', '{first}', '{first}', 1000, 'history-pull-a');");
        }

        return (store, year, before);
    }

    static Task<ChartAveragesOutcome> Run(TemporaryStore store) =>
        new ChartAverager(FixedClock.At(new DateTimeOffset(2026, 10, 9, 23, 30, 0, TimeSpan.Zero), SessionZones.UnitedStates), store.DatabaseFile)
            .RunAsync("chart-averages-test");

    static (IReadOnlyDictionary<DateOnly, double> Values, string? Pull, string? Reason) Average(TemporaryStore store, string name)
    {
        using var connection = store.Open();
        using var command = connection.CreateCommand();

        command.CommandText = "SELECT sessions, pull, reason FROM chart_average WHERE ticker = 'AAA' AND name = $name;";
        command.Parameters.AddWithValue("$name", name);

        using var reader = command.ExecuteReader();

        Assert.True(reader.Read(), $"no {name} row stored");

        using var sessions = JsonDocument.Parse(reader.GetString(0));

        return (
            sessions.RootElement.EnumerateArray().ToDictionary(
                pair => DateOnly.ParseExact(pair[0].GetString()!, "yyyy-MM-dd", CultureInfo.InvariantCulture),
                pair => pair[1].GetDouble()),
            reader.IsDBNull(1) ? null : reader.GetString(1),
            reader.IsDBNull(2) ? null : reader.GetString(2));
    }

    [Fact]
    public async Task TheTwoHundredDayAverageHasAValueFromTheYearsFirstSessionReadThroughThePulledSessionsBeforeIt()
    {
        var (store, year, _) = Stored(scale: 1m);

        using (store)
        {
            var outcome = await Run(store);

            Assert.Equal((1, 0, 3), (outcome.NamesWarmed, outcome.NamesWithout, outcome.RowsWritten));

            var (values, pull, reason) = Average(store, "sma200");

            // Every session the indicator rows leave empty, the year's first 199, and no other.
            Assert.Equal(year.Take(199), values.Keys.Order());
            Assert.Equal(("history-pull-a", (string?)null), (pull, reason));

            // On the year's first session the average is of the 199 sessions before it, closing at 50 + 1 to 50 + 199,
            // and its own close of 100: (51 + 249) * 199 / 2 + 100, over 200.
            Assert.Equal(((51 + 249) * 199 / 2.0 + 100) / 200, values[year[0]], 9);

            // On the 199th the window holds the before's newest one, closing at 51, and the year's first 199 closes.
            Assert.Equal((51 + Enumerable.Range(100, 199).Sum()) / 200.0, values[year[198]], 9);

            // The 20-day and the 50-day fill the year's first 19 and 49.
            Assert.Equal(19, Average(store, "sma20").Values.Count);
            Assert.Equal(49, Average(store, "sma50").Values.Count);
        }
    }

    [Fact]
    public async Task APullOnAnotherScaleIsBroughtToTheStoresByTheTwoClosesOfTheFirstSessionBothHold()
    {
        // The pull's prices are 0.98 of the store's: the warm-up read at the pull's own closes would sit 2 per cent
        // under the year, and brought by the ratio of the first session's closes, 100 over 98, it is the store's.
        var (store, year, _) = Stored(scale: 0.98m);

        using (store)
        {
            await Run(store);

            Assert.Equal(((51 + 249) * 199 / 2.0 + 100) / 200, Average(store, "sma200").Values[year[0]], 9);
        }
    }

    [Fact]
    public async Task ANameWithNoPullAndOneWhosePullStopsAtAMissingSessionSayWhy()
    {
        var (bare, _, _) = Stored(scale: 1m, pull: false);

        using (bare)
        {
            var outcome = await Run(bare);

            Assert.Equal((0, 1), (outcome.NamesWarmed, outcome.NamesWithout));

            var (values, pull, reason) = Average(bare, "sma200");

            Assert.Empty(values);
            Assert.Equal(((string?)null, ChartAverager.NoPull), (pull, reason));
        }

        // A pull missing the session before the year's first stops the warm-up there, so none is read.
        var (gapped, _, before) = Stored(scale: 1m);

        using (gapped)
        {
            gapped.Execute($"DELETE FROM pulled_bar WHERE session_date = '{Day(before[^1])}';");

            await Run(gapped);

            Assert.Equal(((string?)null, ChartAverager.MissingSession), (Average(gapped, "sma200").Pull, Average(gapped, "sma200").Reason));
        }
    }
}
