using EquityBrief.Core.Filter;
using static EquityBrief.Core.Families.FamilyRule;

namespace EquityBrief.Core.Families;

// One sector on a night: how many of its members hold a return, their median, and its rank among the
// sectors ranked, none where it holds too few to rank.
public sealed record SectorStanding(string Sector, int Counted, double? Median, int? Rank);

// Where one member stands on a night: its sector's standing, its place among that sector's members by
// return, counted from one, and how many of them the top quarter takes.
public sealed record LeaderStanding(SectorStanding? Sector, int? Place, int Cut);

// What the sector leader's rule reads for one member on one night: the market check the night stored, the
// sector the membership names for it, its return over the long span as the swing readings stored it, where
// it stands, the pullback's setup, trigger and trade gates as the swing filter stored them with the plan
// that trade gate read, and the exclusions the filter stored for it.
public sealed record LeaderInputs(
    string Ticker,
    Gate Market,
    string? Sector,
    double? Return,
    LeaderStanding Standing,
    int SectorsRanked,
    bool Setup,
    bool Trigger,
    bool Trade,
    decimal? Entry,
    decimal? Stop,
    decimal? Target,
    double? RewardToRisk,
    IReadOnlyList<string> Exclusions);

// The sector leader family's rule: a stock in the top quarter of one of the strongest sectors, at a
// pullback's buy point. The sectors are ranked by the median of their members' returns over the long span
// the swing readings store, and a stock's own return is ranked inside its sector. Its buy point, stop and
// target are the pullback's: the swing filter's setup, trigger and trade gates as stored that night, with
// sector leadership in place of the filter's trend and strength gate.
//
// Every setting here is provisional until the family's sweep proposes the values its freeze registers.
// see: A sector leader is a stock in the top quarter of a top three sector, bought at the pullback's buy point
// see: A family runs on provisional settings until its freeze, and nothing before the freeze counts toward a checkpoint
public static class LeaderRule
{
    public const string Name = "leader";

    // The sectors, of those ranked, a leader's sector has to be among.
    public const int TopSectors = 3;

    // The share of a sector's members, by return, a leader has to be among: the top quarter, rounded up.
    public const int QuarterOf = 4;

    // The fewest members holding a return a sector is ranked on.
    public const int SectorFloor = 5;

    public const string Sector = "sector";
    public const string Leader = "leader";

    public static readonly string[] Order = [Market, Sector, Leader, SwingGates.Setup, SwingGates.Trigger, Trade];

    // Every sector's standing on a night and every member's place in its own, from each member's sector and
    // return. A sector is ranked on the median of its members' returns, highest first and the sector's name
    // where two tie; a sector holding fewer members with a return than the floor is not ranked. A member's
    // place is its return's among its sector's, highest first and the ticker where two tie.
    public static (IReadOnlyList<SectorStanding> Sectors, IReadOnlyDictionary<string, LeaderStanding> Members) Standings(
        IReadOnlyList<(string Ticker, string? Sector, double? Return)> members)
    {
        var bySector = members
            .Where(member => member.Sector is { Length: > 0 } && member.Return is not null)
            .GroupBy(member => member.Sector!, StringComparer.Ordinal)
            .Select(sector => (Sector: sector.Key, Returns: sector.OrderByDescending(member => member.Return).ThenBy(member => member.Ticker, StringComparer.Ordinal).ToArray()))
            .ToArray();

        var ranked = bySector
            .Where(sector => sector.Returns.Length >= SectorFloor)
            .Select(sector => (sector.Sector, Median: Median([.. sector.Returns.Select(member => member.Return!.Value)])))
            .OrderByDescending(sector => sector.Median)
            .ThenBy(sector => sector.Sector, StringComparer.Ordinal)
            .Select((sector, at) => (sector.Sector, sector.Median, Rank: at + 1))
            .ToDictionary(sector => sector.Sector, StringComparer.Ordinal);

        var sectors = bySector
            .Select(sector => ranked.TryGetValue(sector.Sector, out var held)
                ? new SectorStanding(sector.Sector, sector.Returns.Length, held.Median, held.Rank)
                : new SectorStanding(sector.Sector, sector.Returns.Length, Median([.. sector.Returns.Select(member => member.Return!.Value)]), null))
            .OrderBy(sector => sector.Rank ?? int.MaxValue)
            .ThenBy(sector => sector.Sector, StringComparer.Ordinal)
            .ToArray();

        var standing = sectors.ToDictionary(sector => sector.Sector, StringComparer.Ordinal);
        var places = new Dictionary<string, LeaderStanding>(StringComparer.Ordinal);

        foreach (var sector in bySector)
        {
            var cut = (sector.Returns.Length + QuarterOf - 1) / QuarterOf;

            foreach (var (member, at) in sector.Returns.Select((member, at) => (member, at)))
            {
                places[member.Ticker] = new LeaderStanding(standing[sector.Sector], at + 1, cut);
            }
        }

        foreach (var member in members.Where(member => !places.ContainsKey(member.Ticker)))
        {
            places[member.Ticker] = new LeaderStanding(member.Sector is { Length: > 0 } named && standing.TryGetValue(named, out var held) ? held : null, null, 0);
        }

        return (sectors, places);
    }

