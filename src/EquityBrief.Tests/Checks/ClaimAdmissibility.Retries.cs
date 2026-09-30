using EquityBrief.Api.Reading;
using EquityBrief.Core.Facts;
using EquityBrief.Core.Research;
using EquityBrief.Worker.Research;

namespace EquityBrief.Tests.Checks;

// claim-admissibility, what a retry is told and what a second refusal says: each thing the first draft was
// refused for named on a line of its own with what to do about it and the draft never pasted back, a second
// draft carrying one of them left out saying it repeated it, and two cases whose sides cannot be told apart
// refused.
// see: A retry names each thing the check refused, and a second draft repeating one is left out
public partial class ClaimAdmissibility
{
    static readonly StoredDocument AnAdmittedDocument = new(
        "doc-1",
        "https://www.businesswire.com/a-release",
        "A release",
        new DateOnly(2026, 9, 1),
        new DateTimeOffset(2026, 9, 8, 21, 0, 0, TimeSpan.Zero),
        "The company reported revenue of 1.2 billion for the quarter.",
        Admissibility.Accepted);

    static readonly Fact[] OneFigure = [new("latest quarter revenue", "1200000000", "a filing")];

    [Fact]
    public void ARetryIsToldEachRefusedFigureAndSentenceOnALineOfItsOwnAndNeverTheWholeDraft()
    {
        const string Kept = "Revenue came to $1.2 billion in the quarter [D1].";
        const string Figure = "Margins reached 71.3% as costs fell [D1].";
        const string Uncited = "The market is arguing about what comes next.";

        var brief = RetryBrief.For("What the company sells", Kept + " " + Figure + " " + Uncited, OneFigure, [AnAdmittedDocument], null, "a stored reason");

        Assert.StartsWith(RetryBrief.Opening, brief, StringComparison.Ordinal);
        Assert.EndsWith(RetryBrief.Closing, brief, StringComparison.Ordinal);
        Assert.Contains($"- \"71.3%\" in the sentence \"{Figure}\": {ClaimRules.UnmatchedFigure}. {RetryBrief.Do(ClaimRules.UnmatchedFigure)}", brief, StringComparison.Ordinal);
        Assert.Contains($"- the sentence \"{Uncited}\": {ClaimRules.Uncited}. {RetryBrief.Do(ClaimRules.Uncited)}", brief, StringComparison.Ordinal);

        // One line for each refused thing and none for what passed, so the draft is never pasted back.
        Assert.Equal(2, brief.Split('\n').Count(line => line.StartsWith("- ", StringComparison.Ordinal)));
        Assert.DoesNotContain(Kept, brief, StringComparison.Ordinal);

        // Each figure refusal says to remove it or replace it from the facts file, and each uncited sentence
        // to remove it or cite it.
        Assert.Contains("replace it with a figure listed under Facts", RetryBrief.Do(ClaimRules.UnmatchedFigure), StringComparison.Ordinal);
        Assert.Contains("end it with the marker of the listed document", RetryBrief.Do(ClaimRules.Uncited), StringComparison.Ordinal);
    }

