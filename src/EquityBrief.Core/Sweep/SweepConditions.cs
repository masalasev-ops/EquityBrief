using System.Globalization;

namespace EquityBrief.Core.Sweep;

// The seven conditions the rerun tests beside the nine dials, each read from the data as it stood on the
// session, on the operator's ruling of 2026-09-30. Each is a dial with "off" at its loose end and its tested
// settings after it, so the plateau, the depth, the proposal and the variants read it as they read any dial;
// two of them, the earnings beat and the pullback's shape, are two dials each.
// see: A starting point is proposed from the centre of a plateau of the stored history and never its best variation, and nothing is registered before the operator approves it
public static class SweepConditions
{
    // 1. Near the 52-week high: the close over the highest high of the 252 sessions to the session, at least
    // this (George and Hwang 2004).
    public static IReadOnlyList<double> HighRatios { get; } = [0.80, 0.85, 0.90, 0.95];

    // 2. Sector strength: the stock's sector among the strongest this many of the eleven by its median 126-session
    // return over the members labelled on the session (Moskowitz and Grinblatt 1999).
    public static IReadOnlyList<int> SectorTops { get; } = [6, 5, 4, 3];

    // 3. Volume on the turn-up day: the trigger event's session volume over the fifty-session average to the
    // session before it, at least this.
    public static IReadOnlyList<double> TurnVolumes { get; } = [1.0, 1.25, 1.5, 2.0];

    // 4. A momentum reset: Wilder's 14-day RSI below this on a session since the reference high, and up on the
    // trigger's session.
    public static IReadOnlyList<double> RsiLevels { get; } = [45, 40, 35, 30];

    // 5. A recent earnings beat: the newest print whose reaction session is on or before the session, its surprise
    // as filed, within this many sessions (Bernard and Thomas 1989), and by at least this much per cent.
    public static IReadOnlyList<int> BeatWindows { get; } = [63, 40, 20];

    public static IReadOnlyList<double> BeatSizes { get; } = [0, 5];

    // 6. Tightening during the pullback: the stored tightness reading, the last ten sessions' true range over the
    // last fifty's, at most this.
    public static IReadOnlyList<double> Tightness { get; } = [0.9, 0.75, 0.6];

    // 7. The pullback's shape: at most this many sessions since the reference high, and no gap down inside it
    // larger than this many typical moves.
    public static IReadOnlyList<int> PullbackLengths { get; } = [25, 15, 10];

    public static IReadOnlyList<double> GapMoves { get; } = [2.0, 1.5, 1.0];

    // The values beyond each dial's tight end the search may look at, at the spacing of the last two tested, and
    // within what the rule allows; a dial with none cannot be extended and is named as a limit of the search.
    public static IReadOnlyList<double> HighRatiosBeyond { get; } = [1.0];

    public static IReadOnlyList<int> SectorTopsBeyond { get; } = [2, 1];

    public static IReadOnlyList<double> TurnVolumesBeyond { get; } = [2.5, 3.0];

    public static IReadOnlyList<double> RsiLevelsBeyond { get; } = [25, 20];

    public static IReadOnlyList<int> BeatWindowsBeyond { get; } = [];

    public static IReadOnlyList<double> TightnessBeyond { get; } = [0.45, 0.3];

    public static IReadOnlyList<int> PullbackLengthsBeyond { get; } = [5];

    public static IReadOnlyList<double> GapMovesBeyond { get; } = [0.5];

    // The middle of a condition's tested settings, the looser of the two middles on an even count: what step (c)
    // crosses each surviving condition on at, a rule fixed before any result is read.
    public static int Middle(int count) => (count - 1) / 2;

    public const int Dials = 9;

