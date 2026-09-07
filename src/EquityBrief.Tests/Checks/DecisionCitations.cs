namespace EquityBrief.Tests.Checks;

// decision-resolves and no-superseded-citation.
//
// A number tells a reader nothing and forces a lookup; a name tells them the
// thing directly. A misremembered name fails to resolve here, where a
// misremembered number would resolve to the wrong decision and nobody would
// notice.
public class DecisionCitations
{
    // The placeholder in BUILD_PLAN.md's carried obligations table writes the
    // citation form out in full to describe it, so it looks like a citation and
    // names no decision. Exempted here by its exact text rather than by
    // loosening the pattern, which would exempt real mistakes too.
    const string ThePlaceholder = "<name>";

    static IReadOnlyList<CorpusFinding> Cited() =>
        Corpus.SourceAndDocuments()
            .SelectMany(file => Corpus.Citations(File.ReadAllText(file), file))
            .Where(citation => citation.Detail != ThePlaceholder)
            .ToArray();

    [Fact]
    public void EveryCitationResolvesToADecision()
    {
        var names = Corpus.DecisionNames(Corpus.Read("docs/DECISIONS.md")).ToHashSet(StringComparer.Ordinal);
        var cited = Cited();

        Assert.True(cited.Count >= 13, $"Found {cited.Count} citations, expected at least 13.");
        Assert.True(names.Count >= 80, $"Found {names.Count} decision names, expected at least 80.");

        var unresolved = cited.Where(citation => !names.Contains(citation.Detail)).ToArray();

        Assert.DoesNotContain(unresolved, _ => true);
    }

    [Fact]
    public void NoCitationResolvesToASupersededDecision()
    {
        var superseded = Corpus.SupersededNames(Corpus.Read("docs/DECISIONS.md")).ToHashSet(StringComparer.Ordinal);

        Assert.True(superseded.Count >= 5, $"Found {superseded.Count} superseded decisions, expected at least 5.");
        Assert.DoesNotContain(Cited(), citation => superseded.Contains(citation.Detail));
    }

    [Fact]
    public void NoTwoDecisionsShareAName()
    {
        var names = Corpus.DecisionNames(Corpus.Read("docs/DECISIONS.md"));

        Assert.Equal(names.Count, names.Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public void NoDecisionNameEndsInPunctuation()
    {
        // A name ending in a period is awkward to cite and invites the
        // paraphrase decision-resolves exists to reject.
        Assert.DoesNotContain(
            Corpus.DecisionNames(Corpus.Read("docs/DECISIONS.md")),
            name => name.Length > 0 && ".,;:!?".Contains(name[^1]));
    }

    [Fact]
    public void TheReaderFindsBothFormsAndRejectsAName()
    {
        // The permanent proof that the reader reads and that an unresolved name
        // would be caught.
        var inADocument = Corpus.Citations("a rule (" + "see" + ": Code owns every number) here", "d");
        var inCode = Corpus.Citations("// " + "see" + ": Code owns every number", "c");

        Assert.Equal("Code owns every number", Assert.Single(inADocument).Detail);
        Assert.Equal("Code owns every number", Assert.Single(inCode).Detail);

        var names = Corpus.DecisionNames(Corpus.Read("docs/DECISIONS.md")).ToHashSet(StringComparer.Ordinal);

        Assert.DoesNotContain("A decision nobody ever wrote", names);
    }
}
