using EquityBrief.Core.Providers;
using EquityBrief.Core.Time;
using EquityBrief.Worker.Bars;

namespace EquityBrief.Worker.Membership;

// The night's first two steps run by hand, outside the night: the members of the night's own index and of the indices
// read beside it loaded, and each member holding no bar asked for its year, under a run id of their own, on the session
// the clock is handed. It runs no other step, so a session's evaluations and books are never written again by it,
// which a night run again for an earlier session would do over registrations whose code has since moved.
// see: The universe is the S&P 1500's three indices with each member tagged by its index, and membership is fetched
public static class MembersVerb
{
    // The run ids the verb writes, which no page reads as a night.
    public const string ByHandPrefix = "members-by-hand-";

    public static async Task<int> RunAsync(
        string[] args,
        Func<NightFeeds> feeds,
        IClock clock,
        string databaseFile,
        TextWriter output,
        TextWriter error)
    {
        NightFeeds resolved;

        try
        {
            resolved = feeds();
        }
        catch (Exception refusal) when (refusal is InvalidOperationException or DirectoryNotFoundException)
        {
            error.WriteLine("members: " + refusal.Message);

            return 1;
        }

        var index = VerbArguments.Value(args, "--index") ?? "GSPC";
        var runId = FormattableString.Invariant($"{ByHandPrefix}{clock.UtcNow:yyyyMMddTHHmmss.fffffffZ}");

        try
        {
            var rows = await new MembershipLoader(resolved.Membership, clock, databaseFile, resolved.Funds).LoadAsync(index, runId);
            var outcome = await new Backfill(resolved.Historical, clock, databaseFile)
                .RunAsync(index, runId, wider: MembershipLoader.WiderOf(resolved.Funds, index));

            output.WriteLine(FormattableString.Invariant(
                $"members: {rows} membership row(s) written under {runId}; backfill: {outcome.RowsWritten} bar(s) written over {outcome.Requests} request(s), {(outcome.Unserved ?? []).Count} member(s) holding no year after it"));

            return 0;
        }
        catch (ProviderRefusal refusal)
        {
            error.WriteLine("members: " + refusal.Message);

            return 1;
        }
    }
}
