namespace EquityBrief.Core.Loop;

// A family's setup at the ledger's loose gates on a night, as a rule stating the ranking is handed its candidates: the
// stock, the setup's own plan as prices, the sessions it is given and the catalogue's readings of it, by which the
// ranking's score orders it. Laid by the setup ledger from its rows and read by the steps that list, which hold no other
// part of the ledger.
// see: The engines' hooks are eight forms a registration states and all default off, and every source a rule pins moves only in a checkpoint that registers every rule reading it again
public sealed record LooseSetup(
    string Ticker,
    decimal Entry,
    decimal Stop,
    decimal? Target,
    decimal? Trail,
    int Cap,
    double? RiskMoves,
    IReadOnlyList<double?> Readings)
{
    // The target's distance over the stop's, none where the plan names no target.
    public double? RewardToRisk => Target is { } target && Entry > Stop ? Prices.Statistic.FromPrice(target - Entry) / Prices.Statistic.FromPrice(Entry - Stop) : null;
}
