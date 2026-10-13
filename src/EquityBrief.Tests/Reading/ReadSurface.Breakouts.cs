using System.Net;
using System.Text.RegularExpressions;
using EquityBrief.Core.Families;
using EquityBrief.Core.Filter;
using EquityBrief.Core.Time;
using EquityBrief.Worker.Families;

namespace EquityBrief.Tests.Reading;

// read-surface, 13.2: the breakout's card on Tonight, read back off the rendered page over a constructed
// night the lister drew from two families: its trade with a trailing stop and no target, why each stock is
// listed in the figures its gates stored, a stock both families passed drawn once with the other's label
// and named in the later card's note, and a card that lists nothing saying how far the members got.
// see: A breakout is a close above the year's high on heavy volume after its ranges narrowed, sold on a trailing stop with no target
public partial class ReadSurface
{
    // A member's stored answer under the breakout on the night: the gates it passed, with the figures the
    // card's words are read from, its trade and its place among the names the family passed. A row stored
    // before the freeze names no window.
    static void BreakoutAnswer(TemporaryStore store, string ticker, int? place, string close, string high, double multiple, double ratio, string? stop, bool newHigh = true, bool volume = true, bool tightened = true, string? window = "126")
    {
        var gates = FamilyRule.GatesJson(
        [
            new Gate(FamilyRule.Market, true, "breadth at or above its floor", FamilyRule.Values(("breadth", "0.6"), ("floor", "0.5"))),
            new Gate(
                BreakoutRule.NewHigh,
                newHigh,
                "the close against its high",
                window is null
                    ? FamilyRule.Values(("close", close), ("high", high), ("sessions", "252"))
                    : FamilyRule.Values(("close", close), ("high", high), ("sessions", "252"), (BreakoutRule.WindowValue, window))),
            new Gate(BreakoutRule.Volume, volume, "the volume against its average", FamilyRule.Values(("multiple", FamilyRule.Figure(multiple)), ("floor", "1.5"))),
            new Gate(BreakoutRule.Tightened, tightened, "the ranges against the ones before", FamilyRule.Values(("ratio", FamilyRule.Figure(ratio)), ("ceiling", "1"))),
            new Gate(FamilyRule.Trade, stop is not null, "the stop two typical moves beneath", FamilyRule.Values(("close", close))),
        ]);
        var passed = newHigh && volume && tightened && stop is not null;
        var missed = new[] { newHigh, volume, tightened, stop is not null }.Count(gate => !gate);

        store.Execute(
            "INSERT INTO family_result (session_date, ticker, family, passed, missed, place, entry, stop, target, order_by, exclusions, gates) " +
            $"VALUES ('{TheSwitch}', '{ticker}', 'breakout', {(passed ? 1 : 0)}, {missed}, {(place is { } at ? at.ToString(System.Globalization.CultureInfo.InvariantCulture) : "NULL")}, " +
            $"'{close}', {(stop is null ? "NULL" : $"'{stop}'")}, NULL, {FamilyRule.Figure(multiple)}, '[]', '{gates}');");
    }

    // The lister run again over a night whose bar is stored already, replacing the list it drew.
    static Task RedrawTheFamilies(TemporaryStore store) =>
        new FamilyLister(FixedClock.At(new DateTimeOffset(2026, 10, 2, 23, 40, 0, TimeSpan.Zero), SessionZones.UnitedStates), store.DatabaseFile)
            .RunAsync("families-redrawn-" + Guid.NewGuid().ToString("n"));

