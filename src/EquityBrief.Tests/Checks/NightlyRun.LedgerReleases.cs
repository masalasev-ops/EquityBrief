using EquityBrief.Core.Ledger;
using EquityBrief.Core.Time;
using EquityBrief.Worker.Ledger;

namespace EquityBrief.Tests.Checks;

// nightly-run, 20.8: the ledger's step reads the release readings of its own rows of the days a slot reads back across
// before the night again. A member's release that a slot read only after the night that could read it stored its rows
// is written into that night's setups and its member's readings by the next night, a row whose readings stand as read is
// left as it is, and the step's row counts the rows it read again.
// see: The release readings join the ledger's catalogue once their history read is complete, and their use is judged on the years after the model's cutoff
public partial class NightlyRun
{
    [Fact]
    public async Task TheLedgerStepReadsTheReleaseReadingsOfItsOwnRowsOfTheWeekBeforeAgain()
    {
        using var store = new TemporaryStore().Migrated();

        // The store of the ledger's step above: three S&P 500 members at 100 over seventy sessions, AAA closing at 110 on
        // the seventy-first after a print beating by ten per cent, a breakout and a drift setup; AAA and BBB file under
        // filers the store knows, CCC under none, and a reading version's history read is complete.
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
        store.Execute($"INSERT INTO calendar (ticker, event_date, kind, timing, detail, observed_at) VALUES ('AAA', '{LedgerStamp(70)}', 'earnings', 'before', '{{\"surprise\":\"10\"}}', '{LedgerStamp(70)}T21:00:00Z');");
        store.Execute("INSERT INTO company (ticker, fetched_at, cik) VALUES ('AAA', '2026-01-05T23:40:00.000Z', '0000000101'), ('BBB', '2026-01-05T23:40:00.000Z', '0000000102');");
        store.Execute(
            "INSERT INTO reading_pin (model, model_file, sha256, size, modified, runtime_name, runtime_version, prompt_version, temperature, seed, answer_tokens, context, cutoff, written_at, history_completed_at, run_id) "
            + "VALUES ('m', 'f', 'h', 1, '2026-01-02T00:00:00.0000000Z', 'r', 'v', 'p', 0, 1, 1, 1, '2025-01-31', '2026-01-02T00:00:00Z', '2026-01-03T00:00:00Z', 'constructed');");

        var clock = FixedClock.At(new DateTimeOffset(2026, 4, 15, 21, 10, 0, TimeSpan.Zero), SessionZones.UnitedStates);
        var ledger = new SetupLedger(clock, store.DatabaseFile);

        await ledger.NightAsync("night-ledger");

        // No slot has read AAA's release yet: its two setups and every member's readings hold none of the four.
        Assert.Equal(["breakout|1|1|1|1", "drift|1|1|1|1"], Texts(store, "SELECT family || '|' || (release_results IS NULL) || '|' || (release_outlook IS NULL) || '|' || (release_tone IS NULL) || '|' || (release_demand IS NULL) FROM setup ORDER BY family;"));

        // A slot then stores and reads AAA's release, which the SEC accepted at 20:30 UTC on the seventieth session, before
        // the late slot's start, so that session's night could read it.
        store.Execute(
            "INSERT INTO release_document (accession, cik, form, filed, accepted_at, exhibit, url, text_gzip, text_sha256, text_length, run_id) "
            + $"VALUES ('0000000101-26-000001', '0000000101', '8-K', '{LedgerStamp(70)}', '{LedgerStamp(70)}T20:30:00Z', 'ex99.htm', 'https://www.sec.gov/Archives/edgar/data/1/ex99.htm', zeroblob(1), 'h', 1, 'constructed');");
        store.Execute(
            "INSERT INTO release_reading (accession, exhibit, pin, outcome, cause, results, outlook, tone, demand, seconds, prompt_tokens, read_at, run_id) "
            + $"VALUES ('0000000101-26-000001', 'ex99.htm', 1, 'read', NULL, 'better', 'raised', 'confident', 'stronger', 3.0, 5000, '{LedgerStamp(71)}T12:10:00Z', 'constructed');");

        foreach (var ticker in new[] { "AAA", "BBB", "CCC" })
        {
            LedgerBar(store, ticker, 71, 100m, 1_000_000);
        }

        var next = await ledger.NightAsync("night-ledger-next");

        // The night after reads it into the seventieth's rows: AAA's two setups and its member row, three rows, while BBB's
        // and CCC's, holding no release, stand as read and are left; its own night reads it too.
        var first = LedgerReadings.IndexOf(LedgerReleases.Columns[0]);
        double?[] Readings(string ticker, int at) =>
            System.Text.Json.JsonSerializer.Deserialize<double?[]>(Texts(store, $"SELECT readings FROM ledger_reading WHERE ticker = '{ticker}' AND session_date = '{LedgerStamp(at)}';").Single())![first..(first + 4)];

        Assert.Equal(["breakout|1.0|1.0|1.0|1.0", "drift|1.0|1.0|1.0|1.0"], Texts(store, $"SELECT family || '|' || release_results || '|' || release_outlook || '|' || release_tone || '|' || release_demand FROM setup WHERE session_date = '{LedgerStamp(70)}' ORDER BY family;"));
        Assert.Equal<double?>([1.0, 1.0, 1.0, 1.0], Readings("AAA", 70));
        Assert.Equal<double?>([null, null, null, null], Readings("BBB", 70));
        Assert.Equal<double?>([null, null, null, null], Readings("CCC", 70));
        Assert.Equal<double?>([1.0, 1.0, 1.0, 1.0], Readings("AAA", 71));
        Assert.Equal(3, next.Indices.Single(index => index.Index == "GSPC").ReleasesAgain);
        Assert.Contains(
            "2 window(s) closed on the S&P 500, 3 row(s) of the nights before with their release readings read again",
            Texts(store, $"SELECT detail FROM run_log WHERE stage = '{SetupLedger.Stage}' ORDER BY rowid DESC LIMIT 1;").Single(),
            StringComparison.Ordinal);

        // Run again, every row stands as read and none is read again.
        var again = await ledger.NightAsync("night-ledger-again");

        Assert.Equal(0, again.Indices.Single(index => index.Index == "GSPC").ReleasesAgain);
        Assert.DoesNotContain("read again", Texts(store, $"SELECT detail FROM run_log WHERE stage = '{SetupLedger.Stage}' ORDER BY rowid DESC LIMIT 1;").Single(), StringComparison.Ordinal);
    }
}
