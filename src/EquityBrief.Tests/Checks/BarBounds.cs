using EquityBrief.Core.Providers;
using EquityBrief.Core.Time;
using EquityBrief.Tests.Harness;
using EquityBrief.Worker.Bars;
using EquityBrief.Worker.Membership;
using Microsoft.Data.Sqlite;

namespace EquityBrief.Tests.Checks;

internal sealed record ImpossibleBar(string Ticker, string SessionDate, string Reason);

// bar-bounds. Every stored bar has its low at or below its open and its close,
// and its high at or above both, so a bar that could not have traded is found
// rather than drawn.
//
// This is the check the 1.2 store needed and did not have. The backfill stored
// the provider's adjusted close beside its unadjusted open, high and low, and 96
// of 756 fixture bars carried a close outside their own low and high. Nothing
// looked. Every figure phase 2 computes rests on these rows, and the first thing
// that would have shown it is a chart drawn a phase later.
//
// The finding is not the mixed price set. The finding is that a bar could be
// internally impossible and no instrument asked, which is why this reads the
// property off a populated store on every CI run rather than sitting beside the
// migration as a test of the arithmetic that happens to produce it. The
// arithmetic can change; the property may not.
public class BarBounds
{
    const string Fixture = "membership-2026-09-05";
    const string Index = "GSPC";

    static readonly DateTimeOffset Instant = new(2026, 9, 5, 21, 10, 0, TimeSpan.Zero);

    static string FixtureFolder() => Path.Combine(Repository.Root, "fixtures", Fixture);

    // A store with the fixture's year in it. Nothing here reaches data/: a check
    // reading the live store is a check whose result depends on last night.
    static async Task<TemporaryStore> Populated()
    {
        var store = new TemporaryStore().Migrated();
        var clock = FixedClock.At(Instant, SessionZones.UnitedStates);

        await new MembershipLoader(
            RecordedIndexMembershipFeed.FromFile(Path.Combine(FixtureFolder(), "index-constituents.json")),
            clock,
            store.DatabaseFile).LoadAsync(Index, "run-0");

        await new Backfill(
            RecordedHistoricalBarFeed.FromFolder(FixtureFolder()),
            clock,
            store.DatabaseFile).RunAsync(Index, "run-1");

        return store;
    }

    // Read off the store rather than off the parser, because the property is
    // about what is held and not about what was returned.
    internal static (int Scanned, IReadOnlyList<ImpossibleBar> Impossible) Scan(TemporaryStore store)
    {
        using var connection = new SqliteConnection($"Data Source={store.DatabaseFile}");
        connection.Open();

        using var command = connection.CreateCommand();
        command.CommandText = "SELECT ticker, session_date, open, high, low, close, raw_close FROM bar;";

        var impossible = new List<ImpossibleBar>();
        var scanned = 0;

        using var reader = command.ExecuteReader();

        while (reader.Read())
        {
            scanned++;

            var ticker = reader.GetString(0);
            var date = reader.GetString(1);

            // Through the money helper, so a value that will not round-trip is a
            // failure here rather than a silent comparison of two strings.
            var open = EquityBrief.Data.Money.FromStorage(reader.GetString(2));
            var high = EquityBrief.Data.Money.FromStorage(reader.GetString(3));
            var low = EquityBrief.Data.Money.FromStorage(reader.GetString(4));
            var close = EquityBrief.Data.Money.FromStorage(reader.GetString(5));

            if (low > open || low > close)
            {
                impossible.Add(new ImpossibleBar(ticker, date, $"low {low} is above open {open} or close {close}"));
            }
            else if (high < open || high < close)
            {
                impossible.Add(new ImpossibleBar(ticker, date, $"high {high} is below open {open} or close {close}"));
            }
            else if (reader.IsDBNull(6))
            {
                impossible.Add(new ImpossibleBar(ticker, date, "no raw close, so its adjustment factor cannot be audited"));
            }
        }

        return (scanned, impossible);
    }

