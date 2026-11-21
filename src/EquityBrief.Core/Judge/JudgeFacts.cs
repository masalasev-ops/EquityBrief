using System.Globalization;
using EquityBrief.Core.Prices;

namespace EquityBrief.Core.Judge;

// Which of the judge's two rounds a field is shown in: the short row every member gets, or the full sheet of a stock it
// keeps, which holds every field of the row as well.
public enum JudgeRound
{
    Row = 1,
    Sheet = 2,
}

// One field of what the judge sees: its key, the round it is shown in, its words, and the stored rows it is read from.
public sealed record JudgeField(string Key, JudgeRound Round, string Words, string Source);

// One member's facts on a night as the judge is shown them, each field's shown text by its key, a field the member does
// not hold reading not on file.
public sealed record JudgeFact(string Index, string Ticker, DateOnly Session, IReadOnlyDictionary<string, string> Shown)
{
    public string Of(string key) => Shown.TryGetValue(key, out var text) ? text : JudgeFacts.NotOnFile;
}

// The fields the operator approved at 24.1 and no other, and how each is shown: with no name, ticker, industry, date or
// price, a size as a band, and a field a member does not hold written as not on file. Three fields the ledger's night
// step leaves empty are worked here from the stored daily bars, never in that step, so no rule's version moves: the
// return over the year to a month ago, the distance from the year's high and, where the night's ladder holds a plan, its
// reward against its risk.
// see: The AI stock picker sees the fields the operator approved and nothing that names a company, a date or a price
public static class JudgeFacts
{
    public const string NotOnFile = "not on file";

    // The sessions a year's high reads, the store's year of bars, and the month the year's return leaves out; the year's
    // return reaches back to the oldest of them, one session fewer.
    public const int YearSessions = 252;

    public const int MonthSessions = 21;

    // The days of news a member's good and bad stories are counted over, to the night.
    public const int NewsDays = 30;

    // The size bands' lower edges in dollars of company value: small under the first, mid-size under the second, large
    // under the third and very large from it.
    public const decimal MidSizeFrom = 5_000_000_000m;

    public const decimal LargeFrom = 20_000_000_000m;

    public const decimal VeryLargeFrom = 200_000_000_000m;

