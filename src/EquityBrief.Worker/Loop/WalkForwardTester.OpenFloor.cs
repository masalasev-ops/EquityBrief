using System.Globalization;
using EquityBrief.Core.Configuration;
using EquityBrief.Core.Families;
using EquityBrief.Core.Loop;
using EquityBrief.Worker.Cards;
using EquityBrief.Worker.Sweep;

namespace EquityBrief.Worker.Loop;

// 21.3's open floor through the walk-forward tester by hand, on one index: the drift's rules standing on the index walked
// as the per-rule test walks them, the live or provisional rule carrying the open floor's walk beside its own, the same
// listings with a fill whose reward to risk at the open is under 1 skipped and its place on the night spent, and the open
// floor judged against that rule as a one-setting procedure in the drift's own correction, its four parts and its test
// years. It registers nothing and writes no store row, its answer an HTML page with its figures beside it under the data
// root's loop folder; the open floor is registered, or put as an approval, only on the operator's word at 21.14.
// see: A read-only check changes no rule, and an exit's pricing changes only by a fill step on the operator's word
public sealed partial class WalkForwardTester
{
    public const string OpenFloorVerb = "loop-open-floor";

    // The open floor's words on an index: the rule it rides, with the fill it skips, as the record command words the
    // S&P 500's.
    public static string OpenFloorWordsOf(string rule) =>
        FormattableString.Invariant($"{rule}, skipping a fill whose reward to risk at the open is under {RuleRecorder.OpenFloor:0.##}");

