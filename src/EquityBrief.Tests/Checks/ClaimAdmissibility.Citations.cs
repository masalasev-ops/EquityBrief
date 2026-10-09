using EquityBrief.Core.Facts;
using EquityBrief.Core.Research;

namespace EquityBrief.Tests.Checks;

// claim-admissibility, the 6.4 correction's half: a figure in a sentence citing documents alone is one a document it
// cites states, read at the scale the document states its tables in and a percentage only against a percentage; [N]
// names the night's stored figures, which are held to the facts file alone; and an answer written as JSON is refused in
// every section. Worked by hand over constructed facts and documents.
// see: A sentence names the night's stored figures by [N] and a document by its marker, and a figure in a sentence citing documents alone is one they state
public partial class ClaimAdmissibility
{
    static readonly Fact[] NightFacts =
    [
        new("close", "205.15", "bar"),
        new("latest quarter revenue", "67199000000", "fundamental"),
        new("latest quarter net income", "12070000000", "fundamental"),
        new("dividend forward rate", "7.12", "fundamental"),
        new("index reading", "51.4", "fundamental"),
    ];

    static StoredDocument Release(string body) =>
        new("r", "https://www.sec.gov/Archives/edgar/data/93410/release.htm", "Results release", new DateOnly(2026, 7, 31), FetchedAt, body, Admissibility.Accepted);

    static (string Offending, string Reason)[] Refused(string section, string prose, StoredDocument document) =>
        [.. ClaimRules.Check(section, prose, NightFacts, [document]).Findings.Select(finding => (finding.Offending, finding.Reason))];

    [Fact]
    public void AFigureInASentenceCitingDocumentsAloneIsOneTheyStateReadAtTheScaleTheirTablesState()
    {
        const string Section = "What the company sells";

        // A results release whose table states its unit once, as Chevron's does: 67,199 read at millions is
        // $67.199 billion, which "$67.2 billion" rounds, and the night's close is in no release.
        var millions = Release("Sales and other operating revenues (Millions of Dollars) 67,199 and net income attributable to the company 12,070.");

        Assert.Empty(Refused(Section, "Revenue was $67.2 billion and net income $12.07 billion [D1].", millions));
        Assert.Equal([("205.15", ClaimRules.FigureNoCitedDocumentHolds)], Refused(Section, "The close was 205.15 [D1].", millions));

        // The night's figure under [N] is held to the facts file alone, with or without a document beside it, and a
        // sentence citing both holds each figure to the facts file.
        Assert.Empty(Refused(Section, "The close was 205.15 [N].", millions));
        Assert.Empty(Refused(Section, "Revenue was $67.2 billion and the close 205.15 [D1] [N].", millions));
        Assert.Empty(Refused(Section, "The close was 205.15. [N]", millions));

        // A figure the facts file does not hold is refused by the facts file's rule under either mark, once.
        Assert.Equal([("206.00", ClaimRules.UnmatchedFigure)], Refused(Section, "The close was 206.00 [N].", millions));
        Assert.Equal([("206.00", ClaimRules.UnmatchedFigure)], Refused(Section, "The close was 206.00 [D1].", millions));

        // The same table with no statement of its unit is read as written, so the billions are not in it.
        var bare = Release("Sales and other operating revenues 67,199 and net income attributable to the company 12,070.");

        Assert.Equal(
            [("$67.2 billion", ClaimRules.FigureNoCitedDocumentHolds), ("$12.07 billion", ClaimRules.FigureNoCitedDocumentHolds)],
            Refused(Section, "Revenue was $67.2 billion and net income $12.07 billion [D1].", bare));

        // A table in thousands, and a figure the document writes with its own unit, each read at its scale.
        Assert.Empty(Refused(Section, "Revenue was $67.2 billion [D1].", Release("(in thousands, except per share amounts) Revenues 67,199,000")));
        Assert.Empty(Refused(Section, "Revenue was $67.2 billion [D1].", Release("Revenue reached $67.2 billion in the quarter.")));

        // A document refused by admissibility states nothing a sentence may rest a figure on.
        var refused = Release("Sales and other operating revenues (Millions of Dollars) 67,199.") with { Admissibility = Core.Research.Admissibility.PriceForecast };

        Assert.Contains(
            ("$67.2 billion", ClaimRules.FigureNoCitedDocumentHolds),
            ClaimRules.Check(Section, "Revenue was $67.2 billion [D2].", NightFacts, [millions, refused]).Findings.Select(finding => (finding.Offending, finding.Reason)));

        // The rule holds researched sections alone: the key under each figure rests on the facts file.
        Assert.True(ClaimRules.Check(ClaimRules.ComputedSection, "The close was 205.15.", NightFacts, []).Passes);
    }

