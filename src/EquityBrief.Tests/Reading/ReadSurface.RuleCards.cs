using System.Net;
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
// see: A variant's picks are shown on its card when chosen and its results only under its tests
// see: A card's stretch line counts its mark over past empty nights and draws none under 30 completed stretches
public partial class ReadSurface
{
    internal static readonly string[] RuleCardsPageClaims =
    [
        CheckReach.Key("15.7 Tonight", "A family's card, its selector"),
        CheckReach.Key("15.7 Tonight", "A family's card, a variant chosen"),
        CheckReach.Key("15.7 Tonight", "A family's card, its stretch line"),
        CheckReach.Key("15.7 Tonight", "A family's card, the breakouts forming"),
        CheckReach.Key("15.7 Tonight", "A family's card, no variant registered on the S&P 400 or 600"),
        CheckReach.Key("15.10 Run", "Anything to worry about, a live rule past its mark"),
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
        Assert.Contains("<select data-rule-choice=\"breakout\"", card, StringComparison.Ordinal);
        Assert.Contains($"<option value=\"{RuleScreenWords.LiveSlug}\" data-variant=\"none\">{LiveBreakout}</option>", card, StringComparison.Ordinal);
        Assert.Contains($"<option value=\"{slug}\" selected data-variant=\"1\">Variant 1: {NeighbourBreakout}</option>", card, StringComparison.Ordinal);
        Assert.Contains($"<p class=\"variant-band\" role=\"status\" data-variant=\"1\">{RuleScreenWords.Band(1)}</p>", card, StringComparison.Ordinal);

        // Its words with the clause the live rule's lack marked: the 251-session high, where the live rule reads 126.
        Assert.Contains("<mark class=\"differs\">", card, StringComparison.Ordinal);
        Assert.Contains("251", Marked(card).Single(), StringComparison.Ordinal);
        Assert.DoesNotContain("126", Marked(card).Single(), StringComparison.Ordinal);

        // Its own pick, K3, which the live rule does not list, and none of the live rule's rows.
        Assert.Contains("<tr data-ticker=\"K3\" data-variant-pick=\"true\">", card, StringComparison.Ordinal);
        Assert.DoesNotContain("data-ticker=\"K1\"", card, StringComparison.Ordinal);
        Assert.Contains("<ol class=\"rule-funnel\" data-gates=\"5\"><li data-gate=\"market\" data-passed=\"10\">", card, StringComparison.Ordinal);
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

        Assert.Contains("select[data-rule-choice]", shell, StringComparison.Ordinal);
        Assert.Contains("location.hash = merged(rule.getAttribute('data-rule-choice'), rule.value === 'live' ? null : rule.value)", shell, StringComparison.Ordinal);
        Assert.Contains("if (value === null) { params.delete(key); } else { params.set(key, value); }", shell, StringComparison.Ordinal);
        Assert.Contains("location.hash = mergedInto(pick)", shell, StringComparison.Ordinal);
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

        Assert.Contains("<select data-rule-choice=\"breakout\"", card, StringComparison.Ordinal);
        Assert.Single(System.Text.RegularExpressions.Regex.Matches(card, "<option "));
        Assert.Contains($"<option value=\"{RuleScreenWords.LiveSlug}\" selected data-variant=\"none\">{RuleScreen.ProvisionalChoice}</option>", card, StringComparison.Ordinal);
        Assert.Contains($"<span class=\"no-variant\" data-no-variant=\"true\">{RuleScreen.NoVariantLine("S&P 400")}</span>", card, StringComparison.Ordinal);
        Assert.Contains("<li data-gate=\"the market check closed\" data-passed=\"3\">", card, StringComparison.Ordinal);

        // The breakouts forming under it: M3 half a move under its high, the volume needed against tonight's, the
        // gates it still fails and its report inside the window, under the closing line.
        Assert.Contains("<section class=\"forming\" data-forming=\"1\" data-drawn=\"1\" data-market-open=\"true\">", card, StringComparison.Ordinal);
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