    public async Task<int> OpenFloorAsync(string index, CancellationToken cancellation = default)
    {
        if (!Indices.Contains(index, StringComparer.Ordinal))
        {
            output.WriteLine($"{OpenFloorVerb}: name an index with '--index', GSPC, MID or SML");

            return 2;
        }

        if (NightLock.Holder(dataRoot) is { } holder)
        {
            output.WriteLine($"{OpenFloorVerb}: the night holds the store ({holder}); run it once the night has finished");

            return 2;
        }

        if (SweepRunner.InTheNightsWindow(clock.UtcNow, Expected))
        {
            output.WriteLine(FormattableString.Invariant($"{OpenFloorVerb}: a run started now would reach the night's window, which begins at {SweepRunner.PauseFrom:hh\\:mm} UTC on a weekday; run it after the night"));

            return 2;
        }

        var started = clock.UtcNow;
        var history = new SweepHistory(databaseFile);
        var through = await history.NewestSessionAsync(cancellation);
        var large = index == LargeIndex;
        var inputs = large
            ? await history.ReadAsync(through, output.WriteLine, cancellation)
            : await history.ReadAsync(through, output.WriteLine, cancellation, index: index, asItStood: true);

        if (inputs.Names.Count == 0)
        {
            output.WriteLine($"{OpenFloorVerb}: the store holds no member of the {DecisionCards.NameOf(index)}; pull them first with 'history-pull --members --index {index}', then their history");

            return 2;
        }

        var read = await ReadAsync(index, history, through, inputs, cancellation);

        if (read.Folds.Count == 0)
        {
            output.WriteLine(FormattableString.Invariant($"{OpenFloorVerb}: the history ends {through:yyyy-MM-dd}, before the first test year, {LoopFolds.FirstTestYear}"));

            return 2;
        }

        // What the drift's walk reads of the per-rule verb's inputs: the register, the approved settings, the market series
        // for a registration's switches, the catalogue's readings and the stored trees for a rule standing at hooks, the
        // release readings for a tone variant, and the sweep's own reading where a frozen rule states a level the
        // extended grid's walk does not read; nothing a family other than the drift needs.
        var market = await history.MarketAsync(through, cancellation);
        var standingRules = Core.Candidates.ShadowColumn.StandingAt(await RegisterAsync(databaseFile, cancellation), started);
        var tones = await TonesAsync(read, cancellation);
        var own = OwnReads.None;

        if (!large)
        {
            var needed = RuleTests.OwnSwingsNeeded(standingRules, index).Where(one => one.Family == DriftRule.Name).ToArray();

            if (needed.Length > 0)
            {
                var large500 = await IndexSweepRunner.OwnSwingRead.LargeAsync(history, through, output.WriteLine, cancellation);
                var swings = new Dictionary<(string Family, double StopFloor), IndexSweepRunner.OwnSwingRead>();

                foreach (var (family, stopFloor) in needed)
                {
                    output.WriteLine(FormattableString.Invariant($"{OpenFloorVerb}: reading the {DecisionCards.NameOf(index)}'s {family} as its own sweep reads it{(stopFloor > 0 ? $", its stop held at least {stopFloor:0.##} typical move under the buy" : string.Empty)}"));
                    swings[(family, stopFloor)] = await IndexSweepRunner.OwnSwingRead.ReadAsync(read, large500, family, history, through, stopFloor, cancellation);
                }

                own = new OwnReads(swings, null);
            }
        }

        var drift = RuleTests.Drift(new RuleTestInputs(
            read,
            standingRules,
            await StandingAsync(databaseFile, index, cancellation),
            market,
            null!,
            await LoopReadings.ReadAsync(read, databaseFile, through, cancellation),
            await StoredTreesAsync(databaseFile, cancellation),
            null,
            output.WriteLine,
            null,
            tones,
            null,
            null,
            null,
            own));
        var head = drift.FirstOrDefault(rule => rule.Standing != RuleTests.Variant);

        if (head is null || head.OpenFloor is null)
        {
            output.WriteLine($"{OpenFloorVerb}: the drift's {(head is null ? "live or provisional rule does not stand on the" : "rule cannot be walked on the")} {DecisionCards.NameOf(index)}{(head?.Why is { } why ? ": " + why : string.Empty)}, so the open floor was not judged");

            return 2;
        }

        var floorWords = OpenFloorWordsOf(head.Rule);
        var floor = new RuleUnderTest(DriftRule.Name, floorWords, RuleTests.Variant, floorWords, LoopProcedures.Risks, head.Cap, head.OpenFloor);
        var tested = RuleTests.Judge(read, [.. drift, floor]);
        var judged = tested.Single(one => ReferenceEquals(one.Rule, floor));

        foreach (var one in tested)
        {
            output.WriteLine(RuleLine(one));
        }

        static double? MeanEdge(IReadOnlyList<(int Entry, double? Edge)>? units)
        {
            var edges = (units ?? []).Select(unit => unit.Edge).OfType<double>().ToArray();

            return edges.Length == 0 ? null : edges.Average();
        }

        var verdict = judged.Verdict;
        var lines = new List<string>
        {
            FormattableString.Invariant($"The {DecisionCards.NameOf(index)}'s {head.Standing} drift, {head.Rule}: {head.Trades?.Count ?? 0} trade(s) over the history read through {through:yyyy-MM-dd}, mean edge after costs {CheckReports.Cell(MeanEdge(head.Trades), true)} risks a trade."),
            FormattableString.Invariant($"The open floor beside it, {floorWords}: {head.OpenFloor.Count} trade(s) with {head.Skipped} listing(s) skipped, their places on their nights spent, mean edge after costs {CheckReports.Cell(MeanEdge(head.OpenFloor), true)} risks a trade."),
            verdict is null
                ? $"Not judged: {judged.Why}"
                : FormattableString.Invariant($"Judged against the {head.Standing} rule as a one-setting procedure in the drift's own correction over {tested.Count(one => one.Rule.Standing == RuleTests.Variant && one.Verdict is not null)} variant(s): {RuleVerdict.Of(judged.Passed, judged.Why, verdict.Adjusted, verdict.Gate, verdict.Stable, verdict.Trimmed, verdict.Counts)}; adjusted p {CheckReports.Cell(verdict.Adjusted)} against the bar of {LoopGate.Bar.ToString(CultureInfo.InvariantCulture)} over {verdict.Blocks} block(s), the gate {(verdict.Gate ? "cleared" : "not cleared")}; stable {(verdict.Stable ? "yes" : "no")}, better in {verdict.Better} of {verdict.Counted} counted year(s); trimmed {CheckReports.Cell(verdict.Trimmed, true)}; {verdict.Units} unit(s), the count {(verdict.Counts ? "met" : "not met")}."),
        };

        foreach (var line in lines)
        {
            output.WriteLine($"{OpenFloorVerb}: {line}");
        }

        var years = judged.Evidence?.Years ?? [];
        var (report, figures) = CheckReports.Write(
            dataRoot,
            FormattableString.Invariant($"open-floor-{index}"),
            $"The drift's open floor through the tester on the {DecisionCards.NameOf(index)}",
            lines,
            [
                new CheckReports.Table(
                    "The open floor's test years against the rule, each year's units and total edge after costs in risks",
                    ["Year", "Complete", "Rule's units", "Rule's total", "Open floor's units", "Open floor's total", "Better"],
                    [.. years.Select(year => (IReadOnlyList<string>)[CheckReports.Cell(year.Year), year.Complete ? "yes" : "no", CheckReports.Cell(year.CurrentUnits), CheckReports.Cell(year.CurrentTotal, true), CheckReports.Cell(year.ProposedUnits), CheckReports.Cell(year.ProposedTotal, true), year.Better ? "yes" : "no"])]),
                new CheckReports.Table(
                    "Every drift rule standing on the index as the per-rule test judges it, the open floor among them",
                    ["Rule", "Standing", "Read against", "Verdict"],
                    [.. tested.Select(one => (IReadOnlyList<string>)[one.Rule.Rule, one.Rule.Standing, one.Against, one.Verdict is { } parts ? RuleVerdict.Of(one.Passed, one.Why, parts.Adjusted, parts.Gate, parts.Stable, parts.Trimmed, parts.Counts) : one.Why ?? string.Empty])]),
            ],
            new
            {
                index,
                through,
                rule = new { head.Rule, head.Standing, trades = head.Trades?.Count ?? 0, meanEdge = MeanEdge(head.Trades) },
                openFloor = new { words = floorWords, floor = RuleRecorder.OpenFloor, trades = head.OpenFloor.Count, skipped = head.Skipped, meanEdge = MeanEdge(head.OpenFloor) },
                verdict,
                why = judged.Why,
                years,
                judgedWith = tested.Where(one => one.Rule.Standing == RuleTests.Variant).Select(one => one.Rule.Rule).ToArray(),
            },
            clock.UtcNow);

        output.WriteLine($"{OpenFloorVerb}: written to {report} with its figures at {figures}; registered nothing and wrote no store row");

        return 0;
    }
}
