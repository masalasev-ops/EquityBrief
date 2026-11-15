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

    // A strategy's month: how many changes were tried for it, how many passed, and whether the strongest waits for the
    // operator's word or holds it already.
    public static string LoopTriedSaid(int tried, int passed, bool put, bool waiting)
    {
        if (tried == 0)
        {
            return "No change was tried for this strategy this month.";
        }

        var opening = Formatted($"{(tried == 1 ? "1 change was" : Formatted($"{tried} changes were"))} tried for this strategy this month; ");
        var did = passed == 1 ? "one passed" : Formatted($"{passed} passed");

        return opening + (passed == 0
            ? (tried == 1 ? "it did not beat luck." : "none beat luck.")
            : !put
                ? did + ", and none is put to you this month."
                : waiting
                    ? did + (passed == 1 ? ", and waits for your word." : ", and the strongest waits for your word.")
                    : did + (passed == 1 ? ", and you have decided on it." : ", and you have decided on the strongest."));
    }

    public static string LoopSummarySaid(string month, IReadOnlyList<LoopSummaryRow> rows)
    {
        var tried = rows.Sum(row => row.Tested);
        var put = rows.Count(row => row.Put is not null);
        var waiting = rows.Count(row => row.Put is not null && row.Word is null);

        return tried == 0
            ? Formatted($"No change was tried for any strategy in the run for {month}.")
            : Formatted($"The run for {month} tried {tried} changes across {rows.Count(row => row.Tested > 0)} strategies; {(put == 0 ? "none beat luck" : waiting == 0 ? (put == 1 ? "one beat luck and you have decided on it" : Formatted($"{put} beat luck and you have decided on them")) : waiting == 1 ? "one beat luck and waits for your word" : Formatted($"{waiting} beat luck and wait for your word"))}.");
    }

    // From the second 23.10 correction of 2026-11-13 an approval the operator withdrew is counted as withdrawn and not approved.
    // see: An approval the operator withdraws is drawn as withdrawn on their word and undone by the next restart's remedy
    public static string LoopDecisionsSaid(LoopDecided decided)
    {
        var withdrawn = decided.Decisions.Count(one => one.Withdrawal is not null);
        var approved = decided.Decisions.Count(one => one.Decision != LoopDecisions.Declined && one.Withdrawal is null);
        var declined = decided.Decisions.Count(one => one.Decision == LoopDecisions.Declined);
        var changes = decided.Decisions.Count == 1 ? "1 change" : Formatted($"{decided.Decisions.Count} changes");

        return decided.Decisions.Count == 0
            ? "You have not yet given your word on any change on this index."
            : withdrawn == 0
                ? Formatted($"You gave your word on {changes} on this index: {approved} approved and {declined} declined.")
                : Formatted($"You gave your word on {changes} on this index: {approved} approved, {declined} declined and {withdrawn} {(withdrawn == 1 ? "approval" : "approvals")} withdrawn.");
    }

    public static string LoopAlarmSaid(LoopDecided decided)
    {
        var flagged = decided.Alarms.GroupBy(one => one.Family).Count(family => family.OrderByDescending(one => one.Period).First().Flagged);

        return decided.Alarms.Count == 0
            ? "The safety alarm has no finished month to read on this index yet."
            : flagged == 0
                ? Formatted($"The safety alarm read {decided.Alarms.GroupBy(one => one.Family).Count()} strategies on this index, and none is flagged.")
                : Formatted($"The safety alarm flags {(flagged == 1 ? "1 strategy" : Formatted($"{flagged} strategies"))} on this index: its trades have done worse than chance would give it two months running.");
    }
}
