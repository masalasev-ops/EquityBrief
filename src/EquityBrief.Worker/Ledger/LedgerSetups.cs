using EquityBrief.Core.Families;
using EquityBrief.Core.Ledger;
using EquityBrief.Core.Prices;
using EquityBrief.Core.Readings;
using EquityBrief.Core.Sweep;
using EquityBrief.Worker.Indices;
using EquityBrief.Worker.Sweep;

namespace EquityBrief.Worker.Ledger;

// One setup as the ledger stores it: its identity, the words of the live rule's setting its pass was read at, whether
// that setting passes it, its anchor and its plan in typical moves, and its readings in the catalogue's order.
public sealed record SetupRow(
    string Index,
    string Family,
    string Ticker,
    DateOnly Session,
    string Rule,
    bool LivePass,
    SetupAnchor Anchor,
    SetupPlan Plan,
    double?[] Readings)
{
    public double? Reading(string column) => Readings[LedgerReadings.IndexOf(column)];
}

// One family's setups on one session of one index, with the members the gates were read over.
public sealed record FamilySetups(string Family, int Members, IReadOnlyList<SetupRow> Rows)
{
    public int LivePasses => Rows.Count(row => row.LivePass);
}

// The market series the readings take, each over its own sessions: the index's own series, or its fund's on the S&P
// 400 and 600, the VIX, SPY, IJH, IJR and HYG, any of them absent where the store holds none.
public sealed record LedgerMarket(IReadOnlyDictionary<string, IReadOnlyList<(DateOnly Session, double Close)>> Series)
{
    public const string Index = "GSPC";
    public const string Vix = "VIX";
    public const string Large = "SPY";
    public const string Mid = "IJH";
    public const string Small = "IJR";
    public const string Credit = "HYG";

    public static IReadOnlyList<string> Named { get; } = [Index, Vix, Large, Mid, Small, Credit];

    public static LedgerMarket None { get; } = new(new Dictionary<string, IReadOnlyList<(DateOnly, double)>>(StringComparer.Ordinal));

    public IReadOnlyList<(DateOnly Session, double Close)>? Of(string name) => Series.TryGetValue(name, out var held) ? held : null;

    // The series an index reads its own level against: the S&P 500 its index, the 400 and 600 their funds.
    public static string IndexSeriesOf(string indexCode) => indexCode switch
    {
        "MID" => Mid,
        "SML" => Small,
        _ => Index,
    };
}

// What the ledger reads of one index on one session besides its series: each member's quarters as filed, its sector as
// filed, and the members at a new high and a new low on each session.
public sealed record LedgerContext(
    IReadOnlyDictionary<string, IReadOnlyList<FiledIncome>> Income,
    IReadOnlyDictionary<string, string?> Sectors,
    int[] Highs,
    int[] Lows);

// The setups of one index on one session, family by family, through the sweep's own readings of the series: the
// pullback's candidates as the sweep reads them, the breakout's highs and ranges as its sweep reads them, and the
// drift's reaction as its sweep reads it, each held to the family's loose gates and the index's floors, with the live
// rule's own setting read beside it. Every reading is taken from the series to the session and never past it.
// see: A setup is every member-session a family's loose gates pass, and its readings are defined once and read as they stood
public static class LedgerSetups
{
    public static IReadOnlyList<string> Families { get; } = [SetupFamilies.Pullback, BreakoutRule.Name, DriftRule.Name];

    static readonly int LiveHigh = SweepAxes.ReferenceHighs.ToList().IndexOf(SweepDesign.Live.ReferenceHigh);
    static readonly int LiveAge = SweepCandidate.AgeAt(SweepDesign.Live.Trigger, SweepDesign.Live.Support);
    static readonly int LivePlan = SweepCandidate.PlanAt(SweepDesign.Live.Plan, SweepDesign.Live.Support);
    static readonly byte LiveUptrend = (byte)(1 << (int)SweepDesign.Live.Uptrend);

    // The live pullback setting on an index: the S&P 500's filter as the ideas' run replays it, and the base the 400's
    // and 600's provisional rule reads.
    public static DialSetting LivePullback(string indexCode) => indexCode == IndexFamilies.LargeIndex ? SweepIdeas.Today : SweepIdeas.BaseRule.Setting;

    public static string PullbackWords(string indexCode) => LivePullback(indexCode).Describe(SweepGrid.Extended);

    public static string BreakoutWords { get; } = Words(BreakoutSweep.Grid, IndexNightRead.BreakoutAsFrozen);

