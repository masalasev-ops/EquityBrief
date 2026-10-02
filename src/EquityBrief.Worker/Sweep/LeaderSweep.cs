using EquityBrief.Core.Families;
using EquityBrief.Core.Prices;
using EquityBrief.Core.Returns;
using EquityBrief.Core.Sweep;

namespace EquityBrief.Worker.Sweep;

// Where one member stood on one session the loosest setting of the sector leaders' grid reads: its sector's rank
// and how many of the sector's members held a return, its place among them and its return.
public readonly record struct LeaderStandingRead(int Name, int Bar, int Session, int Rank, int Counted, int Place, double Return);

// A leader the pullback's setup, trigger and trade pass on its session: where it stood, the close, the typical
// move and the stop's distance in it, and what the pullback's trade there came to under the pullback's own
// exit, with its benchmark.
public readonly record struct LeaderReading(
    int Name,
    int Bar,
    int Session,
    int Rank,
    int Counted,
    int Place,
    double Return,
    double Close,
    double Move,
    double StopMoves,
    double? Result,
    int Ends,
    double Benchmark);

// The sector leaders' sweep: each session's sectors ranked and each member placed by the night's own rule, the
// pullback's setup, trigger and trade at the live settings read with leadership in place of the trend and
// strength gate, the listings each setting of the grid makes, and the pullback's trade and its benchmark as
// the sweep's candidates score them.
// see: A setup family's sweep replays its own rule over the stored history and proposes the best edge among the settings meeting its floors
public static class LeaderSweep
{
    public static FamilyGrid Grid { get; } = new(
        [
            ("sectors", [2, LeaderRule.TopSectors, 4]),
            ("share", [LeaderRule.QuarterOf, 3, 2]),
        ],
        [1, 0]);

    // The live pullback's settings on the grid every candidate's corner is placed on, and where its plan, its
    // exit, its reference high and its trigger's arrival sit among a candidate's readings.
    public static DialSetting LiveSetting => SweepGrid.Extended.Carry(SweepGrid.Coarse, DialSetting.LiveOnCoarse);

    static int PlanAt => SweepCandidate.PlanAt(SweepDesign.Live.Plan, SweepDesign.Live.Support);

    static int ExitAt => SweepDesign.Live.ExitIndex;

    static int HighAt => SweepAxes.ReferenceHighs.ToList().IndexOf(SweepDesign.Live.ReferenceHigh);

    static int AgeAt => SweepCandidate.AgeAt(SweepDesign.Live.Trigger, SweepDesign.Live.Support);

    // Every scored session the market check left open: its members with a bar and no gap ranked by the night's
    // own rule on their long return and the sector the membership names, and those inside the loosest setting's
    // sectors and share kept. A name the membership names no sector for is in no ranking, and is counted.
    public static (IReadOnlyList<LeaderStandingRead> Leaders, int Carrying, int NotCarrying) Standings(
        IReadOnlyList<SweepSeries> series,
        IReadOnlyList<SweepColumns.Session> sessions,
        SweepBenchmark.Members members,
        int firstScored)
    {
        var top = Grid.Dials[0].Levels.Max();
        var share = Grid.Dials[1].Levels.Min();
        var leaders = new List<LeaderStandingRead>();

        for (var session = firstScored; session < members.Names.Length; session++)
        {
            if (!(sessions[session].Breadth >= FamilySweep.MarketFloor))
            {
                continue;
            }

            var names = members.Names[session];
            var bars = members.Bars[session];
            var (rows, places) = StandingsOn(series, members, session);

            for (var at = 0; at < names.Length; at++)
            {
                var place = places[rows[at].Ticker];

                if (place.Sector is { Rank: { } rank } sector && place.Place is { } within && rank <= top && within <= Cut(sector.Counted, share))
                {
                    leaders.Add(new LeaderStandingRead(names[at], bars[at], session, rank, sector.Counted, within, rows[at].Return!.Value));
                }
            }
        }

        var carrying = series.Count(one => one.Name.Sector is { Length: > 0 });

        return (leaders, carrying, series.Count - carrying);
    }

