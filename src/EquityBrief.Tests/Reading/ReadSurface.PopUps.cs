using System.Text.RegularExpressions;
using EquityBrief.Web.App;

namespace EquityBrief.Tests.Reading;

// read-surface, 5.8: every panel the stylesheet shows while the pointer is over its holder is fixed to the
// window and placed by the shell's script, so one drawn inside a table's sideways-scrolling box floats over
// the page rather than growing a scrollbar around itself and being cut off, which is what a reason's values
// on tonight's list did.
public partial class ReadSurface
{
    // The class of each panel the stylesheet shows on hover: the last class of every selector in a rule that
    // shows it with the pointer over its holder.
    static IReadOnlyList<string> PanelsShownOnHover(string css) =>
    [
        .. Regex.Matches(css, @"([^{}]+)\{([^}]*)\}")
            .Where(rule => rule.Groups[2].Value.Contains("display:block", StringComparison.Ordinal))
            .SelectMany(rule => rule.Groups[1].Value.Split(','))
            .Where(selector => selector.Contains(":hover", StringComparison.Ordinal))
            .Select(selector => Regex.Match(selector.Trim(), @"\.([a-z][a-z0-9-]*)$").Groups[1].Value)
            .Where(panel => panel.Length > 0)
            .Distinct(StringComparer.Ordinal),
    ];

    [Fact]
    public void EveryPanelShownOnHoverIsFixedToTheWindowAndPlacedByTheShell()
    {
        var css = Stylesheet.Css;
        var panels = PanelsShownOnHover(css);
        var shell = new SinglePageApp().Shell("EquityBrief");
        var placed = Regex.Match(shell, @"const pop = cell \? cell\.querySelector\('([^']+)'\) : null;");

        // The four there are: a name's year, a heading's sentence, a reason's values and a business's sentences.
        Assert.Equal(["head-tip", "peer-pop", "says", "why"], panels.Order(StringComparer.Ordinal));
        Assert.True(placed.Success, "the shell places no panel");

        foreach (var panel in panels)
        {
            // Hidden by a rule that fixes it to the window, and by no rule that places it inside its holder.
            var hidden = Regex.Matches(css, @"([^{}]+)\{([^}]*)\}")
                .Where(rule => Regex.IsMatch(rule.Groups[1].Value, $@"\.{Regex.Escape(panel)}\s*$") && rule.Groups[2].Value.Contains("display:none", StringComparison.Ordinal))
                .ToArray();

            Assert.True(hidden.Length == 1, $"{panel} is hidden by {hidden.Length} rule(s)");
            Assert.Contains("position:fixed", hidden[0].Groups[2].Value, StringComparison.Ordinal);
            Assert.DoesNotMatch($@"\.{Regex.Escape(panel)}[^{{}},]*\{{[^}}]*position:absolute", css);

            // And the shell's script places it beside its holder.
            Assert.Contains("." + panel, placed.Groups[1].Value.Split(", "));
        }

        // The reader is shown to find a panel shown on hover and to leave a rule showing one without the pointer.
        Assert.Equal(["tip"], PanelsShownOnHover(".x:hover .tip{display:block}.y .other{display:block}.z:focus .third{display:block}"));
    }
}
