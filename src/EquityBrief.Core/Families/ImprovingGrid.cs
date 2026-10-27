using System.Globalization;
using EquityBrief.Core.Ledger;

namespace EquityBrief.Core.Families;

// One setting of the Fundamentals family's history test: the score a newly read filing must lift a stock to, whether a
// flag must have stood on the night before, whether the score must rise above the filing before's, whether the sales
// growth must be faster than the quarter before's, the score a trade is held at or above, each score in nine and read as
// a share of the checks that applied, and the most sessions a trade is held.
public sealed record ImprovingSweepSetting(int BuyInNine, bool FlagFirst, bool Rise, bool Faster, int HoldInNine, int Sessions)
{
    public string Key => string.Create(
        CultureInfo.InvariantCulture,
        $"buy={BuyInNine}|flag={(FlagFirst ? "first" : "off")}|rise={(Rise ? "on" : "off")}|faster={(Faster ? "on" : "off")}|hold={HoldInNine}|limit={Sessions}");

    public string Words => string.Create(
        CultureInfo.InvariantCulture,
        $"a newly read filing lifting its score to at least {BuyInNine} in 9{(Rise ? " and above its previous filing's" : string.Empty)}{(Faster ? ", its sales growth faster than the quarter before's" : string.Empty)}{(FlagFirst ? ", a flag standing the night before" : string.Empty)}; held while its score stays at {HoldInNine} in 9 or better, at most {Sessions} sessions");

    // How many dials a setting stands from another.
    public int Changes(ImprovingSweepSetting other) =>
        (BuyInNine != other.BuyInNine ? 1 : 0)
        + (FlagFirst != other.FlagFirst ? 1 : 0)
        + (Rise != other.Rise ? 1 : 0)
        + (Faster != other.Faster ? 1 : 0)
        + (HoldInNine != other.HoldInNine ? 1 : 0)
        + (Sessions != other.Sessions ? 1 : 0);
}

// The Fundamentals family's history test's grid: the score to buy at 7, 8 or 9 in 9; a flag required first or not; the
// score's rise over its previous filing required or not; the sales growth speeding up required or not; held at 6 or 7 in
// 9; and the time limit at 63, 126 or 252 sessions. The starting setting is the night's own.
// see: The Fundamentals family's history test walks its trades over the pulled history on 144 settings an index and registers nothing
public static class ImprovingGrid
{
    public static IReadOnlyList<int> BuyLevels { get; } = [7, 8, 9];

    public static IReadOnlyList<int> HoldLevels { get; } = [6, 7];

    public static IReadOnlyList<int> SessionLevels { get; } = [63, 126, 252];

    public static ImprovingSweepSetting Starting { get; } =
        new(ImprovingRule.Starting.BuyInNine, false, true, true, ImprovingRule.Starting.HoldInNine, ImprovingRule.Starting.Sessions);

    // Whether a newly read filing buys under a setting: its score of five checks or more at the bar, above the filing
    // before's where the setting asks it, its sales growth faster than the quarter before's where it asks that, and a flag
    // standing the night before where it asks one. At the starting setting it is the night's own answer.
    public static bool Buys(int passed, int applied, int passedBefore, int appliedBefore, double? growthChange, bool flaggedBefore, ImprovingSweepSetting setting) =>
        FundamentalScorecard.Clears(passed, applied, setting.BuyInNine)
        && (!setting.Rise || ImprovingRule.Share(passed, applied) > ImprovingRule.Share(passedBefore, appliedBefore))
        && (!setting.Faster || growthChange > 0)
        && (!setting.FlagFirst || flaggedBefore);

    // Every level of every dial, the score to buy slowest.
    public static IReadOnlyList<ImprovingSweepSetting> Settings { get; } =
    [
        .. from buy in BuyLevels
           from flag in new[] { false, true }
           from rise in new[] { true, false }
           from faster in new[] { true, false }
           from hold in HoldLevels
           from sessions in SessionLevels
           select new ImprovingSweepSetting(buy, flag, rise, faster, hold, sessions),
    ];
}
