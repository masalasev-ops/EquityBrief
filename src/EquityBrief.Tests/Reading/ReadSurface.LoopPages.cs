using System.Globalization;
using EquityBrief.Tests.Checks;
using EquityBrief.Tests.Harness;

namespace EquityBrief.Tests.Reading;

// The pages 21.13 brings under the plain-words check, each drawn over stores the read-surface tests construct, so every
// kind of card each draws is read in the reader's words. Paper trading on the example account and on an account set,
// open and closed, with a family's versions and its scorecard; the Loop page on an index with no run, a month that tried
// nothing and a month whose every setup tried changes with their reasons, test years, findings and readings; and the
// Ledger on an index holding no setup and on one holding every setup's years with a chosen setup's path and the readings
// that went with winners.
// see: A redesigned page speaks in the reader's words: every card opens on a sentence from its numbers, and no internal term reaches the screen outside the glossary
public partial class ReadSurface
{
    internal static async Task<IReadOnlyList<(string Page, string Drawn, string Html)>> LoopPagesForPlainWords()
    {
        var pages = new List<(string, string, string)>();

        async Task Read(string page, string drawn, TemporaryStore store, params string[] routes)
        {
            using var host = new Host(store.Root);
            using var client = host.CreateClient();

            foreach (var route in routes)
            {
                pages.Add((page, drawn + " " + route, await client.GetStringAsync(route)));
            }
        }

        using (var example = await FamilyPagesStore())
        {
            await Read(PlainWords.PaperPage, "the S&P 500 on the example account", example, "/screens/paper");
        }

        using (var set = await PaperIndexStore())
        {
            await Read(
                PlainWords.PaperPage,
                "the S&P 400 on an account set",
                set,
                "/screens/paper?universe=400",
                "/screens/paper?universe=400&bought=closed",
                "/screens/paper?universe=400&variants=drift&rule=drift");
        }

        using (var loop = new TemporaryStore().Migrated())
        {
            loop.Execute("INSERT INTO loop_run (run_id, month, index_code, through, started_at, ended_at, folds) VALUES ('loop-test-MID-20260905T120000Z', '2026-09', 'MID', '2026-09-04', '2026-09-05T12:00:00Z', '2026-09-05T12:01:00Z', 5);");
            loop.Execute($"INSERT INTO loop_run (run_id, month, index_code, through, started_at, ended_at, folds) VALUES ('{OctoberRun}', '2026-10', 'MID', '2026-10-08', '2026-10-09T12:00:00Z', '2026-10-09T12:01:00Z', 5);");

            LoopProposalRow(loop, "breakout", "the grid", null, "the breakout rule today", "risks", 351, "1", 0, 0, 2, 0, "0", 1, "0.141", 0);
            LoopProposalRow(loop, "drift", "the grid", "the drift rule within 3 sessions", "the drift rule today", "risks", 1181, "0.1752", 0, 1, 4, 3, "12.5", 1, "0.09", 3);
            LoopProposalRow(loop, "drift", "winners against losers, ranked 1", "also requires rsi at or above 61.5", "the drift rule today", "risks", 1200, "0.5", 0, 0, 4, 1, "1", 1, "0.09", 0, "rsi at or above 61.5 kept 300 of the 1200 finished listings");
            LoopProposalRow(loop, "pullback", "the autopsy's exit, ranked 1", "exit: a trail 1.5 typical moves under the highest close once a close is 2 risks up, with no target", "the pullback rule today", "risks", 1044, "0.41", 0, 1, 4, 4, "30.1", 1, "0.09", 1, "the median trade's best close was 1.04 risks above its buy before it ended");
            LoopProposalRow(loop, "heavyweight", "the six settings, design (a)", "design (a), the profit gate and the interest cover", "the heavyweights rule today", "points", 57, "0.5999", 0, 0, 4, 2, "0.012", 1, "0.004", 3);
            LoopFindingRow(loop, "pullback", "best", 1.04, 1372, "the median trade's best close was 1.04 risks above its buy before it ended");
            LoopTestRow(loop, "drift", "the grid", 2022, 1, "the drift rule within 3 sessions", 64, 83, 13.58, 1.498);
            LoopTestRow(loop, "drift", "the grid", 2026, 0, null, 210, 210, -56.081, -56.081);
            LoopTestRow(loop, "heavyweight", "the six settings, design (a)", 2024, 1, "design (a), the profit gate and the interest cover", 12, 12, -0.039, 0.099);
            loop.Execute(
                "INSERT INTO loop_reading (run_id, index_code, family, reading, units, winners, losers, winners_median, losers_median, deciles) VALUES " +
                $"('{OctoberRun}', 'MID', 'drift', 'rsi', 1200, 560, 640, 63.1, 58.4, '[-0.2,-0.1,0,0.05,0.1,0.1,0.15,0.2,0.25,0.3]');");

            await Read(
                PlainWords.LoopPage,
                "the S&P 400's runs and the S&P 600 with none",
                loop,
                "/screens/loop?universe=400",
                "/screens/loop?universe=400&month=2026-09",
                "/screens/loop?universe=600");
        }

        using (var empty = new TemporaryStore().Migrated())
        {
            await Read(PlainWords.LedgerPage, "an index holding no setup", empty, "/screens/ledger");
        }

        using (var ledger = new TemporaryStore().Migrated())
        {
            foreach (var family in new[] { "pullback", "breakout", "drift", "dip", "heavyweight", "quality", "improving", "fundamentals" })
            {
                LedgerYearRow(ledger, family, 2025, 20, 5, 0, 0, 20, "-0.1", "-0.2", "-1,-0.8,-0.5,-0.2,0,0.2,0.5,1,2");
                LedgerYearRow(ledger, family, 2026, 40, 10, 8, 4, 30, "0.4", "0.1", "-1,-0.5,0,0.25,0.5,1,1.5,2,3");
            }

            LedgerSettledRow(ledger, "AAA", "2026-03-09", "target", "2026-03-16", 2.0, 1.5, "night");
            LedgerSettledRow(ledger, "BBB", "2025-02-03", "stop", "2025-02-10", -1.0, -1.2, "history");
            LinkRow(ledger, "loop-links-GSPC-20261016T120000Z", "GSPC", "rsi", 0.9, 0.2, "no link beyond the shuffle");
            LinkRow(ledger, "loop-links-GSPC-20261016T120000Z", "GSPC", "alpha_six", 0.4242, 0.1, "goes with winners");

            for (var day = new DateOnly(2026, 2, 23); day <= new DateOnly(2026, 3, 20); day = day.AddDays(1))
            {
                if (day.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday)
                {
                    continue;
                }

                var stamp = day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

                ledger.Execute($"INSERT INTO bar (ticker, session_date, open, high, low, close, volume, source, observed_at, raw_close) VALUES ('AAA', '{stamp}', '100', '101', '99', '100', 1000, 'constructed', '{stamp}T21:00:00Z', '100');");
            }

            await Read(
                PlainWords.LedgerPage,
                "every setup's years with a chosen path and the readings that went with winners",
                ledger,
                "/screens/ledger",
                "/screens/ledger?family=pullback",
                "/screens/ledger?family=pullback&setup=AAA.2026-03-09");
        }

        return pages;
    }
}
