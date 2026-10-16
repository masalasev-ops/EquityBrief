using System.Globalization;
using EquityBrief.Core.Ledger;
using EquityBrief.Core.Time;
using EquityBrief.Worker.Ledger;

namespace EquityBrief.Tests.Checks;

// nightly-run, 17.3: the history built twice over a constructed S&P 600, with a night between the builds that changes the
// store as a night does: a member's year fetched again after a split, a member joining the index, and the bar store's
// oldest session dropped into the kept bars. Every setup the first build compared has the same comparison after the
// second, the night leaves the history's rows as the build wrote them, a member taken over with its path still open is
// left out of the comparison, and a path the history's span ends inside is replayed over the bar store's own closes. The
// monthly rebuild computes the history beside the standing build and writes it only where no family gives fewer setups
// a benchmark beyond the tolerance, writing nothing otherwise.
// see: A setup's benchmark leaves out a member whose prices stop before its trade ends, as the sweeps do
// see: The history's setups are settled by the build alone, which replays them over the store's own prices after its span
// see: The monthly run writes the history again only where no family on any index loses more than half a per cent of its benchmarks or ten setups
public partial class NightlyRun
{
    const int ComparisonFrom = 65;
    const int ComparisonThrough = 135;
    const int ComparisonNewest = 140;
    const int ComparisonNight = 141;

    // AAA breaks out on the 70th session and falls back on the next, EEE the same on the 75th, and GGG, held only from
    // the quarter's end before, breaks out on the 134th and holds; every other close is 100, and the night's halves.
    static decimal ComparisonClose(string ticker, int at) => (ticker, at) switch
    {
        (_, ComparisonNight) => 50m,
        ("AAA", 70) or ("EEE", 75) => 110m,
        ("GGG", >= 134) => 110m,
        _ => 100m,
    };

    sealed record ComparedSetup(string Family, string Ticker, string Session, double? Result, double? Benchmark, double? Edge, int Settled, string End, string? EndedOn);

