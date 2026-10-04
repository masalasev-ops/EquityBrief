using EquityBrief.Core.Bars;

namespace EquityBrief.Core.Families;

// One member on a session as the heavyweights read it: its ticker, the company it belongs to, its sector on the
// session, its value, the dollars it traded over the fifty sessions to the session, its return over the look-back,
// and its close with its 50 and 200-session averages. A figure the night could not read is none.
public sealed record HeavyweightMember(
    string Ticker,
    string Company,
    string? Sector,
    decimal? Value,
    decimal DollarVolume,
    double? Return,
    double Close,
    double? Average50,
    double? Average200);

// The settings the heavyweights are read at: how many of a sector's largest companies its leaders are chosen
// among, the sessions a return is read over, and how many leaders a sector holds.
public sealed record HeavyweightSettings(int Largest, int LookBack, int Leaders);

// One of a sector's largest companies on a rebalance session, by the listing held: its place by value, its value,
// its return and its lead over the sector's, whether it passes the trend gate, and whether it is a leader bought.
public sealed record HeavyweightRanked(int Place, string Ticker, string Company, decimal Value, double? Return, double? Lead, bool Trend, bool Leader);

// One sector on a rebalance session: the mean of its members' returns over how many held one, its largest
// companies in order of value, and the leaders the rule buys.
public sealed record HeavyweightSector(string Sector, double? Return, int Counted, IReadOnlyList<HeavyweightRanked> Largest, IReadOnlyList<string> Leaders);

// The sector heavyweights: on the first session of each month, the largest companies of each sector by value, one
// listing a company, and among them the one leading its sector by most over the look-back, its sector's return the
// mean of its members' own, bought where that lead is above nothing and the stock passes the trend gate. A holding
// ends at a month's first session where the rule would not buy it, at any close under its 200-session average, and
// at its last session as a member. The settings are provisional until the family's sweep proposes the ones its
// freeze registers.
// see: The sector heavyweights hold the largest companies leading their sectors, rotated on the first session of each month
// see: A heavyweight is bought where it leads its sector above nothing and passes the trend gate, and sold where the rule would not buy it
// see: A sector's return is the mean of its members' own returns over the look-back
public static class HeavyweightRule
{
    public const string Name = "heavyweight";

    // The largest companies of a sector the leaders are chosen among.
    public const int Largest = 5;

    // The sessions a return is read over, half a year of them.
    public const int LookBack = 126;

    // The leaders a sector holds.
    public const int Leaders = 1;

    public static HeavyweightSettings Provisional { get; } = new(Largest, LookBack, Leaders);

    // A session rebalances where no rebalance was read before it in its month: the first session of each month, the
    // first session read where none was read before, so the book holds from its first night, and the session after a
    // month's first that the night did not run on, so a month is never passed over.
    public static bool Rebalances(DateOnly session, DateOnly? lastRebalance) =>
        lastRebalance is not { } last || last.Year != session.Year || last.Month != session.Month;

    // The session the book next rebalances on after a night, the first session of the month after the night's on the
    // exchange's own calendar, and none past the end of the calendar's table.
    public static DateOnly? NextRebalance(DateOnly night)
    {
        var first = new DateOnly(night.Year, night.Month, 1).AddMonths(1);

        if (night < ExchangeClosures.CoveredFrom || first > ExchangeClosures.CoveredThrough)
        {
            return null;
        }

        for (var day = first; day.Month == first.Month; day = day.AddDays(1))
        {
            if (ExchangeClosures.IsSession(day))
            {
                return day;
            }
        }

        return null;
    }

    // The trend gate: the close above its 50-session average, and that above its 200-session average.
    public static bool Trending(double close, double? average50, double? average200) =>
        average50 is { } fifty && average200 is { } twoHundred && close > fifty && fifty > twoHundred;

    // A close under its 200-session average ends a holding.
    public static bool Broken(double close, double? average200) =>
        average200 is { } twoHundred && close < twoHundred;

    // Every sector on a rebalance session, by name: the mean of its members' own returns, a member holding none left
    // out; its largest companies by value, one listing a company; and its leaders, the largest companies leading the
    // sector above nothing and passing the trend gate, the largest lead first and the ticker settling a tie. A member
    // filing no sector stands in none, and a member with no value stands in its sector's mean and in no rank.
    public static IReadOnlyList<HeavyweightSector> Read(IReadOnlyList<HeavyweightMember> members, HeavyweightSettings settings)
    {
        var sectors = new List<HeavyweightSector>();

        foreach (var sector in members.Where(member => member.Sector is not null).GroupBy(member => member.Sector!, StringComparer.Ordinal).OrderBy(sector => sector.Key, StringComparer.Ordinal))
        {
            var returns = sector.Where(member => member.Return is not null).Select(member => member.Return!.Value).ToArray();
            double? sectorReturn = returns.Length > 0 ? returns.Average() : null;
            var byTicker = sector.ToDictionary(member => member.Ticker, StringComparer.Ordinal);

            var largest = CompanyRank
                .Ranked(sector.Where(member => member.Value is not null).Select(member => new CompanyListing(member.Ticker, member.Company, member.Value!.Value, member.DollarVolume)))
                .Take(settings.Largest)
                .Select((listing, at) =>
                {
                    var member = byTicker[listing.Ticker];
                    double? lead = member.Return is { } own && sectorReturn is { } mean ? own - mean : null;

                    return (Listing: listing, Place: at + 1, Member: member, Lead: lead, Trend: Trending(member.Close, member.Average50, member.Average200));
                })
                .ToArray();

            var leaders = largest
                .Where(ranked => ranked.Lead > 0 && ranked.Trend)
                .OrderByDescending(ranked => ranked.Lead)
                .ThenBy(ranked => ranked.Listing.Ticker, StringComparer.Ordinal)
                .Take(settings.Leaders)
                .Select(ranked => ranked.Listing.Ticker)
                .ToArray();

            sectors.Add(new HeavyweightSector(
                sector.Key,
                sectorReturn,
                returns.Length,
                [.. largest.Select(ranked => new HeavyweightRanked(ranked.Place, ranked.Listing.Ticker, ranked.Listing.Company, ranked.Listing.Value, ranked.Member.Return, ranked.Lead, ranked.Trend, leaders.Contains(ranked.Listing.Ticker, StringComparer.Ordinal)))],
                leaders));
        }

        return sectors;
    }

    // The stocks the rule buys on a rebalance session, every sector's leaders.
    public static IReadOnlySet<string> Buys(IReadOnlyList<HeavyweightSector> sectors) =>
        sectors.SelectMany(sector => sector.Leaders).ToHashSet(StringComparer.Ordinal);

    // A holding's edge: its result less its size cut's return over the same sessions, and none while either is open.
    // see: A sector heavyweight's trade is scored by its percent return less the equal-weighted return of the size cut it was chosen from
    public static double? Edge(double? result, double? cutReturn) =>
        result is { } own && cutReturn is { } cut ? own - cut : null;
}
