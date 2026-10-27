using EquityBrief.Core.Ledger;
using EquityBrief.Core.Providers;

namespace EquityBrief.Tests.Providers;

// A report's own XBRL instance, read where the archive's facts answer carries no figure of the report. The capture is a
// served 10-Q's instance, trimmed to the contexts, units and figures the test reads, each element as the archive served
// it: its contexts and units in the instance's own namespace with no prefix, its share count's unit the bare name
// "shares", and its net income stated twice for each period and once more under a segment. The figures expected were
// read off the capture by hand.
// see: A report the archive's facts answer carries no figure of is read from the report's own XBRL instance
public class SecEdgarInstanceTests
{
    const string Accession = "0001628280-26-050134";

    static readonly DateOnly Filed = new(2026, 7, 28);

    internal const string Abbott = """
        <?xml version="1.0" encoding="utf-8"?>
        <xbrl
          xml:lang="en-US"
          xmlns="http://www.xbrl.org/2003/instance"
          xmlns:dei="http://xbrl.sec.gov/dei/2026"
          xmlns:iso4217="http://www.xbrl.org/2003/iso4217"
          xmlns:us-gaap="http://fasb.org/us-gaap/2026"
          xmlns:xbrldi="http://xbrl.org/2006/xbrldi"
          xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance">
            <context id="c-1">
                <entity>
                    <identifier scheme="http://www.sec.gov/CIK">0000001800</identifier>
                </entity>
                <period>
                    <startDate>2026-01-01</startDate>
                    <endDate>2026-06-30</endDate>
                </period>
            </context>
            <context id="c-4">
                <entity>
                    <identifier scheme="http://www.sec.gov/CIK">0000001800</identifier>
                </entity>
                <period>
                    <instant>2026-06-30</instant>
                </period>
            </context>
            <context id="c-5">
                <entity>
                    <identifier scheme="http://www.sec.gov/CIK">0000001800</identifier>
                </entity>
                <period>
                    <startDate>2026-04-01</startDate>
                    <endDate>2026-06-30</endDate>
                </period>
            </context>
            <context id="c-6">
                <entity>
                    <identifier scheme="http://www.sec.gov/CIK">0000001800</identifier>
                </entity>
                <period>
                    <startDate>2025-04-01</startDate>
                    <endDate>2025-06-30</endDate>
                </period>
            </context>
            <context id="c-7">
                <entity>
                    <identifier scheme="http://www.sec.gov/CIK">0000001800</identifier>
                </entity>
                <period>
                    <startDate>2025-01-01</startDate>
                    <endDate>2025-06-30</endDate>
                </period>
            </context>
            <context id="c-23">
                <entity>
                    <identifier scheme="http://www.sec.gov/CIK">0000001800</identifier>
                    <segment>
                        <xbrldi:explicitMember dimension="us-gaap:StatementEquityComponentsAxis">us-gaap:RetainedEarningsMember</xbrldi:explicitMember>
                    </segment>
                </entity>
                <period>
                    <startDate>2026-04-01</startDate>
                    <endDate>2026-06-30</endDate>
                </period>
            </context>
            <unit id="usd">
                <measure>iso4217:USD</measure>
            </unit>
            <unit id="shares">
                <measure>shares</measure>
            </unit>
            <dei:EntityCommonStockSharesOutstanding contextRef="c-4" decimals="INF" id="f-26" unitRef="shares">1730383296</dei:EntityCommonStockSharesOutstanding>
            <us-gaap:NetIncomeLoss contextRef="c-5" decimals="-6" id="f-89" unitRef="usd">928000000</us-gaap:NetIncomeLoss>
            <us-gaap:NetIncomeLoss contextRef="c-6" decimals="-6" id="f-90" unitRef="usd">1779000000</us-gaap:NetIncomeLoss>
            <us-gaap:NetIncomeLoss contextRef="c-1" decimals="-6" id="f-91" unitRef="usd">2005000000</us-gaap:NetIncomeLoss>
            <us-gaap:NetIncomeLoss contextRef="c-7" decimals="-6" id="f-92" unitRef="usd">3104000000</us-gaap:NetIncomeLoss>
            <us-gaap:NetIncomeLoss contextRef="c-5" decimals="-6" id="f-117" unitRef="usd">928000000</us-gaap:NetIncomeLoss>
            <us-gaap:NetIncomeLoss contextRef="c-23" decimals="-6" id="f-281" unitRef="usd">928000000</us-gaap:NetIncomeLoss>
            <us-gaap:RevenueFromContractWithCustomerExcludingAssessedTax contextRef="c-5" decimals="-6" id="f-60" unitRef="usd">12593000000</us-gaap:RevenueFromContractWithCustomerExcludingAssessedTax>
            <us-gaap:RevenueFromContractWithCustomerExcludingAssessedTax contextRef="c-1" decimals="-6" id="f-62" unitRef="usd">23757000000</us-gaap:RevenueFromContractWithCustomerExcludingAssessedTax>
            <us-gaap:NetCashProvidedByUsedInOperatingActivities contextRef="c-1" decimals="-6" id="f-373" unitRef="usd">3803000000</us-gaap:NetCashProvidedByUsedInOperatingActivities>
            <us-gaap:NetCashProvidedByUsedInOperatingActivities contextRef="c-7" decimals="-6" id="f-374" unitRef="usd">3464000000</us-gaap:NetCashProvidedByUsedInOperatingActivities>
            <us-gaap:Assets contextRef="c-4" decimals="-6" id="f-150" unitRef="usd">109215000000</us-gaap:Assets>
        </xbrl>
        """;

