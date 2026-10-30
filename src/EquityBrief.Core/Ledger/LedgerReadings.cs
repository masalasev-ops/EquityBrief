namespace EquityBrief.Core.Ledger;

// One reading the ledger stores of a setup: the column it is stored in and what it is, in one sentence.
public sealed record LedgerReading(string Column, string Definition);

// The ledger's readings, each defined once here and read as it stood on the setup's session: every figure comes from
// the bars to that session, the market series to that session and the quarters and facts filed before it, through the
// rules' own functions, the valuation reading on a night reading the closes before the store's year from the history
// pulled for those years at the store's scale as the history's merged bars hold them, so a row written on the night
// and a row rebuilt from the history for the same session carry the same figures. A reading the inputs do not reach is
// stored as none and never as nought. The version is the pin of this file, of the facts', the valuation reading's, the
// published factors' and the insiders' definitions and of the worker's sources that fill the columns and work the
// members' places, stored on every row, so a row written under another definition can be told apart.
// see: A setup is every member-session a family's loose gates pass, and its readings are defined once and read as they stood
// see: The valuation reading reads the prices before the store's year at the store's scale from the history pulled for those years
public static class LedgerReadings
{
    public const string VersionDeclaration = "public const string Version =";

    public const string Version = "d2ecd6139b90";

    // The reference high the pullback's readings are taken at, the live design's twenty sessions.
    public const int ReferenceHighSessions = 20;

    // The sessions the dollar volume is the mean over, the index floors' own window.
    public const int LiquiditySessions = 50;

    // The sessions a market series' change and relative return are read over.
    public const int IndexAverageSessions = 200;
    public const int VixChangeSessions = 10;
    public const int RelativeReturnSessions = 63;
    public const int CreditAverageSessions = 50;

