namespace EquityBrief.Core.Research;

// The round a trial's paid calls are asked in, which their run log rows carry after the section, so a report's count
// and cost read from the pass's own calls leave the trial's out while the caps' ledger still holds them.
// see: A trial asks a second profile for named sections after a report and records its drafts beside the report, never in it
public static class TrialCalls
{
    public const string Round = "trial";

    public static bool Is(string stage) => stage.Contains(", " + Round, StringComparison.Ordinal);
}
