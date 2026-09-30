using System.Globalization;

namespace EquityBrief.Core.Sweep;

// The seven conditions the rerun tests beside the nine dials, each read from the data as it stood on the
// session, on the operator's ruling of 2026-09-30. Each is a dial with "off" at its loose end and its tested
// settings after it, so the plateau, the depth, the proposal and the variants read it as they read any dial;
// two of them, the earnings beat and the pullback's shape, are two dials each. Each tested list is followed by
// the values beyond its tight end the search may look at, at the spacing of the last two tested and within what
// the rule allows; a dial with none cannot be extended and is named as a limit of the search.
// see: A starting point is proposed from the deepest setting of a plateau on the edge and never its best variation, and nothing is registered before the operator approves it
public static class SweepConditions
{
    // 1. Near the 52-week high: the close over the highest high of the 252 sessions to the session, at least
    // this (George and Hwang 2004). A close cannot be above its own high, so 1.0 is the last value.
    public static IReadOnlyList<double> HighRatios { get; } = [0.80, 0.85, 0.90, 0.95];

    public static IReadOnlyList<double> HighRatiosBeyond { get; } = [1.0];

    // 2. Sector strength: the stock's sector among the strongest this many of the eleven by its median 126-session
    // return over the members labelled on the session (Moskowitz and Grinblatt 1999).
    public static IReadOnlyList<int> SectorTops { get; } = [6, 5, 4, 3];

    public static IReadOnlyList<int> SectorTopsBeyond { get; } = [2, 1];

    // 3. Volume on the turn-up day: the trigger event's session volume over the fifty-session average to the
    // session before it, at least this.
    public static IReadOnlyList<double> TurnVolumes { get; } = [1.0, 1.25, 1.5, 2.0];

    public static IReadOnlyList<double> TurnVolumesBeyond { get; } = [2.5, 3.0];

    // 4. A momentum reset: Wilder's 14-day RSI below this on a session since the reference high, and up on the
    // trigger's session.
    public static IReadOnlyList<double> RsiLevels { get; } = [45, 40, 35, 30];

    public static IReadOnlyList<double> RsiLevelsBeyond { get; } = [25, 20];

    // 5. A recent earnings beat: the newest print whose reaction session is on or before the session, its surprise
    // as filed, within this many sessions (Bernard and Thomas 1989), and by at least this much per cent. A window
    // of 20 less 20 is no window, so the window cannot be extended.
    public static IReadOnlyList<int> BeatWindows { get; } = [63, 40, 20];

    public static IReadOnlyList<int> BeatWindowsBeyond { get; } = [];

    public static IReadOnlyList<double> BeatSizes { get; } = [0, 5];

    // 6. Tightening during the pullback: the stored tightness reading, the last ten sessions' true range over the
    // last fifty's, at most this.
    public static IReadOnlyList<double> Tightness { get; } = [0.9, 0.75, 0.6];

    public static IReadOnlyList<double> TightnessBeyond { get; } = [0.45, 0.3];

    // 7. The pullback's shape: at most this many sessions since the reference high, and no gap down inside it
    // larger than this many typical moves. Nought sessions is no pullback, so 5 is the last length.
    public static IReadOnlyList<int> PullbackLengths { get; } = [25, 15, 10];

    public static IReadOnlyList<int> PullbackLengthsBeyond { get; } = [5];

    public static IReadOnlyList<double> GapMoves { get; } = [2.0, 1.5, 1.0];

    public static IReadOnlyList<double> GapMovesBeyond { get; } = [0.5];

    // Each dial's whole list, tested values first and the values beyond after, which a setting's index reads.
    public static IReadOnlyList<double> AllHighRatios { get; } = [.. HighRatios, .. HighRatiosBeyond];

    public static IReadOnlyList<int> AllSectorTops { get; } = [.. SectorTops, .. SectorTopsBeyond];

    public static IReadOnlyList<double> AllTurnVolumes { get; } = [.. TurnVolumes, .. TurnVolumesBeyond];

    public static IReadOnlyList<double> AllRsiLevels { get; } = [.. RsiLevels, .. RsiLevelsBeyond];

