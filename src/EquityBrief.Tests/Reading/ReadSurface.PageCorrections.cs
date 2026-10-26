using System.Net;
using System.Text.RegularExpressions;
using EquityBrief.Core.Candidates;
using EquityBrief.Tests.Harness;
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
        "src/EquityBrief.Web/Marks/MarkRenderer.TonightTop.cs",
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

    // The S&P 400's night on the universes' store with a pick of each kind among tomorrow's buys: M1 the breakout's, its
    // stop rising with the price and no target; M3 the drift's at RLI's plan of 2026-10-23, bought about 64.83 with its
    // stop at 60.76 and its target at 77.03; and M2 the short-term dip's, sold on its exit with no target.
    static TemporaryStore EachKindStore()
    {
        var store = UniversesStore();

        store.Execute($"UPDATE index_family_result SET passed = 1, place = 1, entry = '64.83', stop = '60.76', target = '77.03', cap = 60, reason = NULL WHERE index_code = 'MID' AND session_date = '{IndexNight}' AND ticker = 'M3' AND family = 'drift';");
        store.Execute($"INSERT INTO index_family_pick (index_code, session_date, ticker, family, state, place, also, held_index, held_family, held_night) VALUES ('MID', '{IndexNight}', 'M3', 'drift', 'listed', 1, '[]', NULL, NULL, NULL);");
        DipNightOn(store);

        return store;
    }

    // From the 22.6 correction of 2026-10-26 each buy's risk line reads its own plan: the drift's worked from its plan to
    // $3.00, and where a setup sets no target the line says why, the breakout's selling on a rising stop and the dip's on
    // its exit signal, where both read "no target".
    [Fact]
    public async Task EachBuysRiskLineReadsItsOwnPlanAndSaysWhyWhereItsSetupSetsNoTarget()
    {
        using var store = EachKindStore();
        using var host = new Host(store.Root);
        using var client = host.CreateClient();

        var page = WebUtility.HtmlDecode(await client.GetStringAsync($"/screens/tonight/{IndexNight}?universe=400"));

        Assert.Equal(["M1", "M3", "M2"], Regex.Matches(Assert.Single(Blocks(page, "<table class=\"list-table buy-points\".*?</table>")), "<tr data-ticker=\"([^\"]+)\" data-family=").Select(match => match.Groups[1].Value));
        Assert.Contains("<td class=\"r num\" data-reward-to-risk=\"2.9975\">$3.00</td>", PickRowOf(page, "M3"), StringComparison.Ordinal);
        Assert.Contains("<td class=\"r num\" data-reward-to-risk=\"none\">No target: it sells on a rising stop</td>", PickRowOf(page, "M1"), StringComparison.Ordinal);
        Assert.Contains("<td class=\"r num\" data-reward-to-risk=\"none\">No target: it sells on its exit signal</td>", PickRowOf(page, "M2"), StringComparison.Ordinal);
        Assert.DoesNotContain(">no target<", page, StringComparison.Ordinal);

        // In the reader's words: no term of section 15.23's list reaches the page.
        Assert.Empty(Checks.PlainWords.FaultsOn(Checks.PlainWords.TonightPage, page, Checks.PlainWords.Terms()));
    }
}
