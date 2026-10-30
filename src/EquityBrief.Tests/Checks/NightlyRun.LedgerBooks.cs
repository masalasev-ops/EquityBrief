using System.Globalization;
using EquityBrief.Core.Ledger;
using EquityBrief.Core.Time;
using EquityBrief.Worker.Ledger;

namespace EquityBrief.Tests.Checks;

// nightly-run, 21.4: the ledger's two book families over a constructed store. On the night a member's filing is first
// read its Fundamentals setup is laid open with no pick, and on the night after its pick is marked from the family's own
// trades; on a reshuffle night each member whose card clears six in nine is a quality setup, its pick marked the night
// after from the live rule's holdings; a later filing that leaves a card unreadable sells the Fundamentals setup at the
// close of the session it is read on and the quality setup at the close of the next reshuffle, each against every member
// of the index held the same sessions.
// see: A book family's setup is each member a reshuffle or a filing leaves at the ledger's gate, held while it clears the hold and never past a year against an equal-weighted hold of the index
public partial class NightlyRun
{
    static string BookDay(DateOnly day) => day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    static void BookBar(TemporaryStore store, string ticker, DateOnly day, decimal close) =>
        store.Execute(
            "INSERT INTO bar (ticker, session_date, open, high, low, close, volume, source, observed_at, raw_close) " +
            $"VALUES ('{ticker}', '{BookDay(day)}', '{close}', '{close + 1m}', '{close - 1m}', '{close}', 1000000, 'constructed', '{BookDay(day)}T21:00:00Z', '{close}');");

    static void BookFact(TemporaryStore store, string cik, FiledFactRow fact) =>
        store.Execute(
            "INSERT INTO filed_fact (cik, concept, period_start, period_end, dollars, filed, form, accession, run_id) " +
            $"VALUES ('{cik}', '{fact.Concept}', '{BookDay(fact.Start)}', '{BookDay(fact.End)}', '{fact.Value}', '{BookDay(fact.Filed)}', '{fact.Form}', '{cik}-{fact.Accession}', 'constructed');");

    static async Task BookNight(TemporaryStore store, DateOnly night)
    {
        var outcome = await new SetupLedger(FixedClock.At(new DateTimeOffset(night.Year, night.Month, night.Day, 21, 10, 0, TimeSpan.Zero), SessionZones.UnitedStates), store.DatabaseFile).NightAsync($"night-books-{BookDay(night)}");

        Assert.All(outcome.Indices, index => Assert.True(index.Fault is null, $"{index.Index} on {BookDay(night)}: {index.Fault}"));
    }

    [Fact]
    public async Task TheLedgersBookSetupsAreLaidAtAFilingAndAReshuffleMarkedByTheFamiliesOwnPicksTheNightAfterAndSoldWhereTheirGateFails()
    {
        using var store = new TemporaryStore().Migrated();

        // Three S&P 500 members at 100 on every weekday from August 2025, AAA at 110 on the sixth of November 2026 and 120
        // on the first of December, each night's bars added before it runs since the ledger's night is the store's newest
        // session; AAA and BBB filers every check passes on, their newest quarter filed on the fifth of August 2026, and
        // CCC no filer.
        var (sixth, seventh, september, second, novemberSixth, december) = (new DateOnly(2026, 8, 6), new DateOnly(2026, 8, 7), new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 2), new DateOnly(2026, 11, 6), new DateOnly(2026, 12, 1));
        var cursor = new DateOnly(2025, 8, 1);

        void BarsThrough(DateOnly through)
        {
            for (; cursor <= through; cursor = cursor.AddDays(1))
            {
                if (cursor.DayOfWeek is not DayOfWeek.Saturday and not DayOfWeek.Sunday)
                {
                    foreach (var ticker in new[] { "AAA", "BBB", "CCC" })
                    {
                        BookBar(store, ticker, cursor, ticker == "AAA" && cursor == novemberSixth ? 110m : ticker == "AAA" && cursor == december ? 120m : 100m);
                    }
                }
            }
        }

