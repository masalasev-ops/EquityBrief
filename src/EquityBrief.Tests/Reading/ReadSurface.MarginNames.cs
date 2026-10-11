using EquityBrief.Core.Report;
using EquityBrief.Web.Marks;

namespace EquityBrief.Tests.Reading;

// read-surface, the 18.2 correction of 2026-10-11: the margins' lines name their newest margins inside the figure, pushed
// apart where two ends sit closer than a line, and a margin no quarter holds is in no legend and named beneath the figure,
// where CVX's page cut "Operating 32.4%" at the figure's edge and named a gross margin line it did not draw. Worked by hand.
public partial class ReadSurface
{
    [Fact]
    public void TheMarginLinesNameTheirEndsInsideTheFigureAndOnlyTheLinesTheyDraw()
    {
        var marks = new MarkRenderer();

        // Four quarters with operating and net margins and no gross margin, the operating margin ending at 32.4 per cent.
        QuarterMargins[] quarters =
        [
            new(new DateOnly(2025, 9, 30), null, 9.0, 7.0),
            new(new DateOnly(2025, 12, 31), null, 8.5, 6.5),
            new(new DateOnly(2026, 3, 31), null, 7.0, 5.0),
            new(new DateOnly(2026, 6, 30), null, 32.4, 18.0),
        ];

        var lines = marks.MarginLines("CVX", quarters);

        // The legend names the two lines drawn and not the gross margin, which a line beneath names as absent.
        Assert.Contains(">Operating margin<", lines, StringComparison.Ordinal);
        Assert.Contains(">Net margin<", lines, StringComparison.Ordinal);
        Assert.DoesNotContain(">Gross margin<", lines, StringComparison.Ordinal);
        Assert.Contains("<p class=\"mark-note\" data-margin-absent=\"gross\">No quarter of these files a gross margin, so no gross line is drawn.</p>", lines, StringComparison.Ordinal);

        // Each end is named whole, starting 8 to the right of the newest quarter's x, 120 short of the plot's right edge:
        // the plot runs from 46 to 626, so the newest quarter stands at 506 and its names at 514, with 112 to spare.
        Assert.Contains("x=\"514\"", lines, StringComparison.Ordinal);
        Assert.Contains(">Operating 32.4%</text>", lines, StringComparison.Ordinal);
        Assert.Contains(">Net 18.0%</text>", lines, StringComparison.Ordinal);
    }
}
