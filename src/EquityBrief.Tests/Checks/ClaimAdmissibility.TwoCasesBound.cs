using EquityBrief.Core.Research;

namespace EquityBrief.Tests.Checks;

// claim-admissibility, 18.5 on the operator's ruling of 2026-10-11: each of the two cases holds at most six sentences,
// the last saying what it needs at the next report, read off the cut the page draws the two halves by; a case of a
// seventh sentence is refused by that sentence's opening and the retry is told the bound. Worked by hand.
// see: Each of the two cases holds at most six sentences, the last saying what it needs at the next report
public partial class ClaimAdmissibility
{
    // A case of the given number of sentences, each citing the one document, the first opening on its side's words
    // and the last saying what it needs at the next report.
    static string Case(string opening, int sentences) =>
        string.Join(
            " ",
            Enumerable.Range(1, sentences).Select(at =>
                at == 1 ? opening + " rests on demand holding [D1]."
                : at == sentences ? "At the next report it needs orders to keep pace [D1]."
                : "Point " + PointNames[at] + " is that the backlog grows [D1]."));

    static readonly string[] PointNames = ["", "one", "two", "three", "four", "five", "six", "seven", "eight"];

    static (string Offending, string Reason)[] Bounded(string bull, string bear) =>
        [.. ClaimRules.Check(ClaimRules.TwoCasesSection, bull + "\n\n" + bear, [], [AnAdmittedDocument]).Findings.Select(finding => (finding.Offending, finding.Reason))];

    [Fact]
    public void EachCaseHoldsAtMostSixSentencesAndASeventhIsRefusedByItsOpening()
    {
        // Six a side, the closing sentence among them, pass.
        Assert.Empty(Bounded(Case(ClaimRules.CaseFor, 6), Case(ClaimRules.CaseAgainst, 6)));

        // A seventh in the bull case is refused by its opening, the closing sentence being the seventh.
        Assert.Equal(
            [("At the next report it needs orders to keep pace [D1].", ClaimRules.TwoCasesLongSide)],
            Bounded(Case(ClaimRules.CaseFor, 7), Case(ClaimRules.CaseAgainst, 6)));

        // Eight in the bear case and seven in the bull case: each side named once, by its seventh sentence.
        Assert.Equal(
            [("At the next report it needs orders to keep pace [D1].", ClaimRules.TwoCasesLongSide), ("Point seven is that the backlog grows [D1].", ClaimRules.TwoCasesLongSide)],
            Bounded(Case(ClaimRules.CaseFor, 7), Case(ClaimRules.CaseAgainst, 8)));

        // A draft whose sides cannot be told apart is refused for that alone, its length not read.
        Assert.Equal(
            [ClaimRules.TwoCasesWithoutSides],
            [.. ClaimRules.Check(ClaimRules.TwoCasesSection, Case("The case", 8), [], [AnAdmittedDocument]).Findings.Select(finding => finding.Reason)]);

        // And the retry is told the bound.
        Assert.Equal(
            "Keep each case to six sentences, the last saying what it needs to see at the next report, choosing the points that matter most.",
            RetryBrief.Do(ClaimRules.TwoCasesLongSide));
    }
}
