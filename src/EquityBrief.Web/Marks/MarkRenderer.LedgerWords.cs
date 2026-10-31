using System.Globalization;
using EquityBrief.Core.Ledger;

namespace EquityBrief.Web.Marks;

// From 21.13 each card of the Ledger opens on one sentence from its numbers, in the reader's words.
// see: A redesigned page speaks in the reader's words: every card opens on a sentence from its numbers, and no internal term reaches the screen outside the glossary
public sealed partial class MarkRenderer
{
    public static string LedgerFamilySaid(string named, IReadOnlyList<LedgerYear> years) =>
        years.Count == 0
            ? Formatted($"{named} holds no past setup on this index yet.")
            : Formatted($"{named}: {years.Sum(year => year.Setups):N0} past setups from {years.Min(year => year.Year)} to {years.Max(year => year.Year)}, {years.Sum(year => year.Settled):N0} of them settled.");

    public static string LedgerPathSaid(string? named, IReadOnlyList<LedgerSetupRow> settled, LedgerPath? path) =>
        path is { } drawn
            ? Formatted($"{drawn.Setup.Ticker}'s setup of {drawn.Setup.Session.ToString("d MMM yyyy", CultureInfo.InvariantCulture)}, drawn from ten trading days before it to its end.")
            : named is null
                ? "Choose a setup above to list its newest settled setups."
                : settled.Count == 0
                    ? Formatted($"{named} has no settled setup on this index yet.")
                    : Formatted($"{named}'s {settled.Count} newest settled setups, the newest on {settled.Max(row => row.Session).ToString("d MMM yyyy", CultureInfo.InvariantCulture)}; choose one to draw its path.");

    public static string LedgerLinksSaid(LinkTable? links) =>
        links is not { } table
            ? "No monthly run has worked which readings go with winners for this setup yet."
            : Formatted($"The run for {table.Month} read {table.Rows.Count} readings: {table.Rows.Count(row => row.Words == ReadingLinks.Winners)} went with winners and {table.Rows.Count(row => row.Words == ReadingLinks.Losers)} with losers.");

    public static string LedgerAnswersSaid(AnswerTable? answers) =>
        answers is not { } table
            ? "No monthly run has worked what the release readings came to for this setup yet."
            : Formatted($"The run for {table.Month} read {table.Rows.Select(row => (row.Question, row.Answer)).Distinct().Count()} answers of the local model against {table.Rows.Where(row => row.Side == ReleaseUsefulness.After).GroupBy(row => row.Question).Select(question => question.Sum(row => row.Setups)).DefaultIfEmpty(0).Max():N0} settled setups after its cutoff of {table.Cutoff.ToString("d MMM yyyy", CultureInfo.InvariantCulture)}.");
}
