using System.Text.Json;
using EquityBrief.Core.Cards;
using EquityBrief.Core.Facts;
using EquityBrief.Core.Ledger;
using EquityBrief.Core.Providers;
using EquityBrief.Core.Report;
using EquityBrief.Core.Research;
using EquityBrief.Tests.Harness;
using EquityBrief.Worker.Facts;

namespace EquityBrief.Tests.Checks;

// fixture-expectations, 18.4: the release before's guided figures in the facts file beside the newest's, under names of
// their own; the claims the short version and the headline are written from, in the page's order; the calendar's ninety
// days; the figures the filing and the fetch state differently beyond rounding; and the risks' two columns asked of Claude
// in its answer's shape. Each worked by hand.
public partial class FixtureExpectations
{
    // The rows 18.4 adds that this check reaches: section 17's two limits and section 18's failure for the two written last
    // with no accepted claim to be written from.
    internal static string[] WrittenLastRows =>
    [
        CheckReach.Key(Scope.LimitsTable, "Calendar days"),
        CheckReach.Key(Scope.LimitsTable, "Figures checked"),
        CheckReach.Key(Scope.FailureTable, "No written section stands accepted when the short version and the headline are reached"),
    ];

    // The release before's outlook as the fixture's capture of it states it, the second quarter's of fiscal 2026.
    const string OutlookBefore =
        "Keysight’s third fiscal quarter of 2026 revenue is expected to be in the range of $1.730 billion to $1.750 billion. "
        + "The mid-point of the revenue range represents year-over-year growth of approximately 29%. Non-GAAP earnings per share for the third "
        + "fiscal quarter of 2026 are expected to be in the range of $2.43 to $2.49, based on a weighted diluted share count of approximately 173 million shares.";

    // see: The facts file carries the release before's guided figures beside the newest's, under names of their own
    [Fact]
    public async Task TheFactsFileCarriesTheReleaseBeforesGuidedFiguresBesideTheNewestsUnderNamesOfTheirOwn()
    {
        var (store, _) = await WithRelease();

        using (store)
        {
            var night = Iso(ProseClock.SessionDateAt(ProseNight));

            IReadOnlyList<Fact> File() => FactsFile.Read(Query(store, $"SELECT payload FROM facts WHERE ticker = 'KEYS' AND session_date = '{night}';").Single());

            string[] Guided(string named) => [.. File().Where(fact => fact.Name.StartsWith(named, StringComparison.Ordinal)).Select(fact => $"{fact.Name}={fact.Value}")];

            // The fixture's filing before the newest was stored with no guidance passage, so the file carries the newest's
            // six figures and none of the release before's.
            var newest = Guided(FactOwners.GuidanceFigure);

            Assert.Equal(6, newest.Length);
            Assert.Empty(Guided(FactOwners.GuidanceBefore));

            // The filing before the newest holding the release before's located passage, as each will once the next
            // results are stored: its six figures join the file in the order the passage states them, read by the reader
            // of the newest's, and the newest's are as they were.
            var guidance = JsonSerializer.Serialize(new { document = "exhibit991-q226pressrelease.htm", filedOn = "2026-05-19", located = true, heading = "Outlook", passage = OutlookBefore });

            Insert(store, $"UPDATE fundamentals SET payload = json_set(payload, '$.guidance', json('{guidance}')) WHERE ticker = 'KEYS' AND filing_date = (SELECT filing_date FROM fundamentals WHERE ticker = 'KEYS' ORDER BY filing_date DESC LIMIT 1 OFFSET 1);");

            await new FactsAssembler(ProseClock, store.DatabaseFile).RunAsync("facts-release-before");

            Assert.Equal(
                ["guidance before figure 1=1730000000", "guidance before figure 2=1750000000", "guidance before figure 3=29", "guidance before figure 4=2.43", "guidance before figure 5=2.49", "guidance before figure 6=173000000"],
                Guided(FactOwners.GuidanceBefore));
            Assert.Equal(newest, Guided(FactOwners.GuidanceFigure));

            // Each is what management said's own, so it writes them and no other section does.
            Assert.All(File().Where(fact => fact.Name.StartsWith(FactOwners.GuidanceBefore, StringComparison.Ordinal)), fact => Assert.Equal(FactOwners.Management, FactOwners.Of(fact)));
        }
    }

