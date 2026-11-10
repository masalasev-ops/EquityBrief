using System.Text.RegularExpressions;
using EquityBrief.Core.Candidates;
using EquityBrief.Core.Families;
using EquityBrief.Worker.Filter;
using EquityBrief.Worker.Rules;
using Xunit;

namespace EquityBrief.Tests.Checks;

// A rule's screen wording sits outside the sources its version pins. Every source a rule's version pins is read here, and
// a sentence in one fails unless the file is on this list, naming why it still holds its words, or the sentence is one
// no page draws: an exception, a refusal to a command line, a registration's stated rule or test, a key in the store.
// see: A rule's screen wording sits outside the sources its version pins and a file still holding it waits for a restart already happening
public class RuleWordingOutOfThePins
{
    // The files still holding wording: each is pinned by a rule that did not restart with the 16.3 remedy of 2026-11-03, so
    // moving its words would restart that rule. Each moves at phase 23's restart, or the next time a rule pinned to it
    // changes version for a real reason.
    public static IReadOnlyList<string> StillHolding { get; } =
    [
        "src/EquityBrief.Core/Candidates/CandidateEvaluator.cs",
        "src/EquityBrief.Core/Candidates/ShadowColumn.cs",
        "src/EquityBrief.Core/Cards/RuleRows.cs",
        "src/EquityBrief.Core/Families/DipRule.cs",
        "src/EquityBrief.Core/Families/FamilyRule.cs",
        "src/EquityBrief.Core/Families/HeavyweightRule.cs",
        "src/EquityBrief.Core/Families/ImprovingGrid.cs",
        "src/EquityBrief.Core/Families/ImprovingRule.cs",
        "src/EquityBrief.Core/Families/QualityRule.cs",
        "src/EquityBrief.Core/Families/SetupFamilies.cs",
        "src/EquityBrief.Core/Filter/OpenTrades.cs",
        "src/EquityBrief.Core/Filter/SwingFunnel.cs",
        "src/EquityBrief.Core/Filter/SwingGates.cs",
        "src/EquityBrief.Core/Ladders/LadderSeries.cs",
        "src/EquityBrief.Core/Ladders/TrendSeries.cs",
        "src/EquityBrief.Core/Ledger/FundamentalScorecard.cs",
        "src/EquityBrief.Core/Ledger/LedgerReadings.cs",
        "src/EquityBrief.Core/Ledger/TradeFills.cs",
        "src/EquityBrief.Core/Loop/ExitMenu.cs",
        "src/EquityBrief.Core/Loop/RuleHooks.cs",
        "src/EquityBrief.Core/Loop/TreeScore.cs",
        "src/EquityBrief.Core/Releases/ReleaseAnswers.cs",
        "src/EquityBrief.Core/Sweep/SweepAxes.cs",
        "src/EquityBrief.Data/ReleaseReadings.cs",
        "src/EquityBrief.Worker/Bars/SeriesGapStop.cs",
        "src/EquityBrief.Worker/Dips/DipFamily.cs",
        "src/EquityBrief.Worker/Families/FamilyLister.cs",
        "src/EquityBrief.Worker/Families/HeavyweightBook.cs",
        "src/EquityBrief.Worker/Filter/SwingFilter.cs",
        "src/EquityBrief.Worker/Filter/SwingReader.cs",
        "src/EquityBrief.Worker/Improving/ImprovingFamily.cs",
        "src/EquityBrief.Worker/Indices/IndexFamilies.cs",
        "src/EquityBrief.Worker/Indices/IndexNightRead.cs",
        "src/EquityBrief.Worker/Quality/QualityFamily.cs",
        "src/EquityBrief.Worker/Sweep/SweepIdeas.cs",
    ];

    // Files whose remaining sentences no page draws: refusals to a command line, what a registration states and what a
    // check prints, each read back by the operator at the terminal.
    static readonly IReadOnlyList<string> NoPageDraws =
    [
        "src/EquityBrief.Core/Rules/RuleVersions.cs",
        "src/EquityBrief.Worker/Rules/RuleVersionScorer.cs",
        "src/EquityBrief.Worker/Indices/IndexRules.cs",
        "src/EquityBrief.Worker/Sweep/HeavyweightSweep.cs",
    ];

    // A literal that reads as words and is compared as a value: the release reader's answer, which the rule's code tests, and
    // from 23.10 a mark's kind as `session_mark` stores it, which every rule's read of the pull compares.
    static readonly IReadOnlyList<string> Keys = ["lowered or withdrawn", "checked and kept"];

    // From 23.10 a file whose sentences are each a key: the test of the buy and sell points' words for each entry, stop and
    // sale, which `plan_point` stores, a plan's registration is read back by and a plan trade's order carries, so a change to
    // one is a change to what a plan names and moves the rule pinning it as it should.
    static readonly IReadOnlyList<string> KeyFiles = ["src/EquityBrief.Core/Loop/PlanPoints.cs"];

    const string FilterVersions = "the swing filter's versions";

    static IEnumerable<string> SourcesOf(CandidateEvaluator evaluator) =>
        evaluator is BookEvaluator or OwnStepCandidate ? evaluator.OwnSources : [.. evaluator.OwnSources, .. CandidateEvaluator.EvaluationSources];