    public static IReadOnlyList<JudgeField> All { get; } =
    [
        new("sector", JudgeRound.Row, "Sector", "company: sector"),
        new("size", JudgeRound.Row, "Size", "member_reading: company_value"),
        new("return_month", JudgeRound.Row, "Return over 1 month", "ledger_reading: return_month"),
        new("return_quarter", JudgeRound.Row, "Return over 3 months", "ledger_reading: return_quarter"),
        new("return_twelve_less_one", JudgeRound.Row, "Return over 12 months less the last", "bar: close, worked here"),
        new("strength", JudgeRound.Row, "Strength against its index", "ledger_reading: strength"),
        new("high_ratio", JudgeRound.Row, "Distance from its 52-week high", "bar: close and high, worked here"),
        new("close_over_fifty", JudgeRound.Row, "Against its 50-day average", "ledger_reading: close_over_fifty"),
        new("close_over_long", JudgeRound.Row, "Against its 200-day average", "ledger_reading: close_over_long"),
        new("rsi", JudgeRound.Row, "Momentum (RSI)", "ledger_reading: rsi"),
        new("volume_ratio", JudgeRound.Row, "Volume against its own average", "ledger_reading: volume_ratio"),
        new("volatility_month", JudgeRound.Row, "How much it usually moves in a day, this month", "ledger_reading: volatility_month"),
        new("business_score", JudgeRound.Row, "Business score", "fundamental_score: passed, applied"),
        new("surprise", JudgeRound.Row, "Last earnings against estimates, and days since", "ledger_reading: surprise_percent, surprise_sessions"),
        new("estimate_revision_month", JudgeRound.Row, "Analysts' estimates over the last month", "ledger_reading: estimate_revision_month"),

        new("depth", JudgeRound.Sheet, "How deep the pullback from its 20-day high", "ledger_reading: depth"),
        new("since_high", JudgeRound.Sheet, "Days since its 20-day high", "ledger_reading: since_high"),
        new("tightness", JudgeRound.Sheet, "How tight the range", "ledger_reading: tightness"),
        new("reward_to_risk", JudgeRound.Sheet, "Reward to the nearest resistance against the risk to support", "ladder: plan, worked here"),
        new("stretch_days", JudgeRound.Sheet, "Stretched from its 20-day average", "ledger_reading: stretch_days"),
        new("gap_days", JudgeRound.Sheet, "Gap up into the night", "ledger_reading: gap_days"),
        new("revenue_growth", JudgeRound.Sheet, "Sales growth on a year before", "ledger_reading: revenue_growth"),
        new("growth_change", JudgeRound.Sheet, "Change in that growth", "ledger_reading: growth_change"),
        new("gross_margin_change", JudgeRound.Sheet, "Gross margin change", "ledger_reading: gross_margin_change"),
        new("operating_margin_change", JudgeRound.Sheet, "Operating margin change", "ledger_reading: operating_margin_change"),
        new("cash_over_income", JudgeRound.Sheet, "Cash against reported profit", "ledger_reading: cash_over_income"),
        new("profit", JudgeRound.Sheet, "Profitable over the last four quarters", "ledger_reading: profit"),
        new("coverage", JudgeRound.Sheet, "Interest covered", "ledger_reading: coverage"),
        new("return_on_assets", JudgeRound.Sheet, "Return on assets", "ledger_reading: return_on_assets"),
        new("return_on_assets_change", JudgeRound.Sheet, "Change in return on assets", "ledger_reading: return_on_assets_change"),
        new("leverage_fall", JudgeRound.Sheet, "Debt against assets, on a year before", "ledger_reading: leverage_fall"),
        new("share_fall", JudgeRound.Sheet, "Shares falling", "ledger_reading: share_fall"),
        new("value_place", JudgeRound.Sheet, "Valuation against its own history", "ledger_reading: value_place"),
        new("release_results", JudgeRound.Sheet, "Latest release: results", "ledger_reading: release_results"),
        new("release_outlook", JudgeRound.Sheet, "Latest release: outlook", "ledger_reading: release_outlook"),
        new("release_tone", JudgeRound.Sheet, "Latest release: tone", "ledger_reading: release_tone"),
        new("release_demand", JudgeRound.Sheet, "Latest release: demand", "ledger_reading: release_demand"),
        new("insider_net_buy", JudgeRound.Sheet, "Insiders' net buying, 90 days", "ledger_reading: insider_net_buy"),
        new("insider_buyers", JudgeRound.Sheet, "Insiders who bought, 90 days", "ledger_reading: insider_buyers"),
        new("insider_officer_buy", JudgeRound.Sheet, "An officer bought", "ledger_reading: insider_officer_buy"),
        new("insider_unplanned_sale", JudgeRound.Sheet, "Unplanned insider selling", "ledger_reading: insider_unplanned_sale"),
        new("short_interest_share", JudgeRound.Sheet, "Shares sold short against the shares out", "ledger_reading: short_interest_share"),
        new("news", JudgeRound.Sheet, "News over 30 days, as the labeller read it", "news_article, news_label"),
        new("sector_month", JudgeRound.Sheet, "Against its sector over 1 month", "ledger_reading: sector_month"),
        new("sector_quarter", JudgeRound.Sheet, "Against its sector over 3 months", "ledger_reading: sector_quarter"),
        new("breadth", JudgeRound.Sheet, "Its index's stocks above their 200-day average", "ledger_reading: breadth"),
        new("vix_over_median", JudgeRound.Sheet, "The VIX against its own median", "ledger_reading: vix_over_median"),
    ];

