using System.Globalization;
using System.Text.RegularExpressions;
using EquityBrief.Api.Reading;
using EquityBrief.Core.Candidates;
using EquityBrief.Core.Ladders;
using EquityBrief.Core.Providers;
using EquityBrief.Core.Research;
using EquityBrief.Core.Returns;
using EquityBrief.Core.Rules;
using EquityBrief.Core.Shortlist;
using EquityBrief.Core.Spending;
using EquityBrief.Core.Swings;
using EquityBrief.Core.Volume;
using EquityBrief.Data;
using EquityBrief.Tests.Harness;
using EquityBrief.Worker.Bars;
using EquityBrief.Worker.Research;
using EquityBrief.Worker.Rules;

namespace EquityBrief.Tests.Checks;

// pinned-constants: the versions the specs state against the build files, section
// 17's figures against what holds them, and the listed restatements against the code.
//
// CHANGELOG.md and PROGRESS.md are records and are not read; a decision that stands is.
public class PinnedConstants
{
    const string Framework = @"net[0-9]+\.[0-9]+";
    const string Band = @"[0-9]+\.[0-9]+\.[0-9]xx";

    // The specs, which is where a version may be stated. Records are exempt for
    // the reason given above. Read once so both checks scan the same population
    // and neither can quietly narrow to the file that happens to state it.
    //
    // The rules files are in it, because a constant stated in one is a constant
    // stated in a spec: they carry CLAUDE.md's own text, and a version that moved
    // in the file that loads for a session working in `tools/` is exactly the one
    // that would go unread.
    internal static IReadOnlyDictionary<string, string> Specs() =>
        Corpus.SpecsAndRules.ToDictionary(spec => spec, Corpus.Read, StringComparer.Ordinal);

    [Fact]
    public void EveryFigureSectionSeventeenStatesInDigitsIsHeldByTheCodeOrNamedForWhatItIs()
    {
        var limits = ArchitectureTables.In(File.ReadAllText(Repository.Architecture)).Single(table => table.Heading == Scope.LimitsTable);
        var stated = StatedFigures.InValues(limits);
        var census = SectionSeventeen(limits, Corpus.Read("docs/PROGRESS.md"), stated);

        Assert.Empty(StatedFigures.Unheld(stated, census));
    }

