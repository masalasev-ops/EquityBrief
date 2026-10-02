using EquityBrief.Core.Filter;
using static EquityBrief.Core.Families.FamilyRule;

namespace EquityBrief.Core.Families;

// One sector on a night: how many of its members hold a return, their median, and its rank among the
// sectors ranked, none where it holds too few to rank.
public sealed record SectorStanding(string Sector, int Counted, double? Median, int? Rank);

// Where one member stands on a night: its sector's standing, its place among that sector's members by
// return, counted from one, and how many of them the top quarter takes.
public sealed record LeaderStanding(SectorStanding? Sector, int? Place, int Cut);

// The settings sector leadership is read at: how many of the sectors ranked a member's sector has to be
// among, and the share of its sector's members by return it has to be among, one of every so many rounded up.
public sealed record LeaderSettings(int TopSectors, int ShareOf);

// Sector leadership: the sectors ranked by the median of their members' returns over the long span the swing
// readings store, and a stock's own return ranked inside its sector. A family of its own until the freeze of
// 2026-10-02, read since by the pullback's variant in the top sectors in place of the trend and strength
// gate, by the swing filter's own evaluator over the standings the filter hands it, and by the leaders'
// sweep. The gates' names are kept for the rows the family stored before, which its picks are still read by.
// see: The sector leaders are a variant of the pullback's starting point and not a family of their own
public static class LeaderRule
{
    public const string Name = "leader";

    // The sectors, of those ranked, a leader's sector has to be among.
    public const int TopSectors = 3;

    // The share of a sector's members, by return, a leader has to be among: the top quarter, rounded up.
    public const int QuarterOf = 4;

    // The settings the pullback's variant reads leadership at, the family's own as its sweep proposed them.
    public static LeaderSettings Live { get; } = new(TopSectors, QuarterOf);

    // The members a share of a sector holding a return takes, rounded up.
    public static int Cut(int counted, int shareOf) => (counted + shareOf - 1) / shareOf;

    // The fewest members holding a return a sector is ranked on.
    public const int SectorFloor = 5;

    // The gates the family stored on its rows, in order, which a pick it listed before is read by.
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
            var cut = Cut(sector.Returns.Length, QuarterOf);

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
}