    [Fact]
    public void APercentageInASentenceIsReadOnlyAgainstAPercentageTheDocumentStates()
    {
        const string Section = "What the company sells";

        // The facts file holds both as numbers; the document writes the rate as a percentage and the reading bare.
        var document = Release("It pays a yield of 7.12% and holds an index reading of 51.4 for the quarter.");

        Assert.Equal(
            [("7.12", ClaimRules.FigureNoCitedDocumentHolds)],
            Refused(Section, "It pays a forward rate of 7.12 [D1].", document));
        Assert.Equal(
            [("51.4%", ClaimRules.FigureNoCitedDocumentHolds)],
            Refused(Section, "It holds a reading of 51.4% [D1].", document));
        Assert.Empty(Refused(Section, "It pays a yield of 7.12% and holds a reading of 51.4 [D1].", document));
    }

    [Fact]
    public void TheNightsMarkEndsASentenceAsADocumentsDoesAndCountsAsItsCitation()
    {
        var sentences = ClaimRules.Sentences("The close was 205.15. [N] Revenue grew [D1]. It sells fuel [D1][N].");

        Assert.Equal(3, sentences.Count);
        Assert.True(sentences[0].CitesNight);
        Assert.Empty(sentences[0].Citations);
        Assert.False(sentences[1].CitesNight);
        Assert.Equal([1], sentences[1].Citations);
        Assert.True(sentences[2].CitesNight);
        Assert.Equal([1], sentences[2].Citations);

        // The mark is no figure, and a sentence citing it names its source, so it is not refused as uncited.
        Assert.Empty(ClaimRules.Figures("[N]"));
        Assert.Empty(Refused("What the company sells", "Its close sits above its averages [N].", Release("text")));
        Assert.Equal(
            [("Its close sits above its averages.", ClaimRules.Uncited)],
            Refused("What the company sells", "Its close sits above its averages.", Release("text")));
    }

    [Fact]
    public void ASectionCitingTheNightsFiguresListsThemAmongItsSourcesLinkedToTheCardDrawingThem()
    {
        var marks = new EquityBrief.Web.Marks.MarkRenderer();
        var document = new EquityBrief.Web.Marks.SourceCell("r", "Results release", "https://www.sec.gov/Archives/edgar/data/93410/release.htm", new DateOnly(2026, 7, 31));

        var cites = marks.WrittenSection(
            "CVX",
            new EquityBrief.Web.Marks.WrittenCell("The short version", "The close was 205.15 [N]. It sells fuel [D1].", new DateOnly(2026, 10, 9), "a writer", ["r"]),
            [document]);

        Assert.Contains("<ol class=\"section-sources\" data-cites=\"1\"><li data-marker=\"N\">[N] <a href=\"#numbers\">the figures the night stored</a>", cites, StringComparison.Ordinal);
        Assert.Contains("<li data-marker=\"D1\" data-document=\"r\">[D1] ", cites, StringComparison.Ordinal);

        // A section citing [N] alone still lists it, and one citing documents alone lists no [N].
        Assert.Contains("data-marker=\"N\"", marks.WrittenSection("CVX", new("The short version", "The close was 205.15 [N].", new DateOnly(2026, 10, 9), "a writer", []), []), StringComparison.Ordinal);
        Assert.DoesNotContain(
            "data-marker=\"N\"",
            marks.WrittenSection("CVX", new("The short version", "It sells fuel [D1].", new DateOnly(2026, 10, 9), "a writer", ["r"]), [document]),
            StringComparison.Ordinal);
    }

    [Fact]
    public void AnAnswerWrittenAsJsonIsRefusedInEverySectionAndProseOpeningOnAMarkIsNot()
    {
        var document = Release("text");

        foreach (var section in ClaimRules.Sections)
        {
            foreach (var answer in new[] { "{\"error\": \"no text at all\"}", " [1, 2] " })
            {
                var verdict = ClaimRules.Check(section, answer, NightFacts, [document]);

                Assert.False(verdict.NoAdmissibleSource, section);
                Assert.Equal([(answer.Trim(), ClaimRules.UnusableAnswer)], verdict.Findings.Select(finding => (finding.Offending, finding.Reason)));
            }
        }

        // Prose that opens on a mark is prose, and a bracket that is not JSON is read as written.
        Assert.True(ClaimRules.Check("What the company sells", "[N] The close was 205.15.", NightFacts, [document]).Passes);
        Assert.DoesNotContain(
            ClaimRules.UnusableAnswer,
            ClaimRules.Check("What the company sells", "[D1] It sells fuel.", NightFacts, [document]).Findings.Select(finding => finding.Reason));

        // A retry is told what to do about each.
        Assert.Contains(ClaimRules.NightMark, RetryBrief.Do(ClaimRules.FigureNoCitedDocumentHolds), StringComparison.Ordinal);
        Assert.Equal("Answer in plain prose, with no JSON.", RetryBrief.Do(ClaimRules.UnusableAnswer));
    }
}
