using EquityBrief.Core.Research;

namespace EquityBrief.Tests.Checks;

// claim-admissibility, 18.2: a quotation in any section is a document's own words, word for word in an admitted document
// the sentence cites, read without regard to case, to curly or straight marks or to how the spaces fall; a paraphrase set in
// quotation marks is refused, a quotation left open past its sentence's end is refused whole, and a sentence citing nothing
// is refused by its own rule with its quotations not read. Worked by hand over constructed releases.
// see: What management said is written from the newest and the previous results releases, and a quotation in any section appears word for word in a document it cites
public partial class ClaimAdmissibility
{
    static StoredDocument Said(string body, string admissibility = Admissibility.Accepted) =>
        new("m", "https://www.sec.gov/Archives/edgar/data/1601046/release.htm", "Results release", new DateOnly(2026, 8, 18), FetchedAt, body, admissibility);

    static (string Offending, string Reason)[] Quoting(string prose, params StoredDocument?[] documents) =>
        [.. ClaimRules.Check(ClaimRules.ManagementSection, prose, NightFacts, documents).Findings.Select(finding => (finding.Offending, finding.Reason))];

    const string Words = "Revenue grew in every region. We saw “strong demand across our communications customers” and the company’s backlog grew,\n   with   orders ahead of shipments.";

    [Fact]
    public void AQuotationIsADocumentsOwnWordsWordForWordInADocumentTheSentenceCites()
    {
        var release = Said(Words);

        // Word for word, in straight or curly marks, whatever the case, the apostrophe's form or how the spaces fall, and
        // with the punctuation a sentence closes a quotation on taken off its end.
        Assert.Empty(Quoting("What is working is \"strong demand across our communications customers\" [D1].", release));
        Assert.Empty(Quoting("What is working is “Strong Demand across our Communications customers” [D1].", release));
        Assert.Empty(Quoting("It said \"the company's backlog grew, with orders ahead of shipments\" [D1].", release));
        Assert.Empty(Quoting("It said \"revenue grew in every region.\" [D1]", release));

        // A paraphrase set in quotation marks is refused, naming its words and the rule.
        Assert.Equal(
            [("demand stayed strong", ClaimRules.QuotationNoCitedDocumentHolds)],
            Quoting("What is working is \"demand stayed strong\" [D1].", release));

        // Words the document holds in another order are no quotation of it.
        Assert.Equal(
            [("orders ahead of backlog", ClaimRules.QuotationNoCitedDocumentHolds)],
            Quoting("It said \"orders ahead of backlog\" [D1].", release));

        // A span holding no letter is no quotation, and is left to the rules for figures.
        Assert.Empty(ClaimRules.Quotations("It named \"2026\" and \"--\" [D1]."));
        Assert.Equal(["strong demand", "orders rose"], ClaimRules.Quotations("It said \"strong demand,\" and “orders rose.” [D1]"));
    }

    [Fact]
    public void AQuotationIsHeldToTheAdmittedDocumentsTheSentenceCitesAndNoOther()
    {
        var release = Said(Words);
        var other = Said("Margins narrowed in the quarter.");

        // Held to the document the sentence cites, not one beside it in the list.
        Assert.Empty(Quoting("It said \"margins narrowed in the quarter\" [D2].", release, other));
        Assert.Equal(
            [("margins narrowed in the quarter", ClaimRules.QuotationNoCitedDocumentHolds)],
            Quoting("It said \"margins narrowed in the quarter\" [D1].", release, other));

        // A sentence citing two documents is held to either.
        Assert.Empty(Quoting("It said \"margins narrowed in the quarter\" and \"revenue grew in every region\" [D1] [D2].", release, other));

        // A document admissibility refused holds nothing a quotation may rest on, and nor does one the store does not hold.
        var refused = Said(Words, Admissibility.PriceForecast);

        Assert.Contains(
            ("strong demand across our communications customers", ClaimRules.QuotationNoCitedDocumentHolds),
            Quoting("It saw \"strong demand across our communications customers\" [D2].", release, refused));
        Assert.Contains(
            ("strong demand across our communications customers", ClaimRules.QuotationNoCitedDocumentHolds),
            Quoting("It saw \"strong demand across our communications customers\" [D2].", release, null));

        // The night's mark alone names no document, so a quotation in a sentence citing [N] alone is refused.
        Assert.Equal(
            [("strong demand across our communications customers", ClaimRules.QuotationNoCitedDocumentHolds)],
            Quoting("It saw \"strong demand across our communications customers\" [N].", release));
    }

