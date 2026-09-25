using EquityBrief.Core.Candidates;
using EquityBrief.Core.Returns;

namespace EquityBrief.Core.Filter;

// The live swing filter's place in the candidate register, as a shape acceptance reads it: which
// candidate stands for the live settings, how many acceptances have already retired one, and what
// accepting another costs. The page draws the cost from here and the command refuses by it, so the
// count read before an acceptance and the count the command holds it to are one statement.
// see: A shape acceptance restarts the live filter's edge clock, and after one acceptance while the list is live each further one states the blocks it restarts
public static class SwingFamily
{
    // Every candidate standing for the live settings is named from here, and nothing else in the
    // register is.
    public const string LivePrefix = "the live swing filter";

    public static string LiveCandidate(string version) => FormattableString.Invariant($"{LivePrefix}, version {version}");

    public static bool IsLive(string candidate) => candidate.StartsWith(LivePrefix, StringComparison.Ordinal);

    // The live filter's candidate standing at the instant, or none before the family registers.
    public static RegisterRow? Standing(IReadOnlyList<RegisterRow> rows, DateTimeOffset at) =>
        CandidateFamily.Standing(rows, at).LastOrDefault(row => IsLive(row.Candidate));

    // The acceptances already taken while a live candidate stood: each one retired the candidate
    // it replaced, and nothing else retires one.
    public static int AcceptedWhileLive(IReadOnlyList<RegisterRow> rows) =>
        rows.Count(row => row.Event == CandidateFamily.Retired && row.Retires is { } retired && IsLive(retired));

    // The non-empty blocks the live candidate's clock has run by the night, by the arithmetic every
    // candidate's record is read with, from the first night that evaluated it.
    public static int Blocks(IReadOnlyList<CandidateSetup> setups, DateOnly? first, DateOnly night) =>
        CandidateRecord.For(setups, first ?? night, night, ReasonVerdict.Significance).Blocks;

    // Why an acceptance is refused on the count it states, or null. With no live candidate standing it
    // restarts nothing and states nothing; the first acceptance after one stands costs its restart
    // and need state nothing; every later one states the non-empty blocks the live filter's clock
    // has run, the figure the run page draws beside the proposal, and any other count is refused.
    public static string? BoundRefusal(RegisterRow? live, int acceptedWhileLive, int blocks, int? stated)
    {
        if (live is null)
        {
            return stated is null or 0
                ? null
                : FormattableString.Invariant($"no live filter candidate is registered, so accepting restarts nothing, and --restarts {stated} states blocks there are none of.");
        }

        if (acceptedWhileLive == 0)
        {
            return stated is null || stated == blocks
                ? null
                : FormattableString.Invariant($"'{live.Candidate}' has run {blocks} non-empty block(s), and --restarts {stated} states another count.");
        }

        return stated switch
        {
            null => FormattableString.Invariant(
                $"{acceptedWhileLive} acceptance(s) have already restarted the live filter, so shape is frozen: accepting restarts the {blocks} non-empty block(s) '{live.Candidate}' has run, and the command states them with --restarts {blocks}."),
            var count when count != blocks => FormattableString.Invariant(
                $"'{live.Candidate}' has run {blocks} non-empty block(s), the count the run page draws beside the proposal, and --restarts {count} states another."),
            _ => null,
        };
    }
}
