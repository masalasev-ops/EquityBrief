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
    internal static IReadOnlyList<CorpusFinding> Cited() =>
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
    internal static IReadOnlyList<CorpusFinding> CitedOutsideTheRecords()
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
    public void ACitationInsideARulesFileIsReadAndIsLostIfTheRulesDirectoryLeavesThePopulation()
    {
        // The permanent proof that widening the population did something. The
        // rules files carry CLAUDE.md's own text, citations included, so a check
        // reading a fixed list of eight documents would stop reaching them the
        // moment the text moved, with nothing going red to say so. That is the
        // shape this harness refuses: a check that narrows its own scope keeps
        // passing.
        //
        // Over a constructed file rather than over the real ones, so it asserts
        // the reader and the population rather than today's contents.
        var rules = Corpus.Rules;

        Assert.All(rules, path => Assert.StartsWith(".claude/rules/", path, StringComparison.Ordinal));

        // Every rules file is in the population the citation readers walk, and
        // in the one `pinned-constants` and `changelog-reconciles` treat as specs;
        // that each of those checks reads it is asserted on the check below.
        var documents = Corpus.Documents;
        var reached = Corpus.SourceAndDocuments().ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var path in rules)
        {
            Assert.Contains(path, documents);
            Assert.Contains(path, Corpus.SpecsAndRules);
            Assert.Contains(
                Path.Combine(Repository.Root, path.Replace('/', Path.DirectorySeparatorChar)),
                reached);
        }

        // The reader finds a citation in a rules file exactly as it finds one in
        // CLAUDE.md, and the name it yields is what `decision-resolves` resolves.
        const string constructed =
            "**A rule moved here.** It still cites its decision (" + "see" + ": Code owns every number).";

        var found = Assert.Single(Corpus.Citations(constructed, ".claude/rules/corpus-edits.md"));

        Assert.Equal("Code owns every number", found.Detail);
        Assert.Contains(found.Detail, Corpus.DecisionNames(Corpus.Read("docs/DECISIONS.md")));

        // And the other direction, which is what makes the widening assertable:
        // over a population built the way it was before this pass, being the five
        // specs and the three records, the same citation is reached by nothing.
        var beforeTheWidening = Corpus.Specs.Concat(Corpus.Records).ToArray();

        Assert.DoesNotContain(".claude/rules/corpus-edits.md", beforeTheWidening);
        Assert.All(rules, path => Assert.DoesNotContain(path, beforeTheWidening));

        // A citation that a narrowed population cannot see is a citation nothing
        // resolves, which is the failure the widening exists to prevent. Asserted
        // as the count it is: eight files before, twelve after, and the four are
        // the ones carrying the moved text.
        Assert.Equal(8, beforeTheWidening.Length);
        Assert.Equal(beforeTheWidening.Length + rules.Count, documents.Count);
    }

    [Fact]
    public void EveryCheckOverTheWidenedPopulationsReadsEveryRulesFile()
    {
        var rules = Corpus.Rules;

        var pinned = PinnedConstants.Specs();

        Assert.All(rules, path => Assert.True(pinned.ContainsKey(path), $"pinned-constants does not read {path}."));

        foreach (var path in rules)
        {
            Assert.True(ChangelogReconciles.Numstat("0\t1\t" + path).DeletedFromSpec, $"changelog-reconciles does not count a deletion from {path}.");
            Assert.False(ChangelogReconciles.Numstat("1\t0\t" + path).DeletedFromSpec, $"changelog-reconciles counts an addition to {path} as a deletion.");
        }

        Assert.False(ChangelogReconciles.Numstat("0\t1\tdocs/PROGRESS.md").DeletedFromSpec);

        // The citations each rules file carries, read from the file on its own,
        // are among what `decision-resolves`, `no-superseded-citation` and
        // `obligation-reconciles` read.
        static string Key(CorpusFinding citation) =>
            $"{Path.GetFileName(citation.File)}:{citation.Line}:{citation.Detail}";

        var decisions = rules.SelectMany(path => Corpus.Citations(Corpus.Read(path), path)).Select(Key).ToArray();
        var obligations = rules.SelectMany(path => Corpus.Citations(Corpus.Obligation, Corpus.Read(path), path)).Select(Key).ToArray();

        Assert.True(decisions.Length >= 1, $"The rules files carry {decisions.Length} decision citations, expected at least 1.");
        Assert.True(obligations.Length >= 1, $"The rules files carry {obligations.Length} obligation citations, expected at least 1.");

        bool InRules(CorpusFinding citation) =>
            citation.File.Replace('\\', '/').Contains("/.claude/rules/", StringComparison.Ordinal);

        Assert.Equal(decisions.Order(StringComparer.Ordinal), DecisionCitations.Cited().Where(InRules).Select(Key).Order(StringComparer.Ordinal));
        Assert.Equal(decisions.Order(StringComparer.Ordinal), DecisionCitations.CitedOutsideTheRecords().Where(InRules).Select(Key).Order(StringComparer.Ordinal));
        Assert.Equal(obligations.Order(StringComparer.Ordinal), ObligationReconciles.Cited().Where(InRules).Select(Key).Order(StringComparer.Ordinal));
    }

    [Fact]
    public void ARulesDirectoryReadBelowItsFloorRefusesRatherThanReturningWhatItFound()
    {
        using var directory = new TemporaryDirectory();

        Assert.Throws<InvalidOperationException>(() => Corpus.RulesIn(Path.Combine(directory.Path, "absent")));

        for (var count = 0; count < Corpus.RulesFloor; count++)
        {
            var refusal = Assert.Throws<InvalidOperationException>(() => Corpus.RulesIn(directory.Path));

            Assert.Contains($"Read {count} rules files", refusal.Message, StringComparison.Ordinal);

            File.WriteAllText(Path.Combine(directory.Path, $"rule-{count}.md"), "---\npaths: docs/**\n---\n");
        }

        File.WriteAllText(Path.Combine(directory.Path, "not-a-rule.txt"), "");

        Assert.Equal(
            Enumerable.Range(0, Corpus.RulesFloor).Select(at => $".claude/rules/rule-{at}.md"),
            Corpus.RulesIn(directory.Path));
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
