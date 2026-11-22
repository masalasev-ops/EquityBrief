using System.Net;
using System.Text.RegularExpressions;
using EquityBrief.Core.Cards;
using EquityBrief.Core.Judge;
using EquityBrief.Tests.Harness;
using EquityBrief.Web.App;
using EquityBrief.Worker.Judge;
using Xunit;

namespace EquityBrief.Tests.Reading;

// The picker's page drawn from its approved mockup, read back off the page over a constructed store of three indices: the
// three index tiles, tonight's picks one at a time with their two tabs, each fact the picker named with the tenth it stood in
// among its own index's stocks worked by hand, everything else it saw grouped, and each index's own rules reading none yet.
public partial class ReadSurface
{
    internal static string[] PickerPageRows =>
    [
        CheckReach.Key("15.26 The AI stock picker", "Its three accounts"),
        CheckReach.Key("15.26 The AI stock picker", "Tonight's picks one at a time"),
        CheckReach.Key("15.26 The AI stock picker", "Where each fact stands"),
        CheckReach.Key("15.26 The AI stock picker", "Its own rules"),
    ];

    // Each member's return over the month on the night: the S&P 500's four and the S&P 600's five, and the S&P 400's two,
    // whose run marked none.
    static readonly (string Index, string Ticker, double Month)[] PickerPageMembers =
    [
        ("GSPC", "AAA", 0.20), ("GSPC", "BBB", 0.08), ("GSPC", "CCC", 0.06), ("GSPC", "DDD", 0.12),
        ("MID", "MMM", 0.03), ("MID", "NNN", 0.01),
        ("SML", "ZZZ", 0.05), ("SML", "YYY", 0.01), ("SML", "XXX", 0.02), ("SML", "WWW", 0.03), ("SML", "VVV", 0.04),
    ];

    // The night of 19 Nov under this prompt: the S&P 500's run picks AAA and BBB, the S&P 600's ZZZ and WWW, each naming its
    // return over the month and two facts it was shown none of, and the S&P 400's run marks none. Each fact row is stored
    // as the night's writer stores it, its tenths placed by the night's own code. Every stock opens and closes at 100, so each
    // first pick is bought at the open of 20 Nov and stays open; the account of $10,000 risking all of it a trade with a
    // position at most 6 in 10 of it buys 60 shares of the first, $6,000, and has $4,000 left, under the second's 60.
    static TemporaryStore PickerPageStore()
    {
        var store = new TemporaryStore().Migrated();
        var night = new DateOnly(2026, 11, 19);

        AccountFile.Write(store.Root, new AccountSettings(10_000m, 100m, 0.6m));

        store.Execute(
            "INSERT INTO judge_pin (id, reading_pin_id, model, file, sha256, size, modified, context, prompt_version, written_at) VALUES "
            + $"(1, 1, 'm', 'f', 's', 1, '2026-11-01T00:00:00Z', 8192, '{JudgePrompt.Version}', '2026-11-19T01:00:00Z');");
        store.Execute(
            "INSERT INTO judge_run (run_id, session_date, fact_version, pin_id, seed, outcome, started_at, ended_at, screened, kept, asks, asks_failed, detail, index_code, standing) VALUES "
            + "('pick-500', '2026-11-19', 'w', 1, 20261119, 'picked', '2026-11-20T01:00:00Z', '2026-11-20T01:10:00Z', 4, 6, 2, 0, '', 'GSPC', 'running'), "
            + "('pick-400', '2026-11-19', 'w', 1, 20261119, 'none', '2026-11-20T01:10:00Z', '2026-11-20T01:20:00Z', 2, 0, 1, 0, '', 'MID', 'running'), "
            + "('pick-600', '2026-11-19', 'w', 1, 20261119, 'picked', '2026-11-20T01:20:00Z', '2026-11-20T01:30:00Z', 5, 8, 2, 0, '', 'SML', 'running');");

        string Pick(string run, string index, int rank, string ticker) =>
            $"('2026-11-19', {rank}, '{run}', '{index}', '{ticker}', '1 week', '{JudgeMenu.Whole}', 'Picked on its month.', '100', 2.0, '98', '104', NULL, 'return_month,rsi,news', 1)";

        store.Execute(
            "INSERT INTO judge_pick (session_date, rank, run_id, index_code, ticker, hold, take_profit, reason, last_close, typical_move, stop, target, give_up_distance, fields, in_quarter) VALUES "
            + string.Join(", ", Pick("pick-500", "GSPC", 1, "AAA"), Pick("pick-500", "GSPC", 2, "BBB"), Pick("pick-600", "SML", 1, "ZZZ"), Pick("pick-600", "SML", 2, "WWW")) + ";");

        var placed = JudgeFacts.PlacedAmongTheirIndex(
        [
            .. PickerPageMembers.Select(member => (
                new JudgeFact(member.Index, member.Ticker, night, new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["sector"] = "Industrials", ["size"] = "small", ["return_month"] = JudgeFacts.Percent(member.Month), ["depth"] = "2%",
                }),
                (IReadOnlyDictionary<string, double?>)new Dictionary<string, double?>(StringComparer.Ordinal) { ["return_month"] = member.Month })),
        ]);