    // The field list's version: a hash of every field and every bound above, which a night's rows and a naming test are
    // stored under, so a test passed under one list answers for no other.
    public static string Version { get; } = Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(
        string.Join("\n", All.Select(field => $"{field.Key}|{field.Round}|{field.Words}|{field.Source}"))
        + FormattableString.Invariant($"\n{MidSizeFrom}|{LargeFrom}|{VeryLargeFrom}|{YearSessions}|{MonthSessions}|{NewsDays}"))))[..12];

    // The three fields of the mockup dropped at 24.1 until a change that moves the ledger's pins anyway fills them.
    public static IReadOnlyList<string> Dropped { get; } = ["band_strength", "rating_actions_net", "insider_cluster_buyers"];

    public static IEnumerable<JudgeField> In(JudgeRound round) => All.Where(field => round == JudgeRound.Sheet || field.Round == JudgeRound.Row);

    // The return over the year to a month ago: the close a month before the night over the oldest close of the store's
    // year, 251 sessions before it, less one, over the member's closes oldest first to the night; none where they hold
    // fewer than the year's sessions.
    public static double? ReturnTwelveLessOne(IReadOnlyList<decimal> closes)
    {
        if (closes.Count < YearSessions)
        {
            return null;
        }

        var monthAgo = closes[closes.Count - 1 - MonthSessions];
        var yearAgo = closes[closes.Count - YearSessions];

        return yearAgo <= 0 ? null : Statistic.FromRatio(monthAgo / yearAgo) - 1;
    }

    // The night's close over the highest high of the year's sessions ending on the night, less one; none where the
    // member's bars reach back fewer than a year.
    public static double? HighRatio(IReadOnlyList<decimal> closes, IReadOnlyList<decimal> highs)
    {
        if (closes.Count < YearSessions || highs.Count != closes.Count)
        {
            return null;
        }

        var high = highs.Skip(highs.Count - YearSessions).Max();

        return high <= 0 ? null : Statistic.FromRatio(closes[^1] / high) - 1;
    }

    // A plan's reward over its risk, none where either is missing or the risk is nothing.
    public static double? RewardToRisk(decimal? reward, decimal? risk) =>
        reward is { } up && risk is { } down && down > 0 ? Statistic.FromRatio(up / down) : null;

    public static string SizeBand(decimal? companyValue) => companyValue switch
    {
        null => NotOnFile,
        < MidSizeFrom => "small",
        < LargeFrom => "mid-size",
        < VeryLargeFrom => "large",
        _ => "very large",
    };

    // A figure as a whole signed percent, read from a fraction.
    public static string Percent(double? fraction) =>
        fraction is { } held && double.IsFinite(held) ? Signed(Math.Round(held * 100, MidpointRounding.AwayFromZero)) + "%" : NotOnFile;

    // A share of a whole as a whole percent, carrying no sign.
    public static string Share(double? fraction) =>
        fraction is { } held && double.IsFinite(held) ? Math.Round(held * 100, MidpointRounding.AwayFromZero).ToString(CultureInfo.InvariantCulture) + "%" : NotOnFile;

    // A fall in a fraction, higher where it fell, as which way it moved and by how many points.
    public static string Fell(double? fall, string what) => fall switch
    {
        null => NotOnFile,
        { } held when !double.IsFinite(held) => NotOnFile,
        { } held when Math.Round(held * 100, 1, MidpointRounding.AwayFromZero) == 0 => what + " unchanged",
        { } held => $"{what} {(held > 0 ? "fell" : "rose")} {Math.Abs(Math.Round(held * 100, 1, MidpointRounding.AwayFromZero)).ToString("0.#", CultureInfo.InvariantCulture)} points of assets",
    };

    // A figure already in percent, as a whole signed percent.
    public static string PercentAsStored(double? percent) => Percent(percent / 100);

    // A change in a fraction as whole signed points.
    public static string Points(double? fraction) =>
        fraction is { } held && double.IsFinite(held) ? Signed(Math.Round(held * 100, 1, MidpointRounding.AwayFromZero)) + " points" : NotOnFile;

    // A ratio over its own average read as how far above or below it, a whole signed percent.
    public static string Against(double? ratio) => Percent(ratio - 1);

    public static string Times(double? ratio, int places = 1) =>
        ratio is { } held && double.IsFinite(held) ? Math.Round(held, places, MidpointRounding.AwayFromZero).ToString("0." + new string('0', places), CultureInfo.InvariantCulture) + " times" : NotOnFile;

    public static string Count(double? count, string one, string many) =>
        count is { } held && double.IsFinite(held) ? ((long)Math.Round(held)).ToString(CultureInfo.InvariantCulture) + " " + (Math.Round(held) == 1 ? one : many) : NotOnFile;

    public static string Typical(double? moves) =>
        moves is { } held && double.IsFinite(held) ? Math.Round(held, 1, MidpointRounding.AwayFromZero).ToString("0.0", CultureInfo.InvariantCulture) + " typical daily moves" : NotOnFile;

    public static string YesNo(double? flag) => flag is { } held ? (held > 0 ? "yes" : "no") : NotOnFile;

    // A place among others as the share it stands above, as a whole percent.
    public static string Place(double? share, string among) =>
        share is { } held && double.IsFinite(held) ? $"above {Math.Round(held * 100, MidpointRounding.AwayFromZero).ToString(CultureInfo.InvariantCulture)}% of {among}" : NotOnFile;

    // A release reading's answer in its own words: 1, 0 or -1.
    public static string Answer(double? answer, string better, string same, string worse) => answer switch
    {
        null => NotOnFile,
        > 0 => better,
        < 0 => worse,
        _ => same,
    };

    public static string Rsi(double? rsi) =>
        rsi is { } held && double.IsFinite(held) ? Math.Round(held, MidpointRounding.AwayFromZero).ToString(CultureInfo.InvariantCulture) + " of 100" : NotOnFile;

    static string Signed(double value) => (value > 0 ? "+" : string.Empty) + value.ToString("0.##", CultureInfo.InvariantCulture);

    // The short row as one line, its fields in the approved order.
    public static string RowText(JudgeFact fact) =>
        string.Join("; ", In(JudgeRound.Row).Select(field => $"{field.Words}: {fact.Of(field.Key)}"));

    // The full sheet, a field a line under the row's, its fields in the approved order.
    public static string SheetText(JudgeFact fact) =>
        string.Join("\n", In(JudgeRound.Sheet).Select(field => $"{field.Words}: {fact.Of(field.Key)}"));
}