    static IReadOnlyList<string> Read(IReadOnlyDictionary<string, IReadOnlyList<ConceptFact>> answer, string concept) =>
        [.. answer[concept].Select(fact => FormattableString.Invariant($"{fact.Start:yyyy-MM-dd}|{fact.End:yyyy-MM-dd}|{fact.Value}"))];

    [Fact]
    public void AReportsOwnInstanceIsReadAsTheFactsAnswerStatesAFigureEachUndimensionedSpanOrDayOnce()
    {
        var answer = SecEdgarInstance.Facts(Abbott, Accession, Filed, "10-Q", FiledFacts.Concepts);

        // Each period's net income once, the figure stated twice for the quarter kept once and the one under the segment
        // passed over.
        Assert.Equal(
            ["2026-04-01|2026-06-30|928000000", "2025-04-01|2025-06-30|1779000000", "2026-01-01|2026-06-30|2005000000", "2025-01-01|2025-06-30|3104000000"],
            Read(answer, "NetIncomeLoss"));
        Assert.Equal(["2026-04-01|2026-06-30|12593000000", "2026-01-01|2026-06-30|23757000000"], Read(answer, "RevenueFromContractWithCustomerExcludingAssessedTax"));
        Assert.Equal(["2026-01-01|2026-06-30|3803000000", "2025-01-01|2025-06-30|3464000000"], Read(answer, "NetCashProvidedByUsedInOperatingActivities"));

        // A balance sheet's line and the cover's share count each stand for the day the context names, the count's unit
        // the bare name of shares.
        Assert.Equal(["2026-06-30|2026-06-30|109215000000"], Read(answer, "Assets"));
        Assert.Equal(["2026-06-30|2026-06-30|1730383296"], Read(answer, "EntityCommonStockSharesOutstanding"));

        // A concept the instance never states holds none, and every figure is dated and named by the report.
        Assert.Empty(answer["ProfitLoss"]);
        Assert.All(answer.Values.SelectMany(figures => figures), fact => Assert.Equal((Filed, "10-Q", Accession), (fact.Filed, fact.Form, fact.Accession)));

        // Stored as the facts answer's figures are, first filed under the report, the quarter's and the six months' alike.
        Assert.Contains(FiledFacts.Stored(answer), row => row is { Concept: "NetIncomeLoss", Value: 928000000m } && row.Start == new DateOnly(2026, 4, 1) && row.Accession == Accession);
    }