        foreach (var ticker in new[] { "AAA", "BBB", "CCC" })
        {
            store.Execute($"INSERT INTO membership (index_code, ticker, joined, \"left\", observed_at) VALUES ('GSPC', '{ticker}', '2025-01-02', NULL, '2026-01-05T21:00:00Z');");
        }

        foreach (var (ticker, cik) in new[] { ("AAA", "0000000101"), ("BBB", "0000000102") })
        {
            store.Execute($"INSERT INTO company (ticker, fetched_at, cik) VALUES ('{ticker}', '2026-08-01T23:40:00.000Z', '{cik}');");

            foreach (var fact in FixtureExpectations.NineFactsForTheLedger())
            {
                BookFact(store, cik, fact);
            }
        }

        // The night of Thursday the sixth of August first reads the filing of the fifth: AAA and BBB are Fundamentals
        // setups, bought at 100 with no stop and the year's cap, open, with no pick yet; no quality setup, the first of the
        // month being the third.
        BarsThrough(sixth);
        await BookNight(store, sixth);

        Assert.Equal(
            ["improving|AAA|100", "improving|BBB|100"],
            Texts(store, "SELECT family || '|' || ticker || '|' || entry FROM setup WHERE family IN ('improving', 'quality') ORDER BY family, ticker;"));
        Assert.Equal(["improving|3|2", "quality|3|0"], Texts(store, "SELECT family || '|' || members || '|' || setups FROM setup_night WHERE index_code = 'GSPC' AND family IN ('improving', 'quality') ORDER BY family;"));
        Assert.Equal(
            ["AAA|100|0|252|null|open", "BBB|100|0|252|null|open"],
            Texts(store, "SELECT ticker || '|' || entry || '|' || stop || '|' || cap || '|' || IFNULL(picked, 'null') || '|' || end FROM setup WHERE family = 'improving' ORDER BY ticker;"));

        // The live rule's trade of that night, stored by the family's own step after the ledger's, marks AAA picked on the
        // night after and BBB not.
        store.Execute(
            "INSERT INTO improving_trade (rule, index_code, ticker, chosen_on, filed, passed_checks, applied, before_passed, before_applied, buy_close, growth, through, sessions, end_reason) "
            + $"VALUES ('provisional', 'GSPC', 'AAA', '{BookDay(sixth)}', '2026-08-05', 9, 9, 0, 0, '100', 1.0, '{BookDay(sixth)}', 0, 'open');");
        BarsThrough(seventh);
        await BookNight(store, seventh);

        Assert.Equal(["AAA|1", "BBB|0"], Texts(store, $"SELECT ticker || '|' || picked FROM setup WHERE family = 'improving' AND session_date = '{BookDay(sixth)}' ORDER BY ticker;"));
        Assert.Empty(Texts(store, $"SELECT ticker FROM setup WHERE family = 'improving' AND session_date = '{BookDay(seventh)}';"));

        // Tuesday the first of September, the first session of its month, reshuffles: AAA and BBB clear six in nine and are
        // quality setups, CCC with no filer is not; the live rule's holding chosen that night marks AAA picked the night after.
        BarsThrough(september);
        await BookNight(store, september);

        Assert.Equal(
            ["AAA|100|0|252|null|open", "BBB|100|0|252|null|open"],
            Texts(store, "SELECT ticker || '|' || entry || '|' || stop || '|' || cap || '|' || IFNULL(picked, 'null') || '|' || end FROM setup WHERE family = 'quality' ORDER BY ticker;"));

        store.Execute(
            "INSERT INTO quality_holding (rule, index_code, ticker, chosen_on, buy_close, growth, through, end_reason) "
            + $"VALUES ('provisional', 'GSPC', 'AAA', '{BookDay(september)}', '100', 1.0, '{BookDay(september)}', 'open');");
        BarsThrough(second);
        await BookNight(store, second);

        Assert.Equal(["AAA|1", "BBB|0"], Texts(store, $"SELECT ticker || '|' || picked FROM setup WHERE family = 'quality' AND session_date = '{BookDay(september)}' ORDER BY ticker;"));
        Assert.Equal(["GSPC|quality|3|0"], Texts(store, $"SELECT index_code || '|' || family || '|' || members || '|' || setups FROM setup_night WHERE family = 'quality' AND session_date = '{BookDay(second)}';"));

