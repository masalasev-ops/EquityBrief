using EquityBrief.Core.Facts;
using EquityBrief.Core.Research;

namespace EquityBrief.Tests.Checks;

// claim-admissibility, the risks as fields: an answer returned as its fields composed into the prose the page
// draws, each confirmation a listed fact with a direction and a level the facts file holds, or an event of one
// kind of the seven with neither, and no two risks resting on one fact or one kind of event.
// see: Each risk is returned as fields and confirmed by a listed fact or an event of one kind, and no two risks share either
public partial class ClaimAdmissibility
{
    static readonly Fact[] RiskFacts =
    [
        new("latest quarter revenue", "1846000000", "a filing"),
        new("forward price to earnings", "20.6612", "a filing"),
        new("sma200", "289.30685", "the indicators"),
    ];

    static string Answer(params string[] risks) => "{\"risks\":[" + string.Join(",", risks) + "]}";

    static string FactRisk(string fact, string level, string direction = RiskFields.FallsBelow, string? words = "the figure watched") =>
        "{\"risk\":\"that demand slows\",\"riskDocument\":1,\"confirm\":{\"fact\":\"" + fact + "\"" + (words is null ? string.Empty : ",\"words\":\"" + words + "\"")
        + "},\"direction\":\"" + direction + "\",\"level\":\"" + level + "\",\"why\":\"orders lead revenue\",\"whyDocument\":1}";

    static string EventRisk(string kind) =>
        "{\"risk\":\"that a rival launches first\",\"riskDocument\":1,\"confirm\":{\"event\":\"a rival's launch\",\"kind\":\"" + kind + "\"},\"why\":\"launches take share\",\"whyDocument\":1}";

    // The answer as a writer stores it, checked as the checker checks it.
    static IReadOnlyList<ClaimFinding> RiskFindings(string answer)
    {
        var (prose, parts) = RiskFields.FromAnswer(answer);

        return ClaimRules.Check(RiskFields.Section, prose, RiskFacts, [AnAdmittedDocument], null, parts).Findings;
    }

    [Fact]
    public void TwoRisksConfirmedByTheSameFactAreRefusedNamingTheFact()
    {
        var findings = RiskFindings(Answer(FactRisk("latest quarter revenue", "$1.85 billion"), FactRisk("Latest quarter revenue", "$1.8 billion")));

        var shared = Assert.Single(findings);

        Assert.Equal(RiskFields.SharedFact, shared.Reason);
        Assert.Equal("Latest quarter revenue", shared.Offending);

        // Two facts, one each: nothing to refuse.
        Assert.Empty(RiskFindings(Answer(FactRisk("latest quarter revenue", "$1.85 billion"), FactRisk("forward price to earnings", "20.66"))));
    }

    [Fact]
    public void TwoEventRisksOfOneKindAreRefusedNamingTheKind()
    {
        var shared = Assert.Single(RiskFindings(Answer(EventRisk("competitive"), EventRisk("competitive"))));

        Assert.Equal(RiskFields.SharedKind, shared.Reason);
        Assert.Equal("competitive", shared.Offending);

        Assert.Empty(RiskFindings(Answer(EventRisk("competitive"), EventRisk("legal"))));
    }

    [Fact]
    public void ALevelTheFactsFileDoesNotHoldIsRefusedByItsOwnRuleAndOnlyByIt()
    {
        var findings = RiskFindings(Answer(FactRisk("latest quarter revenue", "$2.1 billion")));

        var level = Assert.Single(findings);

        Assert.Equal(RiskFields.LevelNotHeld, level.Reason);
        Assert.Equal("$2.1 billion", level.Offending);

        // A fact the file does not list, and a direction that is neither of the two.
        Assert.Equal(
            [RiskFields.FactNotListed],
            [.. RiskFindings(Answer(FactRisk("next quarter revenue", "$1.85 billion"))).Select(finding => finding.Reason)]);
        Assert.Equal(
            [RiskFields.DirectionNotNamed],
            [.. RiskFindings(Answer(FactRisk("latest quarter revenue", "$1.85 billion", "stays high"))).Select(finding => finding.Reason)]);
    }

    [Fact]
    public void AnEventRiskWithNoLevelAndAFactRiskOnANameCarryingDigitsPassAndAreComposedAPartToARisk()
    {
        var answer = Answer(FactRisk("sma200", "289.31", words: "the 200-day average"), EventRisk("regulatory"));
        var (prose, parts) = RiskFields.FromAnswer(answer);

        Assert.Empty(ClaimRules.Check(RiskFields.Section, prose, RiskFacts, [AnAdmittedDocument], null, parts).Findings);

        // One paragraph a risk, each opening on its ordinal with what would confirm it in a sentence of its own,
        // each sentence ending on the marker of the document its field named: a fact in its words crossing its level,
        // and an event after its kind.
        Assert.Equal(
            "The first risk is that demand slows [D1]. That risk would be confirmed if the 200-day average falls below 289.31, because orders lead revenue [D1]."
            + "\n\nThe second risk is that a rival launches first [D1]. That risk would be confirmed by a regulatory event: a rival's launch, because launches take share [D1].",
            prose);

        // The direction and the level set inside what confirms the risk, and why opening on the word the sentence
        // already carries, as a recorded writer answered, compose the same prose.
        var nested = FactRisk("sma200", "289.31", words: "the 200-day average")
            .Replace("\"confirm\":{\"fact\":\"sma200\",\"words\":\"the 200-day average\"},\"direction\":\"falls below\",\"level\":\"289.31\"", "\"confirm\":{\"fact\":\"sma200\",\"words\":\"the 200-day average\",\"direction\":\"falls below\",\"level\":\"289.31\"}", StringComparison.Ordinal)
            .Replace("\"why\":\"orders", "\"why\":\"because orders", StringComparison.Ordinal);

        Assert.Equal(prose, RiskFields.FromAnswer(Answer(nested, EventRisk("regulatory"))).Prose);

        // An event carrying a level is refused, and so is a risks answer that is not its fields.
        Assert.Equal(
            [RiskFields.EventWithLevel],
            [.. RiskFindings(Answer(EventRisk("legal").Replace("\"why\"", "\"level\":\"289.31\",\"why\"", StringComparison.Ordinal))).Select(finding => finding.Reason)]);
        Assert.Equal(
            ("The first risk is that demand slows [D1].", (string?)null),
            RiskFields.FromAnswer("The first risk is that demand slows [D1]."));
        Assert.Contains(
            RiskFields.NotFields,
            ClaimRules.Check(RiskFields.Section, "The first risk is that demand slows [D1].", RiskFacts, [AnAdmittedDocument], null, null).Findings.Select(finding => finding.Reason));
    }