    [Fact]
    public async Task TheBreakoutsCardDrawsItsTrailingTradeAndAStockTwoFamiliesPassIsDrawnOnceWithTheOthersLabel()
    {
        // The pullback's night as the framework's test reads it: F1 to F5 listed at places 1 to 5, F6 and F7
        // past five, HELD held back. The breakout passes three: F1, first in its own order at 2.5 times its
        // average volume, then K1 at 2.1 and K2 at 1.6. K3 missed its volume gate.
        //
        // By hand: F1 is on the pullback's card already, so it is under another on the breakout's and takes
        // none of its five; K1 and K2 take the page's sixth and seventh places.
        using var store = await FamilyNightStore();

        Evening(store, TheSwitch, filter: false, [new Member("K1", Trend: false), new Member("K2", Trend: false), new Member("K3", Trend: false)]);
        BreakoutAnswer(store, "F1", 1, "100", "99.75", 2.5, 0.9, "96");
        BreakoutAnswer(store, "K1", 2, "50", "49.5", 2.1, 0.8, "47");
        BreakoutAnswer(store, "K2", 3, "80.25", "80", 1.6, 1, "76.25", window: null);
        BreakoutAnswer(store, "K3", null, "30", "29", 1.2, 0.7, "28", volume: false);
        await RedrawTheFamilies(store);

        using var host = new Host(store.Root);
        using var client = host.CreateClient();

        var page = WebUtility.HtmlDecode(await client.GetStringAsync($"/screens/tonight/{TheSwitch}"));
        var card = FamilyCardOf(page, BreakoutRule.Name);

        // The card: the second of the page's families, two picks, on provisional settings, in the ruling's words.
        var families = SetupFamilies.InPageOrder.Count;

        Assert.StartsWith(FormattableString.Invariant($"<section class=\"family-card\" data-family=\"breakout\" data-place=\"2\" data-of=\"{families}\" data-picks=\"2\" data-state=\"provisional\" data-live-since=\"none\" data-variants=\"0\" data-rule=\"live\" data-variant=\"none\" data-folded=\"false\">"), card, StringComparison.Ordinal);
        Assert.Contains("<span class=\"status-pill provisional\" title=\"Provisional: not yet frozen\">Provisional</span> <span class=\"fam-count\">2 picks tonight</span>", card, StringComparison.Ordinal);
        Assert.Contains(FormattableString.Invariant($"<div class=\"lbl\">Setup 2 of {families} · Breakout from a base</div>"), page, StringComparison.Ordinal);
        Assert.Contains("<span class=\"fam-swatch\" data-family=\"breakout\"></span>Breakouts to a new high</span>", card, StringComparison.Ordinal);

        // Its rule in words, written from the settings it froze at: a 126-session high, 1.5 times the volume, ranges
        // at 0.85 of the 20 sessions before and a stop 1.5 typical moves under, a click away beneath its chips.
        Assert.Contains("<p class=\"rule-words\">A stock closes above its highest price of the 126 sessions before on 1.5 times its average volume, after its daily ranges narrowed to at most 0.85 of those over the 20 sessions before them. Stop 1.5 typical moves below, raised as the price climbs and never lowered; no target.</p>", card, StringComparison.Ordinal);

        // Its picks named by their place among the buy points, K1 and K2, and no name for F1 or K3; their rows in the
        // buy points, K1 sixth and K2 seventh.
        Assert.Equal(["K1", "K2"], Regex.Matches(Assert.Single(Blocks(card, "<p class=\"card-picks\".*?</p>")), "data-ticker=\"([^\"]+)\"").Select(match => match.Groups[1].Value));
        Assert.All(new[] { "F1", "K3" }, ticker => Assert.DoesNotContain($"data-ticker=\"{ticker}\" href", card, StringComparison.Ordinal));
        Assert.Contains("<td class=\"place\" data-place=\"6\">6</td>", PickRowOf(page, "K1"), StringComparison.Ordinal);
        Assert.Contains("<td class=\"place\" data-place=\"7\">7</td>", PickRowOf(page, "K2"), StringComparison.Ordinal);

        // K1's trade: bought at 50 with the stop at 47, and no target, since the stop trails the price: the target's
        // cell and the reward to risk say so.
        var first = PickRowOf(page, "K1");

        Assert.Contains("<td class=\"r num plan-buy\" data-buy=\"50\">50.00</td><td class=\"r num plan-stop\" data-stop=\"47\">47.00</td><td class=\"r num plan-target\" data-target=\"none\">trailing</td>", first, StringComparison.Ordinal);
        Assert.Contains("<td class=\"r num\" data-reward-to-risk=\"none\">open</td>", first, StringComparison.Ordinal);

        // F1, which both families passed, is drawn once, in tonight's buy points tagged with the pullback, with the
        // breakout's label and the pullback's own trade, and the breakout's card names it in a note.
        var points = Regex.Match(page, "<table class=\"list-table buy-points\".*?</table>", RegexOptions.Singleline).Value;
        var shared = PickRowOf(page, "F1");

        Assert.Empty(Regex.Matches(page.Replace(points, string.Empty, StringComparison.Ordinal), "<tr data-ticker=\"F1\""));
        Assert.Single(Regex.Matches(points, "<tr data-ticker=\"F1\" data-family=\"pullback\""));
        Assert.DoesNotContain("data-family=\"breakout\" data-selects=\"F1\"", points, StringComparison.Ordinal);
        Assert.Contains("<span class=\"also-family\" data-also=\"Breakout\">also a breakout</span>", shared, StringComparison.Ordinal);
        Assert.Contains("data-target=\"110\">110.00</td>", shared, StringComparison.Ordinal);
        Assert.DoesNotContain("also-family", PickRowOf(page, "F2"), StringComparison.Ordinal);
        Assert.Equal(
            ["F1 also qualified here tonight and is shown once, under Pullbacks to support, with both labels. One stock is one trade."],
            Regex.Matches(card, "<p class=\"family-note\">(.*?)</p>").Select(match => match.Groups[1].Value));

        // Read back against the store in both directions: each card names the stocks the page's list holds as listed
        // under its family, in their places, and the one under another holds no place.
        foreach (var family in new[] { SetupFamilies.Pullback, BreakoutRule.Name })
        {
            Assert.Equal(
                Strings(store, $"SELECT ticker FROM family_pick WHERE session_date = '{TheSwitch}' AND family = '{family}' AND state = 'listed' ORDER BY place;"),
                Regex.Matches(Assert.Single(Blocks(FamilyCardOf(page, family), "<p class=\"card-picks\".*?</p>")), "data-ticker=\"([^\"]+)\"").Select(match => match.Groups[1].Value));
        }

        Assert.Equal(["F1"], Strings(store, $"SELECT ticker FROM family_pick WHERE session_date = '{TheSwitch}' AND state = 'under another' AND place IS NULL;"));
        Assert.Equal(["[\"breakout\"]"], Strings(store, $"SELECT also FROM family_pick WHERE session_date = '{TheSwitch}' AND ticker = 'F1' AND state = 'listed';"));
    }