    public static IReadOnlyList<int> AllBeatWindows { get; } = [.. BeatWindows, .. BeatWindowsBeyond];

    public static IReadOnlyList<double> AllTightness { get; } = [.. Tightness, .. TightnessBeyond];

    public static IReadOnlyList<int> AllPullbackLengths { get; } = [.. PullbackLengths, .. PullbackLengthsBeyond];

    public static IReadOnlyList<double> AllGapMoves { get; } = [.. GapMoves, .. GapMovesBeyond];

    // The seven conditions, numbered from 1, and the sessions the 52-week high is read over.
    public const int Count = 7;

    public const int HighSessions = 252;

    // The sessions the volume average the turn-up day's volume is read against runs over, the night's fifty.
    public const int VolumeSessions = 50;

    // The middle of a condition's tested settings, the looser of the two middles on an even count: what step (c)
    // crosses each surviving condition on at, a rule fixed before any result is read.
    public static int Middle(int count) => (count - 1) / 2;

    // The tested settings of the conditions, each one condition switched on at one of its values with the rest
    // off, in condition order: 4, 4, 4, 4, 3 windows by 2 sizes, 3, and 3 lengths with 3 gaps, 31 in all.
    public static IReadOnlyList<(int Condition, ConditionSetting Setting)> Settings
    {
        get
        {
            var settings = new List<(int, ConditionSetting)>();

            for (var at = 0; at < HighRatios.Count; at++) settings.Add((1, ConditionSetting.Off with { High = at }));
            for (var at = 0; at < SectorTops.Count; at++) settings.Add((2, ConditionSetting.Off with { Sector = at }));
            for (var at = 0; at < TurnVolumes.Count; at++) settings.Add((3, ConditionSetting.Off with { Volume = at }));
            for (var at = 0; at < RsiLevels.Count; at++) settings.Add((4, ConditionSetting.Off with { Rsi = at }));

            for (var window = 0; window < BeatWindows.Count; window++)
            {
                for (var size = 0; size < BeatSizes.Count; size++)
                {
                    settings.Add((5, ConditionSetting.Off with { BeatWindow = window, BeatSize = size }));
                }
            }

            for (var at = 0; at < Tightness.Count; at++) settings.Add((6, ConditionSetting.Off with { Tightness = at }));
            for (var at = 0; at < PullbackLengths.Count; at++) settings.Add((7, ConditionSetting.Off with { Length = at }));
            for (var at = 0; at < GapMoves.Count; at++) settings.Add((7, ConditionSetting.Off with { Gap = at }));

            return settings;
        }
    }

    // A condition switched on at the middle of its tested settings, what step (c) crosses a survivor on at: the
    // beat at a 40-session window of any size, and the shape at 15 sessions with a 1.5-move gap.
    public static ConditionSetting OnAtTheMiddle(int condition) => condition switch
    {
        1 => ConditionSetting.Off with { High = Middle(HighRatios.Count) },
        2 => ConditionSetting.Off with { Sector = Middle(SectorTops.Count) },
        3 => ConditionSetting.Off with { Volume = Middle(TurnVolumes.Count) },
        4 => ConditionSetting.Off with { Rsi = Middle(RsiLevels.Count) },
        5 => ConditionSetting.Off with { BeatWindow = Middle(BeatWindows.Count), BeatSize = 0 },
        6 => ConditionSetting.Off with { Tightness = Middle(Tightness.Count) },
        7 => ConditionSetting.Off with { Length = Middle(PullbackLengths.Count), Gap = Middle(GapMoves.Count) },
        _ => throw new ArgumentOutOfRangeException(nameof(condition), condition, "no such condition"),
    };

    // Two settings joined: each condition on in either, at that one's value.
    public static ConditionSetting Join(ConditionSetting one, ConditionSetting other) => new(
        Math.Max(one.High, other.High),
        Math.Max(one.Sector, other.Sector),
        Math.Max(one.Volume, other.Volume),
        Math.Max(one.Rsi, other.Rsi),
        Math.Max(one.BeatWindow, other.BeatWindow),
        one.BeatWindow >= 0 ? one.BeatSize : other.BeatSize,
        Math.Max(one.Tightness, other.Tightness),
        Math.Max(one.Length, other.Length),
        Math.Max(one.Gap, other.Gap));

