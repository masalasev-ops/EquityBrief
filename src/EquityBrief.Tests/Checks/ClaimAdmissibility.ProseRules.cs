using EquityBrief.Api.Reading;
using EquityBrief.Core.Research;
using EquityBrief.Worker.Research;

namespace EquityBrief.Tests.Checks;

// claim-admissibility, the corpus's two prose rules held to a written section: a sentence
// carrying the banned word in any form, or an em dash, is refused as a sentence naming no
// document is, the second draft carrying it too is left out, and the page names the rule
// without drawing the sentence. The word and the dash are assembled from their parts, as
// banned-prose assembles them, so this file is not an occurrence of either.
public partial class ClaimAdmissibility
{
    static readonly string BannedWord = "hon" + "est";

    static readonly string EmDash = ((char)0x2014).ToString();

    // The clean researched paragraph, its first sentence opened with the given words, which
    // leaves every citation and figure it passes on as they were.
    static Written OpenedWith(string opening)
    {
        var clean = SectionNamed("a clean researched paragraph");

        return clean with { Prose = opening + clean.Prose };
    }

    [Fact]
    public async Task ASentenceCarryingTheBannedWordInAnyFormIsRefusedTwiceLeftOutAndNamedWithoutTheWord()
    {
        using var store = await WithSources();

        // A form of the word rather than the word itself, capitalised, so the rule is shown to
        // read any form as the corpus's check does.
        var carrying = OpenedWith(char.ToUpperInvariant(BannedWord[0]) + BannedWord[1..] + "ly, ");
        var sentence = ClaimRules.Sentences(carrying.Prose)[0].Text;

        await AssertRefusedTwiceAndNamed(store, carrying, sentence, ClaimRules.ClaimOfCandour, BannedWord);
    }

    [Fact]
    public async Task ASentenceCarryingAnEmDashIsRefusedTwiceLeftOutAndNamedWithoutTheDash()
    {
        using var store = await WithSources();

        var carrying = OpenedWith("In short" + EmDash + " ");
        var sentence = ClaimRules.Sentences(carrying.Prose)[0].Text;

        await AssertRefusedTwiceAndNamed(store, carrying, sentence, ClaimRules.EmDashed, EmDash);
    }

    static async Task AssertRefusedTwiceAndNamed(TemporaryStore store, Written carrying, string sentence, string rule, string carried)
    {
        Pending(store, carrying, 1);
        await Checker(store).RunAsync("check-prose-rule-first");

        var first = Assert.Single(Stored(store, carrying.Section));

        // Refused for that sentence alone, since the paragraph it was made from is accepted, and
        // recorded with the draft's own sentence, as a sentence naming no document is.
        Assert.Equal(ClaimChecker.Rejected, first.Status);
        Assert.Equal($"{rule}: {sentence}", first.Reason);

        // Asked for again up to its last retry, and the last draft carrying it too is left out.
        await CheckedInTurn(store, [.. Enumerable.Repeat(carrying, ClaimChecker.Retries)], "check-prose-rule-again", firstVersion: 2);

        var rows = Stored(store, carrying.Section);

        Assert.Equal([.. Enumerable.Repeat(ClaimChecker.Rejected, ClaimChecker.Retries), ClaimChecker.Fallback], [.. rows.Select(row => row.Status)]);
        Assert.StartsWith(ClaimChecker.RejectedOnEveryRetry, rows[^1].Reason!, StringComparison.Ordinal);

        // The name page names the rule and never draws the sentence carrying the word or the dash.
        var drawn = NameScreen.Refused(rows[^1].Reason!);

        Assert.Contains(rule, drawn, StringComparison.Ordinal);
        Assert.DoesNotContain(carried, drawn, StringComparison.OrdinalIgnoreCase);
    }
}
