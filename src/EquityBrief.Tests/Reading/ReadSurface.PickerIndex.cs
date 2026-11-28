using System.Net;
using EquityBrief.Core.Cards;
using EquityBrief.Core.Judge;
using EquityBrief.Tests.Harness;
using EquityBrief.Web.App;
using Xunit;

namespace EquityBrief.Tests.Reading;

// 24.24's AI stock picker page within one index, read back off the page over a constructed store and worked by hand: the
// index chosen under Universe and its own version's record, random picks and the same plan read over that index's members
// alone; Tonight's line of the night's marks; the record's variant "a fixed 5" in an account of its own; a pick its account
// had no cash for shown not bought with its result still recorded; and a version from before 24.24, which picked across
// the three indices, drawn apart on each.
// see: The AI stock picker picks within each index, the best quarter of the stocks it marks and at most 5
public partial class ReadSurface
{
    internal static string[] PickerIndexRows =>
    [
        CheckReach.Key("15.26 The AI stock picker", "Within one index"),
        CheckReach.Key("15.26 The AI stock picker", "The night's marks"),
        CheckReach.Key("15.26 The AI stock picker", "A fixed 5"),
        CheckReach.Key("15.26 The AI stock picker", "A pick past the cash"),
    ];

    // A version from before 24.24 picking OLD on the night of 18 Nov across the three indices; and on the night of 19 Nov this
    // version picking within each index: on the S&P 500, 5 marked, AAA and BBB its best quarter and CCC "a fixed 5"'s alone,
    // each a week with the whole at the target from a close of 100 and a typical move of 2; on the S&P 600, 1 marked, ZZZ.
    // AAA opens at 101 on 20 Nov and sells at 107 on the 24th, 3 risks; every other stands at 100, so a week's time sells it
    // for nothing. The account is $10,000 risking all of it a trade, a position at most 6 in 10 of it: AAA takes 59 shares at
    // $101, $5,959, leaving $4,041, under BBB's 60 shares at $100.
    static TemporaryStore PickerIndexStore()
    {
        var store = new TemporaryStore().Migrated();

        AccountFile.Write(store.Root, new AccountSettings(10_000m, 100m, 0.6m));

        store.Execute(
            "INSERT INTO judge_pin (id, reading_pin_id, model, file, sha256, size, modified, context, prompt_version, written_at) VALUES "
            + $"(1, 1, 'm', 'f', 's', 1, '2026-11-01T00:00:00Z', 8192, '{JudgePrompt.Earlier[^1]}', '2026-11-19T01:00:00Z'), "
            + $"(2, 1, 'm', 'f', 's', 1, '2026-11-01T00:00:00Z', 8192, '{JudgePrompt.Version}', '2026-11-20T01:00:00Z');");
        store.Execute(
            "INSERT INTO judge_run (run_id, session_date, fact_version, pin_id, seed, outcome, started_at, ended_at, screened, kept, asks, asks_failed, detail, index_code, standing) VALUES "
            + "('pick-18', '2026-11-18', 'v', 1, 20261118, 'picked', '2026-11-19T01:00:00Z', '2026-11-19T01:10:00Z', 9, 1, 2, 0, '', NULL, NULL), "
            + "('pick-19-500', '2026-11-19', 'w', 2, 20261119, 'picked', '2026-11-20T01:00:00Z', '2026-11-20T01:10:00Z', 5, 5, 2, 0, '', 'GSPC', 'running'), "
            + "('pick-19-600', '2026-11-19', 'w', 2, 20261119, 'picked', '2026-11-20T01:20:00Z', '2026-11-20T01:30:00Z', 2, 1, 2, 0, '', 'SML', 'running');");
        store.Execute(
            "INSERT INTO judge_pick (session_date, rank, run_id, index_code, ticker, hold, take_profit, reason, last_close, typical_move, stop, target, give_up_distance, fields, in_quarter) VALUES "
            + $"('2026-11-18', 1, 'pick-18', 'MID', 'OLD', '1 week', '{JudgeMenu.Whole}', 'Picked across the three.', '100', 2.0, '98', '104', NULL, NULL, 1), "
            + $"('2026-11-19', 1, 'pick-19-500', 'GSPC', 'AAA', '1 week', '{JudgeMenu.Whole}', 'Rising estimates.', '100', 2.0, '98', '104', NULL, 'rsi,depth,strength', 1), "
            + $"('2026-11-19', 2, 'pick-19-500', 'GSPC', 'BBB', '1 week', '{JudgeMenu.Whole}', 'A quiet climb.', '100', 2.0, '98', '104', NULL, 'rsi,depth,strength', 1), "
            + $"('2026-11-19', 3, 'pick-19-500', 'GSPC', 'CCC', '1 week', '{JudgeMenu.Whole}', 'The fixed five alone.', '100', 2.0, '98', '104', NULL, 'rsi,depth,strength', 0), "
            + $"('2026-11-19', 1, 'pick-19-600', 'SML', 'ZZZ', '1 week', '{JudgeMenu.Whole}', 'A small cap.', '100', 2.0, '98', '104', NULL, 'rsi,depth,strength', 1);");

        var facts = new List<string> { "('MID', '2026-11-18', 'OLD', 'v', '{}', 'r', 's', '2026-11-19T01:00:00Z', 'f')" };

        foreach (var (index, ticker) in new[] { ("GSPC", "AAA"), ("GSPC", "BBB"), ("GSPC", "CCC"), ("GSPC", "DDD"), ("SML", "ZZZ"), ("SML", "YYY") })
        {
            facts.Add($"('{index}', '2026-11-19', '{ticker}', 'w', '{{\"rsi\":\"50 of 100\",\"depth\":\"2%\",\"strength\":\"+1%\",\"tenths\":{{\"rsi\":5}}}}', 'r', 's', '2026-11-20T01:00:00Z', 'f')");
        }

        store.Execute("INSERT INTO judge_fact (index_code, session_date, ticker, version, fields, row_text, sheet_text, written_at, run_id) VALUES " + string.Join(", ", facts) + ";");

        string[] sessions = ["2026-11-18", .. JudgeSessions];
        string[] aaaOpens = ["100", "100", "101", "104", "107", "107", "107", "107"];
        string[] aaaCloses = ["100", "100", "103", "106", "107", "107", "107", "107"];
        var bars = new List<string>();

        for (var at = 0; at < sessions.Length; at++)
        {
            bars.Add($"('AAA', '{sessions[at]}', '{aaaOpens[at]}', '{aaaCloses[at]}', '{aaaOpens[at]}', '{aaaCloses[at]}', 1000, 'eod', '{sessions[at]}T22:00:00Z')");

            foreach (var flat in new[] { "BBB", "CCC", "DDD", "ZZZ", "YYY", "OLD" })
            {
                bars.Add($"('{flat}', '{sessions[at]}', '100', '100', '100', '100', 1000, 'eod', '{sessions[at]}T22:00:00Z')");
            }
        }

        store.Execute("INSERT INTO bar (ticker, session_date, open, high, low, close, volume, source, observed_at) VALUES " + string.Join(", ", bars) + ";");
        store.Execute("INSERT INTO indicator (ticker, session_date, name, value, bar_count) VALUES "
            + string.Join(", ", new[] { "AAA", "BBB", "CCC", "DDD", "ZZZ", "YYY" }.Select(ticker => $"('{ticker}', '2026-11-19', 'atr14', 2.0, 14)").Append("('OLD', '2026-11-18', 'atr14', 2.0, 14)")) + ";");

        return store;
    }

