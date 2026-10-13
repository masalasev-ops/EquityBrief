namespace EquityBrief.Core.Cards;

// The gate of a rule's funnel that stopped the most members: the gate, how many of those reaching it it stopped, and
// how many reached it.
public sealed record FunnelDrop(string Gate, int Stopped, int Reached);

// The gate a card's funnel names as stopping the most, read off the funnel the night stored, each gate's count the
// members passing it and every gate before: the largest fall from one gate's count to the next, the earlier gate taking
// a tie, and none where no gate stopped a member that reached it.
public static class RuleFunnelDrop
{
    public static FunnelDrop? Largest(IReadOnlyList<(string Gate, int Passed)> funnel)
    {
        FunnelDrop? largest = null;

        for (var at = 1; at < funnel.Count; at++)
        {
            var stopped = funnel[at - 1].Passed - funnel[at].Passed;

            if (stopped > 0 && (largest is null || stopped > largest.Stopped))
            {
                largest = new FunnelDrop(funnel[at].Gate, stopped, funnel[at - 1].Passed);
            }
        }

        return largest;
    }
}