        // AAA files the three months to the thirtieth of September on the fifth of November, net income alone, so from the
        // session that reads it no measure's four quarters can be read and its card clears nothing. Friday the sixth reads
        // it: AAA's Fundamentals setup of the sixth of August is sold at that close, 110 over 100, sixty-six sessions on, the
        // thirteen weeks of weekdays from Thursday the sixth of August to Thursday the fifth of November and the Friday,
        // against the three members held the same sessions, AAA's tenth and the others' nothing, a mean of a thirtieth;
        // BBB's stays open. AAA's quality setup waits for the next reshuffle.
        BookFact(store, "0000000101", new FiledFactRow("NetIncomeLoss", new DateOnly(2026, 7, 1), new DateOnly(2026, 9, 30), 10m, new DateOnly(2026, 11, 5), "10-Q", "q3-2026"));
        BarsThrough(novemberSixth);
        await BookNight(store, novemberSixth);

        var filed = Texts(store, $"SELECT end || '|' || sessions || '|' || ended_on || '|' || settled FROM setup WHERE family = 'improving' AND ticker = 'AAA' AND session_date = '{BookDay(sixth)}';").Single();

        Assert.Equal($"{SetupEnds.Filing}|66|{BookDay(novemberSixth)}|1", filed);
        Assert.Equal(0.1, LedgerScalar<double>(store, $"SELECT result FROM setup WHERE family = 'improving' AND ticker = 'AAA' AND session_date = '{BookDay(sixth)}';"), 9);
        Assert.Equal(0.1 / 3, LedgerScalar<double>(store, $"SELECT benchmark FROM setup WHERE family = 'improving' AND ticker = 'AAA' AND session_date = '{BookDay(sixth)}';"), 9);
        Assert.Equal(0.1 - (0.1 / 3), LedgerScalar<double>(store, $"SELECT edge FROM setup WHERE family = 'improving' AND ticker = 'AAA' AND session_date = '{BookDay(sixth)}';"), 9);
        Assert.Equal(["BBB|open|0"], Texts(store, $"SELECT ticker || '|' || end || '|' || settled FROM setup WHERE family = 'improving' AND session_date = '{BookDay(sixth)}' AND ticker <> 'AAA';"));
        Assert.Equal(["AAA|open|0", "BBB|open|0"], Texts(store, $"SELECT ticker || '|' || end || '|' || settled FROM setup WHERE family = 'quality' AND session_date = '{BookDay(september)}' ORDER BY ticker;"));

        // Tuesday the first of December reshuffles: AAA's quality setup of September is sold at that close, 120 over 100,
        // sixty-five sessions on, the thirteen weeks of weekdays between the two Tuesdays, against the members' mean of a
        // fifteenth; BBB's stays open; and the night's own quality setups are BBB alone, AAA's card clearing nothing.
        BarsThrough(december);
        await BookNight(store, december);

        var rebalanced = Texts(store, $"SELECT end || '|' || sessions || '|' || ended_on || '|' || settled FROM setup WHERE family = 'quality' AND ticker = 'AAA' AND session_date = '{BookDay(september)}';").Single();

        Assert.Equal($"{SetupEnds.Rebalance}|65|{BookDay(december)}|1", rebalanced);
        Assert.Equal(0.2, LedgerScalar<double>(store, $"SELECT result FROM setup WHERE family = 'quality' AND ticker = 'AAA' AND session_date = '{BookDay(september)}';"), 9);
        Assert.Equal(0.2 / 3, LedgerScalar<double>(store, $"SELECT benchmark FROM setup WHERE family = 'quality' AND ticker = 'AAA' AND session_date = '{BookDay(september)}';"), 9);
        Assert.Equal(["BBB|open|0"], Texts(store, $"SELECT ticker || '|' || end || '|' || settled FROM setup WHERE family = 'quality' AND session_date = '{BookDay(september)}' AND ticker <> 'AAA';"));
        Assert.Equal(["BBB"], Texts(store, $"SELECT ticker FROM setup WHERE family = 'quality' AND session_date = '{BookDay(december)}';"));
    }
}
