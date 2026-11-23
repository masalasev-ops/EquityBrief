using System.Net;
using System.Text.RegularExpressions;
using EquityBrief.Core.Judge;
using EquityBrief.Tests.Harness;
using EquityBrief.Web.App;
using Xunit;

namespace EquityBrief.Tests.Reading;

// The 24.25 correction of 2026-11-22: the picks the earlier version made across all 1,500 stocks are no index's. On a store
// where only that version has picked, every index's tile reads not started with the night its first picks come from, and no
// index's tonight shows a pick; the earlier version's picks stand once, in their own region under the tiles, each card naming
// the index its stock belongs to, and fold into one line once all its trades are done. On a store where each index's
// version has picked, each index shows only its own, the tile and the heading counting the same.
// see: Each picker version's picks are bought and walked in an account of its own, and the record counts the newest version's
public partial class ReadSurface
{
    internal static string[] PickerAcrossRows =>
    [
        CheckReach.Key("15.26 The AI stock picker", "The earlier version's picks across all three indices"),
    ];

    static TemporaryStore AcrossStore(bool ownNights, bool sold = false)
    {
        var store = new TemporaryStore().Migrated();

        store.Execute($"INSERT INTO judge_pin (id, reading_pin_id, model, file, sha256, size, modified, context, prompt_version, written_at) VALUES (1, 1, 'm', 'f', 's', 1, '2026-11-01T00:00:00Z', 8192, '{JudgePrompt.Version}', '2026-11-20T01:00:00Z');");
        store.Execute("INSERT INTO judge_run (run_id, session_date, fact_version, pin_id, seed, outcome, started_at, ended_at, screened, kept, asks, asks_failed, detail) VALUES ('pick-20', '2026-11-20', 'v', 1, 20261120, 'picked', '2026-11-21T01:00:00Z', '2026-11-21T01:10:00Z', 1500, 3, 2, 0, '');");

        string Pick(string night, string run, string index, int rank, string ticker) =>
            $"('{night}', {rank}, '{run}', '{index}', '{ticker}', '1 week', '{JudgeMenu.Whole}', 'A reason.', '100', 2.0, '98', '104', NULL, NULL, 1)";

        var picks = new List<string> { Pick("2026-11-20", "pick-20", "GSPC", 1, "WWW"), Pick("2026-11-20", "pick-20", "SML", 2, "LFS"), Pick("2026-11-20", "pick-20", "MID", 3, "EXP") };
        var bars = new List<(string Ticker, string Session, string Open, string Close)> { ("WWW", "2026-11-20", "100", "100"), ("LFS", "2026-11-20", "100", "100"), ("EXP", "2026-11-20", "100", "100") };

        if (sold)
        {
            // Every one of its trades bought at Monday's open and sold at Tuesday's after a close far past its take-profit.
            foreach (var ticker in new[] { "WWW", "LFS", "EXP" })
            {
                bars.Add((ticker, "2026-11-23", "100", "130"));
                bars.Add((ticker, "2026-11-24", "130", "130"));
            }
        }

        if (ownNights)
        {
            store.Execute(
                "INSERT INTO judge_run (run_id, session_date, fact_version, pin_id, seed, outcome, started_at, ended_at, screened, kept, asks, asks_failed, detail, index_code, standing) VALUES "
                + "('pick-23-500', '2026-11-23', 'v', 1, 20261123, 'picked', '2026-11-24T00:00:00Z', '2026-11-24T00:10:00Z', 503, 1, 2, 0, '', 'GSPC', 'running'), "
                + "('pick-23-400', '2026-11-23', 'v', 1, 20261123, 'picked', '2026-11-24T00:10:00Z', '2026-11-24T00:20:00Z', 400, 1, 2, 0, '', 'MID', 'running'), "
                + "('pick-23-600', '2026-11-23', 'v', 1, 20261123, 'picked', '2026-11-24T00:20:00Z', '2026-11-24T00:30:00Z', 602, 1, 2, 0, '', 'SML', 'running');");
            picks.Add(Pick("2026-11-23", "pick-23-500", "GSPC", 1, "LRG"));
            picks.Add(Pick("2026-11-23", "pick-23-400", "MID", 1, "MDD"));
            picks.Add(Pick("2026-11-23", "pick-23-600", "SML", 1, "SMM"));
            bars.AddRange([("LRG", "2026-11-23", "100", "100"), ("MDD", "2026-11-23", "100", "100"), ("SMM", "2026-11-23", "100", "100")]);
        }

        store.Execute(
            "INSERT INTO judge_pick (session_date, rank, run_id, index_code, ticker, hold, take_profit, reason, last_close, typical_move, stop, target, give_up_distance, fields, in_quarter) VALUES "
            + string.Join(", ", picks) + ";");
        store.Execute(
            "INSERT INTO bar (ticker, session_date, open, high, low, close, volume, source, observed_at) VALUES "
            + string.Join(", ", bars.Select(bar => $"('{bar.Ticker}', '{bar.Session}', '{bar.Open}', '{bar.Close}', '{bar.Open}', '{bar.Close}', 1000, 'eod', '{bar.Session}T22:00:00Z')")) + ";");
        store.Execute(
            "INSERT INTO indicator (ticker, session_date, name, value, bar_count) VALUES "
            + string.Join(", ", bars.Select(bar => $"('{bar.Ticker}', '{bar.Session}', 'atr14', 2.0, 14)")) + ";");

        return store;
    }