    static IReadOnlyList<HeldFigure> SectionSeventeen(
        ArchitectureTable limits,
        string progress,
        IReadOnlyList<(string Row, string Stated)> stated)
    {
        string Value(string row) => limits.Body.Single(cells => cells.Count > 1 && cells[0] == row)[1];

        decimal Figure(string row, int at) => StatedFigures.ValueOf(stated.Where(figure => figure.Row == row).ElementAt(at).Stated);

        bool Recorded(string figure) => Regex.IsMatch(progress, $@"(?<![\d.,]){Regex.Escape(figure)}(?![\d]|[.,]\d)");

        bool Landed(string checkpoint) => Regex.IsMatch(progress, $@"^### {Regex.Escape(checkpoint)} - ", RegexOptions.Multiline);

        const string Zero = "a limit of zero, which nightly-cost counts over a recorded night";
        const string Measured = "a measurement the record carries";
        const string Checkpoint = "the checkpoint that measured it";

        const string Calls = "Model calls in the nightly run";
        const string Clock = "Nightly wall clock, at index size";
        const string Retry = "Per-request timeout and the night's deadline";
        const string Budget = "Weighted-call budget";
        const string QueueRow = "Overnight queue";
        const string Admissible = "Source admissibility";
        const string Significance = "Significance threshold";
        const string Versions = "Rule versions scored at once";

        return
        [
            new(Calls, "0", 0, Zero),
            new("Per-name network calls in the nightly run", "0", 0, Zero),
            new(Clock, "5", (decimal)RetryPolicy.WallClock.TotalMinutes, "RetryPolicy.WallClock in minutes"),
            new(Clock, "500", null, "the index's nominal size, named as the figure the night does not read", () => Value(Clock).Contains("rather than as the literal 500", StringComparison.Ordinal)),
            new(Clock, "503", null, Measured, () => Recorded("503")),
            new(Retry, "3", RetryPolicy.Standard.Attempts, "RetryPolicy.Standard.Attempts"),
            new(Retry, "2", (decimal)RetryPolicy.Standard.WaitBefore(2).TotalSeconds, "the wait before the second attempt"),
            new(Retry, "4", (decimal)RetryPolicy.Standard.WaitBefore(3).TotalSeconds, "the wait before the third attempt"),
            new(Retry, "30", (decimal)RetryPolicy.Standard.Timeout.TotalSeconds, "RetryPolicy.Standard.Timeout in seconds"),
            new(Retry, "15", (decimal)RetryPolicy.Standard.Deadline.TotalMinutes, "RetryPolicy.Standard.Deadline in minutes"),
            new("Waiting on another writer", "600", StoreConnection.WaitSeconds, "StoreConnection.WaitSeconds"),
            new(Budget, "100,000", ProviderWeights.DailyAllowance, "ProviderWeights.DailyAllowance"),
            new(Budget, "100", ProviderWeights.BulkEndOfDay, "ProviderWeights.BulkEndOfDay"),
            new(Budget, "1", ProviderWeights.HistoricalPerTicker, "ProviderWeights.HistoricalPerTicker"),
            new(Budget, "10", ProviderWeights.Fundamentals, "ProviderWeights.Fundamentals"),
            new(Budget, "5", ProviderWeights.News, "ProviderWeights.News"),
            new(Budget, "1", ProviderWeights.EarningsCalendar, "ProviderWeights.EarningsCalendar"),
            new("Bar history kept", "1", BarFetcher.RetentionYears, "BarFetcher.RetentionYears"),
            new("Backfill", "5", Backfill.RetryNights, "Backfill.RetryNights"),
            new("Backfill", "7", Backfill.WeeklyRetryDays, "Backfill.WeeklyRetryDays"),
            new("Level window", "60", VolumeProfileSeries.Window, "VolumeProfileSeries.Window"),
            new("Swing lookback", "3", SwingSeries.Lookback, "SwingSeries.Lookback"),
            new("Tranches, exits", "3", LadderSeries.MostTranches, "LadderSeries.MostTranches"),
            new("Tranches, exits", "5", LadderSeries.MostExits, "LadderSeries.MostExits"),
            new("Earnings horizon", "20", ShortlistSeries.EarningsHorizonSessions, "ShortlistSeries.EarningsHorizonSessions"),
            new("List display", "20", TonightScreen.Drawn, "TonightScreen.Drawn"),
            new(QueueRow, "1", OvernightQueue.DefaultHours, "OvernightQueue.DefaultHours"),
            new(QueueRow, "6.10", null, Checkpoint, () => Landed("6.10")),
            new(QueueRow, "503", null, Measured, () => Recorded("503")),
            new(QueueRow, "43", Math.Round(Figure(QueueRow, 2) * Figure(QueueRow, 4) / 60m), "the names at the slowest pass, in minutes"),
            new(QueueRow, "5.13", null, Measured, () => Recorded("5.13")),
            new("Research passes per name per open", "1", null, "a count the runner keeps by refusing a second pass on the same day, which no constant holds", () => FixtureExpectations.Reach.Covers(Scope.LimitsTable, "Research passes per name per open")),
            new("Research staleness triggers", "90", Staleness.BaselineDays, "Staleness.BaselineDays"),
            new("Spend cap", "10", SpendCaps.DefaultDay, "SpendCaps.DefaultDay"),
            new("Spend cap", "50", SpendCaps.DefaultMonth, "SpendCaps.DefaultMonth"),
            new("Theme search parameters", "3", ThemeSearch.ResultsASite, "ThemeSearch.ResultsASite"),
            new("Theme search parameters", "10", ThemeSearch.MostPages, "ThemeSearch.MostPages"),
            new("Theme search parameters", "5", ThemeSearch.MentionsPerTenThousand, "ThemeSearch.MentionsPerTenThousand"),
            new("Theme search parameters", "10,000", ThemeSearch.CountedOver, "ThemeSearch.CountedOver"),
            new("Theme search parameters", "30,000", ThemeSearch.CharactersAPage, "ThemeSearch.CharactersAPage"),
            new(Admissible, "6.3", null, Checkpoint, () => Landed("6.3")),
            new(Admissible, "3", Admissibility.SentencesInAParagraph, "Admissibility.SentencesInAParagraph"),
            new(Admissible, "40", Admissibility.WordsInAParagraph, "Admissibility.WordsInAParagraph"),
            new(Admissible, "1", Admissibility.RiskWarningsThatRefuse, "Admissibility.RiskWarningsThatRefuse"),
            new(Admissible, "1", Admissibility.InvitationsBesideAProduct, "Admissibility.InvitationsBesideAProduct"),
            new(Admissible, "411", null, Measured, () => Recorded("411")),
            new("Setup resolution", "63", ForwardReturnSeries.SetupSessionCap, "ForwardReturnSeries.SetupSessionCap"),
            new("Minimum resolved setups", "250", ReasonVerdict.MinimumResolved, "ReasonVerdict.MinimumResolved"),
            new("Minimum resolved setups", "60", ReasonVerdict.MinimumSessions, "ReasonVerdict.MinimumSessions"),
            new("Minimum resolved setups", "400", ReasonVerdict.MinimumBeforeALiveReasonIsRetired, "ReasonVerdict.MinimumBeforeALiveReasonIsRetired"),
            new("Family size and correction", "8", CandidateFamily.Maximum, "CandidateFamily.Maximum"),
            new(Significance, "0.05", (decimal)ReasonVerdict.Significance, "ReasonVerdict.Significance"),
            new(Significance, "6", ReasonVerdict.LiveFamily, "ReasonVerdict.LiveFamily"),
            new(Significance, "8", CandidateFamily.Maximum, "CandidateFamily.Maximum"),
            new(Versions, "2", RuleVersions.MostOfTheMergeDistance, "RuleVersions.MostOfTheMergeDistance"),
            new(Versions, "4", RuleVersions.MostPerRule, "RuleVersions.MostPerRule"),
            new(Versions, "3", LadderRules.All.Count - 1, "the ladder rules other than the merge distance"),
            new(Versions, "14", RuleVersions.MostAtOnce, "RuleVersions.MostAtOnce"),
        ];
    }