    static IReadOnlyList<(string Pinned, string Source)> Pins() =>
    [
        .. CandidateEvaluators.All.SelectMany(evaluator => SourcesOf(evaluator).Select(source => (evaluator.Name, source))),
        .. RuleVersionScorer.CodeVersionSources.Select(source => ("the ladder rules' windows", source)),
        .. SwingFilter.CodeVersionSources.Select(source => (FilterVersions, source)),
    ];

    // The string literals of a source a page could draw: no comment, no verbatim string, which is SQL here, nothing inside
    // a thrown exception, and at least three words.
    internal static IReadOnlyList<string> Sentences(string text)
    {
        var bare = Regex.Replace(text, @"//[^\n]*", string.Empty);

        bare = Regex.Replace(bare, @"@""(?:[^""]|"""")*""", "\"\"");
        bare = Regex.Replace(bare, @"throw new [^;]*;", string.Empty, RegexOptions.Singleline);

        return
        [
            .. Regex.Matches(bare, @"\$?""([^""\n]*)""")
                .Select(match => match.Groups[1].Value)
                .Where(literal => Regex.IsMatch(literal, @"[A-Za-z']+ [a-z']+ [a-z']+")),
        ];
    }

    [Fact]
    public void TheReaderFindsASentenceAndLeavesACommentAThrowAndSqlAlone()
    {
        const string source = """
            // a comment of many words here
            const string Query = @"SELECT the row FROM the table";
            throw new InvalidOperationException("a thrown message of many words");
            return new Gate(Trade, false, "no close is stored for the night", Values());
            var name = "two words";
            """;

        Assert.Equal(["no close is stored for the night"], Sentences(source));
    }

    [Fact]
    public void EveryPinnedSourceHoldsNoWordingUnlessTheListNamesIt()
    {
        var root = Repository.Root;
        var pinned = Pins().Select(pin => pin.Source).Distinct(StringComparer.Ordinal).ToArray();

        var unlisted = pinned
            .Where(source => !StillHolding.Contains(source) && !NoPageDraws.Contains(source) && !KeyFiles.Contains(source))
            .Where(source => File.Exists(Path.Combine(root, source)))
            .SelectMany(source => Sentences(File.ReadAllText(Path.Combine(root, source))).Except(Keys).Select(sentence => $"{source}: {sentence}"))
            .ToArray();

        Assert.True(unlisted.Length == 0, "wording in a pinned source: " + string.Join("; ", unlisted.Take(12)));
    }

    [Fact]
    public void EveryFileTheListNamesIsPinnedAndHoldsWording()
    {
        var root = Repository.Root;
        var pinned = Pins().Select(pin => pin.Source).ToHashSet(StringComparer.Ordinal);

        Assert.All(StillHolding, source =>
        {
            Assert.Contains(source, pinned);
            Assert.NotEmpty(Sentences(File.ReadAllText(Path.Combine(root, source))));
        });
    }

    // The list's reason read off the code: every file on it is pinned by a rule the 16.3 remedy did not restart.
    [Fact]
    public void EveryFileStillHoldingWordingIsPinnedByARuleThatDidNotRestart()
    {
        var pins = Pins();
        var notRestarting = CandidateEvaluators.All
            .Where(evaluator => evaluator is BookEvaluator or OwnStepCandidate)
            .Select(evaluator => evaluator.Name)
            .Append(FilterVersions)
            .ToHashSet(StringComparer.Ordinal);

        Assert.All(StillHolding, source =>
            Assert.Contains(pins, pin => pin.Source == source && notRestarting.Contains(pin.Pinned)));
    }

    [Fact]
    public void TheWordingFileIsPinnedByNoRule()
    {
        Assert.DoesNotContain(Pins(), pin => pin.Source == "src/EquityBrief.Core/Families/RuleWording.cs");
    }

    // Drawn on the Loop page, as every page opening a line on a rule's name draws it: the words on the screen, and the key
    // beneath them as the register stores it.
    [Fact]
    public void APageDrawsADriftRulesNameInItsWordsAndKeepsItsKey()
    {
        const string Registered = "the drift rule within 3 sessions, the target at 2.5 times the risk";

        var drawn = new EquityBrief.Web.Marks.MarkRenderer().LoopRule(DriftRule.Name, Registered);

        Assert.Contains($"data-rule=\"{Registered}\"", drawn, StringComparison.Ordinal);
        Assert.Contains("The drift rule within 3 sessions, the nearer of the next price band about two typical moves up, or 2.5 times the risk", drawn, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("the live drift rule within 3 sessions, up 0.5 typical moves on 2 times the volume, the target at 2.5 times the risk", "the live drift rule within 3 sessions, up 0.5 typical moves on 2 times the volume, the nearer of the next price band about two typical moves up, or 2.5 times the risk")]
    [InlineData("the drift rule within 3 sessions, the target at 3 times the risk, the stop at least 1 typical move beneath", "the drift rule within 3 sessions, the nearer of the next price band about two typical moves up, or 3 times the risk, the stop at least 1 typical move beneath")]
    [InlineData("the breakout rule at a 126-session high", "the breakout rule at a 126-session high")]
    public void ADriftRulesNameIsDrawnInTheWordsThatNameItsTarget(string registered, string drawn)
    {
        Assert.Equal(drawn, RuleWording.ScreenName(registered));
    }
}
