using System.Text.RegularExpressions;
using EquityBrief.Tests.Checks;

namespace EquityBrief.Tests.Harness;

internal sealed record PlanCheckpoint(string Id, string Text);

// BUILD_PLAN's checkpoint sections, read as the first statement of which
// checkpoint does which work.
//
// A due point written into Scope is a second statement of that same fact, and
// nothing kept the two together. 8ac2442 reordered phase 1, moving the chart
// from 1.6 to 1.3 and the bar fetcher from 1.3 to 1.4, and fifteen due points
// kept the old order. Nothing caught it, because the reconciliation asserts a
// due point exists in the plan rather than that it names the checkpoint doing
// the work, and every stale value named a checkpoint that exists and sat in a
// plausible order.
//
// So where the plan names a subject, the due point is read from the plan. Scope
// carries only what cannot be derived, and each of those says why. A reorder
// then moves the due points because it moved the text.
internal static class PlanCheckpoints
{
    static IReadOnlyList<PlanCheckpoint>? cached;

    internal static IReadOnlyList<PlanCheckpoint> All() =>
        cached ??= In(Corpus.Read("docs/BUILD_PLAN.md"));

    internal static IReadOnlyList<PlanCheckpoint> In(string plan, int floor = 30)
    {
        var headings = Regex.Matches(plan, @"^### (\d+\.\d+) (.*)$", RegexOptions.Multiline);
        var found = new List<PlanCheckpoint>();

        for (var index = 0; index < headings.Count; index++)
        {
            var start = headings[index].Index;
            var end = index + 1 < headings.Count ? headings[index + 1].Index : plan.Length;

            found.Add(new PlanCheckpoint(headings[index].Groups[1].Value, plan[start..end]));
        }

        // Phase 0's checkpoints are a summary rather than eight sections, so the
        // floor is over phases 1 to 6. A parse returning nothing would silently
        // send every subject to the residual map, which is the failure this
        // guard exists to prevent.
        return found.Count >= floor
            ? found
            : throw new InvalidOperationException(
                $"Read {found.Count} checkpoint sections from BUILD_PLAN.md, expected at least {floor}. " +
                "A plan that could not be parsed would derive no due point at all and leave every " +
                "one of them written twice with nothing reconciling the two.");
    }

    // A planning checkpoint names what it settles, not what it builds.
    //
    // This is not a convenience. 3.0's text names the trend classifier, the
    // ladder builder, the shortlist builder and the fundamentals fetcher, and
    // builds none of them: it settles the rules they will be written to. Reading
    // a due point from it would put four components at 3.0 and fail every one of
    // them the moment 3.0 landed, which is earlier than any of the code. The
    // same holds for 4.0 and the base rate. Naming a later point than needed is
    // safe and naming an earlier one is not, so the checkpoint that only decides
    // is not a checkpoint that can end a claim.
    internal static bool Builds(PlanCheckpoint checkpoint) =>
        !checkpoint.Id.EndsWith(".0", StringComparison.Ordinal);

    // The earliest building checkpoint whose text names the subject. Earliest,
    // because a subject named at 2.1 and again at 2.6 is first owed where it
    // first appears, and naming a later point would claim nothing asserts
    // something that already does.
    internal static string? DueFor(string subject) => DueFor(subject, All());

    internal static string? DueFor(string subject, IReadOnlyList<PlanCheckpoint> checkpoints) =>
        checkpoints
            .Where(Builds)
            .FirstOrDefault(checkpoint => Names(checkpoint.Text, subject))?.Id;

    // Whole words, case insensitive. The plan writes "the bar fetcher" where the
    // catalogue writes "Bar fetcher", and a substring match would find "Level
    // builder" inside "Ladder builder" and hand back the wrong checkpoint.
    internal static bool Names(string text, string subject) =>
        Regex.IsMatch(
            text,
            @"(?<![A-Za-z])" + Regex.Escape(subject) + @"(?![A-Za-z])",
            RegexOptions.IgnoreCase);
}
