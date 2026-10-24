using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using EquityBrief.Core.Cards;

namespace EquityBrief.Tests.Checks;

// fixture-expectations, 19.3: every setup card built one way, read off the page the fixture's whole night draws. Each card
// lists nothing on the night; the one family whose rule's night the index stores says in one line which gate stopped the
// most of the members reaching it, worked by the suite from the stored funnel by its own arithmetic, and that no usual
// stretch is read under the floors; the others say Nothing tonight with their reason beneath and draw no tabs or funnel.
// see: A setup's card draws its live picks as rows of its own, each ticker opening its chart page
public partial class FixtureExpectations
{
    [Fact]
    public async Task TheFixturesSetupCardsEachSayInOneLineWhereMostStoppedOverTheNightsOwnFunnel()
    {
        var expected = Expected("setup-cards");

        Assert.Equal("derived", expected.GetProperty("derivation").GetString());
        Assert.Equal(["gate_result"], expected.GetProperty("tables").EnumerateArray().Select(table => table.GetString()!));

        var night = expected.GetProperty("night").GetString()!;
        string[] cards = [.. expected.GetProperty("cards").EnumerateArray().Select(card => card.GetString()!)];
        string[] ruled = [.. expected.GetProperty("ruled").EnumerateArray().Select(card => card.GetString()!)];

        var (store, code, _, _) = await FixtureReplay.NightAsync();
        using var held = store;

        Assert.Equal(0, code);

        // The derivation's ground: every S&P 500 member's stored gate result on the night fails its trigger, which reads
        // its arrival off gate rows stored for the sessions before the night and finds none.
        var triggers = Query(store, $"SELECT ticker || '|' || gates FROM gate_result WHERE session_date = '{night}' ORDER BY ticker;")
            .Select(row => row.Split('|', 2))
            .Select(row => (Ticker: row[0], Passed: JsonDocument.Parse(row[1]).RootElement.GetProperty("gates").EnumerateArray().Single(gate => gate.GetProperty("gate").GetString() == "trigger").GetProperty("passed").GetBoolean()))
            .ToArray();

        Assert.Equal(4, triggers.Length);
        Assert.All(triggers, member => Assert.False(member.Passed, $"{member.Ticker} passed its trigger on the fixture's first filter night."));

        // The S&P 500's rule nights the whole night stored: the families whose rule stands on the index, each with its
        // funnel and its completed stretches.
        var rows = Query(store, $"SELECT family || '|' || gates || '|' || completed FROM rule_night WHERE index_code = 'GSPC' AND session_date = '{night}' ORDER BY family;")
            .Select(row => row.Split('|'))
            .ToArray();

        Assert.Equal(ruled, rows.Select(row => row[0]));

        // Each funnel carries the four members the night read for its rule, each of which reached its first gate.
        Assert.All(rows, row => Assert.Equal(4, RuleRows.ReachedOf(row[1])));

        // From 22.6 the card's sentence by the suite's own arithmetic: no picks, then the step where the largest fall from
        // one gate's count to the next fell, the earlier gate taking a tie, in the words section 15.23 declares for a stock
        // missing it, with the fall and the count reaching it, and where some went past it the furthest step any passed.
        static string Sentence(string family, string gates)
        {
            var funnel = JsonDocument.Parse(gates).RootElement.EnumerateArray()
                .Select(gate => (Gate: gate.GetProperty("gate").GetString()!, Passed: gate.GetProperty("passed").GetInt32()))
                .ToArray();
            var said = "No picks tonight.";
            var (dropAt, stopped) = (-1, 0);

            for (var at = 1; at < funnel.Length; at++)
            {
                if (funnel[at - 1].Passed - funnel[at].Passed is var fell && fell > stopped)
                {
                    (dropAt, stopped) = (at, fell);
                }
            }

            if (dropAt < 0)
            {
                return said;
            }

            said += FormattableString.Invariant($" The step most stocks missed: {Web.Marks.ReaderWords.Missed(family, funnel[dropAt].Gate)}, {stopped} of the {funnel[dropAt - 1].Passed} that got that far.");

            var lastAt = Array.FindLastIndex(funnel, gate => gate.Passed > 0 && gate.Gate != "market");

            return lastAt >= dropAt
                ? said + FormattableString.Invariant($" The furthest any got: {Web.Marks.ReaderWords.Passed(family, funnel[lastAt].Gate)}, {funnel[lastAt].Passed} of them.")
                : said;
        }

        // The completed stretches against the thirty a usual stretch waits for, none past them being read on a store of
        // one night, which each ruled card's quiet-nights line says.
        Assert.All(rows, row => Assert.Equal("0", row[2]));
        Assert.Equal(30, RuleStretch.CompletedStretchesFloor);

        // Each card read back off the page in the one family order, the ruled family's sentence the one worked above with
        // its funnel marking that gate, and every other family's no picks with the reason its answers give, one choice in
        // its Showing list and no funnel.
        using var host = new Reading.ReadSurface.Host(store.Root);
        using var client = host.CreateClient();

        var page = WebUtility.HtmlDecode(await client.GetStringAsync($"/screens/tonight/{night}"));

        Assert.Contains($"<p class=\"market-line\" data-index=\"{expected.GetProperty("indexDrawn").GetString()}\" data-open=\"true\"", page, StringComparison.Ordinal);
        Assert.Equal(cards, Regex.Matches(page, "<section class=\"family-card[^\"]*\" data-family=\"([^\"]+)\"").Select(match => match.Groups[1].Value));

        foreach (var family in cards.Where(family => family != Core.Families.HeavyweightRule.Name))
        {
            var card = Regex.Match(page, $"<section class=\"family-card\" data-family=\"{family}\".*?</section>", RegexOptions.Singleline).Value;

            Assert.Contains("data-picks=\"0\"", card, StringComparison.Ordinal);
            Assert.DoesNotContain("card-picks", card, StringComparison.Ordinal);

            if (ruled.Contains(family))
            {
                var row = rows.Single(stored => stored[0] == family);
                var stated = family == Core.Families.DipRule.Name ? expected.GetProperty("dipLine").GetString()! : expected.GetProperty("ruledLine").GetString()!;

                // The sentence the file states by hand is the suite's own arithmetic over the stored funnel, and the card's.
                Assert.StartsWith(Sentence(family, row[1]), stated, StringComparison.Ordinal);
                Assert.Contains($"<p class=\"card-summary\" data-summary><span class=\"family-empty-line\" data-picks=\"0\" data-flagged=\"false\">{stated}</span></p>", card, StringComparison.Ordinal);
                Assert.Contains("it is too new to know a normal quiet run, 0 of the 30 runs it needs having ended", card, StringComparison.Ordinal);
                var (gates, largestDrop) = family == Core.Families.DipRule.Name
                    ? (expected.GetProperty("dipFunnel").GetProperty("gates").GetInt32(), expected.GetProperty("dipFunnel").GetProperty("largestDrop").GetString())
                    : (9, "setup");

                Assert.Contains($"<div class=\"funnel-bar\" data-gates=\"{gates}\" data-largest-drop=\"{largestDrop}\" data-market-row=\"false\">", card, StringComparison.Ordinal);
            }
            else
            {
                // One choice, the setup as it runs, as its pill reads with no freeze registered, and no funnel.
                Assert.Contains($"<p class=\"card-summary\" data-summary><span class=\"family-empty-line\" data-picks=\"0\" data-flagged=\"false\">{expected.GetProperty("unruledLine").GetString()} ", card, StringComparison.Ordinal);
                Assert.DoesNotContain("funnel-bar", card, StringComparison.Ordinal);
            }
        }

        // Each card's header and Showing list: a ruled family's choices with the setup as it runs first, and an unruled
        // family's that one alone, each pill reading not yet tested with no freeze registered.
        foreach (var family in cards.Where(family => family != Core.Families.HeavyweightRule.Name))
        {
            var head = Regex.Match(page, $"<section class=\"card setup-card\" id=\"setup-{family}\" data-card=\"family\".*?</header>.*?</div></div>", RegexOptions.Singleline).Value;

            Assert.Contains("<span class=\"status-pill provisional\"", head, StringComparison.Ordinal);
            Assert.Contains($"<select id=\"rule-{family}\" class=\"rule-select\" data-rule-choice=\"{family}\"", head, StringComparison.Ordinal);
            Assert.Equal("The setup as it runs, not yet tested", Regex.Matches(head, "<option [^>]*>([^<]*)</option>").Select(match => match.Groups[1].Value).First());
        }

        // From the 19.3 correction of 2026-10-24 the stocks one gate short each card draws, as the file works them: NFLX under
        // the dip at its trend alone, the dip's card drawing no number of its own, so no card's number column holds a row.
        var close = Regex.Matches(page, "<section class=\"family-card[^\"]*\" data-family=\"([^\"]+)\".*?</section>", RegexOptions.Singleline)
            .SelectMany(card => Regex.Matches(card.Value, "<tr data-ticker=\"([^\"]+)\" data-gate=\"([^\"]+)\">(.*?)</tr>", RegexOptions.Singleline)
                .Select(row => (Row: $"{card.Groups[1].Value}|{row.Groups[1].Value}|{row.Groups[2].Value}", Family: card.Groups[1].Value, Numbers: Regex.Matches(row.Groups[3].Value, "data-number=").Count)))
            .ToArray();

        Assert.Equal(expected.GetProperty("closeRows").EnumerateArray().Select(row => row.GetString()!), close.Select(row => row.Row));
        Assert.All(close, row => Assert.Equal(CardNumbers.Of(row.Family).Count, row.Numbers));
        Assert.Empty(CardNumbers.Of(Core.Families.DipRule.Name));

        // The heavyweights, having read no rebalance, hold nothing in none of the eleven sectors.
        var heavyweights = expected.GetProperty("heavyweights");
        var tiles = Regex.Match(page, "<div class=\"hw-tiles\"[^>]*>").Value;

        Assert.Contains($"data-holdings=\"{heavyweights.GetProperty("holdings").GetInt32()}\" data-sectors=\"{heavyweights.GetProperty("sectors").GetInt32()}\" data-of=\"{heavyweights.GetProperty("of").GetInt32()}\"", tiles, StringComparison.Ordinal);
        Assert.Equal(heavyweights.GetProperty("of").GetInt32(), Core.Families.GicsSectors.Funds.Count);
        Assert.Contains("<div class=\"hw-tile\"><span class=\"tile-label\">Since the last reshuffle</span><b>none</b><span>no reshuffle read yet</span></div>", page, StringComparison.Ordinal);
    }
}