    public static string DriftWords { get; } = Words(DriftSweep.Grid, IndexNightRead.DriftAsFrozen);

    static string Words(FamilyGrid grid, int[] setting) =>
        string.Join(", ", grid.Dials.Select((dial, at) => FormattableString.Invariant($"{dial.Dial} {grid.Value(setting, at)}")));

    public static IReadOnlyList<FamilySetups> On(
        string indexCode,
        IReadOnlyList<SweepSeries> series,
        IReadOnlyList<SweepColumns.Session> sessions,
        SweepBenchmark.Members members,
        IReadOnlyList<DateOnly> calendar,
        int at,
        LedgerMarket market,
        LedgerContext context)
    {
        var closes = series.Select(one => one.Bars.Select(bar => Statistic.FromPrice(bar.Close)).ToArray()).ToArray();

        return
        [
            Pullbacks(indexCode, series, closes, sessions, members, calendar, at, market, context),
            Breakouts(indexCode, series, closes, sessions, members, calendar, at, market, context),
            Drifts(indexCode, series, closes, sessions, members, calendar, at, market, context),
        ];
    }

    static FamilySetups Pullbacks(
        string indexCode,
        IReadOnlyList<SweepSeries> series,
        double[][] closes,
        IReadOnlyList<SweepColumns.Session> sessions,
        SweepBenchmark.Members members,
        IReadOnlyList<DateOnly> calendar,
        int at,
        LedgerMarket market,
        LedgerContext context)
    {
        var held = members.Names[at];
        var found = new List<SweepCandidate>[series.Count];

        Parallel.For(0, series.Count, name => found[name] = held.Contains(name) ? SweepCandidates.For(series[name], name, sessions, calendar, at, at, at + 1) : []);

        var live = LivePullback(indexCode);
        var words = PullbackWords(indexCode);
        var rows = new List<SetupRow>();

        foreach (var candidate in found.SelectMany(list => list).Where(candidate => candidate.Session == at))
        {
            var one = series[candidate.Name];
            var bar = IndexNightRead.BarOf(one, at);

            if (bar < 0
                || (candidate.Uptrend & LiveUptrend) == 0
                || candidate.Band[(int)SweepDesign.Live.Support] < 0
                || candidate.Plans[LivePlan] is not { } plan
                || !SetupGates.Pullback(candidate.Strength[(int)SweepDesign.Live.Strength], candidate.Depth[LiveHigh], candidate.Age[LiveAge], plan.RewardToRisk)
                || !Clears(indexCode, one, bar))
            {
                continue;
            }

            var setupPlan = new SetupPlan(plan.StopMoves, plan.RewardToRisk, null, SweepDesign.Live.Hold);

            if (setupPlan.On(calendar[at], closes[candidate.Name][bar], one.Atr[bar]) is not { } anchor)
            {
                continue;
            }

            var corner = SweepCorner.Of(
                SweepGrid.Extended,
                candidate.Strength[(int)SweepDesign.Live.Strength],
                candidate.Depth[LiveHigh],
                candidate.DryUp[LiveHigh],
                candidate.Age[LiveAge],
                plan.RewardToRisk,
                plan.StopMoves,
                candidate.Breadth,
                candidate.Band[(int)SweepDesign.Live.Support]);
            var inTheEarningsWindow = SweepDesign.Live.EarningsWindow > 0 && candidate.Earnings >= 0 && candidate.Earnings <= SweepDesign.Live.EarningsWindow;
            var passes = corner is { } placed && placed.Passes(live) && !inTheEarningsWindow;

            var readings = Readings(indexCode, candidate.Name, one, closes[candidate.Name], bar, sessions[at], members, calendar, at, market, context);

            readings[LedgerReadings.IndexOf("reward_to_risk")] = plan.RewardToRisk;
            readings[LedgerReadings.IndexOf("freshness")] = candidate.Age[LiveAge];
            readings[LedgerReadings.IndexOf("band_strength")] = candidate.Band[(int)SweepDesign.Live.Support];

            rows.Add(new SetupRow(indexCode, SetupFamilies.Pullback, one.Name.Ticker, calendar[at], words, passes, anchor, setupPlan, readings));
        }

        return new FamilySetups(SetupFamilies.Pullback, held.Length, Ordered(rows));
    }

