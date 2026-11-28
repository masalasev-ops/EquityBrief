using EquityBrief.Core.Ledger;

namespace EquityBrief.Core.History;

// One reading the every-session check found different: the index, what the table's row was held to, the family of a setup,
// the stock and session, the reading, the table's figure and the other side's, and whether the table reads it its own way by
// design until the pin move carries its shared code into the nights.
public sealed record SessionDifference(string Index, string Kind, string? Family, string Ticker, DateOnly Session, string Reading, double? Table, double? Other, bool Expected)
{
    public const string Night = "night";

    public const string Rebuilt = "rebuilt";

    public const string Setup = "setup";
}

// How the check's settling reads a difference: under 1% accepted; one a built fix covers counted as such; one read off the
// stock's own prices settled by a second source; any other marked unreliable with its reading named.
// see: A difference of 1% or more the check finds is settled by a second source's closes where one agrees with a side, and otherwise its stock-session is marked unreliable and skipped by every reader of the table
public static class CheckSettling
{
    // A difference smaller than this share of the larger side is accepted: 1%.
    public const double Accepted = 0.01;

    public const string Small = "a";

    public const string Built = "b";

    public const string Priced = "c";

    public const string NotPriced = "c, not from its prices";

    // The sessions of a stock's own prices a difference is settled over, the longest any reading looks back with room.
    public const int SettledSessions = 260;

    // The readings that read something besides the stock's own prices: its filings, its calendar, its insiders, the releases,
    // the ratings, the short interest, the market series and the other members of its index or sector, so a source's prices of
    // the stock cannot settle them.
    public static readonly string[] NotFromItsPrices =
    [
        "earnings_sessions", "surprise_sessions", "surprise_percent", "breadth", "strength", "strength_twelve_less_one",
        "highs_less_lows", "index_over_long", "vix", "vix_change", "mid_over_large", "small_over_large", "credit_over_fifty",
        "profit", "coverage", "revenue_growth", "growth_change", "gross_margin_change", "operating_margin_change", "cash_over_income",
        "value_place", "sector_month", "sector_quarter", "alpha_one", "alpha_two", "alpha_three", "alpha_four", "insider_net_buy",
        "insider_buyers", "insider_officer_buy", "insider_unplanned_sale", "release_results", "release_outlook", "release_tone",
        "release_demand", "drop_against_sector", "large_breadth", "fund_over_long", "vix_over_median", "company_value",
        "fundamentals_score", "gross_profitability", "return_on_assets", "return_on_assets_change", "leverage_fall",
        "current_ratio_change", "share_fall", "turnover_change", "gross_margin_year_change", "relative_strength_high",
        "insider_cluster_buyers", "rating_actions_net", "estimate_revision_month", "short_interest_share", "one_off_share",
    ];

    public static bool FromItsPrices(string reading) =>
        LedgerReadings.All.Any(one => one.Column == reading) && !NotFromItsPrices.Contains(reading, StringComparer.Ordinal);

    // The difference's size as a share of the larger side: one side none and the other not is the whole of it.
    public static double Size(SessionDifference difference) => (difference.Table, difference.Other) switch
    {
        (null, null) => 0,
        (null, _) or (_, null) => 1,
        ({ } table, { } other) => Math.Max(Math.Abs(table), Math.Abs(other)) is var larger && larger > 0 ? Math.Abs(table - other) / larger : 0,
    };

    public static string Group(SessionDifference difference) =>
        Size(difference) < Accepted ? Small
        : difference.Expected ? Built
        : FromItsPrices(difference.Reading) ? Priced
        : NotPriced;
}