    [Fact]
    public void EveryRestatementOfAFigureTheCodeHoldsAgreesWithItInEveryDocumentThatStatesIt()
    {
        var documents = StatedFigures.Documents.ToDictionary(
            path => path,
            path => StatedFigures.Prose(path, Corpus.Read(path)),
            StringComparer.Ordinal);

        var restatements = Restatements();
        var counted = restatements.Sum(restatement => restatement.Statements.Values.Sum());

        Assert.True(counted >= 70, $"Counted {counted} restatements in advance, expected at least 70.");
        Assert.Empty(StatedFigures.Disagreeing(documents, restatements));
    }

    static IReadOnlyList<Restatement> Restatements()
    {
        // The bound's arithmetic, from the figures section 17's row states.
        var bound = string.Join(" ", ArchitectureTables.In(File.ReadAllText(Repository.Architecture))
            .Single(table => table.Heading == Scope.LimitsTable)
            .Body.Single(cells => cells.Count > 2 && cells[0] == "Rule versions scored at once"));

        decimal Read(string pattern) =>
            decimal.Parse(Regex.Match(bound, pattern).Groups[1].Value, CultureInfo.InvariantCulture);

        var night = Read(@"(\d+) seconds over the steps before the close");
        var levels = Read(@"a level stage of (\d+) seconds");
        var ladders = Read(@"a ladder stage of (\d+) at");
        var names = Read(@"a ladder stage of \d+ at (\d+) names");
        var worst = (decimal)RuleVersions.WorstCaseSeconds((double)levels, (double)ladders);

        const string Architecture = "docs/ARCHITECTURE.html";
        const string Schema = "docs/SCHEMA.md";
        const string Plan = "docs/BUILD_PLAN.md";
        const string Runbook = "docs/RUNBOOK.md";
        const string Decisions = "docs/DECISIONS.md";

        static Dictionary<string, int> In(params (string Path, int Count)[] counts) =>
            counts.ToDictionary(count => count.Path, count => count.Count, StringComparer.Ordinal);

        return
        [
            new("the merge distance's cap", RuleVersions.MostOfTheMergeDistance,
                [@"{N}\s+(?:windows|rows)\s+of\s+the\s+merge\s+distance(?!\s+would\b)"],
                In((Architecture, 1), (Schema, 1), (Plan, 2), (Runbook, 1), (Decisions, 2))),
            new("each other rule's cap", RuleVersions.MostPerRule,
                [@"{N}\s+of\s+each\s+(?:of\s+the\s+)?other\b", @"{N}\s+of\s+it\s+would\s+take\s+the\s+night\s+past\s+the\s+deadline"],
                In((Architecture, 2), (Schema, 1), (Plan, 2), (Runbook, 1), (Decisions, 1))),
            new("the windows open at once", RuleVersions.MostAtOnce,
                [@"among\s+them,\s+(?:which\s+is\s+)?{N}\s+at\s+once", @"{N}\s+is\s+the\s+sum\s+of\s+the\s+caps"],
                In((Architecture, 2), (Plan, 2))),
            new("the ladder rules", LadderRules.All.Count,
                [@"{N}\s+ladder\s+rules\s+the\s+build\s+carries", @"\bthe\s+{N}\s+rules\s+and\s+the\s+names"],
                In((Schema, 1), (Runbook, 1))),
            new("the ladder rules other than the merge distance", LadderRules.All.Count - 1,
                [@"the\s+other\s+{N}\s+ladder\s+rules"],
                In((Architecture, 1))),
            new("the night the bound is measured on, in seconds", night,
                [@"{N}\s+seconds\s+over\s+(?:the\s+steps\s+before\s+the\s+close|steps\s+\d+\s+to\s+\d+)", @"on\s+a\s+{N}\s+second\s+night"],
                In((Architecture, 1), (Plan, 2), (Decisions, 1))),
            new("that night's level stage, in seconds", levels,
                [@"level\s+stage\s+(?:of\s+)?{N}\s+seconds"],
                In((Architecture, 1), (Plan, 2))),
            new("that night's ladder stage, in seconds", ladders,
                [@"ladder\s+stage\s+(?:of\s+)?{N}\s+at\b", @"every\s+other\s+version\s+{N}\b", @"replay\s+the\s+ladder\s+stage\s+at\s+{N}\b"],
                In((Architecture, 2), (Plan, 2), (Decisions, 1))),
            new("the names that night ran over", names,
                [@"ladder\s+stage\s+(?:of\s+)?\d+\s+at\s+{N}\s+names"],
                In((Architecture, 1), (Plan, 2))),
            new("a merge distance version's replay, in seconds", levels + ladders,
                [@"merge\s+distance\s+version\s+costs\s+{N}\s+seconds", @"replay\s+the\s+level\s+stage\s+at\s+{N}\s+seconds"],
                In((Architecture, 1), (Decisions, 1))),
            new("the fullest register's replays, in seconds", worst,
                [@"(?:others|ladder\s+replays),\s+{N}\s+seconds", @"adds\s+{N}\s+seconds"],
                In((Architecture, 1), (Plan, 1), (Decisions, 1))),
            new("the night at the fullest register, in seconds", night + worst,
                [@"(?:puts\s+the\s+night\s+at|sits\s+at)\s+{N}\b"],
                In((Architecture, 1), (Plan, 2))),
            new("the night's deadline, in seconds", (decimal)RetryPolicy.Standard.Deadline.TotalSeconds,
                [@"(?:against|inside)\s+(?:a|the)\s+deadline\s+of\s+{N}\b"],
                In((Architecture, 1), (Plan, 2), (Decisions, 1))),
            new("the merge distance versions the fullest register replays", RuleVersions.MostOfTheMergeDistance - 1,
                [@"replays\s+{N}\s+merge\s+distance\s+versions?\b", @"replays\s+{N}\s+of\s+the\s+first\b", @"\bat\s+{N}\s+level\s+replays?\s+and\b"],
                In((Architecture, 1), (Plan, 1), (Decisions, 1))),
            new("the other versions the fullest register replays", (LadderRules.All.Count - 1) * (RuleVersions.MostPerRule - 1),
                [@"and\s+{N}\s+others,", @"and\s+{N}\s+of\s+the\s+second\b", @"and\s+{N}\s+ladder\s+replays,\s+\d+\s+seconds"],
                In((Architecture, 1), (Plan, 1), (Decisions, 1))),
            new("the candidate family's maximum", CandidateFamily.Maximum,
                [@"maximum\s+family\s+size\s+of\s+{N}\b", @"the\s+other,\s+at\s+most\s+{N}\b", @"family\s+of\s+{N},\s+needs", @"family\s+is\s+at\s+most\s+{N}\b", @"{N}\s+is\s+the\s+size\s+at\s+which"],
                In((Architecture, 4), (Plan, 4), (Decisions, 5))),
            new("the registration past the family's maximum", CandidateFamily.Maximum + 1,
                [@"\ba\s+{O}\s+candidate\b"],
                In((Runbook, 1), (Decisions, 1))),
            new("a setup's session cap", ForwardReturnSeries.SetupSessionCap,
                [@"(?:\bor|cap\s+of|Neither,)\s+{N}\s+sessions\b"],
                In((Architecture, 2), (Plan, 1), (Decisions, 1))),
            new("the resolved setups a verdict waits on", ReasonVerdict.MinimumResolved,
                [@"minimum\s+(?:is|of)\s+(?<n>\d+)\b", @"(?<n>\d+)\s+because\s+it\s+is\s+the\s+minimum", @"(?<n>\d+)\s+resolved\s+setups,\s+which\s+is\s+the\s+minimum", @"rather\s+than\s+(?<n>\d+)\s+rows\s+however"],
                In((Plan, 4), (Decisions, 2))),
            new("the listing sessions a verdict waits on", ReasonVerdict.MinimumSessions,
                [@"at\s+least\s+{N}\s+distinct\s+listing\s+sessions"],
                In((Architecture, 1), (Plan, 1), (Decisions, 1))),
            new("the live reasons' family", ReasonVerdict.LiveFamily,
                [@"live\s+reasons\s+are\s+(?:one\s+|a\s+)?family\s+of\s+{N}\b"],
                In((Architecture, 1), (Plan, 1))),
            new("the merge distance, in typical moves", (decimal)RuleVersionScorer.LiveParameters(LadderRules.MergeDistance)["typicalMoveMultiple"],
                [@"closer\s+than\s+{N}\s+a\s+typical\s+day's\s+move", @"{N}\s+a\s+typical\s+day's\s+move\s+is\s+the\s+merge\s+distance", @"merge\s+distance\s+{N}\s+a\s+typical\s+day's\s+move", @"merged\s+into\s+bands\s+within\s+{N}\s+a\s+typical", @"anchors\s+sit\s+more\s+than\s+{N}\s+a\s+typical", @"narrower\s+than\s+{N}\s+a\s+typical"],
                In((Architecture, 3), (Plan, 2), (Decisions, 1))),
        ];
    }

