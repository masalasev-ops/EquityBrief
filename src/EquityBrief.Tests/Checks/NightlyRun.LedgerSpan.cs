using EquityBrief.Core.Bars;
using EquityBrief.Core.Families;
using EquityBrief.Core.Time;
using EquityBrief.Worker.Indices;
using EquityBrief.Worker.Ledger;

namespace EquityBrief.Tests.Checks;

// nightly-run: the history built by hand over a constructed S&P 500 whose span ends before the bar store's newest
// session, so each member's closes run on past its own bars for the replay. On each
// rebalance in the span every member of each sector's size cut is a heavyweights' setup, its readings read over the
// member's own closes and its relative strength at a high worked by hand; and a span reaching a session holding the
// night's own rows is written but for that session, which is left to the night with its row as the night wrote it.
// see: The history's setups are settled by the build alone, which replays them over the store's own prices after its span
public partial class NightlyRun
{
    // Four members in two sectors over the exchange's sessions from June 2025 to September's first in 2026, each count
    // filed before the first session, over an index flat at 100: T1 and H1 rising, T3 falling and H2 flat.
    static readonly (string Ticker, string Cik, string Sector, decimal Shares, Func<int, decimal> Close)[] SpanMembers =
    [
        ("T1", "0000000001", GicsSectors.InformationTechnology, 10m, at => 100m + (0.10m * at)),
        ("T3", "0000000003", GicsSectors.InformationTechnology, 8m, at => 150m - (0.05m * at)),
        ("H1", "0000000011", GicsSectors.HealthCare, 20m, at => 50m + (0.05m * at)),
        ("H2", "0000000012", GicsSectors.HealthCare, 5m, _ => 80m),
    ];

    static readonly DateOnly SpanFrom = new(2026, 7, 1);

