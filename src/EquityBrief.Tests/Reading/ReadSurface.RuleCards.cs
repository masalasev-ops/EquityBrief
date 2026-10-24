using System.Net;
using System.Text.RegularExpressions;
using EquityBrief.Api.Reading;
using EquityBrief.Core.Cards;
using EquityBrief.Tests.Harness;
using EquityBrief.Web.Marks;
using EquityBrief.Worker.Candidates;

namespace EquityBrief.Tests.Reading;

// read-surface, 17.2: each card's selector lists the live rule and every variant, a variant chosen in the link redraws
// the card from that rule's rows under the band with the clauses the live rule lacks marked and its own picks, which are
// not tonight's list; the link reopens the same choice and the default returns to the live rule; the stretch line and the
// funnel are drawn from the rule's row; the S&P 400's selector lists the provisional rule alone with the line saying no
// variant is registered before its freeze; and the live rules past their mark stand on the Run pages.
// see: A variant's picks are shown on its card when chosen and its results on paper trading beside its verdict, never on Tonight
// see: A card's stretch line counts its mark over past empty nights and draws none under thirty completed stretches
public partial class ReadSurface
{
    // The card's row as the fourteen parts 17.2 landed, two of which 19.3 retired with the selector, and the checklist's
    // item on a live rule past its mark.
    internal static readonly string[] RuleCardsPageClaims =
    [
        .. new[]
        {
            "a selector beside the card's state line listing the live rule or the provisional rule first and then each registered variant by the number of its first registration with the register's words",
            "the choice kept in the link under the family's own key by a handler merging the key into the query the link holds",
            "the live rule drawn where the link names none or names a rule that does not stand",
            "a variant chosen redraws the card from that rule's rows under the band saying it is scored in the background and not the live rule and its picks are not tonight's list",
            "its words written by code from its parameters with the clauses the live rule's words lack marked",
            "its own picks with their plans in place of the live rule's rows",
            "the chosen rule's funnel with each gate and how many members passed it and every gate before, or that the night did not evaluate the rule",
            "the stretch line saying how many nights the rule has listed nothing for against the mark its own past empty nights set and flagged past it, or how far the floors are from being met while no mark is drawn",
            "under a breakout card the breakouts forming with the count forming and the rows drawn and the nearest misses first",
            "each forming member with the price it would have to close above and the volume the rule would need against tonight's",
            "its ranges' ratio and the gates it still fails and its next report where one falls within the window",
            "a line where the market check closed the rule's list",
            "the closing line that the rule reads its other gates that night as on any other and lists five at most",
            "on the S&P 400 or 600 where no variant stands before a freeze the provisional rule alone with the line that no variant is registered before its freeze",
        }.Select(part => CheckReach.Key("15.7 Tonight", "A family's card's rule, " + part)),
        CheckReach.Key("15.10 Run", "Anything to worry about, no live rule gone longer without a pick than its own past empty nights say it does, and where one has its family named with its stretch against its mark"),
    ];

    static void RuleNightRow(TemporaryStore store, string index, string night, string family, string rule, int listed, string? gates, int? stretch, int? mark, bool flagged, int completed, int sessions) =>
        store.Execute(
            "INSERT INTO rule_night (index_code, session_date, family, rule, evaluated, listed, gates, stretch, mark, flagged, completed, sessions, source) VALUES " +
            $"('{index}', '{night}', '{family}', '{Doubled(rule)}', 1, {listed}, {(gates is null ? "NULL" : $"'{gates}'")}, {(stretch is { } held ? held : "NULL")}, {(mark is { } level ? level : "NULL")}, {(flagged ? 1 : 0)}, {completed}, {sessions}, 'night');");

