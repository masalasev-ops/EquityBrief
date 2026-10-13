using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using EquityBrief.Core.Cards;

namespace EquityBrief.Tests.Checks;

// fixture-expectations, 19.3: every setup card built one way, read off the page the fixture's whole night draws. Each card
// lists nothing on the night; the one family whose rule's night the index stores says in one line which gate stopped the
// most of the members reaching it, worked by the suite from the stored funnel by its own arithmetic, and that no usual
// stretch is read under the floors; the others say Nothing tonight with their reason beneath and draw no tabs or funnel.
// see: Live picks are drawn once in tonight's buy points, and a setup's card names them by their place
public partial class FixtureExpectations
{
    [Fact]
    public async Task TheFixturesSetupCardsEachSayInOneLineWhereMostStoppedOverTheNightsOwnFunnel()
    {
        var expected = Expected("setup-cards");

        Assert.Equal("derived", expected.GetProperty("derivation").GetString());
        Assert.Empty(expected.GetProperty("tables").EnumerateArray());

        var night = expected.GetProperty("night").GetString()!;
        string[] cards = [.. expected.GetProperty("cards").EnumerateArray().Select(card => card.GetString()!)];
        string[] ruled = [.. expected.GetProperty("ruled").EnumerateArray().Select(card => card.GetString()!)];

        var (store, code, _, _) = await FixtureReplay.NightAsync();
        using var held = store;

        Assert.Equal(0, code);

        // The S&P 500's rule nights the whole night stored: the families whose rule stands on the index, each with its
        // funnel and its completed stretches.
        var rows = Query(store, $"SELECT family || '|' || gates || '|' || completed FROM rule_night WHERE index_code = 'GSPC' AND session_date = '{night}' ORDER BY family;")
            .Select(row => row.Split('|'))
            .ToArray();

        Assert.Equal(ruled, rows.Select(row => row[0]));

        // The line by the suite's own arithmetic: the largest fall from one gate's count to the next, the earlier gate
        // taking a tie, with the fall and the count reaching it; and the completed stretches against the thirty a usual
        // stretch waits for, none past them being read on a store of one night.
        static string Line(string gates, int completed)
        {
            var funnel = JsonDocument.Parse(gates).RootElement.EnumerateArray()
                .Select(gate => (Gate: gate.GetProperty("gate").GetString()!, Passed: gate.GetProperty("passed").GetInt32()))
                .ToArray();
            (string Gate, int Stopped, int Reached)? largest = null;

            for (var at = 1; at < funnel.Length; at++)
            {
                var stopped = funnel[at - 1].Passed - funnel[at].Passed;

                if (stopped > 0 && (largest is null || stopped > largest.Value.Stopped))
                {
                    largest = (funnel[at].Gate, stopped, funnel[at - 1].Passed);
                }
            }

            var parts = new List<string> { "Nothing tonight" };

            if (largest is { } drop)
            {
                parts.Add(FormattableString.Invariant($"most stopped at the {drop.Gate} gate ({drop.Stopped} of {drop.Reached})"));
            }

            parts.Add(FormattableString.Invariant($"no usual stretch yet ({completed} of 30)"));

            return string.Join(" · ", parts);
        }

        Assert.All(rows, row => Assert.Equal(expected.GetProperty("ruledLine").GetString(), Line(row[1], int.Parse(row[2], System.Globalization.CultureInfo.InvariantCulture))));
        Assert.Equal(30, RuleStretch.CompletedStretchesFloor);

        // Each card read back off the page in the one family order, the ruled family's line the one worked above with its
        // funnel marking that gate, and every other family's line Nothing tonight with no tabs and no funnel.
        using var host = new Reading.ReadSurface.Host(store.Root);
        using var client = host.CreateClient();

        var page = WebUtility.HtmlDecode(await client.GetStringAsync($"/screens/tonight/{night}"));

        Assert.Equal(cards, Regex.Matches(page, "<section class=\"family-card[^\"]*\" data-family=\"([^\"]+)\"").Select(match => match.Groups[1].Value));

        foreach (var family in cards.Where(family => family != Core.Families.HeavyweightRule.Name))
        {
            var card = Regex.Match(page, $"<section class=\"family-card\" data-family=\"{family}\".*?</section>", RegexOptions.Singleline).Value;

            Assert.Contains("data-picks=\"0\"", card, StringComparison.Ordinal);
            Assert.DoesNotContain("card-picks", card, StringComparison.Ordinal);

            if (ruled.Contains(family))
            {
                var row = rows.Single(stored => stored[0] == family);

                Assert.Contains($"<p class=\"family-empty-line\" data-picks=\"0\" data-flagged=\"false\">{Line(row[1], int.Parse(row[2], System.Globalization.CultureInfo.InvariantCulture))}</p>", card, StringComparison.Ordinal);
                Assert.Contains("<div class=\"funnel-bar\" data-gates=\"9\" data-largest-drop=\"setup\">", card, StringComparison.Ordinal);
                Assert.Contains("<nav class=\"rule-tabs\"", card, StringComparison.Ordinal);
            }
            else
            {
                // One tab, the provisional rule's, as its pill reads with no freeze registered, and no funnel.
                Assert.Contains($"<p class=\"family-empty-line\" data-picks=\"0\" data-flagged=\"false\">{expected.GetProperty("unruledLine").GetString()}</p>", card, StringComparison.Ordinal);
                Assert.Contains("<span class=\"status-pill provisional\"", card, StringComparison.Ordinal);
                Assert.Equal(["Provisional"], Regex.Matches(card, "<a class=\"rule-tab[^>]*>([^<]*)</a>").Select(match => match.Groups[1].Value));
                Assert.DoesNotContain("funnel-bar", card, StringComparison.Ordinal);
            }
        }

        // The heavyweights, having read no rebalance, hold nothing in none of the eleven sectors.
        var heavyweights = expected.GetProperty("heavyweights");
        var tiles = Regex.Match(page, "<div class=\"hw-tiles\"[^>]*>").Value;

        Assert.Contains($"data-holdings=\"{heavyweights.GetProperty("holdings").GetInt32()}\" data-sectors=\"{heavyweights.GetProperty("sectors").GetInt32()}\" data-of=\"{heavyweights.GetProperty("of").GetInt32()}\"", tiles, StringComparison.Ordinal);
        Assert.Equal(heavyweights.GetProperty("of").GetInt32(), Core.Families.GicsSectors.Funds.Count);
        Assert.Contains("<div class=\"hw-tile\"><b>none</b><span>no rebalance read yet</span></div>", page, StringComparison.Ordinal);
    }
}
