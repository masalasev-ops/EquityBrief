using System.Globalization;
using EquityBrief.Core.Ledger;
using EquityBrief.Core.Readings;

namespace EquityBrief.Core.Families;

// Which margins the fundamentals-first family asks to have widened on the year: the gross or the operating, both, or the
// operating alone.
public enum MarginRule
{
    Either,
    Both,
    Operating,
}

// One setting of the fundamentals-first family: the least revenue growth on the year-earlier quarter, which margins must
// have widened, and the least cash from operations over net income.
public sealed record FundamentalsSetting(double GrowthFloor, MarginRule Margins, double CashFloor)
{
    // The setting as the search and a card key it.
    public string Key => FormattableString.Invariant($"growth>{GrowthFloor:0.##}|margins={Margins.ToString().ToLowerInvariant()}|cash>={CashFloor:0.##}");

    // The setting in words, as a card states the rule.
    public string Words =>
        FormattableString.Invariant($"revenue up more than {GrowthFloor * 100:0} per cent on the year-earlier quarter and growing faster than the quarter before")
        + Margins switch
        {
            MarginRule.Both => ", the gross and the operating margin both up on the year",
            MarginRule.Operating => ", the operating margin up on the year",
            _ => ", the gross or the operating margin up on the year",
        }
        + FormattableString.Invariant($", cash from operations at least {CashFloor.ToString("0.0#", CultureInfo.InvariantCulture)} times net income");
}

// The fundamentals-first family: a company whose business is improving, in an uptrend, bought at the pullback's buy point
// at its base settings. Each part of the rule is read from the ledger's catalogue as it stood on the session, its
// business readings from the facts the company filed before it as first filed, and its profit from the quarters filed
// before it in the index's own form: the four newest summing above nothing and the newest above nothing. A reading not
// held passes no part. Post-earnings drift is not a trigger, since its edge has faded outside the smallest stocks.
// see: The fundamentals-first family buys an improving business in an uptrend at the pullback's buy point
public static class FundamentalsRule
{
    public const string Name = "fundamentals";

    // The parts of the rule a member fails, in the rule's order, the first the row names.
    public const string NoProfit = "the profit check in the index's form";

    public const string NoRevenue = "the revenue check";

    public const string NoMargin = "the margin check";

    public const string NoCash = "the cash check";

    public const string NoTrend = "the trend check";

    public const string NoReadings = "no readings read tonight";

    // The family's dials, each at three levels, and its provisional setting, the middle of none but the brief's own words:
    // revenue growing at all, either margin widening and cash at least the income.
    public static IReadOnlyList<double> GrowthFloors { get; } = [0, 0.05, 0.10];

    public static IReadOnlyList<MarginRule> MarginRules { get; } = [MarginRule.Either, MarginRule.Both, MarginRule.Operating];

    public static IReadOnlyList<double> CashFloors { get; } = [0.8, 1.0, 1.2];

    public static FundamentalsSetting Provisional { get; } = new(0, MarginRule.Either, 1.0);

    // The 27 settings the search reads, registered before it runs: every level of every dial, the growth floor slowest.
    public static IReadOnlyList<FundamentalsSetting> Grid { get; } =
    [
        .. GrowthFloors.SelectMany(growth => MarginRules.SelectMany(margins => CashFloors.Select(cash => new FundamentalsSetting(growth, margins, cash)))),
    ];

    static readonly int RevenueGrowth = LedgerReadings.IndexOf("revenue_growth");
    static readonly int GrowthChange = LedgerReadings.IndexOf("growth_change");
    static readonly int GrossMarginChange = LedgerReadings.IndexOf("gross_margin_change");
    static readonly int OperatingMarginChange = LedgerReadings.IndexOf("operating_margin_change");
    static readonly int CashOverIncome = LedgerReadings.IndexOf("cash_over_income");
    static readonly int CloseOverLong = LedgerReadings.IndexOf("close_over_long");
    static readonly int FiftyOverLong = LedgerReadings.IndexOf("fifty_over_long");

    // The profit check in the index's own form: the four newest quarters filed before the session summing their net
    // income above nothing, and the newest of them above nothing.
    public static bool ProfitInTheIndexForm(IEnumerable<FiledIncome> quarters, DateOnly session) =>
        MemberReadings.Profit(quarters, session)
        && MemberReadings.FiledBefore(quarters, session) is [{ NetIncome: > 0m }, ..];

    // The first part of the rule a member's readings and quarters fail on the session, none where it passes them all.
    public static string? FailsOn(FundamentalsSetting setting, IReadOnlyList<double?> readings, IEnumerable<FiledIncome> quarters, DateOnly session)
    {
        static bool Above(double? value, double floor) => value is { } held && held > floor;

        if (!ProfitInTheIndexForm(quarters, session))
        {
            return NoProfit;
        }

        if (!Above(readings[RevenueGrowth], setting.GrowthFloor) || !Above(readings[GrowthChange], 0))
        {
            return NoRevenue;
        }

        var (gross, operating) = (Above(readings[GrossMarginChange], 0), Above(readings[OperatingMarginChange], 0));
        var margins = setting.Margins switch
        {
            MarginRule.Both => gross && operating,
            MarginRule.Operating => operating,
            _ => gross || operating,
        };

        if (!margins)
        {
            return NoMargin;
        }

        if (readings[CashOverIncome] is not { } cash || cash < setting.CashFloor)
        {
            return NoCash;
        }

        return Above(readings[CloseOverLong], 1) && Above(readings[FiftyOverLong], 1) ? null : NoTrend;
    }
}