    [Fact]
    public void ASentenceCitingNothingIsRefusedWholeAndAQuotationLeftOpenPastItsEndIsRefusedWhole()
    {
        var release = Said(Words);

        // Uncited, refused by its own rule alone, its quotation not read again.
        Assert.Equal(
            [("It said \"demand stayed strong\".", ClaimRules.Uncited)],
            Quoting("It said \"demand stayed strong\".", release));

        // A quotation opened in one sentence and closed in the next is read by neither, so the sentence citing a document
        // with a mark it does not pair is refused whole, the one before it by its own rule.
        Assert.Equal(
            [
                ("It said \"revenue grew in every region.", ClaimRules.Uncited),
                ("We saw strong demand\" [D1].", ClaimRules.QuotationNoCitedDocumentHolds),
            ],
            Quoting("It said \"revenue grew in every region. We saw strong demand\" [D1].", release));
        Assert.Equal(
            [("It said “revenue grew [D1].", ClaimRules.QuotationNoCitedDocumentHolds)],
            Quoting("It said “revenue grew [D1].", release));

        Assert.True(ClaimRules.LeavesAQuotationOpen("We saw strong demand\" [D1]."));
        Assert.True(ClaimRules.LeavesAQuotationOpen("It said ”revenue grew” and “orders rose” [D1]."));
        Assert.False(ClaimRules.LeavesAQuotationOpen("It said \"revenue grew\" and “orders rose” [D1]."));
        Assert.False(ClaimRules.LeavesAQuotationOpen("The company's backlog grew [D1]."));
    }

    [Fact]
    public void ARetryIsToldToCopyTheWordsExactlyOrSayThemWithoutQuotationMarks()
    {
        var brief = RetryBrief.For([.. ClaimRules.Check(ClaimRules.ManagementSection, "What is working is \"demand stayed strong\" [D1].", NightFacts, [Said(Words)]).Findings]);

        Assert.Contains(
            "- \"demand stayed strong\" in the sentence \"What is working is \"demand stayed strong\" [D1].\": " + ClaimRules.QuotationNoCitedDocumentHolds + ". "
            + "Copy the words exactly as the cited document writes them, or say what it says without quotation marks.",
            brief,
            StringComparison.Ordinal);
    }

    [Fact]
    public void WhatManagementSaidIsAskedInThreeParagraphsQuotingTheNewestReleaseWordForWord()
    {
        var ask = SectionPrompt.Asks[ClaimRules.ManagementSection];

        // Its three openings, the release before compared only where one is listed and its figures said in words, and the
        // two lists quoting the newest release's own words exactly.
        Assert.Contains("\"" + ClaimRules.GuidanceOpens + "\"", ask, StringComparison.Ordinal);
        Assert.Contains("\"" + ClaimRules.WorkingOpens + "\"", ask, StringComparison.Ordinal);
        Assert.Contains("\"" + ClaimRules.NotWorkingOpens + "\"", ask, StringComparison.Ordinal);
        Assert.Contains("where one is listed, the release before it", ask, StringComparison.Ordinal);
        Assert.Contains("The release before's figures are not listed under Facts, so say in words how the outlook moved from it.", ask, StringComparison.Ordinal);
        Assert.Contains("quote the release's own words inside double quotation marks, copied exactly", ask, StringComparison.Ordinal);
    }
}