    static async Task<Dictionary<string, string>> PickerPagesAsync(TemporaryStore store)
    {
        using var host = new Host(store.Root);
        using var client = host.CreateClient();
        var pages = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var universe in Universes.Offered)
        {
            pages[universe.Code] = WebUtility.HtmlDecode(await client.GetStringAsync($"/screens/judge?universe={universe.Word}"));
        }

        return pages;
    }

    static string[] CardsIn(string region) =>
        [.. Regex.Matches(region, "<article class=\"home-idea judge-card\" data-ticker=\"([A-Z]+)\">").Select(match => match.Groups[1].Value)];

    [Fact]
    public async Task OnlyTheEarlierVersionHasPickedSoEveryIndexIsNotStartedAndItsPicksStandOnceInTheirOwnRegion()
    {
        using var store = AcrossStore(ownNights: false);
        var pages = await PickerPagesAsync(store);

        // The night of Friday 20 Nov is the newest any version ran, so each index's first picks come from Monday 23 Nov's.
        const string NotStarted = "Not started. Its first picks come from the night of Mon 23 Nov.";

        Assert.Equal(NotStarted, SinglePageApp.NotStartedWords(new DateOnly(2026, 11, 23)));

        foreach (var (code, page) in pages)
        {
            var tiles = Blocks(page, "<section class=\"card\" data-card=\"judge-tiles\">.*?</section>").Single();

            Assert.Equal(3, Regex.Matches(tiles, "<b>Not started</b><span data-started=\"no\">Its first picks come from the night of Mon 23 Nov.</span>").Count);
            Assert.DoesNotContain("picked 3 tonight", tiles, StringComparison.Ordinal);

            var tonight = Blocks(page, "<section class=\"card\" data-card=\"judge-picks\">.*?</section>").Single();

            Assert.Empty(CardsIn(tonight));
            Assert.Contains($"<p class=\"empty\" data-started=\"no\" data-summary>{NotStarted}</p>", tonight, StringComparison.Ordinal);
            Assert.Contains("picks tonight (0)</h2>", tonight, StringComparison.Ordinal);

            // The scoreboard and the four accounts say the same, in place of an empty record's zeros.
            Assert.Equal(2, Regex.Matches(string.Concat(Blocks(page, "<section class=\"card\" data-card=\"judge-(score|chart)\">.*?</section>")), $"data-started=\"no\" data-summary>{Regex.Escape(NotStarted)}</p>").Count);

            // The earlier version's three, once, under the tiles, each naming its own index, in its own account.
            var across = Blocks(page, "<section class=\"card\" data-card=\"judge-across\">.*?</section>").Single();

            Assert.True(page.IndexOf("data-card=\"judge-tiles\"", StringComparison.Ordinal) < page.IndexOf("data-card=\"judge-across\"", StringComparison.Ordinal), code);
            Assert.Contains($"<h2>{SinglePageApp.AcrossTitle}</h2>", across, StringComparison.Ordinal);
            Assert.Equal(["WWW", "LFS", "EXP"], CardsIn(across));
            Assert.Contains("Pick 1 of 3 on the night of 20 Nov, a stock of the S&P 500", across, StringComparison.Ordinal);
            Assert.Contains("Pick 2 of 3 on the night of 20 Nov, a stock of the S&P 600", across, StringComparison.Ordinal);
            Assert.Contains("Pick 3 of 3 on the night of 20 Nov, a stock of the S&P 400", across, StringComparison.Ordinal);
            Assert.Contains("Buys at the next open after its night in the earlier version's own account.", across, StringComparison.Ordinal);
            Assert.Contains("data-across=\"open\"", across, StringComparison.Ordinal);
            Assert.DoesNotContain("large-cap picks tonight (3)", page, StringComparison.Ordinal);
        }

        // Once every trade of it has been bought and sold, its region folds into one line of its record.
        using var sold = AcrossStore(ownNights: false, sold: true);
        var folded = Blocks((await PickerPagesAsync(sold))["GSPC"], "<section class=\"card\" data-card=\"judge-across\">.*?</section>").Single();

        Assert.Contains("data-across=\"folded\"", folded, StringComparison.Ordinal);
        Assert.Contains("3 of its trades have closed", folded, StringComparison.Ordinal);
        Assert.Empty(CardsIn(folded));
    }

    [Fact]
    public async Task WhereEachIndexsVersionHasPickedEachIndexShowsOnlyItsOwnAndItsTileCountsTheSame()
    {
        using var store = AcrossStore(ownNights: true);
        var pages = await PickerPagesAsync(store);

        foreach (var (code, own) in new[] { ("GSPC", "LRG"), ("MID", "MDD"), ("SML", "SMM") })
        {
            var tonight = Blocks(pages[code], "<section class=\"card\" data-card=\"judge-picks\">.*?</section>").Single();

            Assert.Equal([own], CardsIn(tonight));
            Assert.Contains("picks tonight (1)</h2>", tonight, StringComparison.Ordinal);

            var tile = Blocks(pages[code], $"<a href=\"#/picker\\?universe=[0-9]+\" data-index=\"{code}\" class=\"on\" aria-current=\"page\">.*?</a>").Single();

            Assert.Contains("picked 1 tonight", tile, StringComparison.Ordinal);
            Assert.DoesNotContain("Not started", tile, StringComparison.Ordinal);

            // The earlier version's picks never stand among an index's own.
            Assert.Equal(["WWW", "LFS", "EXP"], CardsIn(Blocks(pages[code], "<section class=\"card\" data-card=\"judge-across\">.*?</section>").Single()));
            Assert.DoesNotContain("data-ticker=\"WWW\"", tonight, StringComparison.Ordinal);
        }
    }
}
