using EquityBrief.Core.Families;

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

    public const string Version = "75f41e575f4e";

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
        new("close_over_twenty", RuleWording.LedgerReadingsWords.CloseOverTwenty),
        new("close_over_fifty", RuleWording.LedgerReadingsWords.CloseOverFifty),
        new("close_over_long", RuleWording.LedgerReadingsWords.CloseOverLong),
        new("fifty_over_long", RuleWording.LedgerReadingsWords.FiftyOverLong),
        new("move_share", RuleWording.LedgerReadingsWords.MoveShare),
        new("rsi", RuleWording.LedgerReadingsWords.Rsi),
        new("rsi_up", RuleWording.LedgerReadingsWords.RsiUp),
        new("volume_ratio", RuleWording.LedgerReadingsWords.VolumeRatio),
        new("return_quarter", RuleWording.LedgerReadingsWords.ReturnQuarter),
        new("return_half_year", RuleWording.LedgerReadingsWords.ReturnHalfYear),
        new("return_twelve_less_one", RuleWording.LedgerReadingsWords.ReturnTwelveLessOne),
        new("strength", RuleWording.LedgerReadingsWords.Strength),
        new("strength_twelve_less_one", RuleWording.LedgerReadingsWords.StrengthTwelveLessOne),
        new("high_ratio", RuleWording.LedgerReadingsWords.HighRatio),
        new("since_high", RuleWording.LedgerReadingsWords.SinceHigh),
        new("depth", RuleWording.LedgerReadingsWords.Depth),
        new("dry_up", RuleWording.LedgerReadingsWords.DryUp),
        new("gap_down", RuleWording.LedgerReadingsWords.GapDown),
        new("rsi_low", RuleWording.LedgerReadingsWords.RsiLow),
        new("tightness", RuleWording.LedgerReadingsWords.Tightness),
        new("liquidity", RuleWording.LedgerReadingsWords.Liquidity),
        new("earnings_sessions", RuleWording.LedgerReadingsWords.EarningsSessions),
        new("surprise_sessions", RuleWording.LedgerReadingsWords.SurpriseSessions),
        new("surprise_percent", RuleWording.LedgerReadingsWords.SurprisePercent),
        new("reward_to_risk", RuleWording.LedgerReadingsWords.RewardToRisk),
        new("freshness", RuleWording.LedgerReadingsWords.Freshness),
        new("band_strength", RuleWording.LedgerReadingsWords.BandStrength),
        new("volume_multiple", RuleWording.LedgerReadingsWords.VolumeMultiple),
        new("range_ratio", RuleWording.LedgerReadingsWords.RangeRatio),
        new("reaction_moves", RuleWording.LedgerReadingsWords.ReactionMoves),
        new("breadth", RuleWording.LedgerReadingsWords.Breadth),
        new("highs_less_lows", RuleWording.LedgerReadingsWords.HighsLessLows),
        new("index_over_long", RuleWording.LedgerReadingsWords.IndexOverLong),
        new("vix", RuleWording.LedgerReadingsWords.Vix),
        new("vix_change", RuleWording.LedgerReadingsWords.VixChange),
        new("mid_over_large", RuleWording.LedgerReadingsWords.MidOverLarge),
        new("small_over_large", RuleWording.LedgerReadingsWords.SmallOverLarge),
        new("credit_over_fifty", RuleWording.LedgerReadingsWords.CreditOverFifty),
        new("profit", RuleWording.LedgerReadingsWords.Profit),
        new("coverage", RuleWording.LedgerReadingsWords.Coverage),
        new("revenue_growth", RuleWording.LedgerReadingsWords.RevenueGrowth),
        new("growth_change", RuleWording.LedgerReadingsWords.GrowthChange),
        new("gross_margin_change", RuleWording.LedgerReadingsWords.GrossMarginChange),
        new("operating_margin_change", RuleWording.LedgerReadingsWords.OperatingMarginChange),
        new("cash_over_income", RuleWording.LedgerReadingsWords.CashOverIncome),
        new("value_place", RuleWording.LedgerReadingsWords.ValuePlace),

        // From 19.12 the published price and volume factors, appended so every reading before them keeps its place, each
        // over the member's own bars to the session through the definitions of the core's factor readings.
        // see: The published price and volume factors are ledger readings for the engines to search, and no factor chooses a stock
        new("return_week", RuleWording.LedgerReadingsWords.ReturnWeek),
        new("return_month", RuleWording.LedgerReadingsWords.ReturnMonth),
        new("largest_return", RuleWording.LedgerReadingsWords.LargestReturn),
        new("volatility_month", RuleWording.LedgerReadingsWords.VolatilityMonth),
        new("residual_volatility", RuleWording.LedgerReadingsWords.ResidualVolatility),
        new("beta", RuleWording.LedgerReadingsWords.Beta),
        new("skewness", RuleWording.LedgerReadingsWords.Skewness),
        new("close_place", RuleWording.LedgerReadingsWords.ClosePlace),
        new("up_volume_share", RuleWording.LedgerReadingsWords.UpVolumeShare),
        new("price_volume_correlation", RuleWording.LedgerReadingsWords.PriceVolumeCorrelation),
        new("illiquidity", RuleWording.LedgerReadingsWords.Illiquidity),
        new("overnight_less_intraday", RuleWording.LedgerReadingsWords.OvernightLessIntraday),
        new("sector_month", RuleWording.LedgerReadingsWords.SectorMonth),
        new("sector_quarter", RuleWording.LedgerReadingsWords.SectorQuarter),
        new("low_ratio", RuleWording.LedgerReadingsWords.LowRatio),
        new("streak", RuleWording.LedgerReadingsWords.Streak),
        new("volume_slope", RuleWording.LedgerReadingsWords.VolumeSlope),
        new("alpha_one", RuleWording.LedgerReadingsWords.AlphaOne),
        new("alpha_two", RuleWording.LedgerReadingsWords.AlphaTwo),
        new("alpha_three", RuleWording.LedgerReadingsWords.AlphaThree),
        new("alpha_four", RuleWording.LedgerReadingsWords.AlphaFour),
        new("alpha_six", RuleWording.LedgerReadingsWords.AlphaSix),

        // From 19.13 the insiders' readings, appended so every reading before them keeps its place, each read from the
        // Form 4 and 4/A filings filed in the 90 days before the session through the core's insider readings, and none
        // where the store's insider history does not hold those days.
        // see: Insiders' trades are read from their Form 4 filings, and only open-market purchases and sales are counted
        new("insider_net_buy", RuleWording.LedgerReadingsWords.InsiderNetBuy),
        new("insider_buyers", RuleWording.LedgerReadingsWords.InsiderBuyers),
        new("insider_officer_buy", RuleWording.LedgerReadingsWords.InsiderOfficerBuy),
        new("insider_unplanned_sale", RuleWording.LedgerReadingsWords.InsiderUnplannedSale),

        // From 20.8 the release readings, appended so every reading before them keeps its place: the local model's four
        // answers on the newest results release the night of the session may read that the SEC accepted within the 95
        // days before it, read with its names and dates removed under the newest reading version whose history read is
        // complete, each word turned into its reading by code.
        // see: The release readings join the ledger's catalogue once their history read is complete, and their use is judged on the years after the model's cutoff
        new("release_results", RuleWording.LedgerReadingsWords.ReleaseResults),
        new("release_outlook", RuleWording.LedgerReadingsWords.ReleaseOutlook),
        new("release_tone", RuleWording.LedgerReadingsWords.ReleaseTone),
        new("release_demand", RuleWording.LedgerReadingsWords.ReleaseDemand),

        // From 22.2 the readings the short-term dip's grid reads that the catalogue did not hold, appended so every reading
        // before them keeps its place, each over the member's own bars to the session or the market series to its day.
        // see: The short-term dip's setups clear its drop a grid step past the widest its sweep reads with no trend condition, and the ledger's history budget is raised to hold them
        new("rsi_two", RuleWording.LedgerReadingsWords.RsiTwo),
        new("drop_fall", RuleWording.LedgerReadingsWords.DropFall),
        new("drop_largest", RuleWording.LedgerReadingsWords.DropLargest),
        new("drop_volume", RuleWording.LedgerReadingsWords.DropVolume),
        new("drop_against_sector", RuleWording.LedgerReadingsWords.DropAgainstSector),
        new("large_breadth", RuleWording.LedgerReadingsWords.LargeBreadth),
        new("fund_over_long", RuleWording.LedgerReadingsWords.FundOverLong),
        new("vix_over_median", RuleWording.LedgerReadingsWords.VixOverMedian),
        new("company_value", RuleWording.LedgerReadingsWords.CompanyValue),

        // From 22.8 the fundamentals scorecard's readings, appended so every reading before them keeps its place, each read
        // through the core's one scorecard over the filer's facts as first filed before the session and the member's own
        // closes to it, and read by the monthly loop only from its first run after the run of 2026-11-07.
        // see: The fundamentals scorecard scores every member on nine checks of its filings as first filed, and only the quality family's rule reads it
        new("fundamentals_score", RuleWording.LedgerReadingsWords.FundamentalsScore),
        new("gross_profitability", RuleWording.LedgerReadingsWords.GrossProfitability),
        new("return_on_assets", RuleWording.LedgerReadingsWords.ReturnOnAssets),
        new("return_on_assets_change", RuleWording.LedgerReadingsWords.ReturnOnAssetsChange),
        new("leverage_fall", RuleWording.LedgerReadingsWords.LeverageFall),
        new("current_ratio_change", RuleWording.LedgerReadingsWords.CurrentRatioChange),
        new("share_fall", RuleWording.LedgerReadingsWords.ShareFall),
        new("turnover_change", RuleWording.LedgerReadingsWords.TurnoverChange),
        new("gross_margin_year_change", RuleWording.LedgerReadingsWords.GrossMarginYearChange),

        // From 21.4 the five readings the operator chose at 21.2, appended so every reading before them keeps its place,
        // each none where its source holds nothing for the session, so a store filled later fills them with the catalogue
        // standing where it is; the loop reads by place, so it reads them from the same run as the scorecard's.
        new("relative_strength_high", RuleWording.LedgerReadingsWords.RelativeStrengthHigh),
        new("insider_cluster_buyers", RuleWording.LedgerReadingsWords.InsiderClusterBuyers),
        new("rating_actions_net", RuleWording.LedgerReadingsWords.RatingActionsNet),
        new("estimate_revision_month", RuleWording.LedgerReadingsWords.EstimateRevisionMonth),
        new("short_interest_share", RuleWording.LedgerReadingsWords.ShortInterestShare),
        // From 21.4 the business's state off the SEC's facts as first filed, which the swing filter's version leaving off a
        // deteriorating business reads in the provider's quarters' place, as the place the night orders its list by.
        // see: The seventh swing family candidate leaves off a member whose business the SEC's facts as first filed read deteriorating through the ledger's catalogue, and the nine are registered again at 21.4 with the eight unchanged versions keeping their records
        new(BusinessStateFiled, RuleWording.LedgerReadingsWords.BusinessStateFiled),

        // From 23.10's second part the entry check's readings, appended so every reading before them keeps its place, each read
        // through the entry check's own function over the member's bars to the session, the stored year then being the year
        // the night held and the bars before it the older history, so the night's entry check and the ledger read one figure;
        // the one-off item's share from the newest results release accepted by the session, which the long years hold none of.
        // see: The entry check's readings join the ledger's catalogue at the second restart, read as of each setup's session
        new("stretch_days", RuleWording.LedgerReadingsWords.StretchDays),
        new("stretch_share", RuleWording.LedgerReadingsWords.StretchShare),
        new("gain_twenty", RuleWording.LedgerReadingsWords.GainTwenty),
        new("largest_gain", RuleWording.LedgerReadingsWords.LargestGain),
        new("night_volume_ratio", RuleWording.LedgerReadingsWords.NightVolumeRatio),
        new("gap_days", RuleWording.LedgerReadingsWords.GapDays),
        new("highest_since_years", RuleWording.LedgerReadingsWords.HighestSinceYears),
        new("older_days", RuleWording.LedgerReadingsWords.OlderDays),
        new("one_off_share", RuleWording.LedgerReadingsWords.OneOffShare),
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