    [Fact]
    public async Task TheHistoryBuiltAgainAfterANightThatRefetchesAMemberAddsOneAndDropsTheOldestSessionGivesEverySetupTheSameComparison()
    {
        using var store = ComparisonStore();
        var output = new StringWriter();
        var first = new SetupLedger(FixedClock.At(new DateTimeOffset(2026, 7, 21, 15, 0, 0, TimeSpan.Zero), SessionZones.UnitedStates), store.DatabaseFile);

        Assert.Equal(0, await first.BuildAsync(store.Root, "SML", LedgerSession(ComparisonFrom), LedgerSession(ComparisonThrough), output));
        // Five names' closes run on in the bar store past the span's end; DDD's stop before it.
        Assert.Contains($"5 name(s) replayed past {LedgerStamp(ComparisonThrough)} over the bar store's closes through {LedgerStamp(ComparisonNewest)}", output.ToString(), StringComparison.Ordinal);
        Assert.EndsWith($", 5 name(s) replayed past {LedgerStamp(ComparisonThrough)} over the bar store's closes through {LedgerStamp(ComparisonNewest)}", Texts(store, "SELECT detail FROM run_log WHERE stage = 'ledger-build';").Single(), StringComparison.Ordinal);

        var built = ComparedRows(store);
        ComparedSetup Breakout(IReadOnlyList<ComparedSetup> rows, string ticker, int at) => rows.Single(row => row.Family == "breakout" && row.Ticker == ticker && row.Session == LedgerStamp(at));

        // AAA's own path ends at its stop the next session. BBB's and CCC's reach the cap's close at their buy and EEE's
        // its trail at its buy, each for nothing, and DDD's is still open where its prices stop, so it is left out and
        // the benchmark is AAA's result over the four that ended. From 19.10 AAA is bought at the next session's open of
        // 100, under its stop, and sold at that session's close of 100, so it made nothing.
        var aaa = Breakout(built, "AAA", 70);

        Assert.Equal((1, SetupEnds.Stop, LedgerStamp(71)), (aaa.Settled, aaa.End, aaa.EndedOn));
        Assert.Equal(0.0, aaa.Result!.Value, 12);
        Assert.Equal(aaa.Result!.Value / 4, aaa.Benchmark!.Value, 12);
        Assert.Equal(aaa.Result.Value - aaa.Benchmark.Value, aaa.Edge!.Value, 12);

        // EEE's own path ends the next session; the others reach the cap only after the span's end, on the bar store's
        // own closes, and the benchmark is EEE's result over the four that ended.
        var eee = Breakout(built, "EEE", 75);

        Assert.Equal((1, SetupEnds.Stop, LedgerStamp(76)), (eee.Settled, eee.End, eee.EndedOn));
        Assert.Equal(eee.Result!.Value / 4, eee.Benchmark!.Value, 12);

        // GGG's path and every member's run past the newest close, and no member's prices stopped: no comparison yet.
        var ggg = Breakout(built, "GGG", 134);

        Assert.Equal((0, (double?)null, (double?)null), (ggg.Settled, ggg.Result, ggg.Benchmark));
        Assert.DoesNotContain(built, row => row.Settled == 1 && row.Benchmark is null);

        // The night: every price halves, BBB's year is fetched again after a two-for-one split, the bar store's oldest
        // session is dropped into the kept bars, and HHH joins the index with its year.
        var night = new List<string>();

        foreach (var ticker in new[] { "AAA", "CCC", "EEE", "GGG" })
        {
            night.Add(ComparisonBar("bar", ticker, ComparisonNight, ComparisonClose(ticker, ComparisonNight), ComparisonClose(ticker, ComparisonNight), 1_000_000, "constructed"));
        }

        night.Add($"INSERT INTO kept_bar (ticker, session_date, open, high, low, close, volume, source, observed_at, raw_close, kept_on) SELECT ticker, session_date, open, high, low, close, volume, source, observed_at, raw_close, '{LedgerStamp(ComparisonNight)}' FROM bar WHERE session_date < '{LedgerStamp(51)}';");
        night.Add($"DELETE FROM bar WHERE session_date < '{LedgerStamp(51)}';");
        night.Add("DELETE FROM bar WHERE ticker = 'BBB';");

        for (var at = 51; at <= ComparisonNight; at++)
        {
            night.Add(ComparisonBar("bar", "BBB", at, ComparisonClose("BBB", at) / 2, ComparisonClose("BBB", at), 2_000_000, "refetch"));
            night.Add(ComparisonBar("bar", "HHH", at, 100m, 100m, 1_000_000, "constructed"));
        }

        night.Add($"INSERT INTO membership (index_code, ticker, joined, \"left\", observed_at) VALUES ('SML', 'HHH', '{LedgerStamp(ComparisonNight)}', NULL, '{LedgerStamp(ComparisonNight)}T21:00:00Z');");

        ComparisonExecute(store, night);

        var read = await new SetupLedger(FixedClock.At(new DateTimeOffset(2026, 7, 21, 21, 10, 0, TimeSpan.Zero), SessionZones.UnitedStates), store.DatabaseFile).NightAsync("night-comparison");

        Assert.Equal(LedgerSession(ComparisonNight), read.Session);
        Assert.All(read.Indices, index => Assert.Null(index.Fault));
        Assert.Equal(built, ComparedRows(store));

        var again = new StringWriter();
        var second = new SetupLedger(FixedClock.At(new DateTimeOffset(2026, 7, 22, 15, 0, 0, TimeSpan.Zero), SessionZones.UnitedStates), store.DatabaseFile);

        Assert.Equal(0, await second.BuildAsync(store.Root, "SML", LedgerSession(ComparisonFrom), LedgerSession(ComparisonThrough), again, again: true));

        var rebuilt = ComparedRows(store);

        // The same setups, and each the first build compared with the same result and benchmark.
        Assert.Equal(built.Select(row => (row.Family, row.Ticker, row.Session)), rebuilt.Select(row => (row.Family, row.Ticker, row.Session)));

        foreach (var (before, after) in built.Zip(rebuilt).Where(pair => pair.First.Settled == 1))
        {
            Assert.Equal(1, after.Settled);
            Assert.Equal(before.Result!.Value, after.Result!.Value, 12);
            Assert.Equal(before.Benchmark!.Value, after.Benchmark!.Value, 12);
        }

        // GGG's path and every member's end on the night's close, which the second build reads.
        var settled = Breakout(rebuilt, "GGG", 134);

        Assert.Equal((1, SetupEnds.Stop, LedgerStamp(ComparisonNight)), (settled.Settled, settled.End, settled.EndedOn));
        Assert.NotNull(settled.Benchmark);
        Assert.DoesNotContain(rebuilt, row => row.Settled == 1 && row.Benchmark is null);
    }

