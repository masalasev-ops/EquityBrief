namespace EquityBrief.Core.Ladders;

// One traded exit a tranche is read against: its low edge, the reward from the tranche's buy to it, the reward over the
// tranche's risk, none where the tranche states no risk, and whether it is the top of the ladder, where the trail
// begins and nothing is sold at a price.
public sealed record TargetReward(decimal Target, decimal Reward, decimal? RewardToRisk, bool TrailBegins);

// One tranche of the plan as a share of a full position: its zone, the price it is carried at, the count of equal
// shares it is one of, the stop that sells it and the risk a share carries to it, none where no band sits beneath it,
// and its reward to risk to each traded exit, nearest first.
public sealed record TrancheShare(
    decimal LowEdge,
    decimal HighEdge,
    decimal Buy,
    int Of,
    decimal? Stop,
    decimal? Risk,
    IReadOnlyList<TargetReward> Targets)
{
    // The share in the words the exits use for theirs.
    public string Share => FormattableString.Invariant($"1/{Of}");
}

// The plan's tranches as equal shares of a full position, as its traded exits divide the position, worked from the
// plan the ladder stored with the ladder's own midpoint and by no change to the code a rule's version pins. Each tranche
// is carried at its zone's midpoint and sold at its own stop, its risk the midpoint less that stop and its reward to
// each traded exit that exit's low edge less the midpoint. A tranche whose stop the plan leaves empty, having no band
// beneath it, states no risk and no reward to risk, as the plan's own arithmetic states none for it; the top traded
// exit is where the trail begins, a figure to it read as such and not as a price the plan sells at. Every tranche is
// one share of the same count, so the shares make the whole position.
// see: Each tranche buys an equal share of a full position as the exits divide it, and its reward to risk is read to every traded exit
public static class PlanSizes
{
    public static IReadOnlyList<TrancheShare> Of(IReadOnlyList<Tranche> tranches, IReadOnlyList<Exit> exits)
    {
        Exit[] traded = [.. exits.Where(exit => exit.Traded).OrderBy(exit => exit.LowEdge)];

        return
        [
            .. tranches.Select(tranche =>
            {
                var buy = LadderSeries.Midpoint(tranche);
                decimal? risk = tranche.Stop is { } stop ? buy - stop : null;

                return new TrancheShare(
                    tranche.LowEdge,
                    tranche.HighEdge,
                    buy,
                    tranches.Count,
                    tranche.Stop,
                    risk,
                    [
                        .. traded.Select(exit => new TargetReward(
                            exit.LowEdge,
                            exit.LowEdge - buy,
                            risk is { } at && at > 0 ? (exit.LowEdge - buy) / at : null,
                            exit.Trailing)),
                    ]);
            }),
        ];
    }

    // How many shares the tranches hold against how many make the whole position: equal where every tranche carries
    // its share, which is what the plan's division promises.
    public static (int Held, int Whole) Sum(IReadOnlyList<TrancheShare> shares) =>
        shares.Count == 0 ? (0, 0) : (shares.Count, shares[0].Of);
}
