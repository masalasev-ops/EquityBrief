using EquityBrief.Core.Configuration;
using EquityBrief.Worker.Ledger;
using EquityBrief.Worker.Sweep;

namespace EquityBrief.Worker.Loop;

public sealed partial class WalkForwardTester
{
    public const string EntryCheckVerb = SetupLedger.EntryCheckStage;

    // The tester's entry point to the entry check's read-only measurement, which no rule's version pins, refused while the night holds the
    // store or a run started now would reach its window.
    // see: The entry check's measurement is read-only over the stored years and registers nothing
    public async Task<int> EntryCheckAsync(string index, CancellationToken cancellation = default)
    {
        if (!Indices.Contains(index, StringComparer.Ordinal))
        {
            output.WriteLine($"{EntryCheckVerb}: name an index with '--index', GSPC, MID or SML");

            return 2;
        }

        if (NightLock.Holder(dataRoot) is { } holder)
        {
            output.WriteLine($"{EntryCheckVerb}: the night holds the store ({holder}); run it once the night has finished");

            return 2;
        }

        if (SweepRunner.InTheNightsWindow(clock.UtcNow, Expected))
        {
            output.WriteLine(FormattableString.Invariant($"{EntryCheckVerb}: a run started now would reach the night's window, which begins at {SweepRunner.PauseFrom:hh\\:mm} UTC on a weekday; run it after the night"));

            return 2;
        }

        return await new SetupLedger(clock, databaseFile).EntryCheckAsync(index, dataRoot, output, cancellation);
    }
}