    [Fact]
    public async Task TheMonthlyRebuildIsWrittenOnlyWhereNoFamilyLosesItsBenchmarksBeyondTheTolerance()
    {
        // The tolerance worked by hand: half a per cent of the standing benchmarks rounded up, and never under ten.
        Assert.Equal((10, 10, 11, 20), (SetupLedger.RebuildAllowed(0), SetupLedger.RebuildAllowed(2000), SetupLedger.RebuildAllowed(2001), SetupLedger.RebuildAllowed(4000)));
        Assert.True(SetupLedger.RebuildHolds(4000, 3980));
        Assert.False(SetupLedger.RebuildHolds(4000, 3979));
        Assert.True(SetupLedger.RebuildHolds(100, 90));
        Assert.False(SetupLedger.RebuildHolds(100, 89));
        Assert.True(SetupLedger.RebuildHolds(5, 9));

        using var store = ComparisonStore();
        SetupLedger Ledger(int hour) => new(FixedClock.At(new DateTimeOffset(2026, 7, 21, hour, 0, 0, TimeSpan.Zero), SessionZones.UnitedStates), store.DatabaseFile);

        Assert.Equal(0, await Ledger(12).BuildAsync(store.Root, "SML", LedgerSession(ComparisonFrom), LedgerSession(ComparisonThrough), new StringWriter()));

        var standing = ComparedRows(store);

        // The same history computed again gives AAA's and EEE's breakouts their benchmarks again, and is written.
        var (held, words) = await Ledger(13).RebuildAsync(store.Root, ["SML"], new StringWriter());

        Assert.True(held);
        Assert.Equal("every index's history written again: the S&P 600 2 with a benchmark against the standing 2", words);
        Assert.Equal(standing, ComparedRows(store));
        Assert.Equal(2, Texts(store, "SELECT run_id FROM run_log WHERE stage = 'ledger-build';").Count);

        // Eleven setups the standing build gave a benchmark that no build gives again: eleven short where ten are allowed,
        // so nothing is written and the words name the family with both counts.
        ComparisonExecute(store, Enumerable.Range(1, 11).Select(place =>
            "INSERT INTO setup (index_code, family, ticker, session_date, rule, live_pass, entry, stop, cap, end, settled, source, pin, result, benchmark, edge) "
            + FormattableString.Invariant($"VALUES ('SML', 'breakout', 'ZZ{place:00}', '{LedgerStamp(100)}', 'the breakout', 0, '100', '97', 63, 'stop', 1, 'history', '{LedgerReadings.Version}', -1.0, 0.0, -1.0);")));

        var before = ComparedRows(store);
        var (kept, keptWords) = await Ledger(14).RebuildAsync(store.Root, ["SML"], new StringWriter());

        Assert.False(kept);
        Assert.Equal("the standing history is kept and nothing is written, since the S&P 600's breakout 2 against the standing 13, 11 fewer where 10 are allowed", keptWords);
        Assert.Equal(before, ComparedRows(store));
        Assert.Equal(2, Texts(store, "SELECT run_id FROM run_log WHERE stage = 'ledger-build';").Count);

        // Ten short is within it: written, the session's rows written again in place of those the standing build held.
        ComparisonExecute(store, ["DELETE FROM setup WHERE ticker = 'ZZ11';"]);

        var (written, _) = await Ledger(15).RebuildAsync(store.Root, ["SML"], new StringWriter());

        Assert.True(written);
        Assert.Equal(standing, ComparedRows(store));
    }

