using EquityBrief.Core.Facts;
using EquityBrief.Core.Research;

namespace EquityBrief.Tests.Checks;

// claim-admissibility, 18.4: each figure of the facts file is owned by the one region of the page that draws it, and a
// written section writes only the figures its own region owns, saying the others in words, the two cases and the risks
// owning none; and the short version and the headline are written last from the accepted claims alone, each sentence
// citing claims and nothing else, each figure one a cited claim states and none the tiles show, and the headline one
// sentence carrying no figure. Worked by hand over constructed facts and claims.
// see: Each figure of the facts file is owned by one region of the page, and a written section writes only the figures its own region owns
// see: The short version and the headline are written last from the accepted claims alone, and code writes each where none is accepted
public partial class ClaimAdmissibility
{
    // A section the region rule does not govern, the industry cycle, written from the theme's pages and no facts file, so
    // a test of another rule reads that rule alone.
    internal const string Ungoverned = ClaimRules.CycleSection;

    static readonly Fact[] RegionFacts =
    [
        new("company name", "3M Company", "membership"),
        new("close", "205.15", "bar"),
        new("latest quarter revenue", "1846000000", "fundamental"),
        new("latest quarter earnings per share", "2.82", "fundamental"),
        new("segment Communications Solutions Group revenue 2026-07-31", "1345000000", "fundamental"),
        new("guidance figure 1", "1930000000", "fundamental"),
        new("guidance before figure 1", "1880000000", "fundamental"),
    ];

    static readonly StoredDocument RegionRelease = Release(
        "Revenue was $1,846 million and earnings per share were $2.82. Communications Solutions Group revenue was $1,345 million. "
        + "For the fourth quarter we expect revenue of $1.930 billion, where the release before expected $1.880 billion.");

    static (string Offending, string Reason)[] Owned(string section, string prose) =>
        [.. ClaimRules.Check(section, prose, RegionFacts, [RegionRelease]).Findings.Select(finding => (finding.Offending, finding.Reason))];

    [Fact]
    public void AWrittenSectionWritesTheFiguresItsOwnRegionDrawsAndIsRefusedAnotherRegionsNamingIt()
    {
        // The segments draw the segment's revenue, so the segment commentary writes it; the latest quarter draws the
        // company's revenue and the technical readings the close, so it is refused both, each named with the region.
        Assert.Empty(Owned("The segment commentary", "Communications Solutions Group revenue was $1,345 million [N]."));
        Assert.Equal(
            [("$1,846 million, drawn in " + FactOwners.Quarter, ClaimRules.FigureAnotherRegionOwns)],
            Owned("The segment commentary", "Revenue was $1,846 million [N]."));
        Assert.Equal(
            [("205.15, drawn in " + FactOwners.Technicals, ClaimRules.FigureAnotherRegionOwns)],
            Owned("The segment commentary", "The close was 205.15 [N]."));

        // What management said owns the newest release's guided figures and the release before's, so it states the
        // guidance it moved from, held to the release it cites as well.
        Assert.Empty(Owned(ClaimRules.ManagementSection, "On guidance, management now expects revenue of $1.93 billion, where the release before expected $1.88 billion [D1]."));

        // The two cases own no figure, so each figure is said in words, while a sentence saying it in words passes.
        Assert.Equal(
            [("$1,846 million, drawn in " + FactOwners.Quarter, ClaimRules.FigureAnotherRegionOwns)],
            Owned(ClaimRules.TwoCasesSection, "The bull case rests on revenue of $1,846 million [N]. The bear case rests on supply staying short of demand [D1]."));
        Assert.Empty(Owned(ClaimRules.TwoCasesSection, "The bull case rests on revenue growing by about a third [D1]. The bear case rests on supply staying short of demand [D1]."));

        // A figure the facts file does not hold is refused by its own rule and not as another region's.
        Assert.Equal(
            [("$9 billion", ClaimRules.UnmatchedFigure)],
            Owned("The segment commentary", "Revenue was $9 billion [N]."));
    }

