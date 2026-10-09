using EquityBrief.Core.Ledger;
using EquityBrief.Core.Time;
using EquityBrief.Worker.Ledger;

namespace EquityBrief.Tests.Checks;

// nightly-run, 17.3: the ledger's step over a constructed store. A member closing above its 63-session high on its
// average volume is a breakout setup that the live rule, reading a 126-session high, does not pass; the same
// member's print on the session with a positive surprise is a drift setup anchored on its reaction, stopped at the
// session's low; the night's pick and the member's cost are read beside each; the next night closes the breakout's and
// the drift's windows at their stops while their benchmark, the same plan on every member, is not settled; and a night
// run again replaces its own rows.
// see: A setup is every member-session a family's loose gates pass, and its readings are defined once and read as they stood
public partial class NightlyRun
{
    static readonly DateOnly LedgerStart = new(2026, 1, 5);

    // Weekdays from the start, so the sessions are a plausible exchange calendar.
    static DateOnly LedgerSession(int at)
    {
        var day = LedgerStart;

        for (var step = 0; step < at; step++)
        {
            day = day.AddDays(1);

            while (day.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday)
            {
                day = day.AddDays(1);
            }
        }

        return day;
    }

    static void LedgerBar(TemporaryStore store, string ticker, int at, decimal close, long volume) =>
        store.Execute(
            "INSERT INTO bar (ticker, session_date, open, high, low, close, volume, source, observed_at, raw_close) " +
            $"VALUES ('{ticker}', '{LedgerStamp(at)}', '{close}', '{close + 1m}', '{close - 1m}', '{close}', {volume}, 'constructed', '{LedgerStamp(at)}T21:00:00Z', '{close}');");