    [Fact]
    public void AFigureWithNoNumberOrNoContextItCanReadIsPassedOverAndAnInstanceThatIsNoXmlIsRefused()
    {
        var odd = Abbott
            .Replace("<unit id=\"usd\">", "<context id=\"s-1\"><entity><identifier scheme=\"http://www.sec.gov/CIK\">0000001800</identifier></entity><period><startDate>2026-04-01</startDate><endDate>2026-06-30</endDate></period><scenario><xbrldi:explicitMember dimension=\"us-gaap:StatementScenarioAxis\">us-gaap:ScenarioForecastMember</xbrldi:explicitMember></scenario></context><unit id=\"usd\">", StringComparison.Ordinal)
            .Replace("</xbrl>", """
                <unit id="perShare"><divide><unitNumerator><measure>iso4217:USD</measure></unitNumerator><unitDenominator><measure>shares</measure></unitDenominator></divide></unit>
                <unit id="eur"><measure>iso4217:EUR</measure></unit>
                <us-gaap:ProfitLoss contextRef="c-5" unitRef="usd" xsi:nil="true"/>
                <us-gaap:ProfitLoss contextRef="c-23" unitRef="usd">7</us-gaap:ProfitLoss>
                <us-gaap:ProfitLoss contextRef="s-1" unitRef="usd">1</us-gaap:ProfitLoss>
                <us-gaap:ProfitLoss contextRef="c-6" unitRef="perShare">2</us-gaap:ProfitLoss>
                <us-gaap:ProfitLoss contextRef="c-7" unitRef="eur">3</us-gaap:ProfitLoss>
                <us-gaap:ProfitLoss contextRef="c-4" unitRef="usd">4</us-gaap:ProfitLoss>
                <us-gaap:ProfitLoss contextRef="c-1" unitRef="usd">n/a</us-gaap:ProfitLoss>
                <us-gaap:AssetsCurrent contextRef="c-1" unitRef="usd">5</us-gaap:AssetsCurrent>
                <us-gaap:EntityCommonStockSharesOutstanding contextRef="c-4" unitRef="shares">6</us-gaap:EntityCommonStockSharesOutstanding>
                </xbrl>
                """, StringComparison.Ordinal);

        var answer = SecEdgarInstance.Facts(odd, Accession, Filed, "10-Q", FiledFacts.Concepts);

        // A nil, a context under a segment, one under a scenario, a figure per share, one in another currency, a period's
        // figure stated for a day, one that is no number, a balance sheet's line stated for a period and a count under the
        // wrong taxonomy.
        Assert.Empty(answer["ProfitLoss"]);
        Assert.Empty(answer["AssetsCurrent"]);
        Assert.Equal(["2026-06-30|2026-06-30|1730383296"], Read(answer, "EntityCommonStockSharesOutstanding"));

        Assert.Throws<FormatException>(() => SecEdgarInstance.Facts("not an instance", Accession, Filed, "10-Q", FiledFacts.Concepts));
    }

    [Fact]
    public void TheInstanceIsTheOneAnInlineReportIsExtractedIntoOrTheOneXmlDocumentThatIsNoLinkbase()
    {
        static string Listing(params string[] names) =>
            "{\"directory\":{\"name\":\"/Archives/edgar/data/1800/000162828026050134\",\"item\":["
            + string.Join(",", names.Select(name => "{\"name\":\"" + name + "\",\"type\":\"text.gif\"}"))
            + "]}}";

        // The captured report's listing, its documents as the archive named them.
        Assert.Equal(
            "abt-20260630_htm.xml",
            SecEdgarInstance.InstanceFile(Listing(
                "0001628280-26-050134-index-headers.html", "0001628280-26-050134-index.html", "abt-20260630.htm", "abt-20260630_cal.xml",
                "abt-20260630_def.xml", "abt-20260630_htm.xml", "abt-20260630_lab.xml", "abt-20260630_pre.xml", "FilingSummary.xml", "R1.htm")));

        Assert.Equal("acme-20260630.xml", SecEdgarInstance.InstanceFile(Listing("acme-20260630.htm", "acme-20260630.xml", "acme-20260630_lab.xml", "FilingSummary.xml")));
        Assert.Null(SecEdgarInstance.InstanceFile(Listing("acme-20260630.htm", "acme-20260630.xml", "other.xml")));
        Assert.Null(SecEdgarInstance.InstanceFile(Listing("acme-20260630.htm", "FilingSummary.xml")));
        Assert.Throws<FormatException>(() => SecEdgarInstance.InstanceFile("{\"name\":\"no directory\"}"));
    }
}
