using EquityBrief.Core.Providers;
using EquityBrief.Core.Report;

namespace EquityBrief.Tests.Reading;

// The 18.2 correction of 2026-11-05: a segment table whose segments carry the filer's own label for the consolidation's
// operating segments is read again with that label taken off and its subsegments' lines left out, and the reading stands
// only where its segments sum to the company's total; a table whose first reading sums keeps it.
// see: A segment table whose segments carry the filer's own label for the consolidation's segments is read again without it and its subsegments, standing only where that sums
public partial class ReadSurface
{
    static SegmentGroup OnAxis(string label, string dimension, decimal? revenue) => new(label, dimension, [Revenue(revenue)]);

    // HPQ's table of its filing of 2026-08-27, R36.htm, for the quarter to 2026-07-31 in millions, its groups in the
    // filing's order with the dimension each is filed on: the consolidation's segments labelled "Total segment net revenue"
    // at 15,679, the corporate costs, "Other" at -2 on the reconciling items, the three segments each carrying the
    // consolidation's label, and the five subsegments' lines beneath them; the company's revenue 15,677.
    static SegmentBreakdown HpqTable() =>
        ATable(
            15_677_000_000m,
            OnAxis("Total segment net revenue", "srt_ConsolidationItemsAxis=us-gaap_OperatingSegmentsMember", 15_679_000_000m),
            OnAxis("Corporate and unallocated costs and other", "srt_ConsolidationItemsAxis=us-gaap_CorporateNonSegmentMember", null),
            OnAxis("Other", "srt_ConsolidationItemsAxis=us-gaap_MaterialReconcilingItemsMember", -2_000_000m),
            OnAxis("Personal Systems | Total segment net revenue", "us-gaap_StatementBusinessSegmentsAxis=hpq_PersonalSystemsMember", 11_767_000_000m),
            OnAxis("Printing | Total segment net revenue", "us-gaap_StatementBusinessSegmentsAxis=hpq_PrintingMember", 3_912_000_000m),
            OnAxis("Corporate Investments | Total segment net revenue", "us-gaap_StatementBusinessSegmentsAxis=hpq_CorporateInvestmentsMember", 0m),
            OnAxis("Commercial PS | Personal Systems | Total segment net revenue", "us-gaap_SubsegmentsAxis=hpq_CommercialPSMember", 8_579_000_000m),
            OnAxis("Consumer PS | Personal Systems | Total segment net revenue", "us-gaap_SubsegmentsAxis=hpq_ConsumerPSMember", 3_188_000_000m),
            OnAxis("Supplies | Printing | Total segment net revenue", "us-gaap_SubsegmentsAxis=hpq_SuppliesMember", 2_536_000_000m),
            OnAxis("Commercial Printing | Printing | Total segment net revenue", "us-gaap_SubsegmentsAxis=hpq_CommercialPrintingMember", 1_101_000_000m),
            OnAxis("Consumer Printing | Printing | Total segment net revenue", "us-gaap_SubsegmentsAxis=hpq_ConsumerPrintingMember", 275_000_000m));

    // Read by the stated words alone, every segment's label names a total, so only "Other" stood and the page said the
    // segments summed to -$2.0M against $15.7B. Read again: Other -2, Personal Systems 11,767 and Printing 3,912 sum to
    // 15,677, the company's revenue exactly, Corporate Investments' label naming a corporate amount; Personal Systems is
    // 11,767 of 15,677, 75.06%.
    [Fact]
    public void HpqsSegmentsCarryingTheConsolidationsOwnLabelAreReadAgainAndStandAsTheFilingStatesThem()
    {
        var read = SegmentTable.Read(HpqTable());

        Assert.True(read.Clean);
        Assert.Equal(15_677_000_000m, read.Total);
        Assert.Equal(15_677_000_000m, read.Sum);
        Assert.Equal(["Other|-2000000", "Personal Systems|11767000000", "Printing|3912000000"], read.Rows.Select(row => $"{row.Segment}|{row.Revenue}"));
        Assert.Equal(75.06, Math.Round(read.Rows.Single(row => row.Segment == "Personal Systems").Share, 2));
    }

    // A table whose segments sum at the first reading keeps it, though its filer labels the consolidation's segments too:
    // East 60 and West 40 sum to the company's 100, where read again the two fee lines beneath that label would stand
    // beside them at 200.
    [Fact]
    public void ATableWhoseSegmentsSumAtTheFirstReadingKeepsIt()
    {
        var read = SegmentTable.Read(ATable(
            100_000_000m,
            OnAxis("Total reportable segment revenue", "srt_ConsolidationItemsAxis=us-gaap_OperatingSegmentsMember", 100_000_000m),
            OnAxis("East", "us-gaap_StatementBusinessSegmentsAxis=x_EastMember", 60_000_000m),
            OnAxis("West", "us-gaap_StatementBusinessSegmentsAxis=x_WestMember", 40_000_000m),
            OnAxis("Total reportable segment revenue | Fees", "srt_ConsolidationItemsAxis=us-gaap_OperatingSegmentsMember", 70_000_000m),
            OnAxis("Total reportable segment revenue | Other fees", "srt_ConsolidationItemsAxis=us-gaap_OperatingSegmentsMember", 30_000_000m)));

        Assert.True(read.Clean);
        Assert.Equal(["East", "West"], read.Rows.Select(row => row.Segment));

        // Where neither reading sums, the first stands with what it summed to.
        var neither = SegmentTable.Read(ATable(
            100_000_000m,
            OnAxis("Total reportable segment revenue", "srt_ConsolidationItemsAxis=us-gaap_OperatingSegmentsMember", 100_000_000m),
            OnAxis("East | Total reportable segment revenue", "us-gaap_StatementBusinessSegmentsAxis=x_EastMember", 60_000_000m),
            OnAxis("Fees", "srt_ProductOrServiceAxis=x_FeesMember", 10_000_000m)));

        Assert.Equal(SegmentTable.NoSum, neither.Raw);
        Assert.Equal(10_000_000m, neither.Sum);
    }
}