    static FamilySetups Breakouts(
        string indexCode,
        IReadOnlyList<SweepSeries> series,
        double[][] closes,
        IReadOnlyList<SweepColumns.Session> sessions,
        SweepBenchmark.Members members,
        IReadOnlyList<DateOnly> calendar,
        int at,
        LedgerMarket market,
        LedgerContext context)
    {
        var held = members.Names[at];
        var bars = members.Bars[at];
        var rows = new List<SetupRow>();
        var frozen = IndexNightRead.BreakoutAsFrozen;
        var liveHigh = BreakoutSweep.Grid.Value(frozen, 0);
        var liveVolume = BreakoutSweep.Grid.Value(frozen, 1);
        var liveCeiling = BreakoutSweep.Grid.Value(frozen, 2);
        var liveStop = BreakoutSweep.Grid.Value(frozen, 3);
        var open = sessions[at].Breadth >= FamilySweep.MarketFloor;

        for (var place = 0; place < held.Length; place++)
        {
            var name = held[place];
            var bar = bars[place];
            var one = series[name];
            var close = closes[name][bar];
            var highBefore = BreakoutSweep.HighsBefore(one.Bars, SetupGates.BreakoutHighSessions)[bar];
            var volume = one.Volume50[bar] > 0 ? Statistic.FromVolume(one.Bars[bar].Volume) / one.Volume50[bar] : double.NaN;
            var ranges = BreakoutSweep.RangesBefore(one.Bars, BreakoutRule.RangeSessions)[bar];

            if (!SetupGates.Breakout(close, highBefore, volume, ranges) || !Clears(indexCode, one, bar))
            {
                continue;
            }

            var plan = new SetupPlan(liveStop, null, liveStop, BreakoutRule.CapSessions);

            if (plan.On(calendar[at], close, one.Atr[bar]) is not { } anchor)
            {
                continue;
            }

            var liveHighBefore = BreakoutSweep.HighsBefore(one.Bars, (int)liveHigh)[bar];
            var passes = open && close > liveHighBefore && volume >= liveVolume && (double.IsPositiveInfinity(liveCeiling) || ranges <= liveCeiling);
            var readings = Readings(indexCode, name, one, closes[name], bar, sessions[at], members, calendar, at, market, context);

            readings[LedgerReadings.IndexOf("volume_multiple")] = LedgerReadings.Figure(volume);
            readings[LedgerReadings.IndexOf("range_ratio")] = LedgerReadings.Figure(ranges);

            rows.Add(new SetupRow(indexCode, BreakoutRule.Name, one.Name.Ticker, calendar[at], BreakoutWords, passes, anchor, plan, readings));
        }

        return new FamilySetups(BreakoutRule.Name, held.Length, Ordered(rows));
    }

    static FamilySetups Drifts(
        string indexCode,
        IReadOnlyList<SweepSeries> series,
        double[][] closes,
        IReadOnlyList<SweepColumns.Session> sessions,
        SweepBenchmark.Members members,
        IReadOnlyList<DateOnly> calendar,
        int at,
        LedgerMarket market,
        LedgerContext context)
    {
        var held = members.Names[at];
        var bars = members.Bars[at];
        var rows = new List<SetupRow>();
        var frozen = IndexNightRead.DriftAsFrozen;
        var liveWindow = DriftSweep.Grid.Value(frozen, 0);
        var liveRise = DriftSweep.Grid.Value(frozen, 1);
        var liveVolume = DriftSweep.Grid.Value(frozen, 2);
        var liveMultiple = DriftSweep.Grid.Value(frozen, 3);
        var open = sessions[at].Breadth >= FamilySweep.MarketFloor;

        for (var place = 0; place < held.Length; place++)
        {
            var name = held[place];
            var bar = bars[place];
            var one = series[name];
            var newest = one.NewestSurprise[bar];

            // The reaction session is the setup's own: one setup a report, anchored where the first entry is.
            if (newest < 0 || one.SurpriseBar[newest] != bar || bar < 1)
            {
                continue;
            }

            var close = closes[name][bar];
            var before = one.Atr[bar - 1];
            var rise = before > 0 ? (close - closes[name][bar - 1]) / before : double.NaN;
            var volume = one.Volume50[bar] > 0 ? Statistic.FromVolume(one.Bars[bar].Volume) / one.Volume50[bar] : double.NaN;
            var surprise = one.Name.Surprises[newest].Percent;
            var low = Statistic.FromPrice(one.Bars[bar].Low);
            var move = one.Atr[bar];

            if (!SetupGates.Drift(surprise, rise) || !(close > low) || !(low > 0) || !(move > 0) || !Clears(indexCode, one, bar))
            {
                continue;
            }

            var risk = close - low;
            var byRisk = close + (DriftRule.TargetRiskMultiple * risk);
            var band = BandAbove(one, bar, close);
            var target = !double.IsNaN(band) && band < byRisk ? band : byRisk;
            var plan = new SetupPlan(risk / move, (target - close) / risk, null, DriftRule.CapSessions);
            var anchor = new SetupAnchor(calendar[at], close, low, target, null, DriftRule.CapSessions, risk / move);
            var passes = open && 0 < liveWindow && rise >= liveRise && volume >= liveVolume && liveMultiple == DriftRule.TargetRiskMultiple;
            var readings = Readings(indexCode, name, one, closes[name], bar, sessions[at], members, calendar, at, market, context);

            readings[LedgerReadings.IndexOf("reward_to_risk")] = plan.RewardToRisk;
            readings[LedgerReadings.IndexOf("freshness")] = 0;
            readings[LedgerReadings.IndexOf("volume_multiple")] = LedgerReadings.Figure(volume);
            readings[LedgerReadings.IndexOf("reaction_moves")] = LedgerReadings.Figure(rise);

            rows.Add(new SetupRow(indexCode, DriftRule.Name, one.Name.Ticker, calendar[at], DriftWords, passes, anchor, plan, readings));
        }

        return new FamilySetups(DriftRule.Name, held.Length, Ordered(rows));
    }

