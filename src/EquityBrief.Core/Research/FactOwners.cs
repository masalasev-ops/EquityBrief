using EquityBrief.Core.Facts;

namespace EquityBrief.Core.Research;

// Which region of the name page owns each figure of the facts file, and which figures each written section may write.
//
// A figure is drawn by one region with its source, and a written section restating it is one more place for it to drift
// from that region: a written figure is held to the region the section sits in, and every other figure is named in
// words. A figure is owned by the region that draws it, read off the stage that stored it and its name. The two cases
// and the risks sit in regions owning no figure, so they write none, and a risk's trigger is held by its own rule. The
// short version and the headline are written from the accepted claims and are held to them, the short version writing
// no figure the tiles show.
// see: Each figure of the facts file is owned by one region of the page, and a written section writes only the figures its own region owns
public static class FactOwners
{
    public const string Tiles = "the tiles under the headline";
    public const string Quarter = "the latest quarter";
    public const string Management = "what management said";
    public const string Segments = "the segments";
    public const string Margins = "the margins";
    public const string Dividend = "the dividend's safety";
    public const string Valuation = "the valuation";
    public const string Reactions = "the earnings reactions";
    public const string Moves = "how it got here";
    public const string Technicals = "the technical readings";
    public const string Levels = "the levels";
    public const string Calendar = "the calendar";

    // Every owner in the page's order, which is the order a prompt lists them in.
    public static readonly string[] Regions =
    [
        Tiles, Quarter, Management, Segments, Margins, Dividend, Valuation, Reactions, Moves, Technicals, Levels, Calendar,
    ];

    // The facts the stages store under the fundamentals that the tiles draw: the latest quarter's earnings a share against
    // its estimate and a year before, and the dividend's yield.
    static readonly string[] TileFacts =
    [
        "latest quarter earnings per share",
        "latest quarter earnings per share estimate",
        "latest quarter earnings per share growth on a year earlier",
        "dividend forward yield",
    ];

    static readonly string[] MarginFacts =
    [
        "latest quarter gross margin",
        "latest quarter net margin",
    ];

    static readonly string[] ValuationFacts =
    [
        "trailing earnings per share",
        "trailing price to earnings",
        "forward price to earnings",
    ];

    static readonly string[] DividendFacts =
    [
        "dividend forward annual rate",
        "dividend payout ratio",
    ];

    static readonly string[] CalendarFacts =
    [
        "ex-dividend date",
        "dividend pay date",
    ];

    // The region a fact belongs to, or none for a fact that is a name and no figure.
    public static string? Of(Fact fact) => fact.Source switch
    {
        "bar" or "indicator" or "swing" or "ladder" => Technicals,
        "level" => Levels,
        "move" => Moves,
        "earnings reaction" => Reactions,
        "calendar" => Calendar,
        "membership" => null,
        "fundamental reading" => Reading(fact.Name),
        _ => Filed(fact.Name),
    };

    static string Reading(string name) =>
        name.StartsWith("operating margin", StringComparison.Ordinal) ? Margins
        : name.StartsWith("earnings multiple", StringComparison.Ordinal) ? Valuation
        : Quarter;

    static string Filed(string name) =>
        TileFacts.Contains(name, StringComparer.Ordinal) ? Tiles
        : MarginFacts.Contains(name, StringComparer.Ordinal) ? Margins
        : ValuationFacts.Contains(name, StringComparer.Ordinal) ? Valuation
        : DividendFacts.Contains(name, StringComparer.Ordinal) ? Dividend
        : CalendarFacts.Contains(name, StringComparer.Ordinal) ? Calendar
        : name.StartsWith(SegmentPeriods.Prefix, StringComparison.Ordinal) ? Segments
        : name.StartsWith(GuidanceFigure, StringComparison.Ordinal) || name.StartsWith(GuidanceBefore, StringComparison.Ordinal) ? Management
        : Quarter;

    // The names the guided figures carry, the newest release's and the release before's.
    public const string GuidanceFigure = "guidance figure ";
    public const string GuidanceBefore = "guidance before figure ";

    // The region each written section sits in, whose figures it may write. The two cases and the risks sit in regions of
    // their own that own no figure.
    static readonly Dictionary<string, string[]> Written = new(StringComparer.Ordinal)
    {
        [ClaimRules.CauseSection] = [Moves],
        ["What the company sells"] = [Segments],
        ["The segment commentary"] = [Segments],
        [ClaimRules.ManagementSection] = [Management],
        [ClaimRules.CalendarSection] = [Calendar],
        [ClaimRules.TwoCasesSection] = [],
        [RiskFields.Section] = [],
    };

    // Whether a section's figures are held to the region it sits in: every section written from the facts file, and not
    // the industry cycle, which has none, or the two written from the accepted claims.
    public static bool Governs(string section) => Written.ContainsKey(section);

    // The regions whose figures a section may write.
    public static IReadOnlyList<string> For(string section) =>
        Written.TryGetValue(section, out var regions) ? regions : [];

    public static bool IsTile(Fact fact) => Of(fact) == Tiles;

    // The facts a section may write as figures, and every other fact grouped by the region that draws it, in the page's
    // order, a name carrying no figure listed with the section's own.
    public static (IReadOnlyList<Fact> Own, IReadOnlyList<(string Region, IReadOnlyList<Fact> Facts)> Elsewhere) Split(string section, IReadOnlyList<Fact> facts)
    {
        var mine = For(section);

        return (
            [.. facts.Where(fact => Of(fact) is not { } region || mine.Contains(region, StringComparer.Ordinal))],
            [
                .. Regions
                    .Where(region => !mine.Contains(region, StringComparer.Ordinal))
                    .Select(region => (region, (IReadOnlyList<Fact>)[.. facts.Where(fact => Of(fact) == region)]))
                    .Where(group => group.Item2.Count > 0),
            ]);
    }
}