    public static IReadOnlyList<LedgerReading> All { get; } =
    [
        new("close_over_twenty", "the close over its 20-session average"),
        new("close_over_fifty", "the close over its 50-session average"),
        new("close_over_long", "the close over its 200-session average"),
        new("fifty_over_long", "the 50-session average over the 200-session average"),
        new("move_share", "the typical move over the close"),
        new("rsi", "the relative strength index on the session"),
        new("rsi_up", "1 where the relative strength index rose on the session, 0 where it did not"),
        new("volume_ratio", "the session's volume over the mean of the 50 sessions before it"),
        new("return_quarter", "the return over 63 sessions"),
        new("return_half_year", "the return over 126 sessions"),
        new("return_twelve_less_one", "the return over the 231 sessions ending 21 sessions before"),
        new("strength", "the mean of the member's places among the members' returns over 63 and 126 sessions, the live measure"),
        new("strength_twelve_less_one", "the member's place among the members' returns over the 231 sessions ending 21 sessions before"),
        new("high_ratio", "the close over the highest high of the 252 sessions ending on the session"),
        new("since_high", "the sessions since the highest high of the 20 sessions before"),
        new("depth", "the pullback from that high in typical moves"),
        new("dry_up", "the volume while it came down over the 50-session average"),
        new("gap_down", "the largest gap down inside that pullback in typical moves"),
        new("rsi_low", "the lowest relative strength index since that high"),
        new("tightness", "the mean daily range of the 20 sessions before over that of the 20 before them"),
        new("liquidity", "the base-10 logarithm of the mean of close times volume over the 50 sessions to the session"),
        new("earnings_sessions", "the weekdays to the next report on file, the report's day counted"),
        new("surprise_sessions", "the sessions since the newest surprise's reaction session"),
        new("surprise_percent", "that surprise in per cent"),
        new("reward_to_risk", "the plan's target distance over its stop distance"),
        new("freshness", "for a pullback the sessions since its trigger first fired, for a drift the sessions since its reaction"),
        new("band_strength", "for a pullback the strength of the band holding the close"),
        new("volume_multiple", "for a breakout the session's volume over its 50-session average, for a drift the reaction session's"),
        new("range_ratio", "for a breakout the mean range of the 20 sessions before over that of the 20 before them"),
        new("reaction_moves", "for a drift the reaction session's rise in typical moves"),
        new("breadth", "the share of the index's members closing above their 200-session average"),
        new("highs_less_lows", "the members at a 252-session high less those at a 252-session low, over the members held"),
        new("index_over_long", "the index's own series' close over its 200-session average, the S&P 500 by its index and the 400 and 600 by their funds"),
        new("vix", "the VIX's close"),
        new("vix_change", "the VIX's close over its close 10 sessions before"),
        new("mid_over_large", "IJH's return over SPY's across 63 sessions"),
        new("small_over_large", "IJR's return over SPY's across 63 sessions"),
        new("credit_over_fifty", "HYG's close over its 50-session average"),
        new("profit", "1 where the four newest quarters filed before the session sum their net income above nothing, 0 otherwise"),
        new("coverage", "1 where those quarters' operating income is at least twice their interest expense or the company is a financial one, 0 otherwise"),
        new("revenue_growth", "the newest quarter's revenue as first filed before the session over the same quarter's a year before, less one"),
        new("growth_change", "that growth less the quarter before's growth on its own year before"),
        new("gross_margin_change", "the newest quarter's gross profit over its revenue less the same quarter's a year before"),
        new("operating_margin_change", "the newest quarter's operating income over its revenue less the same quarter's a year before"),
        new("cash_over_income", "the newest fiscal year's cash from operations over its net income, both as first filed before the session, where that income is above nothing"),
        new("value_place", "the four newest quarters' net income as first filed before the session over the company's value, the share count its filings' covers state times the close, as the share of the same figure at the ends of its own twelve quarters before it that it stands above, a tie counting half"),

        // From 19.12 the published price and volume factors, appended so every reading before them keeps its place, each
        // over the member's own bars to the session through the definitions of the core's factor readings.
        // see: The published price and volume factors are ledger readings for the engines to search, and no factor chooses a stock
        new("return_week", "the return over 5 sessions"),
        new("return_month", "the return over 21 sessions"),
        new("largest_return", "the largest daily return of the 21 sessions to the session, after Bali, Cakici and Whitelaw (2011)"),
        new("volatility_month", "the sample standard deviation of the daily returns of the 21 sessions to the session"),
        new("residual_volatility", "the sample standard deviation of the residuals of the daily returns of the 63 sessions to the session on the index's own series' over the same days, after Ang, Hodrick, Xing and Zhang (2006)"),
        new("beta", "the slope of the daily returns of the 251 sessions to the session on the index's own series' over the same days, the 252 closes of the year the night holds, after Frazzini and Pedersen (2014)"),
        new("skewness", "the skewness of the daily returns of the 63 sessions to the session, their mean cubed deviation over their population standard deviation cubed"),
        new("close_place", "the mean over the 10 sessions to the session of the close's place in the session's range, close less low over high less low, a session whose high is its low left out"),
        new("up_volume_share", "the share of the 20 sessions' volume traded on sessions closing above the close before"),
        new("price_volume_correlation", "the correlation of the daily close change with the daily volume over the 20 sessions to the session"),
        new("illiquidity", "the base-10 logarithm of the mean over the 21 sessions to the session of the absolute daily return over the close times the volume, a session trading nothing left out, after Amihud (2002)"),
        new("overnight_less_intraday", "the sum over the 21 sessions to the session of the overnight returns, the open over the close before less one, less the sum of the intraday returns, the close over the open less one, after Lou, Polk and Skouras (2019)"),
        new("sector_month", "the return over 21 sessions less its GICS sector fund's between the same two days, the sector its company files as it stood on the session"),
        new("sector_quarter", "the return over 63 sessions less its GICS sector fund's between the same two days"),
        new("low_ratio", "the close over the lowest low of the 252 sessions ending on the session"),
        new("streak", "the signed count of the consecutive sessions to the session closing the same way on the close before, plus for rises and minus for falls, nought where the session closed level"),
        new("volume_slope", "the slope of on-balance volume over the 20 sessions to the session, each session's volume added on a rise and taken off on a fall, over the mean volume of those sessions"),
        new("alpha_one", "Kakushadze's Alpha#1, rank(Ts_ArgMax(SignedPower(((returns < 0) ? stddev(returns, 20) : close), 2.), 5)) - 0.5, rank a member's place among the index's members on the session and Ts_ArgMax the place of the largest of the 5 sessions to it, 1 for the oldest"),
        new("alpha_two", "Kakushadze's Alpha#2, -1 * correlation(rank(delta(log(volume), 2)), rank(((close - open) / open)), 6), each rank a member's place among the index's members on each of the 6 sessions"),
        new("alpha_three", "Kakushadze's Alpha#3, -1 * correlation(rank(open), rank(volume), 10), each rank a member's place among the index's members on each of the 10 sessions"),
        new("alpha_four", "Kakushadze's Alpha#4, -1 * Ts_Rank(rank(low), 9), Ts_Rank the place of the session's own among the 9 sessions' places, 1 for the lowest"),
        new("alpha_six", "Kakushadze's Alpha#6, -1 * correlation(open, volume, 10)"),

        // From 19.13 the insiders' readings, appended so every reading before them keeps its place, each read from the
        // Form 4 and 4/A filings filed in the 90 days before the session through the core's insider readings, and none
        // where the store's insider history does not hold those days.
        // see: Insiders' trades are read from their Form 4 filings, and only open-market purchases and sales are counted
        new("insider_net_buy", "the dollars of the insiders' open-market purchases, code P, less those of their open-market sales, code S, filed in the 90 days before the session, over the company's value as the valuation reading works it, after Lakonishok and Lee (2001)"),
        new("insider_buyers", "the insiders with an open-market purchase filed in those 90 days, counted once each"),
        new("insider_officer_buy", "1 where an officer bought in the open market in those 90 days, 0 where none did"),
        new("insider_unplanned_sale", "the dollars of the open-market sales filed in those 90 days whose filing does not tick the Rule 10b5-1 box, over the company's value, none where the days reach before 2023-04-01, after Cohen, Malloy and Pomorski (2012)"),

        // From 20.8 the release readings, appended so every reading before them keeps its place: the local model's four
        // answers on the newest results release the night of the session may read that the SEC accepted within the 95
        // days before it, read with its names and dates removed under the newest reading version whose history read is
        // complete, each word turned into its reading by code.
        // see: The release readings join the ledger's catalogue once their history read is complete, and their use is judged on the years after the model's cutoff
        new("release_results", "the quarter against what was expected in the newest results release readable on the session and accepted within the 95 days before it, as the local model read it with its names and dates removed: 1 better, 0 in line, -1 worse, none not stated"),
        new("release_outlook", "that release's outlook: 1 raised, 0 kept, -1 lowered or withdrawn, none where none is given"),
        new("release_tone", "the tone of management's own words in that release: 1 confident, 0 neutral, -1 cautious"),
        new("release_demand", "demand or orders in that release: 1 stronger, 0 steady, -1 weaker, none not stated"),

        // From 22.2 the readings the short-term dip's grid reads that the catalogue did not hold, appended so every reading
        // before them keeps its place, each over the member's own bars to the session or the market series to its day.
        // see: The short-term dip's setups clear its drop a grid step past the widest its sweep reads with no trend condition, and the ledger's history budget is raised to hold them
        new("rsi_two", "the 2-session relative strength index on the session, Wilder's, the short-term dip's drop"),
        new("drop_fall", "the fall over the 3 sessions to the session, the close 3 sessions before less the session's, in the session's typical moves"),
        new("drop_largest", "the largest fall of a single one of those 3 sessions, the close before it less its own, in the typical move standing before it"),
        new("drop_volume", "the mean volume of those 3 sessions over the mean of the 50 sessions before them"),
        new("drop_against_sector", "the fall over those 3 sessions less the median of the same fall of the members of its GICS sector the index held that session, each in its own typical moves, none where fewer than two of them read one"),
        new("large_breadth", "on an S&P 400 or 600 row the S&P 500's breadth on the session, none on an S&P 500 row"),
        new("fund_over_long", "the index's own fund's close over its 200-session average, SPY's on an S&P 500 row and IJH's and IJR's on the S&P 400's and 600's"),
        new("vix_over_median", "the VIX's close over the median of its closes on the 252 sessions of the exchange before the session, a session being a day the S&P 500's index closed"),
        new("company_value", "the base-10 logarithm of the company's value on the session as the valuation reading works it, the newest share count its filings' covers state for a day before the session times the session's close on that count's basis"),

        // From 22.8 the fundamentals scorecard's readings, appended so every reading before them keeps its place, each read
        // through the core's one scorecard over the filer's facts as first filed before the session and the member's own
        // closes to it, and read by the monthly loop only from its first run after the run of 2026-11-07.
        // see: The fundamentals scorecard scores every member on nine checks of its filings as first filed, and only the quality family's rule reads it
        new("fundamentals_score", "the fundamentals scorecard's checks passed over the checks that applied and were read, none where fewer than five were read, after Piotroski (2000)"),
        new("gross_profitability", "the four newest quarters' gross profit as first filed before the session over the total assets at the newest quarter's end, after Novy-Marx (2013)"),
        new("return_on_assets", "the four newest quarters' net income over the total assets at the start of the four quarters"),
        new("return_on_assets_change", "that return less the same a year before"),
        new("leverage_fall", "the long-term debt over the year's average total assets a year before less the same at the newest quarter's end, higher where the debt fell"),
        new("current_ratio_change", "the current assets over the current liabilities at the newest quarter's end less the same a year before"),
        new("share_fall", "one less the newest share count the covers state over the one standing a year before it, brought across each split between them, higher where the shares fell"),
        new("turnover_change", "the four newest quarters' sales over the total assets at their start less the same a year before"),
        new("gross_margin_year_change", "the four newest quarters' gross profit over their sales less the same a year before"),

        // From 21.4 the five readings the operator chose at 21.2, appended so every reading before them keeps its place,
        // each none where its source holds nothing for the session, so a store filled later fills them with the catalogue
        // standing where it is; the loop reads by place, so it reads them from the same run as the scorecard's.
        new("relative_strength_high", "the close over the index's own series' close on the session, over the highest such ratio of the 252 sessions to it, 1 at a new high; none under 252 sessions or where the series holds no close by one of their days"),
        new("insider_cluster_buyers", "the distinct insiders buying in the open market over the 21 sessions to the session, from the Form 4 filings filed before it; none where the store's insider history does not hold those days"),
        new("rating_actions_net", "Yahoo's rating upgrades less downgrades dated over the 21 sessions before the session, from the stock's newest stored answer; none where the store holds no answer or the session is after the answer's ask"),
        new("estimate_revision_month", "the current fiscal year's consensus earnings a share over its figure 30 days before, less 1, as the newest quarters fetch on or before the session states them; none before the first fetch or where either is missing"),
        new("short_interest_share", "the shares short FINRA last reported before the session over the newest share count the filings' covers state; none while no store holds the reports"),
        // From 21.4 the business's state off the SEC's facts as first filed, which the swing filter's version leaving off a
        // deteriorating business reads in the provider's quarters' place, as the place the night orders its list by.
        // see: The seventh swing family candidate leaves off a member whose business the SEC's facts as first filed read deteriorating through the ledger's catalogue, and the nine are registered again at 21.4 with the eight unchanged versions keeping their records
        new(BusinessStateFiled, "the business's state off the SEC's facts as first filed before the session, as the night's place for a state: 0 improving, where both of the two newest filed quarters grew their sales on the same quarter a year earlier and widened their operating margin, 3 deteriorating, where both shrank and narrowed, 1 steady otherwise; none where fewer than two quarters hold the four figures"),
    ];