    [Fact]
    public async Task EveryStoredBarCouldHaveTraded()
    {
        using var store = await Populated();

        var (scanned, impossible) = Scan(store);

        Assert.Empty(impossible);

        // The scope, stated in numbers and floored on the population carrying
        // the property. 756 bars today, three names of a year each. The floor
        // sits below that and above zero, because a scan over an empty store
        // would otherwise report the same green as a scan over a full one.
        Assert.True(scanned >= 700, $"Scanned {scanned} stored bars, expected at least 700.");
    }

    [Fact]
    public async Task AnImpossibleBarIsFound()
    {
        // The permanent negative proof, planted through SQL rather than through
        // the parser. The parser refuses this shape at the boundary, and a check
        // that could only be tested through the thing it guards would be a check
        // that never fails on its own.
        using var store = await Populated();

        store.Execute(
            "INSERT INTO bar (ticker, session_date, open, high, low, close, volume, source, observed_at, raw_close) " +
            "VALUES ('ZZZZ', '2026-09-04', '10', '11', '12', '10.5', 100, 'planted', '2026-09-05T21:10:00Z', '10.5');");

        var (_, impossible) = Scan(store);
        var found = Assert.Single(impossible);

        Assert.Equal("ZZZZ", found.Ticker);
        Assert.Contains("low 12", found.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ACloseAboveTheHighIsFoundAndABarWithNoRawCloseIsToo()
    {
        // The other two shapes, because a check that only caught a low above the
        // open would report green over a close above the high.
        using var store = await Populated();

        store.Execute(
            "INSERT INTO bar (ticker, session_date, open, high, low, close, volume, source, observed_at, raw_close) " +
            "VALUES ('YYYY', '2026-09-04', '10', '11', '9', '11.5', 100, 'planted', '2026-09-05T21:10:00Z', '11.5'), " +
            "('XXXX', '2026-09-04', '10', '11', '9', '10.5', 100, 'planted', '2026-09-05T21:10:00Z', NULL);");

        var (_, impossible) = Scan(store);

        Assert.Equal(2, impossible.Count);
        Assert.Contains(impossible, bar => bar.Ticker == "YYYY" && bar.Reason.Contains("high 11", StringComparison.Ordinal));
        Assert.Contains(impossible, bar => bar.Ticker == "XXXX" && bar.Reason.Contains("raw close", StringComparison.Ordinal));
    }

    [Fact]
    public async Task TheCounterTestShowsAWellFormedBarPasses()
    {
        // Without this, every rejection above would pass just as well over a
        // scan that reported every bar.
        using var store = await Populated();

        store.Execute(
            "INSERT INTO bar (ticker, session_date, open, high, low, close, volume, source, observed_at, raw_close) " +
            "VALUES ('WWWW', '2026-09-04', '10', '11', '9', '10.5', 100, 'planted', '2026-09-05T21:10:00Z', '10.5'), " +
            "('VVVV', '2026-09-04', '10', '10', '10', '10', 100, 'planted', '2026-09-05T21:10:00Z', '10');");

        var (_, impossible) = Scan(store);

        // The second is a session that opened, closed and ranged at one price,
        // which is legal: the comparison is at or below rather than below.
        Assert.Empty(impossible);
    }

    [Fact]
    public void TheParserRefusesAnImpossibleBarBeforeItReachesTheStore()
    {
        // The guard the code carries, beside the check that reads the store. A
        // green report is a statement about the build and never about the
        // running system, so the property is asserted in both places: here so
        // the fault refuses, and over the store so a person reads the figure.
        var refusal = Assert.Throws<FormatException>(() => RecordedHistoricalBarFeed.Parse(
            """[{"date":"2026-09-04","open":10,"high":11,"low":12,"close":10.5,"adjusted_close":10.5,"volume":100}]""",
            "TEST"));

        Assert.Contains("could not have traded", refusal.Message, StringComparison.Ordinal);
    }
}