    static string LedgerStamp(int at) => LedgerSession(at).ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);

    [Fact]
    public async Task TheLedgerStepAppendsTonightsSetupsWithTheLiveRulesPassThePickAndTheCostAndClosesTheirWindowsTheNightAfter()
    {
        using var store = new TemporaryStore().Migrated();

        // Three S&P 500 members over seventy sessions, each at 100 with a one-point range and a million shares; on the
        // seventy-first AAA closes at 110 while the others stay at 100.
        foreach (var ticker in new[] { "AAA", "BBB", "CCC" })
        {
            store.Execute($"INSERT INTO membership (index_code, ticker, joined, \"left\", observed_at) VALUES ('GSPC', '{ticker}', '2025-01-02', NULL, '2026-01-05T21:00:00Z');");

            for (var at = 0; at < 70; at++)
            {
                LedgerBar(store, ticker, at, 100m, 1_000_000);
            }
        }

        LedgerBar(store, "AAA", 70, 110m, 1_000_000);
        LedgerBar(store, "BBB", 70, 100m, 1_000_000);
        LedgerBar(store, "CCC", 70, 100m, 1_000_000);

        // AAA reported before the open of that session, beating by ten per cent; the breakout family listed it; and the
        // member reader priced its round trip at half a per cent of the close.
        store.Execute($"INSERT INTO calendar (ticker, event_date, kind, timing, detail, observed_at) VALUES ('AAA', '{LedgerStamp(70)}', 'earnings', 'before', '{{\"surprise\":\"10\"}}', '{LedgerStamp(70)}T21:00:00Z');");
        store.Execute($"INSERT INTO family_pick (session_date, ticker, family, state, place, also) VALUES ('{LedgerStamp(70)}', 'AAA', 'breakout', 'listed', 1, '[]');");
        store.Execute($"INSERT INTO member_reading (index_code, session_date, ticker, close, cost) VALUES ('GSPC', '{LedgerStamp(70)}', 'AAA', '110', 0.5);");

        var clock = FixedClock.At(new DateTimeOffset(2026, 4, 15, 21, 10, 0, TimeSpan.Zero), SessionZones.UnitedStates);
        var ledger = new SetupLedger(clock, store.DatabaseFile);

        var outcome = await ledger.NightAsync("night-ledger");

        Assert.Equal(LedgerSession(70), outcome.Session);
        Assert.All(outcome.Indices, index => Assert.Null(index.Fault));

        // The S&P 500's part: AAA under the breakout and under the drift, and nothing under the pullback; the 400 and
        // the 600, which the store holds no member of on the night, are not read and write no row.
        Assert.Equal(["GSPC|breakout|3|1|0", "GSPC|drift|3|1|0", "GSPC|pullback|3|0|0"],
            Texts(store, "SELECT index_code || '|' || family || '|' || members || '|' || setups || '|' || live_passes FROM setup_night ORDER BY index_code, family;"));

        // The breakout: bought at 110, the stop and the trail 1.5 of AAA's typical move under, read back off its own
        // risk, open on the night, not passed by the live rule, which reads a high over 126 sessions the series does
        // not hold, picked by the night, and its cost half a per cent of 110 over its risk.
        var breakout = Texts(store, "SELECT entry || '|' || stop || '|' || (CASE WHEN target IS NULL THEN 1 ELSE 0 END) || '|' || trail || '|' || cap || '|' || live_pass || '|' || picked || '|' || end || '|' || settled || '|' || source FROM setup WHERE family = 'breakout';").Single().Split('|');

        Assert.Equal(("110", "1", "63", "0", "1", SetupEnds.Open, "0", SetupLedger.NightSource), (breakout[0], breakout[2], breakout[4], breakout[5], breakout[6], breakout[7], breakout[8], breakout[9]));

        var stop = double.Parse(breakout[1], System.Globalization.CultureInfo.InvariantCulture);
        var trail = double.Parse(breakout[3], System.Globalization.CultureInfo.InvariantCulture);
        var riskMoves = LedgerScalar<double>(store,"SELECT risk_moves FROM setup WHERE family = 'breakout';");
        var cost = LedgerScalar<double>(store,"SELECT cost FROM setup WHERE family = 'breakout';");

        Assert.Equal(1.5, riskMoves);
        Assert.Equal(110 - stop, trail, 6);
        Assert.Equal(0.5 / 100 * 110 / (110 - stop), cost, 5);
        // Its volume over the fifty-session mean, which counts the session's own; and the readings the seventy-one
        // bars do not reach, the 252-session high's and the 200-session average's, stored as null and not nought.
        Assert.Equal(1.0, LedgerScalar<double>(store, "SELECT volume_multiple FROM setup WHERE family = 'breakout';"));
        Assert.Equal(["1|1|1"], Texts(store, "SELECT (high_ratio IS NULL) || '|' || (close_over_long IS NULL) || '|' || (return_twelve_less_one IS NULL) FROM setup WHERE family = 'breakout';"));
        Assert.Equal(["0|0"], Texts(store, "SELECT (close_over_twenty IS NULL) || '|' || (liquidity IS NULL) FROM setup WHERE family = 'breakout';"));

        // The drift: anchored on the reaction session, stopped at its low of 109 for a risk of one point, its target
        // within two and a half risks of the buy, the surprise and the rise stored, not picked, and passed by the live
        // rule only where the night's market check is open, which no breadth closes here.
        var drift = Texts(store, "SELECT entry || '|' || stop || '|' || cap || '|' || picked || '|' || end || '|' || surprise_percent || '|' || freshness || '|' || settled FROM setup WHERE family = 'drift';").Single().Split('|');

        Assert.Equal(("110", "109", "60", "0", SetupEnds.Open, "10.0", "0.0", "0"), (drift[0], drift[1], drift[2], drift[3], drift[4], drift[5], drift[6], drift[7]));
        Assert.True(LedgerScalar<double>(store,"SELECT target FROM setup WHERE family = 'drift';") <= 112.5);
        Assert.True(LedgerScalar<double>(store,"SELECT reaction_moves FROM setup WHERE family = 'drift';") > 0.25);
        Assert.Equal(["ok"], Texts(store, $"SELECT outcome FROM run_log WHERE stage = '{SetupLedger.Stage}';"));
        Assert.StartsWith("2 setup(s), 0 passing the live rule, 0 window(s) closed on the S&P 500; ", Texts(store, $"SELECT detail FROM run_log WHERE stage = '{SetupLedger.Stage}';").Single(), StringComparison.Ordinal);

        // The readings a rule whose hooks read them is listed by on the night are AAA's as its setup stored them, the
        // catalogue's shared readings each; a member the night holds no bar for reads none.
        // see: Every engine's settings hooks land together and all default off, so the families' pins move once
        var tonight = await ledger.ReadingsTonightAsync("GSPC");
        var supplied = tonight("AAA")!;

        foreach (var column in new[] { "close_over_twenty", "close_over_fifty", "move_share", "liquidity", "close_over_long" })
        {
            var at = EquityBrief.Core.Ledger.LedgerReadings.All.Select((reading, place) => (reading, place)).Single(pair => pair.reading.Column == column).place;
            var stored = Texts(store, $"SELECT IFNULL({column}, 'none') FROM setup WHERE family = 'breakout';").Single();

            if (stored == "none")
            {
                Assert.Null(supplied[at]);
            }
            else
            {
                Assert.Equal(double.Parse(stored, System.Globalization.CultureInfo.InvariantCulture), supplied[at]!.Value, 9);
            }
        }

        Assert.Null(tonight("ZZZ"));

        // The night after: AAA falls to 100, through both stops; the others hold at 100, so the same plan on them is
        // still open and neither benchmark is settled. Both windows are closed at the stop with their result, their
        // sessions and the session they ended on, settled no.
        foreach (var ticker in new[] { "AAA", "BBB", "CCC" })
        {
            LedgerBar(store, ticker, 71, 100m, 1_000_000);
        }

        var next = await ledger.NightAsync("night-ledger-next");

        Assert.Equal(LedgerSession(71), next.Session);
        Assert.Equal(2, next.Indices.Single(index => index.Index == "GSPC").Closed);

        var closed = Texts(store, $"SELECT family || '|' || end || '|' || sessions || '|' || ended_on || '|' || settled || '|' || (CASE WHEN benchmark IS NULL THEN 1 ELSE 0 END) || '|' || result FROM setup WHERE session_date = '{LedgerStamp(70)}' ORDER BY family;");

        Assert.Equal(2, closed.Count);
        Assert.StartsWith($"breakout|{SetupEnds.Stop}|1|{LedgerStamp(71)}|0|1|", closed[0], StringComparison.Ordinal);
        Assert.Equal($"drift|{SetupEnds.Stop}|1|{LedgerStamp(71)}|0|1|-10.0", closed[1]);
        Assert.Equal(-10 / (110 - stop), double.Parse(closed[0].Split('|')[6], System.Globalization.CultureInfo.InvariantCulture), 9);

        // Tonight itself holds no setup: nothing broke out and no print landed.
        Assert.Equal(["0", "0", "0"], Texts(store, $"SELECT setups FROM setup_night WHERE index_code = 'GSPC' AND session_date = '{LedgerStamp(71)}' ORDER BY family;"));

        // The night run again writes what it wrote: the same rows, once.
        var again = await ledger.NightAsync("night-ledger-again");

        Assert.Equal(LedgerSession(71), again.Session);
        Assert.Equal(["2", "6"], Texts(store, "SELECT COUNT(*) FROM setup UNION ALL SELECT COUNT(*) FROM setup_night;"));
        Assert.Equal([SetupLedger.Ok, SetupLedger.Ok, SetupLedger.Ok], Texts(store, $"SELECT outcome FROM run_log WHERE stage = '{SetupLedger.Stage}' ORDER BY rowid;"));
    }

    static T LedgerScalar<T>(TemporaryStore store, string sql) where T : struct
    {
        using var connection = store.Open();
        using var command = connection.CreateCommand();
        command.CommandText = sql;

        return (T)Convert.ChangeType(command.ExecuteScalar(), typeof(T), System.Globalization.CultureInfo.InvariantCulture)!;
    }
}
