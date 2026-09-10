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

    // Everything decision-resolves reads, less the three records.
    //
    // A superseded citation in a spec or in code is a live pointer to a dead
    // rule. In a record it is a dated statement of what the corpus held on the
    // day it was written, and PROGRESS.md is append only for that reason: a
    // record is corrected by a new dated entry and never by editing the old one,
    // so a check that refused such a line would forbid the corpus from ever
    // superseding a decision an entry had cited.
    //
    // Narrowed at 5.0, when the trailing stop decision was superseded and the
    // phase 4 sign-off's own citation of it turned this red. The scope is stated
    // in numbers and the exclusion is asserted to be doing work rather than
    // being a filter that matches nothing, which is the drift this file already
    // carries one story about.
    static IReadOnlyList<CorpusFinding> CitedOutsideTheRecords()
    {
        var records = Corpus.Records
            .Select(record => Path.Combine(Repository.Root, record.Replace('/', Path.DirectorySeparatorChar)))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        return [.. Cited().Where(citation => !records.Contains(citation.File))];
    }

    [Fact]
    public void NoCitationInASpecOrInCodeResolvesToASupersededDecision()
    {
        var superseded = Corpus.SupersededNames(Corpus.Read("docs/DECISIONS.md")).ToHashSet(StringComparer.Ordinal);
        var scanned = CitedOutsideTheRecords();

        Assert.True(superseded.Count >= 5, $"Found {superseded.Count} superseded decisions, expected at least 5.");
        Assert.True(scanned.Count >= 100, $"Scanned {scanned.Count} citations outside the records, expected at least 100.");

        Assert.DoesNotContain(scanned, citation => superseded.Contains(citation.Detail));
    }

    [Fact]
    public void TheRecordsAreExcludedByNameAndTheExclusionIsDoingWork()
    {
        // The other half of the narrowing above. A filter that matches nothing
        // reads as a rule and behaves as a comment, so what it removes is
        // asserted rather than assumed: the records do carry a citation of a
        // superseded decision, and every one of them sits in a record.
        var superseded = Corpus.SupersededNames(Corpus.Read("docs/DECISIONS.md")).ToHashSet(StringComparer.Ordinal);

        var records = Corpus.Records
            .Select(record => Path.Combine(Repository.Root, record.Replace('/', Path.DirectorySeparatorChar)))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var removed = Cited()
            .Where(citation => superseded.Contains(citation.Detail))
            .ToArray();

        Assert.True(
            removed.Length >= 1,
            $"Found {removed.Length} superseded citations anywhere in the corpus, expected at least 1. " +
            "With none, this exclusion is a filter that matches nothing.");

        Assert.All(removed, citation => Assert.Contains(citation.File, records));
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

    [Fact]
    public void QuotedPriorTextInTheChangelogIsNotACitation()
    {
        // The permanent proof for the reader's one file-dependent rule, over
        // constructed input. A changelog entry quotes what a document said, and
        // a citation inside that quotation is being reported rather than made.
        // Superseding a decision whose citation the changelog has to quote would
        // otherwise force either a paraphrase of text the format requires
        // verbatim or an edit to an append-only record.
        const string line = "> the paragraph ended (" + "see" + ": The earnings trade is a second book)";

        Assert.Empty(Corpus.Citations(line, "docs/CHANGELOG.md"));

        // Three counter-readings, so the rule is as narrow as it says it is. The
        // same line in any other document is a citation; an unquoted line in the
        // changelog is a citation; and a backslash path reaches the same rule,
        // because Repository hands paths over in the platform's own form.
        Assert.Single(Corpus.Citations(line, "docs/ARCHITECTURE.html"));
        Assert.Single(Corpus.Citations(line[2..], "docs/CHANGELOG.md"));
        Assert.Empty(Corpus.Citations(line, @"E:\a\docs\CHANGELOG.md"));
    }
}