    [Fact]
    public void ACensusOrARestatementThatNoLongerMatchesTheDocumentIsReported()
    {
        var limits = new ArchitectureTable(
            Scope.LimitsTable,
            [
                ["Limit", "Value", "Reason", "Asserted by"],
                ["A limit", $"at most 3 things and 40 others ({Corpus.Decision}: A decision 9)", "because 7", "a check"],
            ]);

        var stated = StatedFigures.InValues(limits);

        Assert.Equal([("A limit", "3"), ("A limit", "40")], stated);

        HeldFigure three = new("A limit", "3", 3, "a constant");
        HeldFigure forty = new("A limit", "40", 40, "another");

        Assert.Empty(StatedFigures.Unheld(stated, [three, forty]));
        Assert.Contains("holds 4", Assert.Single(StatedFigures.Unheld(stated, [three with { Holds = 4 }, forty])), StringComparison.Ordinal);
        Assert.Contains("which no census entry holds", Assert.Single(StatedFigures.Unheld(stated, [three])), StringComparison.Ordinal);
        Assert.Contains("does not state", Assert.Single(StatedFigures.Unheld(stated, [three, forty, new("A limit", "7", 7, "a third")])), StringComparison.Ordinal);
        Assert.Contains("does not stand", Assert.Single(StatedFigures.Unheld(stated, [three, forty with { Holds = null, Stands = () => false }])), StringComparison.Ordinal);
        Assert.Contains("nothing checked", Assert.Single(StatedFigures.Unheld(stated, [three, forty with { Holds = null }])), StringComparison.Ordinal);

        var cap = new Restatement(
            "a cap",
            2,
            [@"{N}\s+windows\s+of\s+the\s+merge\s+distance"],
            new Dictionary<string, int>(StringComparer.Ordinal) { ["a.md"] = 1 });

        Dictionary<string, string> Saying(string text) => new(StringComparer.Ordinal) { ["a.md"] = text };

        Assert.Empty(StatedFigures.Disagreeing(Saying("at most two windows of the merge distance"), [cap]));
        Assert.Contains("and the code holds 2", Assert.Single(StatedFigures.Disagreeing(Saying("at most three windows of the merge distance"), [cap])), StringComparison.Ordinal);
        Assert.Contains("0 time(s), and 1 are counted", Assert.Single(StatedFigures.Disagreeing(Saying("at most a pair of windows of the merge distance"), [cap])), StringComparison.Ordinal);
        Assert.Contains("not a document this reads", Assert.Single(StatedFigures.Disagreeing(new Dictionary<string, string>(StringComparer.Ordinal), [cap])), StringComparison.Ordinal);

        // A superseded decision is what was held, so its figure is not read.
        var decisions = StatedFigures.Prose(
            "docs/DECISIONS.md",
            "**A decision** at most two windows of the merge distance.\n\n## Previously decided\n\n**An old one** at most three windows of the merge distance.");

        Assert.Empty(StatedFigures.Disagreeing(
            new Dictionary<string, string>(StringComparer.Ordinal) { ["docs/DECISIONS.md"] = decisions },
            [cap with { Statements = new Dictionary<string, int>(StringComparer.Ordinal) { ["docs/DECISIONS.md"] = 1 } }]));

        Assert.Equal(9m, StatedFigures.ValueOf("ninth"));
        Assert.Equal(1137m, StatedFigures.ValueOf("1,137"));
        Assert.Equal(0.5m, StatedFigures.ValueOf("half"));
        Assert.Throws<FormatException>(() => StatedFigures.ValueOf("a pair"));
    }