    // The column of the business's state off the SEC's facts, which the swing filter's stage is handed the night's read of.
    public const string BusinessStateFiled = "business_state_filed";

    // The first of the scorecard's readings, and the run of the monthly loop after which it reads them: the run of
    // 2026-11-07 reads none of them, as the operator ruled.
    // see: The fundamentals scorecard and the quality family join phase 22 ahead of the hold on new work, and the monthly run reads none of them before its first run after 2026-11-07
    public const string ScorecardFirst = "fundamentals_score";

    public static DateOnly ScorecardLoopAfter { get; } = new(2026, 11, 7);

    // Whether the monthly loop's run of the month a day falls in reads the reading at a place in the catalogue: November's
    // runs read none of the scorecard's, and December's on read every one.
    public static bool LoopReads(int place, DateOnly run) => place < IndexOf(ScorecardFirst) || new DateOnly(run.Year, run.Month, 1) > ScorecardLoopAfter;

    // The sessions the dip's drop readings are read over, and the volume's sessions before them.
    public const int DropSessions = 3;
    public const int DropVolumeSessions = 50;

    // The sessions the VIX's median is read over, and the sessions a fund's average reads.
    public const int VixMedianSessions = 252;
    public const int FundAverageSessions = 200;

    // From 21.4 the windows of the readings the operator chose at 21.2: the sessions the relative strength's high is read
    // over, the sessions an insider cluster and the rating actions are counted over, and the days a revision reads back.
    public const int RelativeStrengthSessions = 252;
    public const int ClusterSessions = 21;
    public const int RatingActionSessions = 21;
    public const int RevisionDays = 30;

