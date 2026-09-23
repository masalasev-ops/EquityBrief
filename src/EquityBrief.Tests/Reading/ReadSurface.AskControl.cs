using System.Globalization;
using System.Text.RegularExpressions;
using EquityBrief.Api.Passes;
using EquityBrief.Core.Shortlist;
using EquityBrief.Web.App;
using EquityBrief.Web.Marks;

namespace EquityBrief.Tests.Reading;

// read-surface, 9.1: the control on a row of tonight's list that asks for a report, set
// apart from the label beside it saying the name holds none; and 9.3: that control and the
// queue's control taking a report out, each drawn by the rule written for it.
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

    // Every declaration the cascade gives an element, by property: the most specific matching
    // rule's, the later of two equally specific. A property is keyed as a rule writes it, so a
    // shorthand and its longhand are read as two properties; the rules over the two controls
    // write each property one way.
    static IReadOnlyDictionary<string, (string Selector, string Value)> Applied(string css, Element element, IReadOnlyList<Element> holders)
    {
        var applied = new Dictionary<string, (int Specificity, string Selector, string Value)>(StringComparer.Ordinal);

        foreach (var (selectors, body) in RulesAtTheTop(css))
        {
            foreach (var selector in selectors.Split(','))
            {
                var (matches, specificity) = Selects(selector, element, holders);

                if (!matches)
                {
                    continue;
                }

                foreach (var declaration in body.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                {
                    var colon = declaration.IndexOf(':', StringComparison.Ordinal);

                    if (colon > 0 && (!applied.TryGetValue(declaration[..colon].Trim(), out var held) || specificity >= held.Specificity))
                    {
                        applied[declaration[..colon].Trim()] = (specificity, selector.Trim(), declaration[(colon + 1)..].Trim());
                    }
                }
            }
        }

        return applied.ToDictionary(pair => pair.Key, pair => (pair.Value.Selector, pair.Value.Value), StringComparer.Ordinal);
    }

    // The properties a rule written for a button states, over the rules that match the one
    // given: every rule whose own element, the last part of its selector, is a button.
    static IReadOnlySet<string> StatedForButtons(string css, Element button, IReadOnlyList<Element> holders) =>
        RulesAtTheTop(css)
            .SelectMany(rule => rule.Selector.Split(',').Select(selector => (Selector: selector.Trim(), rule.Body)))
            .Where(rule => Regex.Split(rule.Selector, @"\s*>\s*|\s+")[^1].StartsWith("button", StringComparison.Ordinal)
                && Selects(rule.Selector, button, holders).Matches)
            .SelectMany(rule => rule.Body.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            .Where(declaration => declaration.IndexOf(':', StringComparison.Ordinal) > 0)
            .Select(declaration => declaration[..declaration.IndexOf(':', StringComparison.Ordinal)].Trim())
            .ToHashSet(StringComparer.Ordinal);

    static readonly HashSet<string> VoidElements = new(StringComparer.Ordinal)
    {
        "area", "base", "br", "col", "embed", "hr", "img", "input", "link", "meta", "source", "track", "wbr",
    };

    // The elements open at a point in the markup, outermost first, nested the way a parser
    // nests them: an opening tag holds what follows until its closing tag, and a void element
    // or one closing itself holds nothing.
    static IReadOnlyList<Element> HoldersAt(string markup, int at)
    {
        var open = new List<(string Tag, string Opening)>();

        foreach (Match tag in Regex.Matches(markup[..at], "<(?<close>/?)(?<name>[a-z][a-z0-9]*)[^>]*>"))
        {
            var name = tag.Groups["name"].Value;

            if (tag.Groups["close"].Length > 0)
            {
                var last = open.FindLastIndex(held => held.Tag == name);

                if (last >= 0)
                {
                    open.RemoveRange(last, open.Count - last);
                }
            }
            else if (!tag.Value.EndsWith("/>", StringComparison.Ordinal) && !VoidElements.Contains(name))
            {
                open.Add((name, tag.Value));
            }
        }

        return [.. open.Select(held => Opening(held.Opening))];
    }

    [Fact]
    public void TheControlsAskingForAReportAndTakingOneOutAreDrawnByTheirOwnRules()
    {
        // Each of the two small controls has a rule of its own, and the page draws it by that
        // rule only where the rule outranks every rule written for buttons and states every
        // property one of them states. A property the small rule leaves out is drawn at the
        // other rule's value, and a rule it merely equals, stated after it, wins every property
        // the two share. Read off the markup each screen draws: the button, and every element
        // holding it in the fragment its route returns.
        var night = new DateOnly(2026, 9, 18);
        var list = new MarkRenderer().TonightList(
            [new ListingCell("ZZZZ", night, 1, 0, 10m, [ShortlistSeries.AtEntryZone])],
            SinglePageApp.TonightDrawn,
            []);
        var queue = new SinglePageApp().QueueRegion([Queued("ZZZZ", "2026-09-20T10:00:00Z", ResearchRequests.Outstanding)]);

        foreach (var (screen, markup, control) in new[] { ("tonight's list", list, "ask"), ("the queue", queue, "withdraw-control") })
        {
            var form = Regex.Match(markup, $"<form class=\"(?:[^\"]* )?{Regex.Escape(control)}(?: [^\"]*)?\"[^>]*>");

            Assert.True(form.Success, $"{screen} draws no control whose class is {control}");

            var button = Regex.Match(markup[form.Index..], "<button[^>]*>");

            Assert.True(button.Success, $"the control on {screen} carries no button");

            var element = Opening(button.Value);
            var holders = HoldersAt(markup, form.Index + button.Index);

            Assert.Contains(holders, holder => holder.Tag == "form" && holder.Classes.Contains(control));

            var applied = Applied(Stylesheet.Css, element, holders);
            var stated = StatedForButtons(Stylesheet.Css, element, holders);

            // A rule for every posted form's button reaches both controls, so the population
            // this reads is never only the control's own rule.
            Assert.True(stated.Count >= 5, $"the rules written for buttons state {stated.Count} properties over the control on {screen}, expected at least 5");

            var taken = stated
                .Where(property => !Regex.IsMatch(applied[property].Selector, $@"\.{Regex.Escape(control)}(?![\w-])"))
                .Order(StringComparer.Ordinal)
                .Select(property => $"{property}:{applied[property].Value} from {applied[property].Selector}")
                .ToArray();

            Assert.True(taken.Length == 0, $"the button on {screen} is drawn by a rule written for other buttons: {string.Join("; ", taken)}");
        }

        // The reader is shown to give each property to the rule that wins that property: the
        // later of two equal rules wins what they share, a more specific rule wins what it
        // states and leaves the rest to the other, and a state the resting page is not in
        // applies nothing.
        var posted = Opening("<form class=\"ask\" method=\"post\">");
        var pressed = Opening("<button type=\"submit\">");

        var equal = Applied("form.ask button{padding:2px}form[method='post'] button{padding:0 16px;min-height:44px}", pressed, [posted]);

        Assert.Equal(("form[method='post'] button", "0 16px"), equal["padding"]);
        Assert.Equal(("form[method='post'] button", "44px"), equal["min-height"]);

        var outranked = Applied("form.ask[method='post'] button{padding:2px}form[method='post'] button{padding:0 16px;min-height:44px}", pressed, [posted]);

        Assert.Equal(("form.ask[method='post'] button", "2px"), outranked["padding"]);
        Assert.Equal(("form[method='post'] button", "44px"), outranked["min-height"]);
        Assert.False(Applied("form[method='post'] button:disabled{opacity:.6}", pressed, [posted]).ContainsKey("opacity"));

        // And a rule that is not written for a button states nothing the controls must restate.
        Assert.Equal("padding", Assert.Single(StatedForButtons("*{box-sizing:border-box}form.ask{margin:0}form button{padding:0}", pressed, [posted])));

        // The holders are the elements still open where the button opens, and none that closed
        // before it or holds nothing.
        const string nested = "<div class=\"a\"><p>x</p><span class=\"b\"><input type=\"hidden\"><img src=\"q\"/><button>";

        Assert.Equal(["div", "span"], [.. HoldersAt(nested, nested.LastIndexOf("<button", StringComparison.Ordinal)).Select(held => held.Tag)]);
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