    [Fact]
    public void TheFrameworkTheSpecsStateIsTheOneTheBuildUses()
    {
        var built = Versions.FrameworkIn(File.ReadAllText(Repository.DirectoryBuildProps));
        var specs = Specs();
        var stated = Versions.MentionsIn(specs, Framework);

        // Two scopes, and only the second carries the property. The documents
        // opened is a fact about the corpus, so it is context and carries no
        // floor: a floor on it is satisfied by opening a document, and a run
        // finding no mention in any of them would pass having compared nothing.
        //
        // The comparisons are what this check is about. That number cannot be
        // moved by adding a document or by adding a sentence, only by a mention
        // that agrees with the build file, which is the thing being pinned.
        Assert.True(
            stated.Count >= 2,
            $"Compared {stated.Count} framework mentions against {Repository.DirectoryBuildProps}, " +
            $"expected at least 2, over {specs.Count} specs read.");

        Assert.Empty(Versions.Disagreeing(stated, built, "framework mention"));
    }

    [Fact]
    public void TheFeatureBandTheSpecsStateIsTheOneGlobalJsonPins()
    {
        var pinned = Versions.FeatureBand(Versions.SdkVersionIn(File.ReadAllText(Repository.SdkPin)));
        var specs = Specs();
        var stated = Versions.MentionsIn(specs, Band);

        Assert.True(
            stated.Count >= 2,
            $"Compared {stated.Count} band mentions against {Repository.SdkPin}, " +
            $"expected at least 2, over {specs.Count} specs read.");

        Assert.Empty(Versions.Disagreeing(stated, pinned, "band mention"));
    }