    [Fact]
    public async Task ThePickersPageReadsOneIndexItsMarksAFixedFiveAndAPickPastTheCash()
    {
        using var store = PickerIndexStore();
        using var host = new Host(store.Root);
        using var client = host.CreateClient();

        var large = WebUtility.HtmlDecode(await client.GetStringAsync("/screens/judge?universe=500"));
        var small = WebUtility.HtmlDecode(await client.GetStringAsync("/screens/judge?universe=600"));

        // Each index's page names it and draws its own night's picks alone.
        Assert.Contains("<section class=\"judge\" data-index=\"GSPC\">", large, StringComparison.Ordinal);
        Assert.Contains("AI stock picker: S&P 500 large cap", large, StringComparison.Ordinal);
        Assert.Contains("<section class=\"judge\" data-index=\"SML\">", small, StringComparison.Ordinal);
        Assert.Contains("AI stock picker: S&P 600 small cap", small, StringComparison.Ordinal);

        string[] Tonight(string page) =>
            [.. Blocks(Blocks(page, "<section class=\"card\" data-card=\"judge-picks\">.*?</section>").Single(), "<article class=\"home-idea judge-card\" data-ticker=\"[A-Z]+\">").Select(card => card[^5..^2])];

        Assert.Equal(["AAA", "BBB"], Tonight(large));
        Assert.Equal(["ZZZ"], Tonight(small));

        // Tonight's line of the night's marks, by hand: 5 marked on the S&P 500, a quarter rounded up, 2; 1 on the S&P 600.
        Assert.Contains("It rated 5 of the S&P 500's stocks worth buying tonight and picked the best quarter: 2.", large, StringComparison.Ordinal);
        Assert.Contains("It rated 1 of the S&P 600's stocks worth buying tonight and picked the best quarter: 1.", small, StringComparison.Ordinal);

        // BBB past the cash: AAA's 59 shares at $101 leave $4,041, under BBB's 60 at $100; its card says so, and its result
        // still counts in the record: AAA's 3 risks and BBB's nothing, two closed, one a winner, $1.50 per $1 risked.
        var picks = Blocks(large, "<section class=\"card\" data-card=\"judge-picks\">.*?</section>").Single();
        var bbb = Assert.Single(Blocks(picks, "<article class=\"home-idea judge-card\" data-ticker=\"BBB\">.*?</article>"));

        Assert.Contains($"<p class=\"judge-since\" data-cash>{SinglePageApp.NoCashLine}</p>", bbb, StringComparison.Ordinal);
        Assert.DoesNotContain("data-cash", Assert.Single(Blocks(picks, "<article class=\"home-idea judge-card\" data-ticker=\"AAA\">.*?</article>")), StringComparison.Ordinal);
        Assert.Contains("<b>2</b> trades closed<span>2 of 300 needed for a firm verdict</span>", large, StringComparison.Ordinal);
        Assert.Contains("<b>$1.50</b> made per $1 risked", large, StringComparison.Ordinal);

        // The same plan on every member read over the S&P 500's four alone, by hand: AAA 3 and the rest nothing, a mean of
        // 0.75, so AAA 2.25 above and BBB 0.75 under, $0.75 more on the two; the S&P 600's flat members would make it 0.90.
        Assert.Contains("<b>$0.75</b> more per $1 than the same plans bought on every stock those nights", large, StringComparison.Ordinal);

        // "A fixed 5", the version's variant: every pick it ranked, CCC among them, in an account of its own.
        var five = Blocks(large, "<section class=\"card\" data-card=\"judge-fixed-five\">.*?</section>").Single();

        Assert.Contains(SinglePageApp.FixedFiveLine, five, StringComparison.Ordinal);

        // From the operator's order of 2026-11-24, item 11: it shared AAA and BBB with its version, drawn as chips; the quarter of
        // 5 marked, 2 rounded up, decided the version's count, worked by hand. From 24.49 CCC, which differs, is a marked chip and
        // its card is drawn on the trades tab's past nights under the fixed 5's account, the picks tab drawing none.
        Assert.Contains("Tonight it shared 2 of its picks with the version above; 1 differs, marked among the chips below.", five, StringComparison.Ordinal);
        Assert.Contains("A quarter of 5 is 2, so the quarter decided and it picked 2.", five, StringComparison.Ordinal);
        Assert.Equal(["AAA", "BBB"], [.. System.Text.RegularExpressions.Regex.Matches(five, "<span class=\"judge-chip\" data-ticker=\"([A-Z]+)\">").Select(match => match.Groups[1].Value)]);
        Assert.Contains("<span class=\"judge-chip differ\" data-ticker=\"CCC\">CCC</span>", five, StringComparison.Ordinal);
        Assert.Empty(Blocks(five, "<article class=\"home-idea judge-card\" data-ticker=\"[A-Z]+\">"));

        var fiveTrades = WebUtility.HtmlDecode(await client.GetStringAsync("/screens/judge-trades?universe=500&account=fixed-five"));

        Assert.Contains("data-account=\"fixed-five\"", fiveTrades, StringComparison.Ordinal);
        string[] fiveCards = [.. Blocks(fiveTrades, "<article class=\"home-idea judge-card\" data-ticker=\"[A-Z]+\">").Select(card => card[^5..^2])];

        Assert.Contains("CCC", fiveCards);
        // From the 24.25 correction an index's versions are numbered among its own, the earlier version being no index's.
        Assert.Contains("A fixed 5, version 1's variant, picked 19 Nov: its own account, apart from the record", five, StringComparison.Ordinal);
        Assert.DoesNotContain("The fixed five alone.", Blocks(large, "<section class=\"card\" data-card=\"judge-picks\">.*?</section>").Single(), StringComparison.Ordinal);

        // The version from before 24.24, which picked across the three indices, never drawn as an index's version; OLD's week
        // has run out on its flat prices, so from the operator's correction of 2026-11-23 the night of 19 Nov draws no region
        // and no line of it, and its record stays in the scoreboard as an earlier version's.
        foreach (var page in new[] { large, small })
        {
            Assert.Empty(Blocks(page, "<section class=\"card\" data-card=\"judge-across\">.*?</section>"));
            Assert.DoesNotContain("judge-across-open", page, StringComparison.Ordinal);
            Assert.Contains("an earlier version kept apart from this record: 1 of its 1 trade closed, 0 of them winners", Blocks(page, "<section class=\"card\" data-card=\"judge-score\">.*?</section>").Single(), StringComparison.Ordinal);
            Assert.Empty(Blocks(page, "<section class=\"card\" data-card=\"judge-version-[0-9]+\">"));
        }

        Assert.DoesNotContain("data-ticker=\"ZZZ\"", large, StringComparison.Ordinal);
        Assert.DoesNotContain("data-ticker=\"AAA\"", small, StringComparison.Ordinal);
    }
}
