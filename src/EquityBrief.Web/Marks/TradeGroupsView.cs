using System.Globalization;

namespace EquityBrief.Web.Marks;

// One group of trades as every page counts it: those bought, open, closed, closed for a gain, at their target, at their stop,
// and the mean return on the buy over all of them, an open one's at tonight's close.
public sealed record TradeCountView(int Bought, int Open, int Closed, int MadeMoney, int AtTarget, int AtStop, double? MeanReturn, int Selling = 0);

// A trade an earlier version of a family's live rule still holds of a stock: the family, the version that bought it, the day.
public sealed record EarlierHoldingView(string Family, string Version, DateOnly Bought)
{
    public string Words(string setup) =>
        FormattableString.Invariant($"An earlier version of {setup} still holds this stock, bought on {Bought.ToString("d MMM yyyy", CultureInfo.InvariantCulture)} under {Version}. It counts among {TradeGroupsView.SystemWords} and not toward this version's verdict.");
}

// The named groups, once: the system's trades, every trade the live rules bought under any version since each family first
// went live; this version's trades, those of each rule as registered now, which alone count toward a verdict; the same
// trades before this version, those a replay under the version reproduced from before its first session, kept apart under
// each account; the earlier versions' trades; and the stocks to buy at tomorrow's open, which are in none of them until
// bought. The system's are the other three added. The words each group is named in are here, so that every page says the
// same.
// see: Every count of trades names its group and one function works the four groups
public sealed record TradeGroupsView(DateOnly? FirstLive, DateOnly? VersionSince, TradeCountView System, TradeCountView ThisVersion, TradeCountView EarlierVersions, int ToBuy, int ToBuyFromReshuffles = 0)
{
    // The new picks among those to buy, the rest being the monthly books' reshuffle.
    public int NewPicks => ToBuy - ToBuyFromReshuffles;

    public TradeCountView SameTrades { get; init; } = new(0, 0, 0, 0, 0, 0, null);

    public const string SystemWords = "the system's trades";

    public const string VersionWords = "this version's trades";

    public const string SameGroupWords = "the same trades before this version";

    public const string EarlierWords = "earlier versions' trades";

    public const string ToBuyWords = "to buy at tomorrow's open";

    public const string SellsWords = "sells at tomorrow's open";

    // How many of a count of open trades sell at the next open, in the words every page counting them adds, none where none do.
    public static string Selling(int selling) =>
        selling <= 0 ? string.Empty : FormattableString.Invariant($"{selling} of them {(selling == 1 ? "sells" : "sell")} at tomorrow's open");

    // Past picks' tables hold some of the system's trades: how many of its open trades a table holds, from the 22.25
    // correction of 2026-11-11 how many more of its setups' open trades it draws no row of, a live rule's own list having
    // bought a stock the table's list held, and where the rest are.
    public string OpenHere(int here, string table, int more = 0) =>
        FormattableString.Invariant($"Of {SystemWords}, {System.Open} {(System.Open == 1 ? "is" : "are")} open{(System.Selling > 0 ? ", " + Selling(System.Selling) : string.Empty)}: {here} in {table}{(more > 0 ? FormattableString.Invariant($", {more} more of its setups' bought by their rules' own lists while {table}'s list held the stock, and") : " and")} {System.Open - here - more} under the other setups.");

    static string Day(DateOnly day) => day.ToString("d MMM", CultureInfo.InvariantCulture);

    // Paper trading's line above its fold of earlier versions, as the operator worded it, naming no account's start: each
    // account's day stands on its own tile.
    public string SystemLine =>
        FormattableString.Invariant($"The system has bought {System.Bought} {(System.Bought == 1 ? "trade" : "trades")}{(FirstLive is { } first ? " since " + Day(first) : string.Empty)}: {System.Open} open, {(System.Selling > 0 ? Selling(System.Selling) + ", " : string.Empty)}{System.AtTarget} reached their target, {System.AtStop} hit their stop, {(System.Closed - System.AtTarget - System.AtStop is > 0 and var other ? $"{other} sold by their rules' other exits, " : string.Empty)}{Overall} overall.");

    // Tonight's words for the system's open trades: how many, how many of them this version's, and how many the same trades
    // before this version where that group holds any open.
    public string OpenWords =>
        FormattableString.Invariant($"Of {SystemWords}, {System.Open} {(System.Open == 1 ? "is" : "are")} open, {ThisVersion.Open} of them this version's{(SameTrades.Open > 0 ? FormattableString.Invariant($", {SameTrades.Open} {SameGroupWords}") : string.Empty)}");

    // Paper trading's words beneath that line naming the same trades before this version among the system's, none where the
    // group holds none.
    public string SameLine =>
        SameTrades.Bought <= 0
            ? string.Empty
            : FormattableString.Invariant($"{SameTrades.Bought} of them {(SameTrades.Bought == 1 ? "is" : "are")} {SameGroupWords}, kept apart under each account below and in none of its figures.");

    string Overall => System.MeanReturn is { } mean ? (mean * 100).ToString("+0.0;-0.0;0.0", CultureInfo.InvariantCulture) + "%" : "no return yet";
}
