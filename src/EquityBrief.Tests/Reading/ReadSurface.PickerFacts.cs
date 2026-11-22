using System.Net;
using System.Text.RegularExpressions;
using EquityBrief.Core.Judge;
using EquityBrief.Tests.Harness;
using EquityBrief.Web.App;
using Xunit;

namespace EquityBrief.Tests.Reading;

// The picker's facts drawn in the page's own words from the text the picker was shown: a unit of one in the singular; a
// change that rounds to nothing as no change and any other zero with no sign; the share count's change as fewer or more
// shares under a label that says what it measures; the stored text and the model's sheet left as they were.
public partial class ReadSurface
{
    [Fact]
    public void AFactsValueReadsAUnitOfOneInTheSingularAZeroChangeAsNoChangeAndTheShareCountAsFewerOrMore()
    {
        (string Key, string Shown, string Drawn)[] worked =
        [
            ("growth_change", "+1 points", "+1 point"),
            ("gross_margin_change", "-1 points", "-1 point"),
            ("operating_margin_change", "+1.5 points", "+1.5 points"),
            ("insider_buyers", "1 insiders", "1 insider"),
            ("insider_buyers", "11 insiders", "11 insiders"),
            ("since_high", "1 trading days", "1 trading day"),
            ("surprise", "+9%, 1 trading days ago", "+9%, 1 trading day ago"),
            ("depth", "1.0 typical daily moves", "1.0 typical daily move"),
            ("return_month", "-0%", JudgeFactView.NoChange),
            ("return_quarter", "+0%", JudgeFactView.NoChange),
            ("growth_change", "-0 points", JudgeFactView.NoChange),
            ("close_over_fifty", "-0%", "0%"),
            ("return_month", "+10%", "+10%"),
            ("share_fall", "+15%", "15% fewer"),
            ("share_fall", "-4%", "4% more"),
            ("share_fall", "0%", JudgeFactView.NoChange),
            ("share_fall", JudgeFacts.NotOnFile, JudgeFacts.NotOnFile),
        ];

        foreach (var (key, shown, drawn) in worked)
        {
            Assert.Equal(drawn, JudgeFactView.ValueOf(key, shown));
        }

        // No change draws no comparison, where a sign once read "down".
        Assert.Null(JudgeFactView.Compared("return_month", JudgeFactView.ValueOf("return_month", "-0%")));
        Assert.Equal("Down over the month.", JudgeFactView.Compared("return_month", JudgeFactView.ValueOf("return_month", "-3%")));

        // The share count's label says what the reading measures; every other keeps its own.
        Assert.Equal("Share count against a year before", JudgeFactView.LabelOf("share_fall", "Shares falling"));
        Assert.Equal("Momentum (RSI)", JudgeFactView.LabelOf("rsi", "Momentum (RSI)"));
        Assert.Equal("Everything it saw (46 facts): made before the picker named its facts", SinglePageApp.BeforeNamedWords(46));
    }

    [Fact]
    public async Task APicksCardDrawsItsFactsInThePagesWordsAndLeavesTheStoredTextAsThePickerWasShownIt()
    {
        using var store = new TemporaryStore().Migrated();
        const string Fields = "{\"sector\":\"Energy\",\"size\":\"large\",\"share_fall\":\"+15%\",\"return_month\":\"-0%\",\"growth_change\":\"+1 points\",\"insider_buyers\":\"1 insiders\"}";

        store.Execute(
            "INSERT INTO judge_pin (id, reading_pin_id, model, file, sha256, size, modified, context, prompt_version, written_at) VALUES "
            + $"(1, 1, 'm', 'f', 's', 1, '2026-11-01T00:00:00Z', 8192, '{JudgePrompt.Version}', '2026-11-20T01:00:00Z');");
        store.Execute(
            "INSERT INTO judge_run (run_id, session_date, fact_version, pin_id, seed, outcome, started_at, ended_at, screened, kept, asks, asks_failed, detail) VALUES "
            + "('judge-pick-1', '2026-11-20', 'v', 1, 20261120, 'picked', '2026-11-21T01:00:00Z', '2026-11-21T01:10:00Z', 4, 1, 2, 0, '');");
        store.Execute(
            "INSERT INTO judge_pick (session_date, rank, run_id, index_code, ticker, hold, take_profit, reason, last_close, typical_move, stop, target, give_up_distance, fields) VALUES "
            + $"('2026-11-20', 1, 'judge-pick-1', 'GSPC', 'BBB', '1 week', '{JudgeMenu.Whole}', 'Buying back its shares.', '100', 2.0, '98', '104', NULL, 'share_fall,return_month,growth_change');");
        store.Execute($"INSERT INTO judge_fact (index_code, session_date, ticker, version, fields, row_text, sheet_text, written_at, run_id) VALUES ('GSPC', '2026-11-20', 'BBB', 'v', '{Fields}', 'r', 's', '2026-11-21T01:00:00Z', 'f');");
        store.Execute("INSERT INTO bar (ticker, session_date, open, high, low, close, volume, source, observed_at) VALUES ('BBB', '2026-11-20', '100', '100', '100', '100', 1000, 'eod', '2026-11-20T22:00:00Z');");

        using var host = new Host(store.Root);
        using var client = host.CreateClient();
        var card = Assert.Single(Blocks(WebUtility.HtmlDecode(await client.GetStringAsync("/screens/judge")), "<article class=\"home-idea judge-card\" data-ticker=\"BBB\">.*?</article>"));

        Assert.Contains("<tr class=\"why\" data-fact=\"share_fall\"><td class=\"l\">Share count against a year before</td><td class=\"v\">15% fewer</td>", card, StringComparison.Ordinal);
        Assert.Contains("<tr class=\"why\" data-fact=\"return_month\"><td class=\"l\">Return over 1 month</td><td class=\"v\">no change</td><td class=\"b\"></td><td class=\"c\"></td></tr>", card, StringComparison.Ordinal);
        Assert.Contains("<td class=\"v\">+1 point</td>", card, StringComparison.Ordinal);
        Assert.Contains("<tr class=\"\" data-fact=\"insider_buyers\"><td class=\"l\">Insiders who bought, 90 days</td><td class=\"v\">1 insider</td>", card, StringComparison.Ordinal);
        Assert.DoesNotMatch(new Regex(@"[+-]0%|\b1 points\b|\b1 insiders\b|Shares falling"), card);

        // The stored text stays as the picker was shown it.
        Assert.Equal(Fields, Query(store, "SELECT fields FROM judge_fact;").Single());
    }
}
