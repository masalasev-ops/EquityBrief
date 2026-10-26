using EquityBrief.Core.Candidates;
using EquityBrief.Worker.Fills;
using EquityBrief.Worker.Filter;
using EquityBrief.Worker.Rules;

namespace EquityBrief.Tests.Reading;

// read-surface, the page corrections of 2026-10-26 on the operator's ruling of 2026-10-25: each moves a page alone, and
// no source a rule's code version pins is one they write.
// see: A screen reads and renders, and each figure it works out at a price or from stored figures has one function in the core
public partial class ReadSurface
{
    // The shipped sources the page corrections of 2026-10-26 write or edit, each correction adding those it writes.
    static readonly string[] PageCorrectionSources =
    [
        "src/EquityBrief.Core/Cards/PlanRatio.cs",
        "src/EquityBrief.Api/Reading/TonightScreen.Indices.cs",
    ];

    // No source a rule's code version pins is one the page corrections write, so no rule's version moves: no candidate
    // evaluator's own file or sources, the sources every family rule shares, the hooks', the swing filter's, the rule
    // versions' scorer's or the fills'; the reader shown to find a source a rule pins.
    [Fact]
    public void NoSourceARulesVersionPinsIsOneThePageCorrectionsWrite()
    {
        var pinned = CandidateEvaluators.All
            .Select(evaluator => "src/EquityBrief.Core/Candidates/" + evaluator.GetType().Name + ".cs")
            .Concat(CandidateEvaluators.All.SelectMany(evaluator => evaluator.OwnSources))
            .Concat(CandidateEvaluator.EvaluationSources)
            .Concat(Core.Loop.RuleHooks.Sources)
            .Concat(SwingFilter.CodeVersionSources)
            .Concat(RuleVersionScorer.CodeVersionSources)
            .Concat(TradeFiller.Sources)
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        Assert.True(pinned.Length >= 40, $"Read {pinned.Length} pinned source(s), expected at least 40.");
        Assert.All(PageCorrectionSources, path => Assert.True(File.Exists(Path.Combine(Repository.Root, path)), path));
        Assert.Empty(PageCorrectionSources.Intersect(pinned, StringComparer.Ordinal));
        Assert.Contains("src/EquityBrief.Worker/Indices/IndexFamilies.cs", pinned);
        Assert.Contains("src/EquityBrief.Core/Candidates/DipCandidate.cs", pinned);
    }
}