    public static string Describe(ConditionSetting setting)
    {
        static string Number(double value) => value.ToString("0.##", CultureInfo.InvariantCulture);

        var parts = new List<string>();

        if (setting.High >= 0) parts.Add($"the close at least {Number(HighRatios[setting.High])} of its 52-week high");
        if (setting.Sector >= 0) parts.Add($"its sector among the strongest {SectorTops[setting.Sector]}");
        if (setting.Volume >= 0) parts.Add($"the turn-up day's volume at least {Number(TurnVolumes[setting.Volume])} times its average");
        if (setting.Rsi >= 0) parts.Add($"the RSI below {Number(RsiLevels[setting.Rsi])} during the pullback and turned up");
        if (setting.BeatWindow >= 0) parts.Add($"an earnings beat {(setting.BeatSize > 0 ? "of at least " + Number(BeatSizes[setting.BeatSize]) + "%" : "of any size")} within {BeatWindows[setting.BeatWindow]} sessions");
        if (setting.Tightness >= 0) parts.Add($"tightness at most {Number(Tightness[setting.Tightness])}");
        if (setting.Length >= 0) parts.Add($"the pullback at most {PullbackLengths[setting.Length]} sessions long");
        if (setting.Gap >= 0) parts.Add($"no gap down inside it larger than {Number(GapMoves[setting.Gap])} typical moves");

        return parts.Count == 0 ? "no condition" : string.Join(", ", parts);
    }
}

// One setting of the conditions' nine dials, as indexes into each condition's tested values, -1 for off. The
// beat's size dial has no off of its own, being read only while the beat's window is on.
public readonly record struct ConditionSetting(
    int High,
    int Sector,
    int Volume,
    int Rsi,
    int BeatWindow,
    int BeatSize,
    int Tightness,
    int Length,
    int Gap)
{
    public static ConditionSetting Off { get; } = new(-1, -1, -1, -1, -1, 0, -1, -1, -1);

    public bool IsOff => this == Off;

    public int[] Indexes => [High, Sector, Volume, Rsi, BeatWindow, BeatSize, Tightness, Length, Gap];

    public static ConditionSetting FromIndexes(IReadOnlyList<int> at) => new(at[0], at[1], at[2], at[3], at[4], at[5], at[6], at[7], at[8]);

    // The seven conditions this setting switches on, by number from 1.
    public IEnumerable<int> On
    {
        get
        {
            if (High >= 0) yield return 1;
            if (Sector >= 0) yield return 2;
            if (Volume >= 0) yield return 3;
            if (Rsi >= 0) yield return 4;
            if (BeatWindow >= 0) yield return 5;
            if (Tightness >= 0) yield return 6;
            if (Length >= 0 || Gap >= 0) yield return 7;
        }
    }

    // The condition readings of one candidate under one design, as the dials read them.
    public readonly record struct Readings(
        float HighRatio,
        int SectorRank,
        float TurnVolume,
        float RsiLow,
        bool RsiUp,
        int SurpriseSessions,
        float SurprisePercent,
        float Tightness,
        int PullbackSessions,
        float GapMoves);

    // Whether a candidate's readings pass this setting: a reading that is not available fails a condition that is on.
    public bool Passes(in Readings readings)
    {
        if (High >= 0 && !(readings.HighRatio >= SweepConditions.HighRatios[High]))
        {
            return false;
        }

        if (Sector >= 0 && (readings.SectorRank <= 0 || readings.SectorRank > SweepConditions.SectorTops[Sector]))
        {
            return false;
        }

        if (Volume >= 0 && !(readings.TurnVolume >= SweepConditions.TurnVolumes[Volume]))
        {
            return false;
        }

        if (Rsi >= 0 && !(readings.RsiUp && readings.RsiLow < SweepConditions.RsiLevels[Rsi]))
        {
            return false;
        }

        if (BeatWindow >= 0)
        {
            if (readings.SurpriseSessions < 0 || readings.SurpriseSessions > SweepConditions.BeatWindows[BeatWindow] || float.IsNaN(readings.SurprisePercent))
            {
                return false;
            }

            var size = SweepConditions.BeatSizes[Math.Max(0, BeatSize)];

            if (size == 0 ? !(readings.SurprisePercent > 0) : !(readings.SurprisePercent >= size))
            {
                return false;
            }
        }

        if (Tightness >= 0 && !(readings.Tightness <= SweepConditions.Tightness[Tightness]))
        {
            return false;
        }

        if (Length >= 0 && (readings.PullbackSessions < 0 || readings.PullbackSessions > SweepConditions.PullbackLengths[Length]))
        {
            return false;
        }

        if (Gap >= 0 && !(readings.GapMoves < SweepConditions.GapMoves[Gap]))
        {
            return false;
        }

        return true;
    }
}