    // The constructed S&P 600: the pull holds every member to the 100th session and the bar store from the 50th to the
    // newest, DDD's to the 128th, its last close before it was taken over; membership as it stood from the fund's three
    // quarter-end holdings, DDD held to the newest quarter and gone from today's members, so it stands to the day after
    // that quarter, and GGG held from that quarter alone; and the members the night reads, two held since before the
    // history and three first stored by a members pull on the newest session.
    static TemporaryStore ComparisonStore()
    {
        var store = new TemporaryStore().Migrated();
        var statements = new List<string>();

        foreach (var ticker in new[] { "AAA", "BBB", "CCC", "DDD", "EEE", "GGG" })
        {
            for (var at = 0; at <= 100; at++)
            {
                statements.Add(ComparisonBar("pulled_bar", ticker, at, ComparisonClose(ticker, at), ComparisonClose(ticker, at), 1_000_000, ""));
            }

            for (var at = 50; at <= (ticker == "DDD" ? 128 : ComparisonNewest); at++)
            {
                statements.Add(ComparisonBar("bar", ticker, at, ComparisonClose(ticker, at), ComparisonClose(ticker, at), 1_000_000, "constructed"));
            }
        }

        foreach (var period in new[] { "2025-12-31", "2026-03-31", "2026-06-30" })
        {
            statements.Add($"INSERT INTO pulled_snapshot (index_code, period, accession, filed, holdings, equity, pull) VALUES ('SML', '{period}', 'filing-{period}', '{period}', 6, 6, 'holdings-pull-test');");

            foreach (var ticker in period == "2026-06-30" ? new[] { "AAA", "BBB", "CCC", "DDD", "EEE", "GGG" } : new[] { "AAA", "BBB", "CCC", "DDD", "EEE" })
            {
                statements.Add($"INSERT INTO pulled_holding (index_code, period, holding, name, ticker, matched_by, pull) VALUES ('SML', '{period}', '{ticker}-{period}', '{ticker} Corp', '{ticker}', 'isin', 'holdings-pull-test');");
            }
        }

        foreach (var ticker in new[] { "AAA", "BBB", "CCC", "EEE", "GGG" })
        {
            statements.Add($"INSERT INTO pulled_member (index_code, ticker, exchange, pull) VALUES ('SML', '{ticker}', 'US', 'members-pull-test');");
            statements.Add($"INSERT INTO membership (index_code, ticker, joined, \"left\", observed_at) VALUES ('SML', '{ticker}', '{(ticker is "AAA" or "BBB" ? "2025-01-02" : LedgerStamp(ComparisonNewest))}', NULL, '{LedgerStamp(ComparisonNewest)}T21:00:00Z');");
        }

        ComparisonExecute(store, statements);

        return store;
    }

    static string ComparisonBar(string table, string ticker, int at, decimal close, decimal raw, long volume, string source) => table == "pulled_bar"
        ? $"INSERT INTO pulled_bar (ticker, session_date, open, high, low, close, raw_close, volume, pull) VALUES ('{ticker}', '{LedgerStamp(at)}', '{ComparisonPrice(close)}', '{ComparisonPrice(close + 1m)}', '{ComparisonPrice(close - 1m)}', '{ComparisonPrice(close)}', '{ComparisonPrice(raw)}', {volume}, 'history-pull-test');"
        : $"INSERT INTO bar (ticker, session_date, open, high, low, close, volume, source, observed_at, raw_close) VALUES ('{ticker}', '{LedgerStamp(at)}', '{ComparisonPrice(close)}', '{ComparisonPrice(close + 1m)}', '{ComparisonPrice(close - 1m)}', '{ComparisonPrice(close)}', {volume}, '{source}', '{LedgerStamp(at)}T21:00:00Z', '{ComparisonPrice(raw)}');";

    static string ComparisonPrice(decimal value) => value.ToString(CultureInfo.InvariantCulture);

    static void ComparisonExecute(TemporaryStore store, IEnumerable<string> statements)
    {
        using var connection = store.Open();
        using var transaction = connection.BeginTransaction();

        foreach (var sql in statements)
        {
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = sql;
            command.ExecuteNonQuery();
        }

        transaction.Commit();
    }

    static List<ComparedSetup> ComparedRows(TemporaryStore store)
    {
        using var connection = store.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT family, ticker, session_date, result, benchmark, edge, settled, end, ended_on FROM setup WHERE source = 'history' ORDER BY family, ticker, session_date;";

        using var reader = command.ExecuteReader();
        var rows = new List<ComparedSetup>();

        while (reader.Read())
        {
            rows.Add(new ComparedSetup(
                reader.GetString(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.IsDBNull(3) ? null : reader.GetDouble(3),
                reader.IsDBNull(4) ? null : reader.GetDouble(4),
                reader.IsDBNull(5) ? null : reader.GetDouble(5),
                reader.GetInt32(6),
                reader.GetString(7),
                reader.IsDBNull(8) ? null : reader.GetString(8)));
        }

        return rows;
    }
}