    // The 12.6 correction of 2026-10-11: a fact is composed by the words the writer gave a reader and refused where it was
    // given none, its level written as the writer wrote it, a fraction's as its per cent; and an event follows its kind,
    // the seventh kind plainly an event, its leading article lowered, so a clause reads as one. Worked by hand.
    // see: A risk's fact is given in the words a reader reads it by beside its listing, and an event is named after its kind
    [Fact]
    public void AFactIsComposedByItsWordsAndRefusedWithNoneAndAnEventFollowsItsKind()
    {
        // A fact given no words is refused naming its listing, and the retry is told to give them.
        var bare = Assert.Single(RiskFindings(Answer(FactRisk("forward price to earnings", "20.66", words: null))));

        Assert.Equal(RiskFields.FactWithoutWords, bare.Reason);
        Assert.Equal("forward price to earnings", bare.Offending);
        Assert.Contains("words saying it plainly for a reader", RetryBrief.Do(RiskFields.FactWithoutWords), StringComparison.Ordinal);

        // A growth of 0.057553 crossed at 5.8%, its per cent, passes the level's rule and reads as written.
        Fact[] growth = [.. RiskFacts, new("sales growth on a year earlier", "0.057553", "fundamental reading")];
        var (grown, grownParts) = RiskFields.FromAnswer(Answer(FactRisk("sales growth on a year earlier", "5.8%", words: "sales growth on a year earlier")));

        Assert.Equal("The first risk is that demand slows [D1]. That risk would be confirmed if sales growth on a year earlier falls below 5.8%, because orders lead revenue [D1].", grown);
        Assert.Empty(ClaimRules.Check(RiskFields.Section, grown, growth, [AnAdmittedDocument], null, grownParts).Findings);

        // An event written as a clause opening on a capital, of the seventh kind, and one of a kind the reader learns from.
        var clause = "{\"risk\":\"that the separation is blocked\",\"riskDocument\":1,\"confirm\":{\"event\":\"A regulator blocks the separation\",\"kind\":\"other\"},\"why\":\"the deal needs approval\",\"whyDocument\":1}";

        Assert.Equal(
            "The first risk is that the separation is blocked [D1]. That risk would be confirmed by an event: a regulator blocks the separation, because the deal needs approval [D1].",
            RiskFields.FromAnswer(Answer(clause)).Prose);
        Assert.Equal(
            "The first risk is that the separation is blocked [D1]. That risk would be confirmed by an acquisition event: a regulator blocks the separation, because the deal needs approval [D1].",
            RiskFields.FromAnswer(Answer(clause.Replace("\"other\"", "\"acquisition\"", StringComparison.Ordinal))).Prose);

        // Words carrying a figure are refused, since the risks' region draws none.
        Assert.Contains(
            RiskFindings(Answer(FactRisk("latest quarter revenue", "$1.85 billion", words: "revenue above $1.8 billion"))),
            finding => finding.Reason == ClaimRules.FigureAnotherRegionOwns && finding.Offending.StartsWith("$1.8 billion", StringComparison.Ordinal));
    }

    [Fact]
    public void AnEventRiskOfAKindOutsideTheSevenIsRefusedAndTheRetryIsToldTheSeven()
    {
        var kind = Assert.Single(RiskFindings(Answer(EventRisk("macroeconomic"))));

        Assert.Equal(RiskFields.KindNotListed, kind.Reason);
        Assert.Equal("macroeconomic", kind.Offending);

        foreach (var listed in RiskFields.Kinds)
        {
            Assert.Empty(RiskFindings(Answer(EventRisk(listed))));
        }

        // The kinds section 17 states are the checker's, in its order.
        var row = System.Text.RegularExpressions.Regex.Match(Corpus.Read("docs/ARCHITECTURE.html"), "<tr><td>Risk kinds</td><td>(.*?)</td>").Groups[1].Value;

        Assert.Contains(string.Join(", ", RiskFields.Kinds[..^1]) + " and " + RiskFields.Kinds[^1], row, StringComparison.Ordinal);

        // The retry names the kind refused and the seven it may use.
        var (prose, parts) = RiskFields.FromAnswer(Answer(EventRisk("macroeconomic")));
        var brief = RetryBrief.For(RiskFields.Section, prose, RiskFacts, [AnAdmittedDocument], null, "a stored reason", parts);

        Assert.Contains($": {RiskFields.KindNotListed}. {RetryBrief.Do(RiskFields.KindNotListed)}", brief, StringComparison.Ordinal);
        Assert.Contains(string.Join(", ", RiskFields.Kinds), RetryBrief.Do(RiskFields.KindNotListed), StringComparison.Ordinal);
    }
}
