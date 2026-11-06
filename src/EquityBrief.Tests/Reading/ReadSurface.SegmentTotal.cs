using EquityBrief.Core.Providers;
using EquityBrief.Core.Report;
using EquityBrief.Web.Marks;

namespace EquityBrief.Tests.Reading;

// WELL's table of 2026-06-30 as the page read it on 2026-11-06: its consolidated line is resident fees and services alone,
// $2,984.9M, all of it Seniors Housing Operating's, so the segments summed to the table's own total and the page drew Seniors
// Housing Operating as 100.0% of a company whose quarter's revenue was $3,544.6M. HOG's left HDFS off, $1,113.4M against
// $1,230.4M, and NTRS's summed to $1,349.5M against $2,698.0M.
// see: A quarter's segment table whose total misses the company's quarter revenue by more than 5% is drawn as filed with what its rows sum to
public partial class ReadSurface
{
    [Fact]
    public void AQuartersTableWhoseTotalMissesTheCompanysRevenueByMoreThanFivePerCentIsDrawnAsFiledWithItsSum()
    {
        var marks = new MarkRenderer();
        var well = ATable(2_984_900_000m, AGroup("Seniors Housing Operating", 2_984_900_000m), AGroup("Triple-net", 0m), AGroup("Outpatient Medical", 0m));

        // Read against its own total alone the table stands, as it was drawn.
        Assert.True(SegmentTable.Read(well).Clean);

        var read = SegmentTable.Read(well, 3_544_600_000m);

        Assert.False(read.Clean);
        Assert.Equal(SegmentTable.MissesTheCompany, read.Raw);
        Assert.Empty(read.Rows);
        Assert.Equal(
            "<p class=\"degraded\" data-segments=\"raw\" data-report=\"R9.htm\" data-total=\"3544600000\" data-sum=\"2984900000\">The filing's segment table is drawn as filed beneath, because the table's total misses the company's quarter revenue by more than 5%: its segments' revenue sums to $2,984.9M against the company's $3,544.6M.</p>",
            marks.SegmentsTable(read, "USD"));
        Assert.Equal(
            "<p class=\"mark-short\" data-mark=\"share-bars\">No segment's share is drawn, because the table's total misses the company's quarter revenue by more than 5%.</p>",
            marks.ShareBars(read));

        Assert.Equal(SegmentTable.MissesTheCompany, SegmentTable.Read(ATable(1_113_400_000m, AGroup("HDMC", 1_104_600_000m), AGroup("LiveWire", 8_800_000m)), 1_230_400_000m).Raw);
        Assert.Equal(SegmentTable.MissesTheCompany, SegmentTable.Read(ATable(1_349_500_000m, AGroup("Wealth Management", 700_000_000m), AGroup("Asset Servicing", 649_500_000m)), 2_698_000_000m).Raw);

        // A company reporting one segment is held to the same limit.
        Assert.Equal(SegmentTable.MissesTheCompany, SegmentTable.Read(ATable(1_000_000_000m, AGroup("Reportable Segment", 1_000_000_000m)), 1_200_000_000m).Raw);

        // At 5% the segments stand as shares of the company, past it they do not.
        Assert.Equal(0.05m, SegmentTable.CompanyMiss);
        Assert.True(SegmentTable.Read(ATable(950_000_000m, AGroup("Upstream", 500_000_000m), AGroup("Downstream", 450_000_000m)), 1_000_000_000m).Clean);
        Assert.False(SegmentTable.Read(ATable(949_000_000m, AGroup("Upstream", 500_000_000m), AGroup("Downstream", 449_000_000m)), 1_000_000_000m).Clean);
        Assert.True(SegmentTable.Read(ATable(1_050_000_000m, AGroup("Upstream", 600_000_000m), AGroup("Downstream", 450_000_000m)), 1_000_000_000m).Clean);

        // A year's table is read against its own total, the quarter's revenue being no year's.
        var year = new ReportPeriod(12, new DateOnly(2025, 12, 31));

        SegmentFigure Annual(decimal value) => new("us-gaap_Revenues", "Revenue", null, year, value);

        var annual = new SegmentBreakdown("R9.htm", "Segment Information (Details) - USD ($)", 1_000_000, [year], [Annual(4_000_000_000m)], [new("Upstream", "axis=x", [Annual(2_500_000_000m)]), new("Downstream", "axis=x", [Annual(1_500_000_000m)])]);

        Assert.True(SegmentTable.Read(annual, 1_000_000_000m).Clean);
    }
}
