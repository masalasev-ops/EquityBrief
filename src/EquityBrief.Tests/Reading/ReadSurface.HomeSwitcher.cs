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
        var bar = Regex.Match(region, "<div class=\"setup-switch-bar\".*?</div>", RegexOptions.Singleline).Value;

        Assert.Equal(
            [(SetupFamilies.Pullback, "2"), (DriftRule.Name, "1"), (DipRule.Name, "1")],
            Regex.Matches(bar, "<label class=\"setup-switch-chip\" for=\"setup-switch-[^\"]+\" data-family=\"([^\"]+)\"[^>]*>.*?<span class=\"setup-switch-count\">(\\d+)</span></label>").Select(match => (match.Groups[1].Value, match.Groups[2].Value)));
        Assert.Contains($"<span class=\"fam-swatch\" data-family=\"{SetupFamilies.Pullback}\"></span>", bar, StringComparison.Ordinal);
        Assert.Contains($".setup-switch-input:checked+.setup-switch-chip[data-family='{SetupFamilies.Pullback}']{{background:var(--fam-{SetupFamilies.Pullback}-tint);border-color:var(--fam-{SetupFamilies.Pullback})}}", Stylesheet.Css, StringComparison.Ordinal);
        Assert.Contains("<span class=\"setup-switch-hint\">4 ideas tonight in 3 setups</span>", bar, StringComparison.Ordinal);
        Assert.Single(Regex.Matches(bar, " checked>"));
        Assert.Contains($"id=\"setup-switch-{SetupFamilies.Pullback}\" value=\"{SetupFamilies.Pullback}\" checked>", bar, StringComparison.Ordinal);

        var panels = Regex.Matches(region, "<div class=\"setup-switch-panel\" data-setup=\"([^\"]+)\"( hidden)?>(.*?)(?=<div class=\"setup-switch-panel\"|<p class=\"home-all\")", RegexOptions.Singleline)
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

    // From the operator's correction of 2026-11-24 at about 9:45 pm: the switcher wrote the class "sw", which the stylesheet
    // already gave the legend's 12 by 10 pixel swatch, so the swatch's rule shrank the switcher and everything beneath it
    // overlapped. Every class the switcher writes is selected in the stylesheet only inside its own marked block, so a class it
    // shares with a rule meant for something else fails here.
    [Fact]
    public void EveryClassTheSetupSwitchWritesIsSelectedOnlyInItsOwnBlockOfTheStylesheet()
    {
        HomeIdea Idea(string family, string ticker, bool watched = false) =>
            new(family, ticker, null, "Why.", 100m, 90m, 120m, false, null, LongRecord: new SetupLongRecord("GSPC", family, watched ? -0.12 : 0.06, 4702, 2019, 2026));

        var view = Day() with { Ideas = [Idea(SetupFamilies.Pullback, "AAA"), Idea(DipRule.Name, "DDD")], IdeasCount = 2, Watching = [Idea(DriftRule.Name, "CCC", watched: true)] };
        var switcher = SinglePageApp.SetupSwitcher(view, copying: false);
        var outer = Regex.Matches(switcher, "<div class=\"setup-switch-panel\"[^>]*>(?:(?!<div class=\"setup-switch-panel\").)*", RegexOptions.Singleline)
            .Aggregate(switcher, (left, panel) => left.Replace(panel.Value, string.Empty, StringComparison.Ordinal));

        // The switcher's own classes: those it writes on its own elements, its panels' contents being the setups' own markup.
        string[] own = [.. Regex.Matches(outer + string.Concat(Regex.Matches(switcher, "<div class=\"(setup-switch-panel)\"").Select(match => match.Value)), "class=\"([^\"]+)\"")
            .SelectMany(match => match.Groups[1].Value.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            .Where(name => name != "fam-swatch")
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)];

        Assert.Equal(["setup-switch", "setup-switch-bar", "setup-switch-chip", "setup-switch-count", "setup-switch-hint", "setup-switch-input", "setup-switch-panel"], own);

        var begin = Stylesheet.Css.IndexOf("/* setup switch: begin", StringComparison.Ordinal);
        var end = Stylesheet.Css.IndexOf("/* setup switch: end */", StringComparison.Ordinal);

        Assert.True(begin >= 0 && end > begin, "The stylesheet holds no marked block for the setup switch.");

        var outside = Stylesheet.Css[..begin] + Stylesheet.Css[end..];
        var inside = Stylesheet.Css[begin..end];

        foreach (var name in own)
        {
            var selected = new Regex($@"\.{Regex.Escape(name)}(?![\w-])");

            Assert.True(selected.IsMatch(inside), $"The setup switch's class '{name}' has no rule of its own.");
            Assert.False(selected.IsMatch(outside), $"The setup switch's class '{name}' is selected by a rule outside its block, one meant for something else.");
        }

        // The swatch the switcher once collided with keeps its own rule.
        Assert.Contains(".sw{display:inline-block;width:12px;height:10px;", outside, StringComparison.Ordinal);
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