    [Fact]
    public async Task AVariantChosenInTheLinkRedrawsTheCardUnderTheBandWithItsOwnPicksAndTheDifferingClausesMarkedAndTheDefaultIsTheLiveRule()
    {
        // The live breakout and the neighbour at a 251-session high standing on the night; the neighbour's own list kept
        // K3, which the live rule did not list, and the rows the night wrote for each.
        using var store = await FamilyPagesStore();

        RegisteredRule(store, 101, TheSetupFamilies.Breakouts[0], "2026-01-02T12:00:00Z");
        RegisteredRule(store, 103, TheSetupFamilies.Breakouts[1], "2026-01-02T12:00:00Z");
        Kept(store, NeighbourBreakout, "breakout", "K3", TheSwitch, null, null, null);

        var funnel = RuleRows.GatesJson([("market", 10), ("new high", 4), ("volume", 3), ("tightened", 2), ("trade", 2)]);

        RuleNightRow(store, "GSPC", TheSwitch, "breakout", LiveBreakout, 2, funnel, 0, 7, false, 31, 600);
        RuleNightRow(store, "GSPC", TheSwitch, "breakout", NeighbourBreakout, 1, RuleRows.GatesJson([("market", 10), ("new high", 3), ("volume", 2), ("tightened", 1), ("trade", 1)]), 0, null, false, 2, 4);
        RuleNightRow(store, "GSPC", TheSwitch, "pullback", "the live swing filter, version 5", 5, null, 0, null, false, 0, 1);

        using var host = new Host(store.Root);
        using var client = host.CreateClient();

        var slug = RunScreen.Slug(NeighbourBreakout);
        var chosen = WebUtility.HtmlDecode(await client.GetStringAsync($"/screens/tonight/{TheSwitch}?breakout={slug}"));
        var card = Section(chosen, "breakout");

        // The card says which rule drew it, the selector offers both with the variant chosen, and the band stands.
        Assert.Contains($"data-family=\"breakout\" data-place=\"2\" data-of=\"3\" data-picks=\"2\" data-state=\"live\" data-live-since=\"2026-01-02\" data-variants=\"1\" data-rule=\"{slug}\" data-variant=\"1\"", card, StringComparison.Ordinal);
        Assert.Contains("<nav class=\"rule-tabs\" data-rule-choice=\"breakout\"", card, StringComparison.Ordinal);
        Assert.Contains($"<a class=\"rule-tab\" href=\"#\" data-rule-tab=\"breakout\" data-value=\"{RuleScreenWords.LiveSlug}\" data-variant=\"none\" title=\"{LiveBreakout}\">Live</a>", card, StringComparison.Ordinal);

        // From 19.3 the variant's tab names the one setting it differs from the live rule on, its 251-session high where
        // the live rule reads 126, its full name under the pointer; and its chip of it marked.
        Assert.Contains($"<a class=\"rule-tab current\" href=\"#\" data-rule-tab=\"breakout\" data-value=\"{slug}\" data-variant=\"1\" title=\"{NeighbourBreakout}\" aria-current=\"true\">V1 · 251-session high</a>", card, StringComparison.Ordinal);
        Assert.Equal(["251-session high"], Regex.Matches(card, "<li class=\"rule-chip differs\">([^<]*)</li>").Select(match => match.Groups[1].Value));
        Assert.Equal(
            ["251-session high", "volume 1.5 times", "ranges at 0.85", "stop 1.5 moves"],
            Regex.Matches(card, "<li class=\"rule-chip(?: differs)?\">([^<]*)</li>").Select(match => match.Groups[1].Value));
        Assert.Contains($"<p class=\"variant-band\" role=\"status\" data-variant=\"1\">{RuleScreenWords.Band(1)}</p>", card, StringComparison.Ordinal);

        // Its words with the clause the live rule's lack marked: the 251-session high, where the live rule reads 126.
        Assert.Contains("<mark class=\"differs\">", card, StringComparison.Ordinal);
        Assert.Contains("251", Marked(card).Single(), StringComparison.Ordinal);
        Assert.DoesNotContain("126", Marked(card).Single(), StringComparison.Ordinal);

        // Its own pick, K3, which the live rule does not list, and none of the live rule's rows: beside the live rule's
        // passes tonight, K1 and K2, it shares none, adds K3 and drops both.
        Assert.Contains("<tr data-ticker=\"K3\" data-variant-pick=\"true\">", card, StringComparison.Ordinal);
        Assert.DoesNotContain("data-ticker=\"K1\"", card, StringComparison.Ordinal);
        Assert.Contains("<p class=\"rule-share\" data-shared=\"\" data-added=\"K3\" data-dropped=\"K1,K2\">Beside the live rule's passes tonight: shares none · adds K3 · drops K1, K2.</p>", card, StringComparison.Ordinal);

        // Its funnel as one bar a gate, the gate stopping the most of those reaching it marked and named: of the 10 the
        // market passed, 3 made a new high, 7 stopped there, where the volume, the tightening and the trade stopped 1, 1 and
        // 0; the market's row left out while the check is open.
        Assert.Contains("<div class=\"funnel-bar\" data-gates=\"5\" data-largest-drop=\"new high\" data-market-row=\"false\"><ol class=\"rule-funnel\"><li data-gate=\"new high\" data-passed=\"3\" data-reached=\"10\" class=\"largest-drop\">", card, StringComparison.Ordinal);
        Assert.Equal(
            [("new high", "3", "10", true, "30", "3 of 10"), ("volume", "2", "3", false, "66.7", "2 of 3"), ("tightened", "1", "2", false, "50", "1 of 2"), ("trade", "1", "1", false, "100", "1 of 1")],
            FunnelRows(card));
        Assert.Contains("<p class=\"funnel-drop\" data-drop=\"new high\">Most stopped at the new high gate: 7 of the 10 reaching it.</p>", card, StringComparison.Ordinal);
        Assert.Contains("<p class=\"stretch-line\" data-stretch=\"0\" data-mark=\"none\" data-flagged=\"false\" data-completed=\"2\" data-sessions=\"4\">Listed tonight, so its empty stretch is nothing; no mark yet: 2 of 30 stretches completed over 4 of 504 sessions.</p>", card, StringComparison.Ordinal);

        // The default is the live rule: no band, the live rule's rows, its own funnel and its stretch against its mark.
        var live = Section(WebUtility.HtmlDecode(await client.GetStringAsync($"/screens/tonight/{TheSwitch}")), "breakout");

        Assert.Contains($"data-rule=\"{RuleScreenWords.LiveSlug}\" data-variant=\"none\"", live, StringComparison.Ordinal);
        Assert.DoesNotContain("variant-band", live, StringComparison.Ordinal);
        Assert.Contains("data-ticker=\"K1\"", live, StringComparison.Ordinal);
        Assert.DoesNotContain("data-variant-pick", live, StringComparison.Ordinal);
        Assert.DoesNotContain("<mark class=\"differs\">", live, StringComparison.Ordinal);
        Assert.Contains("<p class=\"stretch-line\" data-stretch=\"0\" data-mark=\"7\" data-flagged=\"false\" data-completed=\"31\" data-sessions=\"600\">Listed tonight, so its empty stretch is nothing; on 95% of past empty nights the stretch was 7 or shorter.</p>", live, StringComparison.Ordinal);

        // A choice naming no standing rule returns to the live rule, and the pullback's card keeps its own choice apart.
        var unknown = Section(WebUtility.HtmlDecode(await client.GetStringAsync($"/screens/tonight/{TheSwitch}?breakout=no-such-rule")), "breakout");

        Assert.Contains($"data-rule=\"{RuleScreenWords.LiveSlug}\"", unknown, StringComparison.Ordinal);
        Assert.Contains("data-rule-choice=\"pullback\"", chosen, StringComparison.Ordinal);

        // The shell keeps the choice in the link by merging the card's key into the query it holds, and a row's link
        // merges the keys the link holds, the index among them.
        var shell = await client.GetStringAsync("/");

        Assert.Contains("event.target.closest('a[data-rule-tab]')", shell, StringComparison.Ordinal);
        Assert.Contains("location.hash = merged(tab.getAttribute('data-rule-tab'), tab.getAttribute('data-value') === 'live' ? null : tab.getAttribute('data-value'))", shell, StringComparison.Ordinal);
        Assert.Contains("if (value === null) { params.delete(key); } else { params.set(key, value); }", shell, StringComparison.Ordinal);
        Assert.Contains("location.hash = mergedInto(pick)", shell, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AChosenVariantDrawsNoneOfTheNotesTonightsListHoldsWhichTheLiveRuleDraws()
    {
        // The live breakout and its neighbour standing on the night, and K9, which the live rule passed, held back from
        // tonight's list by its trade of the night before; the neighbour, holding no trade of K9, keeps it on its own list.
        using var store = await FamilyPagesStore();

        RegisteredRule(store, 101, TheSetupFamilies.Breakouts[0], "2026-01-02T12:00:00Z");
        RegisteredRule(store, 103, TheSetupFamilies.Breakouts[1], "2026-01-02T12:00:00Z");
        Kept(store, NeighbourBreakout, "breakout", "K9", TheSwitch, null, null, null);
        store.Execute($"INSERT INTO family_pick (session_date, ticker, family, state, place, also, held_family, held_night) VALUES ('{TheSwitch}', 'K9', 'breakout', 'open trade', NULL, '[]', 'breakout', '{BeforeTheSwitch}');");
        RuleNightRow(store, "GSPC", TheSwitch, "breakout", LiveBreakout, 2, null, 0, null, false, 2, 4);
        RuleNightRow(store, "GSPC", TheSwitch, "breakout", NeighbourBreakout, 1, null, 0, null, false, 2, 4);

        using var host = new Host(store.Root);
        using var client = host.CreateClient();

        var live = Section(WebUtility.HtmlDecode(await client.GetStringAsync($"/screens/tonight/{TheSwitch}")), "breakout");
        var chosen = Section(WebUtility.HtmlDecode(await client.GetStringAsync($"/screens/tonight/{TheSwitch}?breakout={RunScreen.Slug(NeighbourBreakout)}")), "breakout");

        // The live rule's card says why K9 is not on tonight's list; the variant's, drawn as that rule saw the night, lists
        // K9 and draws none of tonight's list's notes.
        Assert.Contains($"<p class=\"family-note\">K9 qualified again tonight but its trade from {BeforeTheSwitch} is still open, so it is not listed. One stock, one trade.</p>", live, StringComparison.Ordinal);
        Assert.Contains("<tr data-ticker=\"K9\" data-variant-pick=\"true\">", chosen, StringComparison.Ordinal);
        Assert.DoesNotContain("class=\"family-note\"", chosen, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TheStretchLineIsFlaggedPastItsMarkAndTheRunPageNamesTheLiveRule()
    {
        using var store = await FamilyPagesStore();

        RegisteredRule(store, 101, TheSetupFamilies.Breakouts[0], "2026-01-02T12:00:00Z");
        RuleNightRow(store, "GSPC", TheSwitch, "breakout", LiveBreakout, 0, null, 9, 7, true, 31, 600);

        using var host = new Host(store.Root);
        using var client = host.CreateClient();

        var card = Section(WebUtility.HtmlDecode(await client.GetStringAsync($"/screens/tonight/{TheSwitch}")), "breakout");

        Assert.Contains("<p class=\"stretch-line flagged\" data-stretch=\"9\" data-mark=\"7\" data-flagged=\"true\" data-completed=\"31\" data-sessions=\"600\">No pick for 9 nights; on 95% of past empty nights the stretch was 7 or shorter. Past its mark.</p>", card, StringComparison.Ordinal);

        var run = WebUtility.HtmlDecode(await client.GetStringAsync($"/screens/run/{TheSwitch}"));

        Assert.Contains(RunScreen.StretchWorry, run, StringComparison.Ordinal);
        Assert.Contains("the breakout's live rule has listed nothing for 9 nights, past its mark of 7", run, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TheSAndP400SelectorListsTheProvisionalRuleAloneWithTheLineThatNoVariantIsRegisteredBeforeItsFreeze()
    {
        using var store = UniversesStore();

        RuleNightRow(store, "MID", IndexNight, "breakout", RuleRows.ProvisionalRule, 1, RuleRows.GatesJson([("the market check closed", 3), ("no setup", 1), ("under the price or dollar volume floor", 1), ("the profit check", 1), ("the interest cover check", 1)]), 0, null, false, 0, 1);
        store.Execute(
            "INSERT INTO forming_row (index_code, session_date, rule, place, ticker, close, high, moves_under, volume_needed, volume, range_ratio, missing, next_earnings, forming) VALUES " +
            $"('MID', '{IndexNight}', '{RuleRows.ProvisionalRule}', 1, 'M3', '50', '50.5', 0.5, 1500000, 800000, 0.9, '[\"new high\",\"volume\"]', '{IndexNight.Replace("10-02", "10-20", StringComparison.Ordinal)}', 1);");

        using var host = new Host(store.Root);
        using var client = host.CreateClient();

        var page = WebUtility.HtmlDecode(await client.GetStringAsync($"/screens/tonight/{IndexNight}?universe=400"));
        var card = Section(page, "breakout");

        Assert.Contains("<nav class=\"rule-tabs\" data-rule-choice=\"breakout\"", card, StringComparison.Ordinal);
        Assert.Single(Regex.Matches(card, "<a class=\"rule-tab"));
        Assert.Contains($"<a class=\"rule-tab current\" href=\"#\" data-rule-tab=\"breakout\" data-value=\"{RuleScreenWords.LiveSlug}\" data-variant=\"none\" title=\"{RuleScreen.ProvisionalChoice}\" aria-current=\"true\">Provisional</a>", card, StringComparison.Ordinal);
        Assert.Contains($"<p class=\"no-variant\" data-no-variant=\"true\">{RuleScreen.NoVariantLine("S&P 400")}</p>", card, StringComparison.Ordinal);

        // Its funnel, stored under the reasons a member fails each part, is drawn as the parts: the market passed by 3, its
        // row left out while the check is open, and the interest cover by 1, the setup stopping 2 of the 3 reaching it,
        // said once, in the one line of a folded card or beneath the bars of an open one, and no line naming the market
        // check closed.
        Assert.Contains("data-market-row=\"false\"", card, StringComparison.Ordinal);
        Assert.DoesNotContain("<li data-gate=\"market\"", card, StringComparison.Ordinal);
        Assert.Contains("<li data-gate=\"interest cover\" data-passed=\"1\" data-reached=\"1\"><span class=\"fb-name\">interest cover</span>", card, StringComparison.Ordinal);
        Assert.Contains(
            card.Contains("data-folded=\"true\"", StringComparison.Ordinal) ? "most stopped at the setup gate (2 of 3)" : "<p class=\"funnel-drop\" data-drop=\"setup\">Most stopped at the setup gate: 2 of the 3 reaching it.</p>",
            card,
            StringComparison.Ordinal);
        Assert.DoesNotContain("the market check closed:", card, StringComparison.Ordinal);

        // The breakouts forming under it, behind an expander: M3 half a move under its high, the volume needed against
        // tonight's, the gates it still fails and its report inside the window, under the closing line.
        Assert.Contains("<details class=\"expander forming\" data-forming=\"1\" data-drawn=\"1\" data-market-open=\"true\"><summary>Breakouts forming: 1</summary>", card, StringComparison.Ordinal);
        Assert.Contains("<tr data-ticker=\"M3\" data-forming-row=\"true\" data-high=\"50.5\" data-volume-needed=\"1500000\">", card, StringComparison.Ordinal);
        Assert.Contains("<td>new high, volume</td><td>2026-10-20</td>", card, StringComparison.Ordinal);
        Assert.Contains(RuleScreenWords.FormingClosingLine, card, StringComparison.Ordinal);

        // The S&P 400's Run page says no setup's rule is past its mark.
        var run = WebUtility.HtmlDecode(await client.GetStringAsync($"/screens/run/{IndexNight}?universe=400"));

        Assert.Contains("<p class=\"stretch-worry\" data-past-mark=\"0\">", run, StringComparison.Ordinal);
    }

    // One family's card section of the page.
    static string Section(string page, string family)
    {
        var start = page.IndexOf($"<section class=\"family-card\" data-family=\"{family}\"", StringComparison.Ordinal);

        Assert.True(start >= 0, $"the page draws no {family} card");

        var end = page.IndexOf("<section class=\"family-card\"", start + 1, StringComparison.Ordinal);

        return page[start..(end < 0 ? page.Length : end)];
    }

    static IEnumerable<string> Marked(string card) =>
        System.Text.RegularExpressions.Regex.Matches(card, "<mark class=\"differs\">(.*?)</mark>").Select(match => match.Groups[1].Value);
}
