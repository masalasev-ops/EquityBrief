using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace EquityBrief.Web.Marks;

// The names the store and the code keep a field under, each with the reader's words a page draws in its place, and what
// finds a code name a page draws. A stored row keeps the name, so a card written before this reads as one written after
// it and no rule's version moves. A reading whose name is already a plain word, its depth or its beta, is not listed.
// see: A page draws every field in the reader's words, and the proof fails a page drawing a field's code name
public static partial class FieldWords
{
    public static IReadOnlyList<(string Field, string Words)> Fields { get; } =
    [
        ("close_over_twenty", "the close against its 20-day average"),
        ("close_over_fifty", "the close against its 50-day average"),
        ("close_over_long", "the close against its 200-day average"),
        ("fifty_over_long", "the 50-day average against the 200-day average"),
        ("move_share", "the typical day's move against the close"),
        ("rsi", "relative strength"),
        ("rsi_up", "relative strength rising"),
        ("volume_ratio", "the day's volume against its 50-day average"),
        ("return_quarter", "the return over 63 days"),
        ("return_half_year", "the return over 126 days"),
        ("return_twelve_less_one", "the return over the year to a month ago"),
        ("strength_twelve_less_one", "its place among the index's returns over the year to a month ago"),
        ("high_ratio", "the close against its 252-day high"),
        ("since_high", "the days since its recent high"),
        ("dry_up", "the volume on the way down against its average"),
        ("gap_down", "the largest gap down on the way down"),
        ("rsi_low", "the lowest relative strength since its high"),
        ("earnings_sessions", "the days to its next report"),
        ("surprise_sessions", "the days since its last surprise"),
        ("surprise_percent", "the size of its last surprise"),
        ("reward_to_risk", "the reward for the risk"),
        ("band_strength", "the strength of the band holding the close"),
        ("volume_multiple", "the signal day's volume against its average"),
        ("range_ratio", "how far its daily range has narrowed"),
        ("reaction_moves", "the report day's rise in typical days' moves"),
        ("highs_less_lows", "new highs less new lows across the index"),
        ("index_over_long", "the index against its 200-day average"),
        ("vix", "the VIX"),
        ("vix_change", "the VIX against 10 days before"),
        ("mid_over_large", "mid-sized companies' return against large ones'"),
        ("small_over_large", "small companies' return against large ones'"),
        ("credit_over_fifty", "the high-yield bond fund against its 50-day average"),
        ("revenue_growth", "sales growth on a year earlier"),
        ("growth_change", "the change in sales growth"),
        ("gross_margin_change", "the change in gross margin on a year earlier"),
        ("operating_margin_change", "the change in operating margin on a year earlier"),
        ("cash_over_income", "cash from operations against net income"),
        ("value_place", "earnings against the company's value"),
        ("return_week", "the return over 5 days"),
        ("return_month", "the return over 21 days"),
        ("largest_return", "the largest one-day return in a month"),
        ("volatility_month", "how much its daily returns varied over a month"),
        ("residual_volatility", "how much its daily returns varied apart from the index's"),
        ("close_place", "where it closes in its daily range"),
        ("up_volume_share", "the share of its volume on rising days"),
        ("price_volume_correlation", "how its price changes and its volume move together"),
        ("overnight_less_intraday", "its overnight returns less its returns during the day"),
        ("sector_month", "the month's return against its sector fund"),
        ("sector_quarter", "the quarter's return against its sector fund"),
        ("low_ratio", "the close against its 252-day low"),
        ("volume_slope", "the trend in its buying and selling volume"),
        ("alpha_one", "published price pattern 1"),
        ("alpha_two", "published price pattern 2"),
        ("alpha_three", "published price pattern 3"),
        ("alpha_four", "published price pattern 4"),
        ("alpha_six", "published price pattern 6"),
        ("insider_net_buy", "the insiders' buying less their selling"),
        ("insider_buyers", "the insiders buying"),
        ("insider_officer_buy", "an officer buying"),
        ("insider_unplanned_sale", "the insiders' unplanned selling"),
        ("insider_cluster_buyers", "the insiders buying together"),
        ("release_results", "the results release's quarter against what was expected"),
        ("release_outlook", "the results release's outlook"),
        ("release_tone", "the tone of the results release"),
        ("release_demand", "demand in the results release"),
        ("rsi_two", "the 2-day relative strength"),
        ("drop_fall", "the fall over 3 days"),
        ("drop_largest", "the largest one-day fall of the 3"),
        ("drop_volume", "the volume over the 3-day fall"),
        ("drop_against_sector", "the 3-day fall against its sector's"),
        ("large_breadth", "the S&P 500's share of stocks above their 200-day average"),
        ("fund_over_long", "the index's fund against its 200-day average"),
        ("vix_over_median", "the VIX against its usual level over a year"),
        ("company_value", "the company's value"),
        ("fundamentals_score", "the fundamentals score"),
        ("gross_profitability", "gross profit against total assets"),
        ("return_on_assets", "return on assets"),
        ("return_on_assets_change", "the change in return on assets on a year earlier"),
        ("leverage_fall", "the fall in its debt against its assets"),
        ("current_ratio_change", "the change in current assets against current liabilities"),
        ("share_fall", "the fall in its share count"),
        ("turnover_change", "the change in sales against assets"),
        ("gross_margin_year_change", "the change in gross margin over a year"),
        ("relative_strength_high", "the relative strength line at its high"),
        ("rating_actions_net", "the analysts' upgrades less their downgrades"),
        ("estimate_revision_month", "the change in the analysts' estimate over a month"),
        ("short_interest_share", "the share of its stock sold short"),
        ("business_state_filed", "the state of the business as its filings read"),
    ];