    // see: The short version and the headline are written last from the accepted claims alone, and code writes each where none is accepted
    [Fact]
    public void TheClaimsAreTheAcceptedSectionsSentencesInThePagesOrderWithThePlansInWordsBeforeTheRisks()
    {
        IReadOnlyList<(string, string)> accepted =
        [
            (ClaimRules.TwoCasesSection, "The bull case rests on demand [D1]. The bear case rests on supply [D2]."),
            (RiskFields.Section, "The first risk is that supply stays short [D2]. That risk would be confirmed by a supply event: a supplier's warning, because supply governs revenue [D2]."),
            ("What the company sells", "It sells test equipment [D1]."),
            (ClaimRules.ManagementSection, "On guidance, management now expects revenue of $1.93 billion [N] [D1]."),
        ];
        Fact[] facts = [new("business state", "improving", "fundamental reading")];

        var claims = AcceptedClaims.Of(accepted, facts, plan: null);

        Assert.Equal(
            [
                $"{AcceptedClaims.Quarter}|Its reported quarters read as improving, read from sales and operating margin alone.",
                $"{AcceptedClaims.Management}|On guidance, management now expects revenue of $1.93 billion.",
                $"{AcceptedClaims.Segments}|It sells test equipment.",
                $"{AcceptedClaims.Plan}|No plan is stored for the night, the plan being read for the S&P 500's members alone.",
                $"{AcceptedClaims.Risks}|The first risk is that supply stays short.",
                $"{AcceptedClaims.Risks}|That risk would be confirmed by a supply event: a supplier's warning, because supply governs revenue.",
                $"{AcceptedClaims.TwoCases}|The bull case rests on demand.",
                $"{AcceptedClaims.TwoCases}|The bear case rests on supply.",
            ],
            claims.Select(claim => $"{claim.Region}|{claim.Text}"));

        // Stored beside a draft and read back whole, and claims code writes alone are no written claim.
        Assert.Equal(claims, AcceptedClaims.Read(AcceptedClaims.Stored(claims)));
        Assert.True(AcceptedClaims.AnyWritten(claims));
        Assert.False(AcceptedClaims.AnyWritten(AcceptedClaims.Of([], facts, plan: null)));
        Assert.Null(AcceptedClaims.Read("{\"risks\": []}"));
    }

    // see: The calendar draws a name's next ninety days of results, ex-dividend dates and the Fed's and the CPI's dates beside the dated items the research read
    [Fact]
    public void TheCalendarDrawsTheNinetyDaysAfterTheNightAndNoDayPastThem()
    {
        var night = new DateOnly(2026, 10, 9);
        var through = night.AddDays(PageCalendar.Days);

        Assert.Equal(new DateOnly(2027, 1, 7), through);

        MarketEvent[] table =
        [
            new(night, "fomc", "FOMC decision", "fed"),
            new(new DateOnly(2026, 10, 28), "fomc", "FOMC decision", "fed"),
            new(through, "cpi", "CPI release", "bls"),
            new(through.AddDays(1), "cpi", "CPI release", "bls"),
        ];

        // The results on day 40 and on day 91, an ex-dividend date declared on day 11: the night's own FOMC, day 91's
        // results and CPI are left out, day 90's CPI drawn, and the FOMC's table ends before the ninetieth day.
        var (items, past) = PageCalendar.Of(
            night,
            [(new DateOnly(2026, 11, 18), "earnings", "after"), (through.AddDays(1), "earnings", "before"), (new DateOnly(2026, 10, 20), "ex-dividend", null)],
            kept: null,
            table);

        Assert.Equal(
            ["2026-10-20|ex-dividend|Ex-dividend, declared|True", "2026-10-28|fomc|FOMC decision|True", "2026-11-18|results|Results, after the close|True", "2027-01-07|cpi|CPI release|True"],
            items.Select(item => $"{Iso(item.Date)}|{item.Kind}|{item.Words}|{item.Declared}"));
        Assert.Equal(["FOMC decision"], past);

        // Results on the ninetieth day itself are drawn, as that day's CPI is.
        Assert.Equal(
            ["2027-01-07|results|Results, before the open"],
            PageCalendar.Of(night, [(through, "earnings", "before")], kept: null, []).Items.Select(item => $"{Iso(item.Date)}|{item.Kind}|{item.Words}"));

        // No date declared: the last ex-date carried forward by a year over four payments, 91 days, 1.08 a year paying
        // 0.27 a share, drawn on day 90 and not on day 91.
        DividendKept Kept(DateOnly last) => new(1.08m, last, []) { PaymentsAYear = 4 };

        var onTheNinetieth = PageCalendar.Of(night, [], Kept(through.AddDays(-91)), []).Items;

        Assert.Equal(["2027-01-07|ex-dividend|Ex-dividend, estimated from its usual interval|False|0.27"], onTheNinetieth.Select(item => $"{Iso(item.Date)}|{item.Kind}|{item.Words}|{item.Declared}|{item.Amount}"));
        Assert.Empty(PageCalendar.Of(night, [], Kept(through.AddDays(-90)), []).Items);
    }