    [Fact]
    public async Task ABreakoutsCardListingNothingSaysHowFarTheMembersGotDownItsGates()
    {
        // The market open and no breakout: of three members, G1 and G2 closed above their year's high, G2
        // on heavy volume as well, and its ranges had not narrowed; G3 did neither.
        using (var store = await FamilyNightStore())
        {
            BreakoutAnswer(store, "G1", null, "50", "49.5", 1.2, 0.8, "47", volume: false);
            BreakoutAnswer(store, "G2", null, "60", "59", 1.9, 1.3, "57", tightened: false);
            BreakoutAnswer(store, "G3", null, "70", "75", 0.9, 0.8, "67", newHigh: false, volume: false);
            await RedrawTheFamilies(store);

            using var host = new Host(store.Root);
            using var client = host.CreateClient();

            var card = FamilyCardOf(WebUtility.HtmlDecode(await client.GetStringAsync($"/screens/tonight/{TheSwitch}")), BreakoutRule.Name);

            Assert.Contains("data-picks=\"0\"", card, StringComparison.Ordinal);
            // Why it lists nothing is said once: by the funnel where the night stored one, and by the night's sentence, behind
            // the fold, where it stored none.
            Assert.Equal(
                !card.Contains("class=\"funnel-bar\"", StringComparison.Ordinal),
                card.Contains("<p class=\"degraded family-empty\" data-picks=\"0\">No stock passed this setup tonight: of 3 members, 2 passed the new high gate, 1 of those the volume gate, 0 of those the tightened gate and 0 of those the trade gate.</p>", StringComparison.Ordinal));
            Assert.DoesNotContain("card-picks", card, StringComparison.Ordinal);
            Assert.DoesNotContain("variant-table", card, StringComparison.Ordinal);
        }

        // A night whose page the family is on and its evaluator stored no answer for says that and no count.
        using (var store = await FamilyNightStore())
        {
            using var host = new Host(store.Root);
            using var client = host.CreateClient();

            var card = FamilyCardOf(WebUtility.HtmlDecode(await client.GetStringAsync($"/screens/tonight/{TheSwitch}")), BreakoutRule.Name);

            Assert.Contains("<p class=\"degraded family-empty\" data-picks=\"0\">No answer of this setup is stored for the night.</p>", card, StringComparison.Ordinal);
        }
    }
}
