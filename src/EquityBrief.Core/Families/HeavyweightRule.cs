using EquityBrief.Core.Bars;

namespace EquityBrief.Core.Families;

// One member on a session as the heavyweights read it: its ticker, the company it belongs to, its sector on the
// session, its value, the dollars it traded over the fifty sessions to the session, its return over the look-back,
// its close with its 50 and 200-session averages, and its beta where a setting reads one. A figure the night could
// not read is none.
public sealed record HeavyweightMember(
    string Ticker,
    string Company,
    string? Sector,
    decimal? Value,
    decimal DollarVolume,
    double? Return,
    double Close,
    double? Average50,
    double? Average200,
    double? Beta = null);

// The settings the heavyweights are read at: how many of a sector's largest companies its leaders are chosen
// among, every one at the most an int holds, the sessions a return is read over, how many leaders a sector holds,
// whether a leader needs a beta of at least one, whether a sector's return is its fund's rather than its members'
// mean, whether the book rebalances weekly rather than monthly, and what ends a holding besides its stock leaving
// the index: no longer leading at a rebalance, a close under its 200-session average, or either.
public sealed record HeavyweightSettings(
    int Largest,
    int LookBack,
    int Leaders,
    bool HighBeta = false,
    bool FundReturn = false,
    bool Weekly = false,
    bool SoldOnLeading = true,
    bool SoldUnderAverage = true);

// One of a sector's largest companies on a rebalance session, by the listing held: its place by value, its value,
// its return and its lead over the sector's, whether it passes the trend gate, and whether it is a leader bought.
public sealed record HeavyweightRanked(int Place, string Ticker, string Company, decimal Value, double? Return, double? Lead, bool Trend, bool Leader);

// One sector on a rebalance session: the mean of its members' returns over how many held one, its largest
// companies in order of value, and the leaders the rule buys.
public sealed record HeavyweightSector(string Sector, double? Return, int Counted, IReadOnlyList<HeavyweightRanked> Largest, IReadOnlyList<string> Leaders);

// The sector heavyweights: on the first session of each month whose stored year holds the closes the readings need,
// the largest companies of each sector by value, one listing a company, and among them the ones leading their sector
// by most over the look-back, bought where that lead is above nothing, the stock passes the trend gate and, where the
// settings ask, its beta is at least one. A holding ends at a month's rebalance where the rule would not buy it, at a
// close under its 200-session average where the settings read one, and at its last session as a member. The night
// holds at the settings the family's freeze registered, the sweep's proposal: the ten largest, twelve months against
// the sector's fund, two leaders a sector, a beta of at least one, and sold on no longer leading.
// see: The sector heavyweights hold the largest companies leading their sectors, rotated on the first session of each month whose stored year holds the closes their readings need
// see: A heavyweight is bought where it leads its sector above nothing and passes the trend gate, and sold where the rule would not buy it
// see: A sector's return is the mean of its members' own returns over the look-back
// see: The sector heavyweights freeze at their sweep's proposal, the proposal's three passing neighbours registered beside them as variants
public static class HeavyweightRule
{
    public const string Name = "heavyweight";

    // The largest companies of a sector the leaders are chosen among.
    public const int Largest = 10;

    // The sessions a return is read over, twelve months of them.
    public const int LookBack = 251;

    // The leaders a sector holds.
    public const int Leaders = 2;

    // The size cut that reads every company of a sector.
    public const int EveryCompany = int.MaxValue;

    // The provisional setting the freeze replaced: the five largest, six months against the members' mean, one leader,
    // sold on either exit.
    public const int ProvisionalLargest = 5;

    public const int ProvisionalLookBack = 126;

    public const int ProvisionalLeaders = 1;

    // A beta's floor where a setting reads one, and the daily returns it is read over, which need one close more.
    public const double BetaFloor = 1.0;

    public const int BetaReturns = 251;

    // The settings the night holds at, and the provisional ones the freeze replaced.
    public static HeavyweightSettings Live { get; } = new(Largest, LookBack, Leaders, HighBeta: true, FundReturn: true, SoldUnderAverage: false);

    public static HeavyweightSettings Provisional { get; } = new(ProvisionalLargest, ProvisionalLookBack, ProvisionalLeaders);

    // A session rebalances where no rebalance was read before it in its month, or its week from its Monday where the
    // settings rebalance weekly: the first session of each period, the first session read where none was read before,
    // so the book holds from its first night, and the session after a period's first that the night did not run on, so
    // a period is never passed over.
    public static bool Rebalances(DateOnly session, DateOnly? lastRebalance, bool weekly = false) =>
        lastRebalance is not { } last
        || (weekly ? MondayOf(last) != MondayOf(session) : last.Year != session.Year || last.Month != session.Month);

    static DateOnly MondayOf(DateOnly day) => day.AddDays(-(((int)day.DayOfWeek + 6) % 7));

    // The closes a setting's readings need to a night: a return over the look-back reads the close that many sessions
    // before the night, so the look-back and one more, and a beta over its daily returns the same where it reads one.
    public static int SessionsNeeded(HeavyweightSettings settings) =>
        Math.Max(settings.LookBack, settings.HighBeta ? BetaReturns : 0) + 1;