    [Fact]
    public void TheWorkflowInstallsAnSdkThePinWillAccept()
    {
        var sdk = Versions.SdkVersionIn(File.ReadAllText(Repository.SdkPin));
        var framework = Versions.FrameworkIn(File.ReadAllText(Repository.DirectoryBuildProps));
        var installs = Versions.Occurrences(File.ReadAllText(Repository.Workflow), "dotnet-version: '[^']+'");

        Assert.True(installs.Count >= 2, $"Found {installs.Count} install steps, expected at least 2.");
        Assert.All(installs, step => Assert.Equal(Versions.MajorMinor(sdk), Versions.MajorMinor(step)));
        Assert.Equal(Versions.MajorMinor(sdk), Versions.MajorMinor(framework));
    }

    [Fact]
    public void TheCheckReportsVersionsThatDisagree()
    {
        // The permanent proof that the assertions above can fail: every
        // extractor returns a different value for different input.
        Assert.Equal("net9.0", Versions.Occurrences("targeting `net9.0`", @"net[0-9]+\.[0-9]+").Single());
        Assert.Equal("9.0", Versions.MajorMinor("net9.0"));
        Assert.Equal("10.0.4xx", Versions.FeatureBand("10.0.400"));
    }

    [Fact]
    public void AVersionThatCannotBeReadFailsRatherThanDefaulting()
    {
        Assert.Throws<FormatException>(() => Versions.MajorMinor("no version here"));
        Assert.Throws<FormatException>(() => Versions.FeatureBand("10.0"));
    }

