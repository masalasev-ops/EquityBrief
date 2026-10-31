using EquityBrief.Core.Loop;

namespace EquityBrief.Web.Marks;

// From 21.13 each card of the Loop page opens on one sentence from its numbers, and each row of its summary on how many
// changes were tried for the setup and whether one beat luck, in the reader's words.
// see: A redesigned page speaks in the reader's words: every card opens on a sentence from its numbers, and no internal term reaches the screen outside the glossary
public sealed partial class MarkRenderer
{
    // A Quality at a fair price holding's row on Tonight opens on its buy and how it is kept.
    public static string QualityBoughtSaid(QualityHoldingCell cell, DateOnly? next) =>
        (cell.Fill is { } fill && cell.BoughtOn is { } on
            ? Formatted($"Bought at {Figures.Price(fill)} on {on.ToString("d MMM", System.Globalization.CultureInfo.InvariantCulture)}. ")
            : "Bought at the next open. ")
        + "No stop and no target: it is kept while it qualifies, checked at each monthly reshuffle"
        + (next is { } reshuffle ? Formatted($", next on {reshuffle.ToString("d MMM", System.Globalization.CultureInfo.InvariantCulture)}.") : ".");

    // A setup tile's quiet nights on Tonight: how many nights in a row it has found nothing and the longest run its history
    // holds, none on a night it found picks.
    public static string? QuietSaid(StretchLineView? stretch) =>
        stretch is not { Stretch: > 0 } quiet
            ? null
            : quiet.Mark is { } level
                ? Formatted($"Quiet {quiet.Stretch} {(quiet.Stretch == 1 ? "night" : "nights")}; up to {level} in its history")
                : Formatted($"Quiet {quiet.Stretch} {(quiet.Stretch == 1 ? "night" : "nights")}; too new to know its usual");

    // A setup's month: how many changes were tried for it and whether one beat luck and waits for the operator's word.
    public static string LoopTriedSaid(int tried, bool put) =>
        tried == 0
            ? "No change was tried for this setup this month."
            : Formatted($"{(tried == 1 ? "1 change was" : Formatted($"{tried} changes were"))} tried for this setup this month; {(put ? "one did, and waits for your word." : (tried == 1 ? "it did not beat luck." : "none beat luck."))}");

    public static string LoopSummarySaid(string month, IReadOnlyList<LoopSummaryRow> rows)
    {
        var tried = rows.Sum(row => row.Tested);
        var put = rows.Count(row => row.Put is not null);

        return tried == 0
            ? Formatted($"No change was tried for any setup in the run for {month}.")
            : Formatted($"The run for {month} tried {tried} changes across {rows.Count(row => row.Tested > 0)} setups; {(put == 0 ? "none beat luck" : put == 1 ? "one beat luck and waits for your word" : Formatted($"{put} beat luck and wait for your word"))}.");
    }

    public static string LoopDecisionsSaid(LoopDecided decided) =>
        decided.Decisions.Count == 0
            ? "You have not yet given your word on any change on this index."
            : Formatted($"You gave your word on {(decided.Decisions.Count == 1 ? "1 change" : Formatted($"{decided.Decisions.Count} changes"))} on this index: {decided.Decisions.Count(one => one.Decision != LoopDecisions.Declined)} approved and {decided.Decisions.Count(one => one.Decision == LoopDecisions.Declined)} declined.");

    public static string LoopAlarmSaid(LoopDecided decided)
    {
        var flagged = decided.Alarms.GroupBy(one => one.Family).Count(family => family.OrderByDescending(one => one.Period).First().Flagged);

        return decided.Alarms.Count == 0
            ? "The live alarm has read no month on this index yet."
            : flagged == 0
                ? Formatted($"The live alarm read {decided.Alarms.GroupBy(one => one.Family).Count()} setups on this index, and none is flagged.")
                : Formatted($"The live alarm flags {(flagged == 1 ? "1 setup" : Formatted($"{flagged} setups"))} on this index: its trades have done worse than chance would give it two months running.");
    }
}
