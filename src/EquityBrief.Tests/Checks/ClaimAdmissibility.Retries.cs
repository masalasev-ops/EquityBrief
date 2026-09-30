using EquityBrief.Api.Reading;
using EquityBrief.Core.Facts;
using EquityBrief.Core.Research;
using EquityBrief.Worker.Research;

namespace EquityBrief.Tests.Checks;

// claim-admissibility, what a retry is told and what the last refusal says: each thing the draft before it was
// refused for named on a line of its own with what to do about it and the draft never pasted back, a section
// asked again up to three times and left out at its third retry, saying so where that retry repeated what the
// draft before it was refused for, and two cases whose sides cannot be told apart refused.
// see: A retry names each thing the check refused, and a section refused on its third retry is left out
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

        // Each figure refusal says to remove it or replace it from the facts file, a number in words that it
        // may also say what it shows with no number and no count word, and each uncited sentence to remove it
        // or cite it.
        Assert.Contains("replace it with a figure listed under Facts", RetryBrief.Do(ClaimRules.UnmatchedFigure), StringComparison.Ordinal);
        Assert.Contains("no count word such as dozens, hundreds or thousands", RetryBrief.Do(ClaimRules.UnmatchableFigure), StringComparison.Ordinal);
        Assert.Contains("end it with the marker of the listed document", RetryBrief.Do(ClaimRules.Uncited), StringComparison.Ordinal);
    }

    // One section's drafts checked one after another on the same day, as a pass's rounds check them.
    internal static async Task CheckedInTurn(TemporaryStore store, IReadOnlyList<Written> drafts, string run, int firstVersion = 1)
    {
        for (var at = 0; at < drafts.Count; at++)
        {
            Pending(store, drafts[at], firstVersion + at);
            await Checker(store).RunAsync($"{run}-{firstVersion + at}");
        }
    }

    [Fact]
    public async Task ARefusedSectionIsAskedAgainUpToItsThirdRetryAndLeftOutThereSayingWhetherTheRetryRepeated()
    {
        var poisoned = SectionNamed("a poisoned paragraph");
        var clean = SectionNamed("a clean computed paragraph");

        // The same refused thing on every draft: each refusal before the last retry kept for another, and the
        // last retry left out, saying it repeated what the draft before it was refused for, drawn by its rule.
        using var store = await WithSources();

        await CheckedInTurn(store, [.. Enumerable.Repeat(poisoned, ClaimChecker.Retries + 1)], "check-repeat");

        var repeated = Stored(store, poisoned.Section);

        Assert.Equal([.. Enumerable.Repeat(ClaimChecker.Rejected, ClaimChecker.Retries), ClaimChecker.Fallback], [.. repeated.Select(row => row.Status)]);
        Assert.StartsWith($"{ClaimChecker.RejectedOnEveryRetry}, {ClaimChecker.LastRetryRepeated}: ", repeated[^1].Reason!, StringComparison.Ordinal);
        Assert.StartsWith($"{ClaimChecker.RejectedOnEveryRetry}, {ClaimChecker.LastRetryRepeated}: {ClaimRules.UnmatchedFigure}", NameScreen.Refused(repeated[^1].Reason!), StringComparison.Ordinal);

        // A draft passing at the last retry is accepted, however many were refused before it that day.
        using var late = await WithSources();

        await CheckedInTurn(late, [.. Enumerable.Repeat(poisoned, ClaimChecker.Retries), clean], "check-late");

        Assert.Equal([.. Enumerable.Repeat(ClaimChecker.Rejected, ClaimChecker.Retries), ClaimChecker.Accepted], [.. Stored(late, poisoned.Section).Select(row => row.Status)]);

        // The words can stand inside a refused sentence, and the page still never draws that sentence, last
        // part or not, in every form of the reason, those rows written while a section had one retry among them.
        const string Sentence = "Shareholders rejected twice, then rejected on every retry, the proposed merger.";

        foreach (var reason in (string[])[
            $"{ClaimChecker.RejectedOnEveryRetry}: {ClaimRules.Uncited}: {Sentence}",
            $"{ClaimChecker.RejectedOnEveryRetry}: {ClaimRules.Uncited}: {Sentence}; {ClaimRules.UnmatchedFigure}: 5%",
            $"{ClaimChecker.RejectedOnEveryRetry}, {ClaimChecker.LastRetryRepeated}: {ClaimRules.Uncited}: {Sentence}; {ClaimRules.UnmatchedFigure}: 5%",
            $"{NameScreen.RejectedTwice}: {ClaimRules.Uncited}: {Sentence}",
            $"{NameScreen.RejectedTwice}: {ClaimRules.Uncited}: {Sentence}; {ClaimRules.UnmatchedFigure}: 5%",
            $"{NameScreen.RejectedTwice}, {NameScreen.RepeatedOnRetry}: {ClaimRules.Uncited}: {Sentence}; {ClaimRules.UnmatchedFigure}: 5%"])
        {
            Assert.DoesNotContain(Sentence, NameScreen.Refused(reason), StringComparison.Ordinal);
            Assert.Contains(ClaimRules.Uncited, NameScreen.Refused(reason), StringComparison.Ordinal);
        }

        // A last retry refused for something the draft before it was not: left out, and not said to repeat.
        using var other = await WithSources();

        await CheckedInTurn(other, [.. Enumerable.Repeat(poisoned, ClaimChecker.Retries), clean with { Prose = clean.Prose + " The close was 123456.78 on the night." }], "check-other");

        var moved = Stored(other, poisoned.Section);

        Assert.Equal(ClaimChecker.Fallback, moved[^1].Status);
        Assert.StartsWith($"{ClaimChecker.RejectedOnEveryRetry}: ", moved[^1].Reason!, StringComparison.Ordinal);
        Assert.DoesNotContain(ClaimChecker.LastRetryRepeated, moved[^1].Reason!, StringComparison.Ordinal);
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
