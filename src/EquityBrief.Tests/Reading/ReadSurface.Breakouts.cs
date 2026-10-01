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
    // card's words are read from, its trade and its place among the names the family passed.
    static void BreakoutAnswer(TemporaryStore store, string ticker, int? place, string close, string high, double multiple, double ratio, string? stop, bool newHigh = true, bool volume = true, bool tightened = true)
    {
        var gates = FamilyRule.GatesJson(
        [
            new Gate(FamilyRule.Market, true, "breadth at or above its floor", FamilyRule.Values(("breadth", "0.6"), ("floor", "0.5"))),
            new Gate(BreakoutRule.NewHigh, newHigh, "the close against the year's high", FamilyRule.Values(("close", close), ("high", high), ("sessions", "252"))),
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
        BreakoutAnswer(store, "K2", 3, "80.25", "80", 1.6, 1, "76.25");
        BreakoutAnswer(store, "K3", null, "30", "29", 1.2, 0.7, "28", volume: false);
        await RedrawTheFamilies(store);

        using var host = new Host(store.Root);
        using var client = host.CreateClient();

        var page = WebUtility.HtmlDecode(await client.GetStringAsync($"/screens/tonight/{TheSwitch}"));
        var card = FamilyCardOf(page, BreakoutRule.Name);

        // The card: the second of the page's families, two picks, on provisional settings, in the ruling's words.
        var families = SetupFamilies.InPageOrder.Count;

        Assert.StartsWith(FormattableString.Invariant($"<section class=\"family-card\" data-family=\"breakout\" data-place=\"2\" data-of=\"{families}\" data-picks=\"2\" data-state=\"provisional\" data-live-since=\"none\" data-variants=\"0\">"), card, StringComparison.Ordinal);
        Assert.Contains("<p class=\"family-state\"><b class=\"provisional\">provisional: not yet frozen; its record starts at the freeze</b> · 2 picks tonight · 0 variants scoring in the background</p>", card, StringComparison.Ordinal);
        Assert.Contains(FormattableString.Invariant($"<div class=\"lbl\">Setup 2 of {families} · Breakout from a base</div><h2>Breakouts to a new high</h2>"), page, StringComparison.Ordinal);
        Assert.Contains($"<p class=\"lede\">{SetupFamilies.Breakouts.Rule}</p>", page, StringComparison.Ordinal);

        // Its rows, in the page's order: K1 sixth and K2 seventh, and no row for F1 or K3.
        Assert.Equal(
            [("K1", "6"), ("K2", "7")],
            Regex.Matches(card, "<tr data-ticker=\"([^\"]+)\" data-family=\"breakout\".*?<td class=\"place\" data-place=\"(\\d+)\">\\2</td>", RegexOptions.Singleline)
                .Select(match => (match.Groups[1].Value, match.Groups[2].Value)));
        Assert.All(new[] { "F1", "K3" }, ticker => Assert.DoesNotContain($"<tr data-ticker=\"{ticker}\"", card, StringComparison.Ordinal));

        // K1's trade: bought at 50 with the stop at 47, and no target, since the stop trails the price: the
        // target's cell and the reward to risk say so, and the bar between the stop and the target has no
        // right end.
        var first = FamilyRowOf(card, "K1");

        Assert.Contains("<td class=\"r num plan-buy\" data-buy=\"50\">50.00</td><td class=\"r num plan-stop\" data-stop=\"47\">47.00</td><td class=\"r num plan-target\" data-target=\"none\">trailing</td>", first, StringComparison.Ordinal);
        Assert.Contains("<td class=\"stop-to-target\"><span class=\"stt trailing\" data-along=\"none\"", first, StringComparison.Ordinal);
        Assert.Contains("<span class=\"stt-open\">no fixed target</span>", first, StringComparison.Ordinal);
        Assert.Contains("<td class=\"r num\" data-reward-to-risk=\"none\">open</td>", first, StringComparison.Ordinal);

        // Why each is listed, in the figures its gates stored.
        Assert.Contains("<td class=\"why-tonight\">Closed at 50.00, above its high of 49.50 over the 251 sessions before, on volume 2.10 times its average, after its daily ranges ran at 0.80 of the 20 sessions before them.</td>", first, StringComparison.Ordinal);
        Assert.Contains("<td class=\"why-tonight\">Closed at 80.25, above its high of 80.00 over the 251 sessions before, on volume 1.60 times its average, after its daily ranges ran at 1.00 of the 20 sessions before them.</td>", FamilyRowOf(card, "K2"), StringComparison.Ordinal);

        // F1, which both families passed, is drawn once, on the pullback's card, with the breakout's label
        // and the pullback's own trade, and the breakout's card names it in a note.
        var pullbacks = FamilyCardOf(page, SetupFamilies.Pullback);
        var shared = FamilyRowOf(pullbacks, "F1");

        Assert.Single(Regex.Matches(page, "<tr data-ticker=\"F1\""));
        Assert.Contains("<tr data-ticker=\"F1\" data-family=\"pullback\" data-also=\"Breakout\"", shared, StringComparison.Ordinal);
        Assert.Contains("<span class=\"also-family\" data-also=\"Breakout\">also a breakout</span>", shared, StringComparison.Ordinal);
        Assert.Contains("data-target=\"110\">110.00</td>", shared, StringComparison.Ordinal);
        Assert.DoesNotContain("also-family", FamilyRowOf(pullbacks, "F2"), StringComparison.Ordinal);
        Assert.Equal(
            ["F1 also qualified here tonight and is shown once, under Pullbacks to support, with both labels. One stock is one trade."],
            Regex.Matches(card, "<p class=\"family-note\">(.*?)</p>").Select(match => match.Groups[1].Value));

        // Read back against the store in both directions: each card's rows are the stocks the page's list
        // holds as listed under its family, in their places, and the one under another holds no place.
        foreach (var family in new[] { SetupFamilies.Pullback, BreakoutRule.Name })
        {
            Assert.Equal(
                Strings(store, $"SELECT ticker FROM family_pick WHERE session_date = '{TheSwitch}' AND family = '{family}' AND state = 'listed' ORDER BY place;"),
                Regex.Matches(FamilyCardOf(page, family), "<tr data-ticker=\"([^\"]+)\"").Select(match => match.Groups[1].Value));
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
            Assert.Contains("<p class=\"degraded family-empty\" data-picks=\"0\">No stock passed this setup tonight: of 3 members, 2 passed the new high gate, 1 of those the volume gate, 0 of those the tightened gate and 0 of those the trade gate.</p>", card, StringComparison.Ordinal);
            Assert.DoesNotContain("<table", card, StringComparison.Ordinal);
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