    static double Median(double[] sorted)
    {
        Array.Sort(sorted);

        return sorted.Length % 2 == 1 ? sorted[sorted.Length / 2] : (sorted[sorted.Length / 2 - 1] + sorted[sorted.Length / 2]) / 2;
    }

    public static FamilyResult Evaluate(LeaderInputs inputs)
    {
        var placed = inputs.Trade && inputs.Entry is not null && inputs.Stop is not null && inputs.Target is not null;

        return new FamilyResult(
            inputs.Ticker,
            Name,
            [
                inputs.Market,
                SectorGate(inputs),
                LeaderGate(inputs),
                Stored(SwingGates.Setup, inputs.Setup, "setup"),
                Stored(SwingGates.Trigger, inputs.Trigger, "trigger"),
                new Gate(
                    Trade,
                    placed,
                    placed
                        ? Invariant($"the pullback's plan: bought at the close of {inputs.Entry} with the stop at {inputs.Stop} and the target at {inputs.Target}")
                        : inputs.Trade ? "the swing filter's trade gate passed and its row stores no plan to buy on" : "the swing filter's trade gate did not pass tonight",
                    Values(("close", Price(inputs.Entry)), ("stop", Price(inputs.Stop)), ("target", Price(inputs.Target)), (RewardToRiskValue, Figure(inputs.RewardToRisk)))),
            ],
            inputs.Entry,
            placed ? inputs.Stop : null,
            placed ? inputs.Target : null,
            inputs.Standing.Sector?.Rank is { } rank ? -rank : null,
            inputs.Exclusions,
            inputs.Return);
    }

    // The member's sector among the top of those ranked.
    static Gate SectorGate(LeaderInputs inputs)
    {
        if (inputs.Sector is not { Length: > 0 } sector)
        {
            return new Gate(Sector, false, "the membership names no sector for the name", Values());
        }

        if (inputs.Standing.Sector is not { } standing)
        {
            return new Gate(Sector, false, Invariant($"no member of its sector, {sector}, holds a return tonight, so the sector is not ranked"), Values(("sector", sector)));
        }

        if (standing.Rank is not { } rank)
        {
            return new Gate(
                Sector,
                false,
                Invariant($"its sector, {sector}, holds {standing.Counted} member(s) with a return, fewer than the {SectorFloor} a sector is ranked on"),
                Values(("sector", sector), ("counted", Whole(standing.Counted))));
        }

        var passed = rank <= TopSectors;

        return new Gate(
            Sector,
            passed,
            Invariant($"its sector, {sector}, ranks {rank} of {inputs.SectorsRanked} by its members' median return, {(passed ? "inside" : "outside")} the top {TopSectors}"),
            Values(("sector", sector), ("rank", Whole(rank)), ("ranked", Whole(inputs.SectorsRanked)), ("median", Figure(standing.Median))));
    }

    // The member's own return among the top quarter of its sector's.
    static Gate LeaderGate(LeaderInputs inputs)
    {
        if (inputs.Return is not { } own || inputs.Standing.Place is not { } place || inputs.Standing.Sector is not { } standing)
        {
            return new Gate(Leader, false, "not available: no return over the long span is stored for the name in a sector", Values());
        }

        var passed = place <= inputs.Standing.Cut;

        return new Gate(
            Leader,
            passed,
            Invariant($"its return of {own * 100:0.0}% is {place} of the {standing.Counted} in its sector, {(passed ? "inside" : "outside")} the top quarter, the first {inputs.Standing.Cut}"),
            Values(("return", Figure(own)), ("place", Whole(place)), ("of", Whole(standing.Counted)), ("cut", Whole(inputs.Standing.Cut))));
    }

    static Gate Stored(string name, bool passed, string gate) =>
        new(name, passed, passed ? $"the swing filter's {gate} gate passed tonight" : $"the swing filter's {gate} gate did not pass tonight", Values());
}
