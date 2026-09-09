namespace EquityBrief.Tests.Checks;

// decision-resolves and no-superseded-citation.
//
// A number tells a reader nothing and forces a lookup; a name tells them the
// thing directly. A misremembered name fails to resolve here, where a
// misremembered number would resolve to the wrong decision and nobody would
// notice.
public class DecisionCitations
{
    // This carried an exemption for a placeholder in BUILD_PLAN.md's carried
    // obligations table, which wrote the citation form out in full to describe
    // it and so looked like a citation naming no decision. The placeholder is
    // gone from the corpus and the filter matched nothing, which is drift of
    // exactly the class this harness refuses. It is removed here rather than
    // later because this is the file the obligation citation reader is modelled
    // on, and copying it forward would have copied the exemption's shape.
    //
    // The passages describing a citation form now name a real decision and a
    // real obligation instead, so they resolve rather than needing exempting.
    static IReadOnlyList<CorpusFinding> Cited() =>
        Corpus.SourceAndDocuments()
            .SelectMany(file => Corpus.Citations(File.ReadAllText(file), file))
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