    // see: A figure the filing and the provider's fetch state differently beyond rounding is listed under the sources with the filing first, and the page draws the fetch's
    [Fact]
    public void AFigureTheFilingAndTheFetchStateDifferentlyBeyondRoundingIsListedFilingFirst()
    {
        // Rounding is half a million dollars or a tenth of a per cent of the larger, whichever is more, at each edge.
        Assert.True(FiguresChecked.Agree(100_000_000m, 100_500_000m));
        Assert.False(FiguresChecked.Agree(100_000_000m, 100_500_001m));
        Assert.True(FiguresChecked.Agree(1_000_000_000m, 1_001_001_000m));
        Assert.False(FiguresChecked.Agree(1_000_000_000m, 1_001_001_002m));

        var end = new DateOnly(2026, 7, 31);
        var start = new DateOnly(2026, 5, 1);
        var filedOn = new DateOnly(2026, 9, 2);

        FiledFactRow Filed(string concept, decimal value, DateOnly? ending = null) => new(concept, start, ending ?? end, value, filedOn, "10-Q", "0001601046-26-000033");

        var latest = new QuarterRow(end, new DateOnly(2026, 8, 18), 1_846_400_000m, 1_272_000_000m, 458_000_000m, 397_000_000m, null, null, null, 3.07m, 2.48m, null, null, true);

        // Revenue within half a million agrees; gross profit two million apart is listed with the filing first; operating
        // income filed for a quarter ending eight days from the fetch's is not the same quarter and is not compared; net
        // income the filing does not state and cash from operations the fetch does not read compare nothing.
        var (conflicts, compared) = FiguresChecked.Of(
            [
                Filed("Revenues", 1_846_000_000m),
                Filed("GrossProfit", 1_270_000_000m),
                Filed("OperatingIncomeLoss", 458_000_000m, end.AddDays(FiguresChecked.SameQuarterWithin + 1)),
                Filed("NetCashProvidedByUsedInOperatingActivities", 437_000_000m),
            ],
            latest);

        Assert.Equal(2, compared);
        Assert.Equal([new FigureConflict("Gross profit", end, 1_270_000_000m, "GrossProfit", filedOn, 1_272_000_000m)], conflicts);

        // With no filed facts stored nothing is compared, which the page tells apart from every line agreeing.
        var none = FiguresChecked.Of([], latest);

        Assert.Empty(none.Conflicts);
        Assert.Equal(0, none.Compared);
    }

    // see: The risks close on what would turn the stock up and what would make it worse, each column opening on a line the plan computes
    [Fact]
    public void ClaudeIsAskedForTheRisksTwoColumnsInTheShapeItsAnswerIsHeldTo()
    {
        var settings = Providers.ResearchModelFeedTests.PinnedClaude();
        var request = new ModelRequest(SectionPrompt.PaidLane, RiskFields.Section, settings.Identity, ["d1"], "system", "prompt");

        using var sent = JsonDocument.Parse(AnthropicMessagesFeed.Body(request, settings));

        var schema = sent.RootElement.GetProperty(AnthropicMessagesFeed.OutputConfigField).GetProperty("format").GetProperty("schema");

        Assert.Equal(["risks", "turnsUp", "makesWorse"], schema.GetProperty("required").EnumerateArray().Select(field => field.GetString()));

        foreach (var (column, opening) in new[] { ("turnsUp", RiskFields.TurnsUp), ("makesWorse", RiskFields.MakesWorse) })
        {
            var line = schema.GetProperty("properties").GetProperty(column).GetProperty("items");

            Assert.Equal(["what", "document"], line.GetProperty("required").EnumerateArray().Select(field => field.GetString()));
            Assert.Contains($"\"{opening}\"", line.GetProperty("properties").GetProperty("what").GetProperty("description").GetString(), StringComparison.Ordinal);
            Assert.False(line.GetProperty("additionalProperties").GetBoolean());
        }

        // An answer in that shape is composed with the columns beneath the risks, and one whose column is no list is no
        // answer as fields.
        const string Risk = "{\"risk\":\"that a rival launches first\",\"riskDocument\":1,\"confirm\":{\"event\":\"a rival's launch\",\"kind\":\"competitive\"},\"why\":\"launches take share\",\"whyDocument\":1}";

        var (prose, parts) = RiskFields.FromAnswer("{\"risks\":[" + Risk + "],\"turnsUp\":[{\"what\":\"The order book converts faster\",\"document\":1}],\"makesWorse\":[{\"what\":\"supply stays short\",\"document\":1}]}");

        Assert.EndsWith("\n\nIt would turn up if the order book converts faster [D1].\n\nIt would get worse if supply stays short [D1].", prose, StringComparison.Ordinal);
        var columns = RiskFields.Columns(parts);

        Assert.NotNull(columns);
        Assert.Equal(["the order book converts faster"], columns.Value.Up.Select(one => one.What));
        Assert.Equal(["supply stays short"], columns.Value.Worse.Select(one => one.What));
        Assert.Null(RiskFields.FromAnswer("{\"risks\":[" + Risk + "],\"turnsUp\":\"the order book converts faster\"}").Parts);
    }
}