    // One session's standings by the night's own rule: each member with a bar and no gap, its sector as the
    // membership files it and its long return, in the members' order, and where each stands.
    public static (IReadOnlyList<(string Ticker, string? Sector, double? Return)> Rows, IReadOnlyDictionary<string, LeaderStanding> Places) StandingsOn(
        IReadOnlyList<SweepSeries> series,
        SweepBenchmark.Members members,
        int session)
    {
        var names = members.Names[session];
        var bars = members.Bars[session];
        var rows = new List<(string Ticker, string? Sector, double? Return)>(names.Length);

        for (var at = 0; at < names.Length; at++)
        {
            var one = series[names[at]];
            var held = one.Return126[bars[at]];

            rows.Add((one.Name.Ticker, one.Name.Sector, double.IsNaN(held) ? null : held));
        }

        return (rows, LeaderRule.Standings(rows).Members);
    }

    // The members a share of a sector takes: a quarter, a third or a half of those holding a return, rounded up.
    public static int Cut(int counted, double share) => (int)Math.Ceiling(counted / share);

    // The leaders the pullback's setup, trigger and trade pass at the live settings, the market check among them,
    // read with a strength that passes every bar so leadership stands in place of the trend and strength gate,
    // and none inside the live design's earnings window, an exclusion the filter carries for them.
    public static IReadOnlyList<LeaderReading> Readings(IReadOnlyList<LeaderStandingRead> leaders, IReadOnlyList<SweepCandidate> candidates, IReadOnlyList<SweepSeries> series)
    {
        var standing = leaders.ToDictionary(one => (one.Name, one.Session));
        var support = (int)SweepDesign.Live.Support;
        var found = new List<LeaderReading>();

        foreach (var candidate in candidates)
        {
            if (!standing.TryGetValue((candidate.Name, candidate.Session), out var held)
                || candidate.Plans[PlanAt] is not { } plan
                || candidate.Band[support] < 0
                || candidate.Age[AgeAt] < 0
                || (candidate.Earnings >= 0 && candidate.Earnings <= SweepDesign.Live.EarningsWindow))
            {
                continue;
            }

            var corner = SweepCorner.Of(SweepGrid.Extended, 1.0, candidate.Depth[HighAt], candidate.DryUp[HighAt], candidate.Age[AgeAt], plan.RewardToRisk, plan.StopMoves, candidate.Breadth, candidate.Band[support]);

            if (corner is not { } placed || !placed.Passes(LiveSetting))
            {
                continue;
            }

            var one = series[held.Name];
            var code = plan.Code[ExitAt];
            double? result = code is SweepPlanOutcomes.Win or SweepPlanOutcomes.Loss or SweepPlanOutcomes.Unresolved && !float.IsNaN(plan.Multiple[ExitAt]) ? plan.Multiple[ExitAt] : null;

            found.Add(new LeaderReading(held.Name, held.Bar, held.Session, held.Rank, held.Counted, held.Place, held.Return, Statistic.FromPrice(one.Bars[held.Bar].Close), one.Atr[held.Bar], plan.StopMoves, result, plan.Ends[ExitAt], plan.Benchmark[ExitAt]));
        }

        return found;
    }

    // The listings one setting makes: a leader of one of the setting's top sectors inside its share, bought on the
    // pullback's plan, in the order of its sector's rank and then its return.
    public static IEnumerable<FamilyListing> Listings(IReadOnlyList<LeaderReading> readings, int[] setting)
    {
        var top = Grid.Value(setting, 0);
        var share = Grid.Value(setting, 1);

        foreach (var reading in readings)
        {
            if (reading.Rank > top || reading.Place > Cut(reading.Counted, share))
            {
                continue;
            }

            yield return new FamilyListing(reading.Name, reading.Bar, reading.Session, -reading.Rank, reading.Return, reading.Close, reading.Close - (reading.StopMoves * reading.Move), double.NaN, double.NaN, ForwardReturnSeries.SetupSessionCap, reading.Move);
        }
    }

    public static (double? Result, int Sessions) ExitOf(LeaderReading reading) => (reading.Result, reading.Ends);
}
