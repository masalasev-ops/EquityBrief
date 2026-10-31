using EquityBrief.Core.Ledger;

namespace EquityBrief.Core.Loop;

// From 21.17 which rules and changes the tests judge on the years from the local model's cutoff's year alone, and what
// the years before it are labelled beside their verdict. Kept apart from the release readings' own sources, which the
// ledger's readings version pins.
// see: A rule or a change reading a release reading is judged on the test years from the model's cutoff's year alone, the earlier years printed beside it
public static class ReleaseJudging
{
    // What the test years before the local model's cutoff's year are labelled wherever they are printed beside a verdict
    // judged from it.
    public const string TrainedAfter = "read by a model trained after them";

    // The words a Fundamentals setting states where it asks a flag first, which reads the release readings of a member's
    // newest results release.
    public const string FlagWords = "a flag standing the night before";

    // Whether a change's words or a rule's read a release reading: as the ledger's catalogue reads them, a condition or a
    // weight naming one of the four, a tree model fitted over the whole catalogue or the drift's tone condition, or a
    // Fundamentals setting asking a flag first.
    public static bool Reads(string? words) =>
        LedgerReleases.Reads(words) || (words is not null && words.Contains(FlagWords, StringComparison.Ordinal));
}
