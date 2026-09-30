namespace EquityBrief.Core.Research;

// The rounds a trial's and a review's paid calls are asked in, which their run log rows carry after the section, so a
// report's count and cost read from the pass's own calls leave them out while the caps' ledger still holds them.
// see: A trial asks a second profile for named sections beside a report, and ships naming none
// see: A review asks a section's model to check its own draft against the section's rules, beside a stated number of reports
public static class TrialCalls
{
    public const string Round = "trial";
    public const string ReviewRound = "review";

    public static bool Is(string stage) =>
        stage.Contains(", " + Round, StringComparison.Ordinal) || stage.Contains(", " + ReviewRound, StringComparison.Ordinal);
}