    [Fact]
    public async Task TheHistoryBuiltOverASpanEndingBeforeTheNewestBarReadsTheHeavyweightsOverEachMembersOwnCloses()
    {
        var (store, sessions) = SpanStore();
        using var held = store;
        var through = sessions[^13];
        var output = new StringWriter();

        Assert.Equal(0, await SpanLedger(store).BuildAsync(store.Root, IndexFamilies.LargeIndex, SpanFrom, through, output));

        // Every member's closes run on in the bar store for the twelve sessions after the span's end.
        Assert.Contains($"4 name(s) replayed past {SpanStamp(through)} over the bar store's closes through {SpanStamp(sessions[^1])}", output.ToString(), StringComparison.Ordinal);

        // The rebalances of July and August each hold every member, each sector's size cut its two members.
        var rows = Texts(store, $"SELECT ticker || '|' || session_date || '|' || relative_strength_high FROM setup WHERE family = '{HeavyweightRule.Name}' ORDER BY session_date, ticker;")
            .Select(row => row.Split('|'))
            .ToArray();

        Assert.Equal(
            ["H1|2026-07-01", "H2|2026-07-01", "T1|2026-07-01", "T3|2026-07-01", "H1|2026-08-03", "H2|2026-08-03", "T1|2026-08-03", "T3|2026-08-03"],
            rows.Select(row => $"{row[0]}|{row[1]}"));

        // By hand: over an index flat at 100 a member's relative strength is its close over its highest close of the 252
        // sessions to the session. T1 and H1 rise and H2 is flat, each at its high; T3 falls, so its highest is the oldest
        // of the 252, 150 less 0.05 a session at the session's place over the same 251 sessions before it.
        foreach (var row in rows)
        {
            var at = Array.IndexOf(sessions, DateOnly.ParseExact(row[1], "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture));
            var expected = row[0] == "T3" ? (150.0 - (0.05 * at)) / (150.0 - (0.05 * (at - 251))) : 1.0;

            Assert.True(at >= 251);
            Assert.Equal(expected, double.Parse(row[2], System.Globalization.CultureInfo.InvariantCulture), 12);
        }
    }

    [Fact]
    public async Task AHistoryBuildOverASpanReachingASessionTheNightWroteLeavesThatSessionToTheNight()
    {
        var (store, sessions) = SpanStore();
        using var held = store;
        var through = sessions[^13];

        // The night's own row of the span's last session, one a family as the night writes them.
        store.Execute($"INSERT INTO setup_night (index_code, family, session_date, members, setups, live_passes, source) VALUES ('GSPC', 'pullback', '{SpanStamp(through)}', 4, 0, 0, 'night');");

        var output = new StringWriter();

        Assert.Equal(0, await SpanLedger(store).BuildAsync(store.Root, IndexFamilies.LargeIndex, SpanFrom, through, output, again: true));
        Assert.Contains($"1 session(s) of the span hold the night's own rows, {SpanStamp(through)} to {SpanStamp(through)}, and are left to the night", output.ToString(), StringComparison.Ordinal);

        // The night's row stands as it wrote it, the history writes no row of its session, and every other session of
        // the span is the history's.
        Assert.Equal(["pullback|night|4"], Texts(store, $"SELECT family || '|' || source || '|' || members FROM setup_night WHERE session_date = '{SpanStamp(through)}';"));
        Assert.Empty(Texts(store, $"SELECT ticker FROM setup WHERE session_date = '{SpanStamp(through)}';"));
        Assert.Equal(
            sessions.Where(session => session >= SpanFrom && session < through).Select(SpanStamp),
            Texts(store, "SELECT DISTINCT session_date FROM setup_night WHERE source = 'history' ORDER BY session_date;"));
    }

    static SetupLedger SpanLedger(TemporaryStore store) =>
        new(FixedClock.At(new DateTimeOffset(2026, 9, 2, 15, 0, 0, TimeSpan.Zero), SessionZones.UnitedStates), store.DatabaseFile);

    static string SpanStamp(DateOnly session) => session.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);

    // The constructed S&P 500: each member and its bars in the bar store, its company and its count as the pulls filed
    // them, and the index's closes as its pull filed them.
    static (TemporaryStore Store, DateOnly[] Sessions) SpanStore()
    {
        var store = new TemporaryStore().Migrated();
        var sessions = ExchangeClosures.SessionsBetween(new DateOnly(2025, 5, 31), new DateOnly(2026, 9, 2)).ToArray();
        var statements = new List<string>();

        foreach (var (ticker, cik, sector, shares, close) in SpanMembers)
        {
            statements.Add($"INSERT INTO membership (index_code, ticker, joined, \"left\", observed_at) VALUES ('GSPC', '{ticker}', NULL, NULL, '2025-01-01T00:00:00Z');");
            statements.Add($"INSERT INTO pulled_company (ticker, cik, sector, industry_group, industry, sub_industry, delisted_on, pull) VALUES ('{ticker}', '{cik}', '{sector}', NULL, NULL, NULL, NULL, 'span-pull');");
            statements.Add($"INSERT INTO pulled_shares (ticker, period_end, filing_date, shares, basis_session, pull) VALUES ('{ticker}', '2025-03-31', '2025-05-01', '{ComparisonPrice(shares)}', '{SpanStamp(sessions[^1])}', 'span-pull');");

            for (var at = 0; at < sessions.Length; at++)
            {
                statements.Add(
                    "INSERT INTO bar (ticker, session_date, open, high, low, close, raw_close, volume, source, observed_at) "
                    + $"VALUES ('{ticker}', '{SpanStamp(sessions[at])}', '{ComparisonPrice(close(at))}', '{ComparisonPrice(close(at) + 1m)}', '{ComparisonPrice(close(at) - 1m)}', '{ComparisonPrice(close(at))}', '{ComparisonPrice(close(at))}', 1000, 'constructed', '{SpanStamp(sessions[at])}T21:00:00Z');");
            }
        }

        foreach (var session in sessions)
        {
            statements.Add($"INSERT INTO pulled_market_bar (series, session_date, open, high, low, close, pull) VALUES ('GSPC', '{SpanStamp(session)}', '100', '100', '100', '100', 'span-pull');");
        }

        ComparisonExecute(store, statements);

        return (store, sessions);
    }
}