    // The lowest band whose low edge sits the drift rule's typical moves or more above the close, as its sweep reads
    // it; none where none does.
    static double BandAbove(SweepSeries one, int bar, double close)
    {
        var move = one.Atr[bar];
        var edges = SweepCandidates.LevelsOn(one, bar)
            .Select(level => Statistic.FromPrice(level.LowEdge))
            .Where(edge => edge > close && (edge - close) / move >= DriftRule.TargetBandMoves)
            .ToArray();

        return edges.Length > 0 ? edges.Min() : double.NaN;
    }

    // The index's floors on the member's session: its close as traded and its dollar volume over the 50 sessions to it.
    static bool Clears(string indexCode, SweepSeries one, int bar)
    {
        var day = one.Bars[bar];
        var traded = day.RawClose > 0m ? day.RawClose : day.Close;
        var window = one.Bars[Math.Max(0, bar - MemberReadings.DollarVolumeSessions + 1)..(bar + 1)].Select(each => (each.Close, each.Volume)).ToArray();

        return SetupGates.ClearsTheFloors(indexCode, traded, MemberReadings.DollarVolume(window));
    }

    static IReadOnlyList<SetupRow> Ordered(List<SetupRow> rows)
    {
        rows.Sort((one, other) => string.CompareOrdinal(one.Ticker, other.Ticker));

        return rows;
    }