    [Fact]
    public void ADocumentStatingAVersionTheBuildDoesNotIsReported()
    {
        // The permanent proof over a constructed corpus, naming the document
        // rather than only the value, which is what a person needs to open.
        var documents = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["agrees.md"] = "the projects target `net10.0`",
            ["disagrees.md"] = "the projects target `net9.0`",
        };

        var disagreeing = Versions.Disagreeing(
            Versions.MentionsIn(documents, Framework), "net10.0", "framework mention");

        Assert.Equal("disagrees.md", Assert.Single(disagreeing).Document);
    }

    [Fact]
    public void AScanThatComparedNothingFailsRatherThanPassingOverAnEmptyResult()
    {
        // The other half, and the one a floor on documents opened would miss.
        // Both documents are read and neither states a framework, so there is
        // nothing to disagree and "none of them disagreed" is vacuously true.
        // Assert.Empty over an empty list passes, so the refusal has to happen
        // before the assertion is reached rather than inside it.
        var documents = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["silent.md"] = "this document states no framework at all",
            ["also-silent.md"] = "and neither does this one",
        };

        var mentions = Versions.MentionsIn(documents, Framework);

        Assert.Empty(mentions);

        var refusal = Assert.Throws<InvalidOperationException>(
            () => Versions.Disagreeing(mentions, "net10.0", "framework mention"));

        Assert.Contains("must fail rather than pass over an empty scan", refusal.Message, StringComparison.Ordinal);
    }
}
