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

    static string FactRisk(string fact, string level, string direction = RiskFields.FallsBelow) =>
        "{\"risk\":\"that demand slows\",\"riskDocument\":1,\"confirm\":{\"fact\":\"" + fact + "\"},\"direction\":\"" + direction + "\",\"level\":\"" + level + "\",\"why\":\"orders lead revenue\",\"whyDocument\":1}";

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
        var answer = Answer(FactRisk("sma200", "289.31"), EventRisk("regulatory"));
        var (prose, parts) = RiskFields.FromAnswer(answer);

        Assert.Empty(ClaimRules.Check(RiskFields.Section, prose, RiskFacts, [AnAdmittedDocument], null, parts).Findings);

        // One paragraph a risk, each opening on its ordinal with what would confirm it in a sentence of its own,
        // each sentence ending on the marker of the document its field named.
        Assert.Equal(
            "The first risk is that demand slows [D1]. That risk would be confirmed by sma200 falls below 289.31, because orders lead revenue [D1]."
            + "\n\nThe second risk is that a rival launches first [D1]. That risk would be confirmed by a rival's launch, a regulatory event, because launches take share [D1].",
            prose);

        // The direction and the level set inside what confirms the risk, and why opening on the word the sentence
        // already carries, as a recorded writer answered, compose the same prose.
        var nested = FactRisk("sma200", "289.31")
            .Replace("\"confirm\":{\"fact\":\"sma200\"},\"direction\":\"falls below\",\"level\":\"289.31\"", "\"confirm\":{\"fact\":\"sma200\",\"direction\":\"falls below\",\"level\":\"289.31\"}", StringComparison.Ordinal)
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