    // From 21.4 the places of the index's breadth and of the VIX's close over its median, the two switches a rule's
    // exposure hook reads off the night's readings, named here so the hooks' own source names no column of the catalogue.
    public static int BreadthPlace { get; } = IndexOf("breadth");

    public static int VixOverMedianPlace { get; } = IndexOf("vix_over_median");

    public static int Count => All.Count;

    public static int IndexOf(string column)
    {
        for (var at = 0; at < All.Count; at++)
        {
            if (string.Equals(All[at].Column, column, StringComparison.Ordinal))
            {
                return at;
            }
        }

        throw new ArgumentOutOfRangeException(nameof(column), column, "The ledger holds no such reading.");
    }

    // A statistic over another, none where either is not a figure or the divisor is nothing.
    public static double? Over(double value, double against) =>
        double.IsNaN(value) || double.IsNaN(against) || against == 0 ? null : value / against;

    // A statistic as stored, none where it is not a figure.
    public static double? Figure(double value) => double.IsNaN(value) ? null : value;

    public static double? Flag(bool value) => value ? 1 : 0;

    // The base-10 logarithm of the mean of close times volume over the window's sessions, the last the session's own,
    // each close already a statistic; none under the window or where the mean is nothing.
    public static double? Liquidity(IReadOnlyList<(double Close, long Volume)> toTheSession)
    {
        if (toTheSession.Count < LiquiditySessions)
        {
            return null;
        }

        var mean = 0.0;

        for (var at = toTheSession.Count - LiquiditySessions; at < toTheSession.Count; at++)
        {
            mean += toTheSession[at].Close * toTheSession[at].Volume;
        }

        mean /= LiquiditySessions;

        return mean > 0 ? Math.Log10(mean) : null;
    }
}