    [Fact]
    public async Task ASecondDraftRepeatingWhatTheFirstWasRefusedForIsLeftOutSayingSoAndOneRefusedForSomethingElseIsNot()
    {
        using var store = await WithSources();

        var poisoned = SectionNamed("a poisoned paragraph");

        // The same refused thing twice: left out, saying the retry repeated it, drawn by its rule.
        Pending(store, poisoned, 1);
        await Checker(store).RunAsync("check-repeat-first");
        Pending(store, poisoned, 2);
        await Checker(store).RunAsync("check-repeat-second");

        var repeated = Stored(store, poisoned.Section);

        Assert.Equal([ClaimChecker.Rejected, ClaimChecker.Fallback], [.. repeated.Select(row => row.Status)]);
        Assert.StartsWith($"{ClaimChecker.RejectedTwice}, {ClaimChecker.RepeatedOnRetry}: ", repeated[1].Reason!, StringComparison.Ordinal);
        Assert.StartsWith($"{ClaimChecker.RejectedTwice}, {ClaimChecker.RepeatedOnRetry}: {ClaimRules.UnmatchedFigure}", NameScreen.Refused(repeated[1].Reason!), StringComparison.Ordinal);

        // The word can stand inside a refused sentence, and the page still never draws that sentence, last
        // part or not, in either form of the reason.
        const string Sentence = "Shareholders rejected twice the proposed merger.";

        foreach (var reason in (string[])[
            $"{ClaimChecker.RejectedTwice}: {ClaimRules.Uncited}: {Sentence}",
            $"{ClaimChecker.RejectedTwice}: {ClaimRules.Uncited}: {Sentence}; {ClaimRules.UnmatchedFigure}: 5%",
            $"{ClaimChecker.RejectedTwice}, {ClaimChecker.RepeatedOnRetry}: {ClaimRules.Uncited}: {Sentence}; {ClaimRules.UnmatchedFigure}: 5%"])
        {
            Assert.DoesNotContain(Sentence, NameScreen.Refused(reason), StringComparison.Ordinal);
            Assert.Contains(ClaimRules.Uncited, NameScreen.Refused(reason), StringComparison.Ordinal);
        }

        // A second draft refused for something the first was not: left out, and not said to repeat.
        using var other = await WithSources();

        var clean = SectionNamed("a clean computed paragraph");

        Pending(other, poisoned, 1);
        await Checker(other).RunAsync("check-other-first");
        Pending(other, clean with { Prose = clean.Prose + " The close was 123456.78 on the night." }, 2);
        await Checker(other).RunAsync("check-other-second");

        var moved = Stored(other, poisoned.Section);

        Assert.Equal([ClaimChecker.Rejected, ClaimChecker.Fallback], [.. moved.Select(row => row.Status)]);
        Assert.StartsWith($"{ClaimChecker.RejectedTwice}: ", moved[1].Reason!, StringComparison.Ordinal);
        Assert.DoesNotContain(ClaimChecker.RepeatedOnRetry, moved[1].Reason!, StringComparison.Ordinal);
    }

    [Fact]
    public void TwoCasesWhoseSidesCannotBeToldApartAreRefusedNamingWhatIsMissing()
    {
        static IReadOnlyList<ClaimFinding> Sides(string prose) =>
            [.. ClaimRules.Check(ClaimRules.TwoCasesSection, prose, [], [AnAdmittedDocument]).Findings.Where(finding => finding.Reason == ClaimRules.TwoCasesWithoutSides)];

        // No sentence opening on the bear case.
        var noBear = Assert.Single(Sides("The bull case is that demand keeps growing [D1]. Orders rose [D1]."));

        Assert.Equal($"no sentence opens on \"{ClaimRules.CaseAgainst}\"", noBear.Offending);

        // A first sentence opening on neither case.
        var noBull = Assert.Single(Sides("Demand keeps growing [D1].\n\nThe bear case is that orders slow [D1]."));

        Assert.Equal($"the first sentence does not open on \"{ClaimRules.CaseFor}\"", noBull.Offending);

        // Both cases in the shape asked for, the bear case in a paragraph of its own or inside the first.
        Assert.Empty(Sides("The bull case is that demand keeps growing [D1].\n\nThe bear case is that orders slow [D1]."));
        Assert.Empty(Sides("The bull case is that demand keeps growing [D1]. The bear case is that orders slow [D1]."));

        // The page cuts the halves by the same rule.
        Assert.Null(ClaimRules.CaseSides(ClaimRules.Rows("Demand keeps growing [D1].\n\nThe bear case is that orders slow [D1].")));
        Assert.Single(ClaimRules.CaseSides(ClaimRules.Rows("The bull case is growth [D1].\n\nThe bear case is a slowdown [D1]."))!.Value.Against);
    }
}