    // Why a rebalance at a setting waits for the store's sessions: the store holding fewer to the night than the
    // setting's readings need; none where it holds them.
    public static string? WaitsForSessions(HeavyweightSettings settings, int held, DateOnly night) =>
        held < SessionsNeeded(settings)
            ? FormattableString.Invariant($"the rebalance waits for a night the store holds the {SessionsNeeded(settings)} sessions its readings need, since it holds {held} to {night:yyyy-MM-dd}")
            : null;

    // Why a rebalance read nothing it could rank by, whatever the cause: no ranked company of any sector reading a lead,
    // or, where the settings read a beta, none reading a beta; none where one does. Such a rebalance waits rather than
    // storing its month and selling every holding on readings that are none.
    public static string? ReadNothing(IReadOnlyList<HeavyweightSector> sectors, HeavyweightSettings settings, Func<string, double?> betaOf, DateOnly night)
    {
        var ranked = sectors.SelectMany(sector => sector.Largest).ToArray();

        return !ranked.Any(one => one.Lead is not null)
            ? FormattableString.Invariant($"the rebalance waits, since no sector's largest companies read a lead on {night:yyyy-MM-dd}")
            : settings.HighBeta && !ranked.Any(one => betaOf(one.Ticker) is not null)
                ? FormattableString.Invariant($"the rebalance waits, since no sector's largest companies read a beta on {night:yyyy-MM-dd}")
                : null;
    }

    // The session a book next rebalances on after a night: the next session on the exchange's calendar with no
    // rebalance read in its month, or its week where the settings rebalance weekly, whose stored year holds the closes
    // the settings' readings need; none past the end of the calendar's table.
    public static DateOnly? NextRebalance(DateOnly night, DateOnly? lastRebalance, HeavyweightSettings settings)
    {
        if (night < ExchangeClosures.CoveredFrom)
        {
            return null;
        }

        for (var day = night.AddDays(1); day <= ExchangeClosures.CoveredThrough; day = day.AddDays(1))
        {
            if (!ExchangeClosures.IsSession(day) || !Rebalances(day, lastRebalance, settings.Weekly))
            {
                continue;
            }

            if (BarRetention.SessionsTo(day) is not { } held)
            {
                return null;
            }

            if (held >= SessionsNeeded(settings))
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

    // A stock's beta: the slope of its daily returns on the index's over the newest 251 of the sessions handed in, each
    // return a session's close over the one before it, the stock's and the index's on the same sessions; none where
    // fewer than 252 sessions are handed in, a close is nothing, or the index's returns do not vary.
    // see: The sector heavyweights hold the largest companies leading their sectors, rotated on the first session of each month whose stored year holds the closes their readings need
    public static double? Beta(IReadOnlyList<(double Stock, double Index)> closes)
    {
        if (closes.Count < BetaReturns + 1)
        {
            return null;
        }

        double stockSum = 0, indexSum = 0, products = 0, squares = 0;

        for (var at = closes.Count - BetaReturns; at < closes.Count; at++)
        {
            var (stockBefore, indexBefore) = closes[at - 1];

            if (stockBefore <= 0 || indexBefore <= 0)
            {
                return null;
            }

            var stock = (closes[at].Stock / stockBefore) - 1.0;
            var index = (closes[at].Index / indexBefore) - 1.0;

            stockSum += stock;
            indexSum += index;
            products += stock * index;
            squares += index * index;
        }

        var spread = squares - (indexSum * indexSum / BetaReturns);

        return spread > 0 ? (products - (stockSum * indexSum / BetaReturns)) / spread : null;
    }

    // Every sector on a rebalance session, by name: the mean of its members' own returns, a member holding none left
    // out; its largest companies by value, one listing a company; and its leaders, the largest companies leading the
    // sector above nothing and passing the trend gate, the largest lead first and the ticker settling a tie. A member
    // filing no sector stands in none, and a member with no value stands in its sector's mean and in no rank. Where
    // a sector's return is handed in, as the sweep hands in its fund's, the lead is read over that and none where it
    // is none; where the settings ask for a beta, a leader holds one at the floor or above.
    public static IReadOnlyList<HeavyweightSector> Read(IReadOnlyList<HeavyweightMember> members, HeavyweightSettings settings, IReadOnlyDictionary<string, double?>? sectorReturns = null)
    {
        var sectors = new List<HeavyweightSector>();

        foreach (var sector in members.Where(member => member.Sector is not null).GroupBy(member => member.Sector!, StringComparer.Ordinal).OrderBy(sector => sector.Key, StringComparer.Ordinal))
        {
            var returns = sector.Where(member => member.Return is not null).Select(member => member.Return!.Value).ToArray();
            double? sectorReturn = sectorReturns is not null
                ? sectorReturns.GetValueOrDefault(sector.Key)
                : returns.Length > 0 ? returns.Average() : null;
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
                .Where(ranked => ranked.Lead > 0 && ranked.Trend && (!settings.HighBeta || ranked.Member.Beta >= BetaFloor))
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