    [Fact]
    public void EachFactIsOwnedByTheRegionDrawingItAndAPromptListsTheRestUnderTheirRegions()
    {
        Assert.Null(FactOwners.Of(RegionFacts[0]));
        Assert.Equal(
            [FactOwners.Technicals, FactOwners.Quarter, FactOwners.Tiles, FactOwners.Segments, FactOwners.Management, FactOwners.Management],
            RegionFacts.Skip(1).Select(FactOwners.Of));
        Assert.Equal(FactOwners.Levels, FactOwners.Of(new Fact("support band 1", "198.4", "level")));
        Assert.Equal(FactOwners.Moves, FactOwners.Of(new Fact("move 1 change", "-0.0921", "move")));
        Assert.Equal(FactOwners.Reactions, FactOwners.Of(new Fact("latest earnings reaction move per cent", "-6.32", "earnings reaction")));
        Assert.Equal(FactOwners.Margins, FactOwners.Of(new Fact("latest quarter gross margin", "0.658722", "fundamental")));
        Assert.Equal(FactOwners.Valuation, FactOwners.Of(new Fact("forward price to earnings", "20.6612", "fundamental")));
        Assert.Equal(FactOwners.Dividend, FactOwners.Of(new Fact("dividend forward annual rate", "7.12", "fundamental")));
        Assert.Equal(FactOwners.Calendar, FactOwners.Of(new Fact("ex-dividend date", "2026-08-19", "fundamental")));
        Assert.Equal(FactOwners.Margins, FactOwners.Of(new Fact("operating margin trend", "0.31", "fundamental reading")));

        // The segment commentary's own figures, a name carrying none among them, and the rest grouped in the page's order.
        var (own, elsewhere) = FactOwners.Split("The segment commentary", RegionFacts);

        Assert.Equal(["company name", "segment Communications Solutions Group revenue 2026-07-31"], own.Select(fact => fact.Name));
        Assert.Equal([FactOwners.Tiles, FactOwners.Quarter, FactOwners.Management, FactOwners.Technicals], elsewhere.Select(group => group.Region));

        // The two written last are held to the claims and not to a region, the industry cycle to neither.
        Assert.True(FactOwners.Governs(ClaimRules.TwoCasesSection));
        Assert.False(FactOwners.Governs(ClaimRules.ShortVersionSection));
        Assert.False(FactOwners.Governs(ClaimRules.HeadlineSection));
        Assert.False(FactOwners.Governs(ClaimRules.CycleSection));
    }

    static readonly Claim[] Claims =
    [
        new("On guidance, management now expects fourth quarter revenue of $1.93 billion.", AcceptedClaims.Management),
        new("What is working, the release says, is demand whose \"growing relevance\" it credits.", AcceptedClaims.Management),
        new("The plan buys in three tranches at the support bands beneath the price, each an equal share of a full position.", AcceptedClaims.Plan),
    ];

    static (string Offending, string Reason)[] Last(string section, string prose, IReadOnlyList<Claim>? claims = null) =>
        [.. ClaimRules.Check(section, prose, RegionFacts, [], parts: claims is null ? AcceptedClaims.Stored(Claims) : AcceptedClaims.Stored(claims)).Findings
            .Select(finding => (finding.Offending, finding.Reason))];

    [Fact]
    public void TheShortVersionCitesTheClaimsItWasHandedAndWritesNoFigureTheTilesShow()
    {
        Assert.Empty(Last(ClaimRules.ShortVersionSection,
            "Management expects fourth quarter revenue of $1.93 billion [C1]. It credits the \"growing relevance\" of demand [C2].\n\n"
            + "The plan buys in three tranches at the support bands beneath the price [C3]."));

        // The latest quarter's earnings a share is a tile's, refused though the facts file holds it.
        Assert.Equal(
            [("$2.82", ClaimRules.FigureTheTilesShow)],
            Last(ClaimRules.ShortVersionSection, "Earnings per share were $2.82 [C1]."));

        // A figure the facts file holds that no cited claim states, and one the facts file does not hold.
        Assert.Equal(
            [("$1,846 million", ClaimRules.FigureNoCitedClaimStates)],
            Last(ClaimRules.ShortVersionSection, "Revenue was $1,846 million [C1]."));
        Assert.Equal(
            [("$1.93 billion", ClaimRules.FigureNoCitedClaimStates)],
            Last(ClaimRules.ShortVersionSection, "Revenue of $1.93 billion is expected [C3]."));
        Assert.Equal(
            [("$9 billion", ClaimRules.UnmatchedFigure)],
            Last(ClaimRules.ShortVersionSection, "Revenue of $9 billion is expected [C1]."));

        // Every sentence cites a claim listed and nothing else.
        Assert.Equal(
            [("Demand is strong.", ClaimRules.ClaimUncited)],
            Last(ClaimRules.ShortVersionSection, "Demand is strong."));
        Assert.Equal(
            [("C4", ClaimRules.ClaimOutOfRange)],
            Last(ClaimRules.ShortVersionSection, "Demand is strong [C4]."));
        Assert.Equal(
            [("D1, N", ClaimRules.NotAClaim)],
            Last(ClaimRules.ShortVersionSection, "Demand is strong [C1] [D1] [N]."));

        // A quotation is a cited claim's own words.
        Assert.Equal(
            [("record demand", ClaimRules.QuotationNoCitedClaimHolds)],
            Last(ClaimRules.ShortVersionSection, "It credits \"record demand\" [C2]."));

        // A draft with no claims stored beside it is held to nothing and refused.
        Assert.Equal(
            [("the stored claims", ClaimRules.NoClaims)],
            Last(ClaimRules.ShortVersionSection, "Demand is strong [C1].", []));

        // A date a cited claim carries passes though the facts file holds no such date, as the dated calendar items' do,
        // and one neither carries is refused, the retry told what to do.
        Claim[] dated = [.. Claims, new("Management will take part in a conference on 2026-09-11.", AcceptedClaims.Calendar)];

        Assert.Empty(Last(ClaimRules.ShortVersionSection, "Management takes part in a conference on 2026-09-11 [C4].", dated));
        Assert.Equal(
            [("2026-09-12", ClaimRules.DateNoCitedClaimCarries)],
            Last(ClaimRules.ShortVersionSection, "Management takes part in a conference on 2026-09-12 [C4].", dated));
        Assert.Equal("Cite the claim that states it, or remove the date.", RetryBrief.Do(ClaimRules.DateNoCitedClaimCarries));
    }

