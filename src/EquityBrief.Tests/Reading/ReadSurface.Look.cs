using System.Text.RegularExpressions;
using EquityBrief.Core.Ladders;
using EquityBrief.Core.Report;
using EquityBrief.Web.App;
using EquityBrief.Web.Marks;

namespace EquityBrief.Tests.Reading;

// read-surface, the 18.1 correction of 2026-10-11: the name page's words and figures read as a reader reads them. A count
// takes its noun's singular or plural and no "(s)"; a figure rounding to nothing is drawn with no sign; a table's number
// columns are headed on the right over figures on the right, a move and a reaction signed in their hue to two places;
// a date alone in a cell is not broken at its hyphens; on a phone a figure keeps a width its words read at; and the
// exported file's title stands in the report's column. The bug hunt of 2026-10-11 found "turned the price 2 time(s)" and
// "-0.0%" across the pages, figures under the next column's heading, and a phone's chart labels four pixels high.
public partial class ReadSurface
{
    [Fact]
    public void TheNamePagesCountsFiguresAndColumnsReadAsAReaderReadsThem()
    {
        var marks = new MarkRenderer();

        // A band's turns: once, twice, then the count, and no "(s)".
        LadderBand Turned(int touches) => new(100m, 102m, "support", 1.2, touches, new DateOnly(2026, 9, 1), null);

        Assert.Contains("turned the price once, last 2026-09-01", marks.LadderFigure("XYZ", [Turned(1)], 110m, live: false), StringComparison.Ordinal);
        Assert.Contains("turned the price twice, last 2026-09-01", marks.LadderFigure("XYZ", [Turned(2)], 110m, live: false), StringComparison.Ordinal);
        Assert.Contains("turned the price 6 times, last 2026-09-01", marks.LadderFigure("XYZ", [Turned(6)], 110m, live: false), StringComparison.Ordinal);

        // The shares a plan's tranches make, three equal shares and no "(s)".
        var shares = marks.SharesTable(
            "XYZ",
            [
                new TrancheShare(10m, 11m, 10.5m, 3, 9.5m, 1m, []),
                new TrancheShare(8m, 9m, 8.5m, 3, 7.5m, 1m, []),
                new TrancheShare(6m, 7m, 6.5m, 3, null, null, []),
            ]);

        Assert.Contains(">3 equal shares of 1/3 make the whole position.</p>", shares, StringComparison.Ordinal);

        // No "(s)" in any of them.
        Assert.DoesNotContain("(s)", marks.LadderFigure("XYZ", [Turned(1), Turned(2)], 110m, live: false), StringComparison.Ordinal);
        Assert.DoesNotContain("(s)", shares, StringComparison.Ordinal);

        // A move's change and its group's median, signed in their hue to two places, the change's column headed on the
        // right: a fall of 7.2 per cent drawn as -7.20%, a rise as +9.22%, and a median a hair under nothing as 0.00%.
        var moves = marks.MovesTable(
            "XYZ",
            [
                new MoveCell(new DateOnly(2026, 1, 5), 5, 9.2194, 1, Group: new MoveGroup("industry", "Integrated Oil", 6, 6, -0.0004)),
                new MoveCell(new DateOnly(2026, 4, 14), 5, -7.2, 2, Group: new MoveGroup("industry", "Integrated Oil", 6, 6, 1.234)),
            ]);

        Assert.Contains("<th class=\"num\">Change</th>", moves, StringComparison.Ordinal);
        Assert.Contains("<td class=\"num up\">+9.22%</td>", moves, StringComparison.Ordinal);
        Assert.Contains("<td class=\"num down\">-7.20%</td>", moves, StringComparison.Ordinal);
        Assert.Contains("<td class=\"nw\">2026-01-05</td>", moves, StringComparison.Ordinal);
        Assert.Contains("data-group-median=\"0\"><span class=\"flat\">0.00%</span>, the median of", moves, StringComparison.Ordinal);
        Assert.Contains("data-group-median=\"1.23\"><span class=\"up\">+1.23%</span>, the median of", moves, StringComparison.Ordinal);

        // A margin a hair under nothing is drawn as nothing, with no minus sign.
        var margins = marks.MarginTable([new QuarterMargins(new DateOnly(2025, 9, 30), null, 2.7, -0.0004)]);

        Assert.Contains(">0.0%</td>", margins, StringComparison.Ordinal);
        Assert.DoesNotContain("-0.0%", margins, StringComparison.Ordinal);

        // On a phone a figure keeps a width its words are read at and scrolls in its box, the plan column apart.
        var phone = Regex.Match(Stylesheet.Css, @"@media \(max-width:640px\)\{\n :root\{--gutter:14px\}(.*?)\n\}", RegexOptions.Singleline).Groups[1].Value;

        Assert.Contains(".fig>svg:not(.plan-column){min-width:560px}", phone, StringComparison.Ordinal);

        // And the exported file's title and stamp stand in the report's column.
        Assert.Contains(".exported>h1,.exported>p.exported{max-width:1000px;margin-inline:auto}", Stylesheet.Css, StringComparison.Ordinal);
    }
}
