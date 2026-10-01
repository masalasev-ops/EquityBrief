using EquityBrief.Core.Candidates;
using EquityBrief.Core.Components;
using EquityBrief.Worker.Ladders;
using EquityBrief.Worker.Rules;
using EquityBrief.Worker.Shortlist;
using EquityBrief.Worker.Filter;
using DataStore = EquityBrief.Core.Components.Store;

namespace EquityBrief.Tests.Checks;

// component-access: the news labels are context. No gate, order or plan reads them, and no source a rule
// version, the swing filter or a candidate evaluator pins names the store.
// see: The news labeller is a process of its own the night starts after the close, and its calls and its spend are its own
public partial class ComponentAccess
{
    [Fact]
    public void NoReasonGatePlanOrCandidateEvaluatorReadsTheNewsLabels()
    {
        Assert.All(
            new[] { ShortlistBuilder.Access, LadderBuilder.Access, RuleVersionScorer.Access, SwingFilter.Access },
            access => Assert.Equal(Touch.None, access.On(DataStore.NewsLabel)));

        static IEnumerable<string> Folder(string relative) =>
            Directory.GetFiles(Path.Combine(Repository.Root, relative), "*.cs", SearchOption.AllDirectories)
                .Select(path => Path.GetRelativePath(Repository.Root, path).Replace('\\', '/'));

        var sources = RuleVersionScorer.CodeVersionSources
            .Concat(SwingFilter.CodeVersionSources)
            .Concat(CandidateEvaluator.EvaluationSources)
            .Concat(Folder("src/EquityBrief.Core/Candidates"))
            .Concat(Folder("src/EquityBrief.Core/Shortlist"))
            .Concat(Folder("src/EquityBrief.Core/Filter"))
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        Assert.True(sources.Length >= 30, $"Read {sources.Length} deciding sources, expected at least 30.");

        var naming = sources
            .Where(path => File.ReadAllText(Path.Combine(Repository.Root, path)) is var text
                && (text.Contains("news_label", StringComparison.Ordinal) || text.Contains("NewsLabel", StringComparison.Ordinal)))
            .ToArray();

        Assert.True(naming.Length == 0, "These deciding sources name the news labels: " + string.Join(", ", naming));
    }
}