    // The filer's own tags for its figures, as the SEC's facts name them. A one-word tag is no code name to a reader.
    static readonly string[] FilerTags =
    [
        .. new[]
        {
            EquityBrief.Core.Ledger.FiledFacts.Revenue,
            EquityBrief.Core.Ledger.FiledFacts.GrossProfit,
            EquityBrief.Core.Ledger.FiledFacts.CostOfSales,
            EquityBrief.Core.Ledger.FiledFacts.OperatingIncome,
            EquityBrief.Core.Ledger.FiledFacts.NetIncome,
            EquityBrief.Core.Ledger.FiledFacts.OperatingCash,
            EquityBrief.Core.Ledger.FiledFacts.LongTermDebt,
        }.SelectMany(tags => tags),
    ];

    static readonly Regex[] Names =
    [
        .. Fields.OrderByDescending(field => field.Field.Length).Select(field => new Regex(@"(?<![\w.\-/@])" + Regex.Escape(field.Field) + @"(?![\w/@\-]|\.[a-z])", RegexOptions.CultureInvariant)),
    ];

    static readonly string[] Said = [.. Fields.OrderByDescending(field => field.Field.Length).Select(field => field.Words)];

    static readonly Regex CodeNames = new(
        @"(?<![\w.\-/@])[a-z][a-z0-9]*(?:_[a-z0-9]+)+(?![\w/@\-]|\.[a-z])"
        + @"|\b(?:" + string.Join("|", EquityBrief.Core.Indicators.IndicatorSeries.Names.Where(name => !name.Contains('_', StringComparison.Ordinal)).Append("rsi")) + @")\b"
        + @"|\b(?:" + string.Join("|", FilerTags.Where(tag => tag.Skip(1).Any(char.IsUpper))) + @")\b"
        + @"|\b(?:us-gaap|dei|srt)[_:]\w+"
        + @"|\b[a-z][a-z0-9]{1,9}:[A-Z]\w+"
        + @"|\b(?:[A-Z][a-z]+){5,}\b",
        RegexOptions.CultureInvariant);

    // A stored sentence with each field's code name in the reader's words, the longer name first so one holding a shorter
    // reads whole. "holds no high_ratio reading tonight" reads "holds no reading tonight of the close against its 252-day high".
    public static string Words(string sentence)
    {
        sentence = StoredMissing().Replace(sentence, "holds no reading tonight of ${field}");

        for (var at = 0; at < Names.Length; at++)
        {
            var words = Said[at];

            sentence = Names[at].Replace(sentence, _ => words);
        }

        return sentence;
    }

    // A label a filing's table states with a tag in it, the standard's or the filer's own, or as a tag's whole name of five
    // words or more, a shorter run of capitals being as likely a name the filer chose: "us-gaap:CostOfRevenue" reads "cost of
    // revenue", "lvs:CorporateExpense" "corporate expense" and "NumberOfReportableSegmentsNotDisclosedFlag" "Number of
    // reportable segments not disclosed flag".
    public static string FilerLabel(string label) =>
        FilerTag().Replace(label, match =>
        {
            var words = Humps().Replace(match.Groups["tag"].Value, hump => " " + hump.Value.ToLowerInvariant()).TrimStart();

            return match.Index == 0 ? char.ToUpperInvariant(words[0]) + words[1..] : words;
        });

    // A name filed as one run of joined capitals, split for the reader by the core, which the night's reader of split names reads too.
    // see: A name filed as one run of joined capitals is split at its capitals for the reader, and a company's own joined names keep their spelling
    public static string Spaced(string name) => EquityBrief.Core.Report.JoinedNames.Spaced(name);

    public static IReadOnlySet<string> CompanysOwnSpelling => EquityBrief.Core.Report.JoinedNames.CompanysOwnSpelling;

    // A rule version's stored settings in words: {"nightsTheNewLabelHolds": 1} reads "nights the new label holds 1".
    public static string Settings(string stored)
    {
        using var settings = JsonDocument.Parse(stored);

        return string.Join(", ", settings.RootElement.EnumerateObject().Select(setting =>
            Humps().Replace(setting.Name, match => " " + match.Value.ToLowerInvariant()) + " " + setting.Value));
    }

    // The text a reader sees on a page: no tag, no attribute, no script and no style, a tooltip's title among what is read.
    public static string Visible(string page) =>
        WebUtility.HtmlDecode(Tags().Replace(Unread().Replace(page, " "), " "));

    // The first field's code name a page's visible text draws, none where it draws none.
    public static string? Drawn(string page) =>
        CodeNames.Match(Visible(page)) is { Success: true } found ? found.Value : null;

    // A percentage drawn with a sign where it rounds to nothing, none where the page draws none.
    public static string? SignedZero(string page) =>
        SignedNothing().Match(Visible(page)) is { Success: true } found ? found.Value : null;

    [GeneratedRegex(@"holds no (?<field>[a-z][a-z0-9_]*) reading tonight")]
    private static partial Regex StoredMissing();

    [GeneratedRegex(@"\b(?:(?:us-gaap|dei|srt)[_:]|[a-z][a-z0-9]{1,9}:(?=[A-Z]))(?<tag>\w+)|\b(?<tag>(?:[A-Z][a-z]+){5,})\b")]
    private static partial Regex FilerTag();

    [GeneratedRegex("[A-Z]")]
    private static partial Regex Humps();

    [GeneratedRegex("<(script|style)\\b.*?</\\1>", RegexOptions.Singleline)]
    private static partial Regex Unread();

    [GeneratedRegex("<[^>]+>")]
    private static partial Regex Tags();

    [GeneratedRegex(@"(?<![\d.,])[+\-−]0(?:\.0+)?%")]
    private static partial Regex SignedNothing();
}
