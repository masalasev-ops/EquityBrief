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
        CheckReach.Key("15.26 The AI stock picker", "One night at a time"),
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

    static async Task<Dictionary<string, string>> PickerPagesAsync(TemporaryStore store, string night = "")
    {
        using var host = new Host(store.Root);
        using var client = host.CreateClient();
        var pages = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var universe in Universes.Offered)
        {
            pages[universe.Code] = WebUtility.HtmlDecode(await client.GetStringAsync($"/screens/judge{(night.Length == 0 ? string.Empty : "/" + night)}?universe={universe.Word}"));
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

            // From the operator's correction of 2026-11-23 the night of 20 Nov is the night drawn, and its region is the
            // earlier version's, in place of an index's picks.
            Assert.Empty(Blocks(page, "<section class=\"card\" data-card=\"judge-picks\">.*?</section>"));

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
            Assert.Contains("Picked before its range began, so it buys at the open. In the earlier version's own account.", across, StringComparison.Ordinal);
            Assert.DoesNotContain("large-cap picks tonight (3)", page, StringComparison.Ordinal);
            Assert.Contains("<span class=\"night-picker\" data-route=\"#/picker/\" data-night=\"2026-11-20\">", page, StringComparison.Ordinal);
        }
    }

    // From the operator's correction of 2026-11-23: on the newest night, 23 Nov, the earlier version is one line under the tiles
    // while it holds trades still open, its link setting the picker to 20 Nov, where its picks stand in that night's region and
    // each tile reads as it stood then; the line goes once its last trade has closed, its record kept in the scoreboard and its
    // account beneath the four accounts.
    // see: The AI stock picker's page draws one night at a time, and its count line says what decided the number
    [Fact]
    public async Task ThePickerPageDrawsOneNightAtATimeAndTheEarlierVersionStandsOnItsOwnNight()
    {
        using var store = AcrossStore(ownNights: true);
        var newest = await PickerPagesAsync(store);
        var then = await PickerPagesAsync(store, "2026-11-20");

        foreach (var code in new[] { "GSPC", "MID", "SML" })
        {
            var word = Universes.ByCode(code)!.Word;
            var tiles = Blocks(newest[code], "<section class=\"card\" data-card=\"judge-tiles\">.*?</section>").Single();

            Assert.Contains($"<p class=\"judge-across-open\" data-across-open=\"3\">{SinglePageApp.AcrossOpenWords(new DateOnly(2026, 11, 20), 3)} <a href=\"#/picker/2026-11-20?universe={word}\">See that night.</a></p>", tiles, StringComparison.Ordinal);
            Assert.Equal("The earlier version, across all 1,500 stocks: 3 trades still open from the night of 20 Nov.", SinglePageApp.AcrossOpenWords(new DateOnly(2026, 11, 20), 3));
            Assert.Empty(Blocks(newest[code], "<section class=\"card\" data-card=\"judge-across\">.*?</section>"));
            Assert.Contains("<span class=\"night-picker\" data-route=\"#/picker/\" data-night=\"2026-11-23\">", newest[code], StringComparison.Ordinal);
            Assert.Contains($"href=\"#/picker/2026-11-20?universe={word}\" title=\"The night before, 2026-11-20\"", newest[code], StringComparison.Ordinal);

            // Its record stays in the scoreboard and its account beneath the four, as an earlier version's.
            Assert.Contains("data-across=\"record\"", Blocks(newest[code], "<section class=\"card\" data-card=\"judge-score\">.*?</section>").Single(), StringComparison.Ordinal);
            Assert.Contains("data-apart=\"across\"", Blocks(newest[code], "<section class=\"card\" data-card=\"judge-chart\">.*?</section>").Single(), StringComparison.Ordinal);

            // On 20 Nov: the earlier version's three in that night's region, the tiles as they stood then, and no line.
            var past = then[code];
            var acrossThen = Blocks(past, "<section class=\"card\" data-card=\"judge-across\">.*?</section>").Single();

            Assert.Equal(["WWW", "LFS", "EXP"], CardsIn(acrossThen));
            Assert.Contains($"<h2>{SinglePageApp.AcrossTitle}</h2>", acrossThen, StringComparison.Ordinal);
            Assert.Empty(Blocks(past, "<section class=\"card\" data-card=\"judge-picks\">.*?</section>"));
            Assert.DoesNotContain("judge-across-open", past, StringComparison.Ordinal);
            Assert.Equal(3, Regex.Matches(Blocks(past, "<section class=\"card\" data-card=\"judge-tiles\">.*?</section>").Single(), "<b>Not started</b>").Count);
            Assert.Contains("Night of 2026-11-20", past, StringComparison.Ordinal);

            // The scoreboard and the four accounts stay as the record stands, never the not started of 20 Nov's tiles, and
            // the lede says nothing of a run that did not happen.
            Assert.DoesNotContain("data-started=\"no\"", string.Concat(Blocks(past, "<section class=\"card\" data-card=\"judge-(score|chart)\">.*?</section>")), StringComparison.Ordinal);
            Assert.DoesNotContain("has not run yet", past, StringComparison.Ordinal);
            Assert.Contains($"href=\"#/picker/2026-11-23?universe={word}\" title=\"The night after, 2026-11-23\"", past, StringComparison.Ordinal);
        }

        // Its three sell at the open on 24 Nov, after a close past their take-profit on the 23rd: with a night of 24 Nov the
        // newest, the line goes and its record stays; the night of 23 Nov, drawn as a past night, still counts the three open.
        using var sold = AcrossStore(ownNights: true, sold: true);

        sold.Execute("INSERT INTO judge_run (run_id, session_date, fact_version, pin_id, seed, outcome, started_at, ended_at, screened, kept, asks, asks_failed, detail, index_code, standing) VALUES ('pick-24-500', '2026-11-24', 'v', 1, 20261124, 'none', '2026-11-25T00:00:00Z', '2026-11-25T00:10:00Z', 503, 0, 2, 0, '', 'GSPC', 'running');");

        Assert.Contains("data-across-open=\"3\"", (await PickerPagesAsync(sold, "2026-11-23"))["GSPC"], StringComparison.Ordinal);

        var closed = (await PickerPagesAsync(sold))["GSPC"];

        Assert.DoesNotContain("judge-across-open", closed, StringComparison.Ordinal);
        Assert.Empty(Blocks(closed, "<section class=\"card\" data-card=\"judge-across\">.*?</section>"));
        Assert.Contains("an earlier version kept apart from this record: 3 of its 3 trades closed", Blocks(closed, "<section class=\"card\" data-card=\"judge-score\">.*?</section>").Single(), StringComparison.Ordinal);
    }

    // The count line on a night where the cap decided the number, from the stored count of marks, read on the page.
    [Fact]
    public async Task TheCountLineSaysTheLimitDecidedWhereAQuarterOfTheMarkedIsMoreThanFive()
    {
        using var store = AcrossStore(ownNights: true);

        store.Execute("UPDATE judge_run SET kept = 132 WHERE run_id = 'pick-23-500';");

        var page = (await PickerPagesAsync(store))["GSPC"];
        var tonight = Blocks(page, "<section class=\"card\" data-card=\"judge-picks\">.*?</section>").Single();

        Assert.Contains("<p class=\"count-line\" data-summary>It rated 132 of the S&P 500's stocks worth buying tonight. A quarter would be 33; it picked 1, under its limit of 5.</p>", tonight, StringComparison.Ordinal);
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

            // The earlier version's picks never stand among an index's own, and from the correction of 2026-11-23 stand on
            // 20 Nov alone, the night of 23 Nov drawing them as one line under the tiles.
            Assert.Empty(Blocks(pages[code], "<section class=\"card\" data-card=\"judge-across\">.*?</section>"));
            Assert.Contains("data-across-open=\"3\"", pages[code], StringComparison.Ordinal);
            Assert.DoesNotContain("data-ticker=\"WWW\"", tonight, StringComparison.Ordinal);
        }
    }
}
