using System.Globalization;
using System.Text.RegularExpressions;
using EquityBrief.Core.Shortlist;
using EquityBrief.Web.App;
using EquityBrief.Web.Marks;

namespace EquityBrief.Tests.Reading;

// read-surface, 9.1: the control on a row of tonight's list that asks for a report, set
// apart from the label beside it saying the name holds none.
//
// The suite has no browser, so the rule that lays the control out is found the way a
// browser finds it: every rule the stylesheet states is matched against the control and
// the elements holding it as the renderer draws them, and the one that wins is the most
// specific, the later of two equally specific. A rule written for the control and beaten
// by a generic one stated after it is a rule the page never applies.
public partial class ReadSurface
{
    sealed record Element(string Tag, IReadOnlySet<string> Classes, IReadOnlyDictionary<string, string> Attributes);

    static Element Opening(string tag)
    {
        var name = Regex.Match(tag, "^<([a-z][a-z0-9]*)").Groups[1].Value;
        var attributes = Regex
            .Matches(tag, "([a-z-]+)=\"([^\"]*)\"")
            .ToDictionary(found => found.Groups[1].Value, found => found.Groups[2].Value, StringComparer.Ordinal);

        var classes = attributes.TryGetValue("class", out var named)
            ? named.Split(' ', StringSplitOptions.RemoveEmptyEntries).ToHashSet(StringComparer.Ordinal)
            : [];

        return new Element(name, classes, attributes);
    }

    // The rules stated outside any at-rule, in the order they are stated. An at-rule's
    // block applies under a condition, a print or a narrow screen, and the list is read
    // at the width it is drawn at on the screen it was found wrong on.
    static IReadOnlyList<(string Selector, string Body)> RulesAtTheTop(string css)
    {
        var text = Regex.Replace(css, @"/\*.*?\*/", string.Empty, RegexOptions.Singleline);
        var rules = new List<(string, string)>();
        var at = 0;

        while (true)
        {
            var open = text.IndexOf('{', at);

            if (open < 0)
            {
                return rules;
            }

            var selector = text[at..open].Trim();
            var depth = 1;
            var close = open + 1;

            for (; depth > 0; close++)
            {
                depth += text[close] switch { '{' => 1, '}' => -1, _ => 0 };
            }

            if (!selector.StartsWith('@'))
            {
                rules.Add((selector, text[(open + 1)..(close - 1)]));
            }

            at = close;
        }
    }

    // Classes and attributes against elements. A compound carrying a pseudo-class matches
    // a state the resting page is not in, so it matches nothing here.
    static (bool Matches, int Specificity) Selects(string selector, Element element, IReadOnlyList<Element> holders)
    {
        var compounds = Regex.Split(selector.Trim(), @"\s*>\s*|\s+");

        static bool One(string compound, Element element)
        {
            if (compound.Contains(':'))
            {
                return false;
            }

            var tag = Regex.Match(compound, "^[a-z][a-z0-9]*").Value;

            return (tag.Length == 0 || tag == element.Tag)
                && Regex.Matches(compound, @"\.([\w-]+)").All(found => element.Classes.Contains(found.Groups[1].Value))
                && Regex.Matches(compound, @"\[([\w-]+)(?:=['""]?([^'""\]]*)['""]?)?\]").All(found =>
                    element.Attributes.TryGetValue(found.Groups[1].Value, out var value)
                    && (!found.Groups[2].Success || value == found.Groups[2].Value));
        }

        var matches = One(compounds[^1], element)
            && compounds[..^1].All(compound => holders.Any(holder => One(compound, holder)));

        var weight = compounds.Sum(compound =>
            (Regex.Matches(compound, @"\.[\w-]+|\[[^\]]+\]").Count * 100)
            + (Regex.IsMatch(compound, "^[a-z]") ? 1 : 0));

        return (matches, weight);
    }

    // The left margin the cascade gives an element, in pixels, from a margin-left or from
    // the margin shorthand's fourth, second or only value.
    static double LeftMargin(Element element, IReadOnlyList<Element> holders)
    {
        var winner = (Specificity: -1, Value: "0");

        foreach (var (selectors, body) in RulesAtTheTop(Stylesheet.Css))
        {
            foreach (var selector in selectors.Split(','))
            {
                var (matches, specificity) = Selects(selector, element, holders);

                if (!matches || specificity < winner.Specificity)
                {
                    continue;
                }

                foreach (Match declaration in Regex.Matches(body, @"(?<name>margin(?:-left)?)\s*:\s*(?<value>[^;]+)"))
                {
                    var values = declaration.Groups["value"].Value.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
                    var left = declaration.Groups["name"].Value == "margin-left"
                        ? values[0]
                        : values.Length switch { 4 => values[3], 1 => values[0], _ => values[1] };

                    winner = (specificity, left);
                }
            }
        }

        return winner.Value == "0"
            ? 0
            : double.Parse(Regex.Match(winner.Value, @"^(-?\d+(?:\.\d+)?)px$").Groups[1].Value, CultureInfo.InvariantCulture);
    }

    [Fact]
    public void TheControlAskingForAReportStandsApartFromTheLabelSayingNoneIsWritten()
    {
        // A row whose name holds no research draws the label and the control beside it,
        // and the control sits against the label's last letter unless a margin the page
        // applies holds it off. Read off the list the renderer draws: the control, and the
        // elements holding it, are the ones the stylesheet is matched against.
        var night = new DateOnly(2026, 9, 18);
        var list = new MarkRenderer().TonightList(
            [new ListingCell("ZZZZ", night, 1, 0, 10m, [ShortlistSeries.AtEntryZone])],
            SinglePageApp.TonightDrawn,
            []);

        var table = Regex.Match(list, "<table class=\"list-table\"[^>]*>");
        var row = Regex.Match(list, "<tr data-ticker=\"ZZZZ\"[^>]*>");
        var cell = Regex.Match(list[row.Index..], "<td class=\"c-nm\">");
        var within = list[(row.Index + cell.Index)..];
        var closes = within.IndexOf("</td>", StringComparison.Ordinal);
        var label = Regex.Match(within, "<a class=\"open unwritten\"[^>]*>not written</a>");
        var control = Regex.Match(within, "<form class=\"[^\"]*\\bask\\b[^\"]*\"[^>]*>");

        Assert.True(table.Success && row.Success && cell.Success, "the list draws no name cell for the row");
        Assert.True(label.Success && label.Index < closes, "the row's name cell carries no label saying no report is written");
        Assert.True(control.Success && control.Index < closes, "the row's name cell carries no control asking for one");

        // Beside the label with nothing between: the gap is the control's own margin.
        Assert.Equal(label.Index + label.Length, control.Index);

        var holders = new[] { Opening(table.Value), Opening("<tbody>"), Opening(row.Value), Opening(cell.Value) };
        var margin = LeftMargin(Opening(control.Value), holders);

        Assert.True(margin >= 4, $"the control asking for a report sits {margin}px from the label beside it");

        // The reader is shown to find the rule that wins rather than the first it meets:
        // a generic rule stated after a specific one it outranks loses, and one it equals
        // wins.
        var form = Opening("<form class=\"ask\" method=\"post\">");

        Assert.True(Selects("form.ask[method='post']", form, []).Specificity > Selects("form[method='post']", form, []).Specificity);
        Assert.Equal(Selects("form.ask", form, []).Specificity, Selects("form[method='post']", form, []).Specificity);
        Assert.False(Selects(".list-table form.ask", form, []).Matches);
        Assert.False(Selects("form.ask:hover", form, holders).Matches);
    }
}
