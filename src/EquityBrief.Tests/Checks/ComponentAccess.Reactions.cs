using EquityBrief.Core.Candidates;
using EquityBrief.Core.Components;
using EquityBrief.Tests.Harness;
using EquityBrief.Worker.Ladders;
using EquityBrief.Worker.Rules;
using EquityBrief.Worker.Shortlist;
using DataStore = EquityBrief.Core.Components.Store;

namespace EquityBrief.Tests.Checks;

// component-access, 11.7: the earnings reaction record is display only, read by no reason, gate,
// plan or candidate evaluator.
public partial class ComponentAccess
{
    [Fact]
    public void NoReasonGatePlanOrCandidateEvaluatorReadsTheEarningsReactionRecord()
    {
        // The three components that fire a reason, gate a tranche or draw a plan, and replay the plan
        // under a rule's versions, each declaring no touch of the record.
        // see: Each print's reaction is read from the nightly calendar and the stored bars, and reaches no reason, gate or plan
        Assert.All(
            new[] { ShortlistBuilder.Access, LadderBuilder.Access, RuleVersionScorer.Access },
            access => Assert.Equal(Touch.None, access.On(DataStore.EarningsReaction)));

        // And no source a rule version or a candidate evaluator pins, nor any evaluator's own or any
        // reason's, names the table or the store, so a reading of it cannot enter a decision
        // through a file the declarations above do not cover.
        static IEnumerable<string> Folder(string relative) =>
            Directory.GetFiles(Path.Combine(Repository.Root, relative), "*.cs", SearchOption.AllDirectories)
                .Select(path => Path.GetRelativePath(Repository.Root, path).Replace('\\', '/'));

        var sources = RuleVersionScorer.CodeVersionSources
            .Concat(CandidateEvaluator.EvaluationSources)
            .Concat(Folder("src/EquityBrief.Core/Candidates"))
            .Concat(Folder("src/EquityBrief.Core/Shortlist"))
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        Assert.True(sources.Length >= 25, $"Read {sources.Length} deciding sources, expected at least 25.");

        var naming = sources
            .Where(path => File.ReadAllText(Path.Combine(Repository.Root, path)) is var text
                && (text.Contains("earnings_reaction", StringComparison.Ordinal) || text.Contains("EarningsReaction", StringComparison.Ordinal)))
            .ToArray();

        Assert.True(naming.Length == 0, "These deciding sources name the earnings reaction record: " + string.Join(", ", naming));
    }
}