        store.Execute("INSERT INTO judge_fact (index_code, session_date, ticker, version, fields, row_text, sheet_text, written_at, run_id) VALUES "
            + string.Join(", ", placed.Select(fact => $"('{fact.Index}', '2026-11-19', '{fact.Ticker}', 'w', '{JudgeFactWriter.FieldsJson(fact)}', 'r', 's', '2026-11-20T01:00:00Z', 'f')")) + ";");

        var bars = new List<string>();

        foreach (var session in new[] { "2026-11-19", "2026-11-20" })
        {
            bars.AddRange(PickerPageMembers.Select(member => $"('{member.Ticker}', '{session}', '100', '100', '100', '100', 1000, 'eod', '{session}T22:00:00Z')"));
        }

        store.Execute("INSERT INTO bar (ticker, session_date, open, high, low, close, volume, source, observed_at) VALUES " + string.Join(", ", bars) + ";");
        store.Execute("INSERT INTO indicator (ticker, session_date, name, value, bar_count) VALUES "
            + string.Join(", ", PickerPageMembers.Select(member => $"('{member.Ticker}', '2026-11-19', 'atr14', 2.0, 14)")) + ";");

        return store;
    }

    static string PickerCard(string page, string ticker) =>
        Assert.Single(Blocks(Blocks(page, "<section class=\"card\" data-card=\"judge-picks\">.*?</section>").Single(), $"<article class=\"home-idea judge-card\" data-ticker=\"{ticker}\">.*?</article>"));

    // The tenth a card's bar fills: the count of cells before the filled one, plus one.
    static int FilledCell(string bar) =>
        Regex.Match(bar, "<div class=\"tenth\"[^>]*>(.*?)</div>").Groups[1].Value.Split("<i").Skip(1).ToList().FindIndex(cell => cell.StartsWith(" class=\"on\"", StringComparison.Ordinal)) + 1;

    [Fact]
    public async Task EachNamedFactsBarFillsTheTenthItStoodInAmongItsOwnIndexsStocks()
    {
        using var store = PickerPageStore();
        using var host = new Host(store.Root);
        using var client = host.CreateClient();

        var large = WebUtility.HtmlDecode(await client.GetStringAsync("/screens/judge?universe=500"));
        var small = WebUtility.HtmlDecode(await client.GetStringAsync("/screens/judge?universe=600"));

        string Bar(string page, string ticker) =>
            Regex.Match(PickerCard(page, ticker), "<tr class=\"why\" data-fact=\"return_month\">.*?</tr>").Value;

        // By hand, the share of the index's figures below a figure, a tie counting half, read up to the next tenth. The S&P
        // 500's four: AAA's 20% stands above three of them, 3.5 of 4, the 9th tenth; BBB's 8% above one, 1.5 of 4, the 4th.
        // The S&P 600's five: ZZZ's 5% above four, 4.5 of 5, the 9th; WWW's 3% above two, 2.5 of 5, the 5th. Read among the
        // S&P 500's members as well, ZZZ's 5% would stand above the same four of nine, 4.5 of 9, the 5th.
        Assert.Equal(9, FilledCell(Bar(large, "AAA")));
        Assert.Equal(4, FilledCell(Bar(large, "BBB")));
        Assert.Equal(9, FilledCell(Bar(small, "ZZZ")));
        Assert.Equal(5, FilledCell(Bar(small, "WWW")));

        Assert.Contains("<td class=\"v\">+20%</td><td class=\"b\"><div class=\"tenth\" data-tenth=\"9\" role=\"img\" aria-label=\"Tenth 9 of 10 among the S&P 500 stocks, lowest on the left\"><i></i><i></i><i></i><i></i><i></i><i></i><i></i><i></i><i class=\"on\"></i><i></i></div></td><td class=\"c\">Up over the month.</td>", Bar(large, "AAA"), StringComparison.Ordinal);
        Assert.Contains("The facts it named, and where each stands among the S&P 600 stocks", PickerCard(small, "ZZZ"), StringComparison.Ordinal);
        Assert.Contains(SinglePageApp.TenthCaption(Universes.Small), PickerCard(small, "ZZZ"), StringComparison.Ordinal);

        // A fact it named but was shown none of draws its words, not on file, and no bar; so does a fact that is a word.
        Assert.Contains("<tr class=\"why\" data-fact=\"rsi\"><td class=\"l\">Momentum (RSI)</td><td class=\"v\">not on file</td><td class=\"b\"></td>", PickerCard(large, "AAA"), StringComparison.Ordinal);
        Assert.Contains("<tr class=\"\" data-fact=\"sector\"><td class=\"l\">Sector</td><td class=\"v\">Industrials</td><td class=\"b\"></td>", PickerCard(large, "AAA"), StringComparison.Ordinal);

        // Everything else it saw, folded under its count and grouped by kind in the order the groups stand, each fact in one
        // group, every field of the sheet in one of them.
        var rest = JudgeFacts.In(JudgeRound.Sheet).Count() - 3;
        var folded = Regex.Match(PickerCard(large, "AAA"), "<details class=\"all-facts\">.*?</details>").Value;
        string[] groups = [.. Regex.Matches(folded, "<p class=\"grp\">(.*?)</p>").Select(match => match.Groups[1].Value)];

        Assert.Contains($"<summary>Everything else it saw ({rest} facts), grouped</summary>", folded, StringComparison.Ordinal);
        Assert.Equal([.. SinglePageApp.FactGroups.Select(group => group.Name)], groups);
        Assert.Equal(rest, Regex.Matches(folded, "<tr class=\"\" data-fact=").Count);
        Assert.Equal(
            [.. JudgeFacts.All.Select(field => field.Key).Order(StringComparer.Ordinal)],
            [.. SinglePageApp.FactGroups.SelectMany(group => group.Keys).Order(StringComparer.Ordinal)]);
    }

    [Fact]
    public async Task ThePickersPageDrawsItsThreeIndexTilesTonightsCarouselAndEachIndexsOwnRules()
    {
        using var store = PickerPageStore();
        using var host = new Host(store.Root);
        using var client = host.CreateClient();

        var pages = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var universe in Universes.Offered)
        {
            pages[universe.Code] = WebUtility.HtmlDecode(await client.GetStringAsync($"/screens/judge?universe={universe.Word}"));
        }

        // The tiles, in the selector's order on every index's page, the index read marked as showing. By hand: the S&P 500
        // and the S&P 600 each bought their first pick and stay open with none closed, their second past the cash and its
        // result not yet in; the S&P 400 marked none, so its account never started.
        foreach (var (code, page) in pages)
        {
            var tiles = Blocks(page, "<section class=\"card\" data-card=\"judge-tiles\">.*?</section>").Single();
            string[] order = [.. Regex.Matches(tiles, "data-index=\"([A-Z]+)\"").Select(match => match.Groups[1].Value)];

            Assert.Equal(["GSPC", "MID", "SML"], order);
            Assert.Contains($"<a href=\"#/picker?universe={Universes.ByCode(code)!.Word}\" data-index=\"{code}\" class=\"on\" aria-current=\"page\">", tiles, StringComparison.Ordinal);
            Assert.Single(Regex.Matches(tiles, "class=\"on\""));
            Assert.Contains("<span>S&P 500, large caps" + (code == "GSPC" ? " (showing)" : string.Empty) + "</span>", tiles, StringComparison.Ordinal);
            Assert.Contains("<b>0.0%</b><span>1 open · 0 closed · picked 2 tonight</span>", tiles, StringComparison.Ordinal);
            Assert.Contains("<b>Not started</b><span>0 open · 0 closed · picked 0 tonight</span>", tiles, StringComparison.Ordinal);
        }

        // Tonight's carousel on the S&P 500: a chip for each pick in rank order, the second marked past the cash; the place
        // of each with arrows to the one before and after, round from the last to the first; each card under its two tabs.
        var tonight = Blocks(pages["GSPC"], "<section class=\"card\" data-card=\"judge-picks\">.*?</section>").Single();

        Assert.Contains("<h2>The AI stock picker's large-cap picks tonight (2)</h2>", tonight, StringComparison.Ordinal);
        Assert.Contains("<label class=\"car-chip\" for=\"judge-slide-1\">AAA<span>1</span></label><label class=\"car-chip skip\" for=\"judge-slide-2\">BBB<span>2, not bought: no cash</span></label>", tonight, StringComparison.Ordinal);
        Assert.Contains("<div class=\"car-nav\"><span>1 of 2</span><label for=\"judge-slide-2\" aria-label=\"Previous pick\">‹</label><label for=\"judge-slide-2\" aria-label=\"Next pick\">›</label></div>", tonight, StringComparison.Ordinal);
        Assert.Contains("<input type=\"radio\" class=\"judge-slide\" name=\"judge-slide\" id=\"judge-slide-1\" checked><input type=\"radio\" class=\"judge-slide\" name=\"judge-slide\" id=\"judge-slide-2\">", tonight, StringComparison.Ordinal);
        Assert.Equal(2, Regex.Matches(Regex.Match(tonight, "<div class=\"judge-slides\">.*").Value, "<article class=\"home-idea judge-card\"").Count);
        Assert.Contains(SinglePageApp.TonightNote, tonight, StringComparison.Ordinal);

        foreach (var ticker in new[] { "AAA", "BBB" })
        {
            var card = PickerCard(pages["GSPC"], ticker);

            Assert.Contains($"<nav class=\"judge-tabs\" aria-label=\"Choose what the card shows\"><label for=\"judge-tonight-{(ticker == "AAA" ? 1 : 2)}-why\" data-tab=\"why\">{SinglePageApp.WhyTab}</label>", card, StringComparison.Ordinal);
            Assert.True(card.IndexOf("<div class=\"judge-panel\" data-tab=\"why\">", StringComparison.Ordinal) < card.IndexOf("<div class=\"judge-panel\" data-tab=\"plan\">", StringComparison.Ordinal));
            Assert.Contains("<div class=\"lad-row\" data-sale=\"buy\"><i class=\"dot lc\"></i><span>Buys at</span><b>the next open</b>", card, StringComparison.Ordinal);
        }

        // An index whose night marked none draws no carousel and says so.
        Assert.DoesNotContain("data-carousel", pages["MID"], StringComparison.Ordinal);
        Assert.Contains("It rated none of the S&P 400's stocks worth buying tonight.", pages["MID"], StringComparison.Ordinal);

        // Each index's own rules: none yet on any.
        foreach (var universe in Universes.Offered)
        {
            var rules = Blocks(pages[universe.Code], "<section class=\"card\" data-card=\"judge-rules\">.*?</section>").Single();

            Assert.Contains($"<div class=\"lbl\">Its own rules, {universe.Name}</div>", rules, StringComparison.Ordinal);
            Assert.Contains($"<p class=\"empty\" data-rules=\"none\" data-summary>{SinglePageApp.NoOwnRule} ", rules, StringComparison.Ordinal);
            Assert.Contains(SinglePageApp.OwnRulesLede(universe), rules, StringComparison.Ordinal);
        }
    }
}