    [Fact]
    public void TheHeadlineIsOneSentenceCarryingNoFigureButADigitOfTheCompanysOwnName()
    {
        Assert.Empty(Last(ClaimRules.HeadlineSection, "Demand runs ahead of what supply lets the company ship [C1] [C3]."));
        Assert.Empty(Last(ClaimRules.HeadlineSection, "3M Company expects demand to keep running ahead of supply [C1]."));

        Assert.Equal(
            [("$1.93 billion", ClaimRules.HeadlineFigure)],
            Last(ClaimRules.HeadlineSection, "Guidance of $1.93 billion leads the quarter [C1]."));
        Assert.Equal(
            [("Demand runs ahead of supply [C1]. The plan buys in stages [C3].", ClaimRules.HeadlineSentences)],
            Last(ClaimRules.HeadlineSection, "Demand runs ahead of supply [C1]. The plan buys in stages [C3]."));
    }

    // The correction of 2026-10-11: CVX's short version written last restated about fifty of its claims, every risk and
    // both cases among them, in a paragraph the checker held to its citations and to nothing about its length.
    [Fact]
    public void TheShortVersionIsThreeParagraphsOfAtMostFiveSentencesEach()
    {
        const string Five = "Demand is strong [C1]. Supply is short [C2]. Guidance rose [C1]. The quarter was a record [C2]. Margins widened [C1].";
        const string Plan = "The plan buys in three tranches at the support bands beneath the price [C3].";

        // Three paragraphs of five sentences pass, a sentence's abbreviation counting no sentence of its own.
        Assert.Empty(Last(ClaimRules.ShortVersionSection, Five + "\n\n" + Five + "\n\n" + Plan));
        Assert.Empty(Last(ClaimRules.ShortVersionSection, "Demand in the U.S. is strong [C1]. Supply is short [C2]. Guidance rose [C1]. The quarter was a record [C2]. Margins widened [C1]."));

        // A sixth sentence refuses the paragraph it sits in, named by its opening, and that paragraph alone.
        Assert.Equal(
            [(Five[..80] + "...", ClaimRules.ShortVersionLongParagraph)],
            Last(ClaimRules.ShortVersionSection, Five + "\n\n" + Five + " Demand is strong [C1].\n\n" + Plan));

        // A fourth paragraph is refused by its opening, however short each is.
        Assert.Equal(
            [("Demand is strong [C1].", ClaimRules.ShortVersionTooManyParagraphs)],
            Last(ClaimRules.ShortVersionSection, "Supply is short [C2].\n\nGuidance rose [C1].\n\n" + Plan + "\n\nDemand is strong [C1]."));

        // The headline is held to its one sentence and to no paragraph.
        Assert.Empty(Last(ClaimRules.HeadlineSection, "Demand runs ahead of what supply lets the company ship [C1] [C3]."));

        // The bound is the one the retry is told, and the retry says what to do about each refusal.
        Assert.Equal((3, 5), (ClaimRules.ShortVersionParagraphs, ClaimRules.ShortVersionParagraphSentences));
        Assert.Equal(
            "Write three paragraphs and no more: what happened, what matters and what the plan does.",
            RetryBrief.Do(ClaimRules.ShortVersionTooManyParagraphs));
        Assert.Equal(
            "Keep each paragraph to five sentences, choosing the claims that matter most, since the sections beneath draw every claim.",
            RetryBrief.Do(ClaimRules.ShortVersionLongParagraph));
    }

    [Fact]
    public void ARetryIsToldWhatToDoAboutEachRefusalOfTheTwoWrittenLast()
    {
        var brief = RetryBrief.For([.. ClaimRules.Check(ClaimRules.ShortVersionSection, "Earnings per share were $2.82 [C1]. Demand is strong.", RegionFacts, [], parts: AcceptedClaims.Stored(Claims)).Findings]);

        Assert.Contains(
            "- \"$2.82\" in the sentence \"Earnings per share were $2.82 [C1].\": " + ClaimRules.FigureTheTilesShow + ". Remove it, since the tiles under the headline show it.",
            brief,
            StringComparison.Ordinal);
        Assert.Contains(
            "- the sentence \"Demand is strong.\": " + ClaimRules.ClaimUncited + ". End it with the claims it rests on, written as [C1], or remove it.",
            brief,
            StringComparison.Ordinal);

        var owned = RetryBrief.For([.. ClaimRules.Check("The segment commentary", "Revenue was $1,846 million [N].", RegionFacts, [RegionRelease]).Findings]);

        Assert.Contains(
            "- \"$1,846 million, drawn in " + FactOwners.Quarter + "\" in the sentence \"Revenue was $1,846 million [N].\": " + ClaimRules.FigureAnotherRegionOwns
            + ". Say what it shows in words, since that region draws the figure, or remove it.",
            owned,
            StringComparison.Ordinal);
    }
}