    public static string Name(int condition) => condition switch
    {
        1 => "near the 52-week high",
        2 => "sector strength",
        3 => "volume on the turn-up day",
        4 => "a momentum reset",
        5 => "a recent earnings beat",
        6 => "tightening during the pullback",
        7 => "the pullback's shape",
        _ => throw new ArgumentOutOfRangeException(nameof(condition), condition, "no such condition"),
    };

    public static string Describe(ConditionSetting setting)
    {
        static string Number(double value) => value.ToString("0.##", CultureInfo.InvariantCulture);

        var parts = new List<string>();

        if (setting.High >= 0) parts.Add($"the close at least {Number(AllHighRatios[setting.High])} of its 52-week high");
        if (setting.Sector >= 0) parts.Add($"its sector among the strongest {AllSectorTops[setting.Sector]}");
        if (setting.Volume >= 0) parts.Add($"the turn-up day's volume at least {Number(AllTurnVolumes[setting.Volume])} times its average");
        if (setting.Rsi >= 0) parts.Add($"the RSI below {Number(AllRsiLevels[setting.Rsi])} during the pullback and turned up");
        if (setting.BeatWindow >= 0) parts.Add($"an earnings beat {(setting.BeatSize > 0 ? "of at least " + Number(BeatSizes[setting.BeatSize]) + "%" : "of any size")} within {AllBeatWindows[setting.BeatWindow]} sessions");
        if (setting.Tightness >= 0) parts.Add($"tightness at most {Number(AllTightness[setting.Tightness])}");
        if (setting.Length >= 0) parts.Add($"the pullback at most {AllPullbackLengths[setting.Length]} sessions long");
        if (setting.Gap >= 0) parts.Add($"no gap down inside it larger than {Number(AllGapMoves[setting.Gap])} typical moves");

        return parts.Count == 0 ? "no condition" : string.Join(", ", parts);
    }
}

// One setting of the conditions' nine dials, as indexes into each condition's values, -1 for off. The beat's
// size dial has no off of its own, being read only while the beat's window is on.
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

    public string Key => string.Join(",", Indexes.Select(at => at.ToString(CultureInfo.InvariantCulture)));

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
        if (High >= 0 && !(readings.HighRatio >= SweepConditions.AllHighRatios[High]))
        {
            return false;
        }

        if (Sector >= 0 && (readings.SectorRank <= 0 || readings.SectorRank > SweepConditions.AllSectorTops[Sector]))
        {
            return false;
        }

        if (Volume >= 0 && !(readings.TurnVolume >= SweepConditions.AllTurnVolumes[Volume]))
        {
            return false;
        }

        if (Rsi >= 0 && !(readings.RsiUp && readings.RsiLow < SweepConditions.AllRsiLevels[Rsi]))
        {
            return false;
        }

        if (BeatWindow >= 0)
        {
            if (readings.SurpriseSessions < 0 || readings.SurpriseSessions > SweepConditions.AllBeatWindows[BeatWindow] || float.IsNaN(readings.SurprisePercent))
            {
                return false;
            }

            var size = SweepConditions.BeatSizes[Math.Max(0, BeatSize)];

            if (size == 0 ? !(readings.SurprisePercent > 0) : !(readings.SurprisePercent >= size))
            {
                return false;
            }
        }

        if (Tightness >= 0 && !(readings.Tightness <= SweepConditions.AllTightness[Tightness]))
        {
            return false;
        }

        if (Length >= 0 && (readings.PullbackSessions < 0 || readings.PullbackSessions > SweepConditions.AllPullbackLengths[Length]))
        {
            return false;
        }

        if (Gap >= 0 && !(readings.GapMoves < SweepConditions.AllGapMoves[Gap]))
        {
            return false;
        }

        return true;
    }
}