// The readings of a market series on a session, each over the series' own sessions: a series' close on a day is its
// close on the last of its sessions on or before that day, and a window is counted in the series' own sessions.
public static class SeriesReadings
{
    // The place of the last session on or before the day, or -1 where the series starts after it.
    public static int At(IReadOnlyList<(DateOnly Session, double Close)> series, DateOnly day)
    {
        var low = 0;
        var high = series.Count - 1;
        var found = -1;

        while (low <= high)
        {
            var middle = (low + high) / 2;

            if (series[middle].Session <= day)
            {
                found = middle;
                low = middle + 1;
            }
            else
            {
                high = middle - 1;
            }
        }

        return found;
    }

    public static double? CloseOn(IReadOnlyList<(DateOnly Session, double Close)> series, DateOnly day) =>
        At(series, day) is var at && at >= 0 ? series[at].Close : null;

    // The close over the mean of the given count of closes ending on the day's own, none under the count.
    public static double? OverAverage(IReadOnlyList<(DateOnly Session, double Close)> series, DateOnly day, int sessions)
    {
        var at = At(series, day);

        if (at < sessions - 1)
        {
            return null;
        }

        var sum = 0.0;

        for (var place = at - sessions + 1; place <= at; place++)
        {
            sum += series[place].Close;
        }

        return LedgerReadings.Over(series[at].Close, sum / sessions);
    }

    // The close over the median of the closes on the given count of the exchange's sessions before the day, a day the
    // series is quoted on that is no session of the exchange's passed over; none where it reaches back over fewer.
    public static double? OverMedianBefore(IReadOnlyList<(DateOnly Session, double Close)> series, DateOnly day, int sessions, Func<DateOnly, bool> isSession)
    {
        var at = At(series, day);

        if (at < 0)
        {
            return null;
        }

        var before = new List<double>(sessions);

        for (var place = at - 1; place >= 0 && before.Count < sessions; place--)
        {
            if (isSession(series[place].Session))
            {
                before.Add(series[place].Close);
            }
        }

        if (before.Count < sessions)
        {
            return null;
        }

        before.Sort();

        var median = sessions % 2 == 1 ? before[sessions / 2] : (before[(sessions / 2) - 1] + before[sessions / 2]) / 2;

        return LedgerReadings.Over(series[at].Close, median);
    }

    // The close over the close the given count of sessions before, none where the series does not reach back.
    public static double? OverBefore(IReadOnlyList<(DateOnly Session, double Close)> series, DateOnly day, int sessions)
    {
        var at = At(series, day);

        return at < sessions ? null : LedgerReadings.Over(series[at].Close, series[at - sessions].Close);
    }

    // One series' return over another's across the count of sessions, each over its own sessions to the day: the
    // ratio of the two closes' ratios, none where either does not reach back.
    public static double? RelativeReturn(
        IReadOnlyList<(DateOnly Session, double Close)> series,
        IReadOnlyList<(DateOnly Session, double Close)> against,
        DateOnly day,
        int sessions)
    {
        var own = OverBefore(series, day, sessions);
        var other = OverBefore(against, day, sessions);

        return own is { } one && other is { } two && two != 0 ? one / two : null;
    }
}