    // Every reading the catalogue defines that is not the family's own, from the series to the bar, the session's
    // places and breadth, the market series to the session's day and the quarters filed before it.
    public static double?[] Readings(
        string indexCode,
        int name,
        SweepSeries one,
        double[] closes,
        int bar,
        SweepColumns.Session session,
        SweepBenchmark.Members members,
        IReadOnlyList<DateOnly> calendar,
        int at,
        LedgerMarket market,
        LedgerContext context)
    {
        var readings = new double?[LedgerReadings.Count];
        var close = closes[bar];
        var high = LiveHigh;
        var highs = SweepAxes.ReferenceHighs.Count;
        var day = calendar[at];
        var ticker = one.Name.Ticker;
        var income = context.Income.GetValueOrDefault(ticker) ?? [];
        var sector = context.Sectors.GetValueOrDefault(ticker);
        var liquidityWindow = one.Bars[Math.Max(0, bar - LedgerReadings.LiquiditySessions + 1)..(bar + 1)].Select(each => (Statistic.FromPrice(each.Close), each.Volume)).ToArray();
        var newest = one.NewestSurprise[bar];
        var indexSeries = market.Of(LedgerMarket.IndexSeriesOf(indexCode));
        var vix = market.Of(LedgerMarket.Vix);
        var large = market.Of(LedgerMarket.Large);
        var mid = market.Of(LedgerMarket.Mid);
        var small = market.Of(LedgerMarket.Small);
        var credit = market.Of(LedgerMarket.Credit);

        void Set(string column, double? value) => readings[LedgerReadings.IndexOf(column)] = value;

        Set("close_over_twenty", LedgerReadings.Over(close, one.Sma20[bar]));
        Set("close_over_fifty", LedgerReadings.Over(close, one.Sma50[bar]));
        Set("close_over_long", LedgerReadings.Over(close, one.Sma200[bar]));
        Set("fifty_over_long", LedgerReadings.Over(one.Sma50[bar], one.Sma200[bar]));
        Set("move_share", LedgerReadings.Over(one.Atr[bar], close));
        Set("rsi", LedgerReadings.Figure(one.Rsi[bar]));
        Set("rsi_up", LedgerReadings.Flag(one.RsiUp[bar]));
        Set("volume_ratio", LedgerReadings.Figure(one.VolumeRatio[bar]));
        Set("return_quarter", LedgerReadings.Figure(one.Return63[bar]));
        Set("return_half_year", LedgerReadings.Figure(one.Return126[bar]));
        Set("return_twelve_less_one", LedgerReadings.Figure(one.ReturnTwelveLessOne[bar]));
        Set("strength", session.Strength[(int)StrengthMeasure.ThreeAndSixMonths].TryGetValue(name, out var live) ? live : null);
        Set("strength_twelve_less_one", session.Strength[TwelveLessOnePlace].TryGetValue(name, out var twelve) ? twelve : null);
        Set("high_ratio", LedgerReadings.Over(close, one.High252[bar]));
        Set("since_high", one.SinceHigh[(bar * highs) + high] is var since && since >= 0 ? since : null);
        Set("depth", LedgerReadings.Figure(one.Depth[(bar * highs) + high]));
        Set("dry_up", LedgerReadings.Figure(one.DryUp[(bar * highs) + high]));
        Set("gap_down", LedgerReadings.Figure(one.GapDown[(bar * highs) + high]));
        Set("rsi_low", LedgerReadings.Figure(one.RsiLow[(bar * highs) + high]));
        Set("tightness", LedgerReadings.Figure(one.Tightness[bar]));
        Set("liquidity", LedgerReadings.Liquidity(liquidityWindow));
        Set("earnings_sessions", EarningsSessions(one.Name.Prints, calendar, at));
        Set("surprise_sessions", newest >= 0 ? at - one.SessionAt[one.SurpriseBar[newest]] : null);
        Set("surprise_percent", newest >= 0 ? LedgerReadings.Figure(one.Name.Surprises[newest].Percent) : null);
        Set("breadth", session.Breadth);
        Set("highs_less_lows", members.Names[at].Length > 0 ? 1.0 * (context.Highs[at] - context.Lows[at]) / members.Names[at].Length : null);
        Set("index_over_long", indexSeries is null ? null : SeriesReadings.OverAverage(indexSeries, day, LedgerReadings.IndexAverageSessions));
        Set("vix", vix is null ? null : SeriesReadings.CloseOn(vix, day));
        Set("vix_change", vix is null ? null : SeriesReadings.OverBefore(vix, day, LedgerReadings.VixChangeSessions));
        Set("mid_over_large", mid is null || large is null ? null : SeriesReadings.RelativeReturn(mid, large, day, LedgerReadings.RelativeReturnSessions));
        Set("small_over_large", small is null || large is null ? null : SeriesReadings.RelativeReturn(small, large, day, LedgerReadings.RelativeReturnSessions));
        Set("credit_over_fifty", credit is null ? null : SeriesReadings.OverAverage(credit, day, LedgerReadings.CreditAverageSessions));
        Set("profit", LedgerReadings.Flag(MemberReadings.Profit(income, day)));
        Set("coverage", LedgerReadings.Flag(MemberReadings.Coverage(income, day, sector)));

        return readings;
    }

    // The place among a session's places of the return over the 231 sessions ending 21 before, after the live measure's
    // and the 126-session return's.
    const int TwelveLessOnePlace = 2;

    // The weekdays from the day to the next report on file after it, the report's own day counted and the day's not;
    // none where no report follows. Weekdays rather than sessions, since on the night the calendar reaches no session
    // after the night and a reading must read the same on the night and from the history.
    public static double? EarningsSessions(IReadOnlyList<DateOnly> prints, IReadOnlyList<DateOnly> calendar, int at)
    {
        var day = calendar[at];
        DateOnly? next = null;

        foreach (var print in prints)
        {
            if (print > day && (next is not { } held || print < held))
            {
                next = print;
            }
        }

        if (next is not { } report)
        {
            return null;
        }

        var weekdays = 0;

        for (var each = day.AddDays(1); each <= report; each = each.AddDays(1))
        {
            if (each.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday))
            {
                weekdays++;
            }
        }

        return weekdays;
    }
}
