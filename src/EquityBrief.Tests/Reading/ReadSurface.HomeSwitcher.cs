using System.Net;
using System.Text.RegularExpressions;
using EquityBrief.Core.Families;
using EquityBrief.Web.App;
using EquityBrief.Web.Marks;
using Xunit;

namespace EquityBrief.Tests.Reading;

// From the operator's correction of 2026-11-24: Home's ideas one setup at a time, chosen from a row of chips each with its
// setup's colour, its name and its count, the first in the page's order chosen when the page opens; and on the AI stock picker's
// page an account's chart drawing no frame until a night reads its first buy.
// see: Home's ideas show one setup at a time, chosen from a row of chips each with its setup's colour and count
public partial class ReadSurface
{
    // A night with a pullback pair, one drift idea and one dip idea: three chips counting 2, 1 and 1 in the page's order, the
    // pullback chosen at first and the other two setups hidden, each setup's panel holding its own header and cards alone.
    [Fact]
    public void HomeDrawsOneSetupAtATimeFromARowOfChipsThePullbackChosenFirst()
    {
        HomeIdea Idea(string family, string ticker) =>
            new(family, ticker, null, "Why.", 100m, 90m, 120m, false, null, LongRecord: new SetupLongRecord("GSPC", family, 0.06, 4702, 2019, 2026));

        HomeIdea[] ideas = [Idea(SetupFamilies.Pullback, "AAA"), Idea(SetupFamilies.Pullback, "BBB"), Idea(DriftRule.Name, "CCC"), Idea(DipRule.Name, "DDD")];
        var region = new SinglePageApp().HomeRegion(new MarkRenderer(), Day() with { Ideas = ideas, IdeasCount = 4 }, string.Empty);
        var bar = Regex.Match(region, "<div class=\"sw-bar\".*?</div>", RegexOptions.Singleline).Value;

        Assert.Equal(
            [(SetupFamilies.Pullback, "2"), (DriftRule.Name, "1"), (DipRule.Name, "1")],
            Regex.Matches(bar, "<label for=\"sw-[^\"]+\" data-family=\"([^\"]+)\"[^>]*>.*?<span class=\"sw-n\">(\\d+)</span></label>").Select(match => (match.Groups[1].Value, match.Groups[2].Value)));
        Assert.Contains($"<span class=\"fam-swatch\" data-family=\"{SetupFamilies.Pullback}\"></span>", bar, StringComparison.Ordinal);
        Assert.Contains($".sw-r:checked+label[data-family='{SetupFamilies.Pullback}']{{background:var(--fam-{SetupFamilies.Pullback}-tint);border-color:var(--fam-{SetupFamilies.Pullback})}}", Stylesheet.Css, StringComparison.Ordinal);
        Assert.Contains("<span class=\"sw-hint\">4 ideas tonight in 3 setups</span>", bar, StringComparison.Ordinal);
        Assert.Single(Regex.Matches(bar, " checked>"));
        Assert.Contains($"id=\"sw-{SetupFamilies.Pullback}\" value=\"{SetupFamilies.Pullback}\" checked>", bar, StringComparison.Ordinal);

        var panels = Regex.Matches(region, "<div class=\"sw-p\" data-setup=\"([^\"]+)\"( hidden)?>(.*?)(?=<div class=\"sw-p\"|<p class=\"home-all\")", RegexOptions.Singleline)
            .Select(match => (Setup: match.Groups[1].Value, Hidden: match.Groups[2].Success, Body: match.Groups[3].Value))
            .ToArray();

        Assert.Equal([(SetupFamilies.Pullback, false), (DriftRule.Name, true), (DipRule.Name, true)], panels.Select(panel => (panel.Setup, panel.Hidden)));

        foreach (var panel in panels)
        {
            Assert.Equal([panel.Setup], Regex.Matches(panel.Body, "<div class=\"idea-setup-h fam-edge\" data-family=\"([^\"]+)\"").Select(match => match.Groups[1].Value));
            Assert.All(Regex.Matches(panel.Body, "<article class=\"home-idea[^\"]*\" data-family=\"([^\"]+)\"").Select(match => match.Groups[1].Value), family => Assert.Equal(panel.Setup, family));
        }

        Assert.Equal(["AAA", "BBB"], Regex.Matches(panels[0].Body, "data-ticker=\"([A-Z]+)\"").Select(match => match.Groups[1].Value).Distinct());
        Assert.Contains("data-wo-track", panels[0].Body, StringComparison.Ordinal);
        Assert.DoesNotContain("data-wo-step", panels[1].Body, StringComparison.Ordinal);

        // The way to tonight's full plans stays beneath the chosen setup's cards, once.
        Assert.Single(Regex.Matches(WebUtility.HtmlDecode(region), "See all 4 of tonight's ideas with their full plans"));
        Assert.Equal("1 idea tonight in 1 setup", SinglePageApp.SwitcherHint(1, 1, 0, 0));
        Assert.Equal("2 ideas tonight in 1 setup, 3 picks watched in 2 setups", SinglePageApp.SwitcherHint(2, 1, 3, 2));
    }

    // An account none of whose values has moved from its first draws no frame and says its line starts once a night reads its
    // first buy; once one has moved the chart is drawn.
    [Fact]
    public void AnAccountsChartDrawsNoFrameUntilANightReadsItsFirstBuy()
    {
        var day = new DateOnly(2026, 11, 24);
        var flat = SinglePageApp.JudgeChart([new JudgeLineView("Its own account", [(day, 100_000m), (day.AddDays(1), 100_000m)])], [], "flat", SinglePageApp.AccountLineWaits);
        var moved = SinglePageApp.JudgeChart([new JudgeLineView("Its own account", [(day, 100_000m), (day.AddDays(1), 100_250m)])], [], "moved", SinglePageApp.AccountLineWaits);

        Assert.Equal($"<p class=\"empty\" data-account-waits>{SinglePageApp.AccountLineWaits}</p>", WebUtility.HtmlDecode(flat));
        Assert.DoesNotContain("<svg", flat, StringComparison.Ordinal);
        Assert.Contains("<svg class=\"judge-chart\"", moved, StringComparison.Ordinal);
        Assert.Equal("Its account's line starts once a night reads its first buy.", SinglePageApp.AccountLineWaits);
    }
}
