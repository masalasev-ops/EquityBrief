using EquityBrief.Core.Bars;
using EquityBrief.Core.Filter;
using EquityBrief.Core.Prices;
using EquityBrief.Web.Marks;

namespace EquityBrief.Api.Reading;

// The projection from stored rows to what the universe screen's marks take.
//
// It sits beside `NameScreen` and in the same seam, and for the same reason:
// the read surface hands back stored values unchanged and the app renders and
// computes nothing, so turning a row into a mark's input is neither.
//
// One thing here is different from `NameScreen` and it is worth stating rather
// than letting a reader find it. `NameScreen` derives no figure, because every
// value the name page draws is a stored column. The universe screen is ordered
// by distance to the nearest level, and that distance is stored nowhere: it is
// tonight's close against a band edge, in typical days' moves, and it changes
// with every session. So it is derived, and this is where. Not in the read API,
// whose claim is that it computes nothing and which `read-surface` asserts over
// the shipped source. Not in the page, whose claim is the same. Code owns it and
// the file that owns it is named for the screen it serves.
// see: A screen reads and renders, and each figure it works out at a price or from stored figures has one function in the core
// see: Code owns every number
// see: Distances are stated as typical days' moves
public static class UniverseScreen
{
    // What a name with no sector is shown as.
    //
    // Named rather than left as an empty cell, and excluded by name from every
    // bucket in the strip rather than collected into one of its own. A falsy
    // value standing in for an absent one is the class this store has been
    // bitten by twice, and a sector filter is exactly where it would bite: a
    // name with no sector must not answer yes to a filter for some sector.
    public const string SectorNotOnFile = "sector not filed";

    // The window the listing strip is drawn over, which section 15.8 states as
    // sixty sessions. The same figure the level window uses, and for the same
    // reason: it is the quarter of trading this system reasons in.
    public const int StripSessions = 60;

    // The rows, ordered as section 15.8 states: by distance to the nearest level
    // ascending, so the top is what nearly fired.
    //
    // A name the night computed nothing for sorts last rather than first. Its
    // distance is absent, and an absent distance read as zero would put every
    // name with no chart at the top of a screen whose whole point is that the
    // top is what nearly fired.
    public static IReadOnlyList<UniverseCell> Rows(
        IReadOnlyList<UniverseRow> rows,
        IReadOnlyDictionary<string, IReadOnlyList<ListingRow>>? history = null,
        IReadOnlyDictionary<string, DateOnly>? nextEventByTicker = null,
        DateOnly? night = null,
        IReadOnlyDictionary<string, SwingReadingRow>? readings = null) =>
    [
        .. rows
            .Select(row => WithReadings(Cell(row, history, nextEventByTicker, night), readings))
            .OrderBy(cell => cell.Nearest is null)
            .ThenBy(cell => cell.Nearest ?? double.MaxValue)
            .ThenBy(cell => cell.Ticker, StringComparer.Ordinal),
    ];

    // The rows of one page, section 15.8's "paged".
    //
    // Every name in the index is five hundred rows carrying a mark each, and a
    // screen that draws them all is a screen that takes seconds to open and
    // cannot be scrolled to a place a reader can name. The page is in the hash
    // with the filters, so a page of a filtered view is a link, which is what
    // that section means by a route being one.
    //
    // The slice is taken after the filters, because the alternative is pages
    // whose size depends on which chips are lit, and a reader on page 3 of an
    // unfiltered table who lights a chip would land somewhere nothing chose.
    public const int PageSize = 50;

    // The page a number names, clamped to the pages that exist. A number past
    // the end gives the last page rather than an empty table, because an empty
    // table is the same picture as a filter that matched nothing and the two
    // are different states.
    public static int PageOf(int rows, int? asked) =>
        Math.Clamp(asked ?? 1, 1, Math.Max(1, (rows + PageSize - 1) / PageSize));

    public static IReadOnlyList<UniverseCell> Page(IReadOnlyList<UniverseCell> rows, int page) =>
        [.. rows.Skip((PageOf(rows.Count, page) - 1) * PageSize).Take(PageSize)];

    // The rows a request draws: the filters applied, then the page cut from what
    // they left.
    //
    // One function rather than two calls at the route, because the order is the
    // property and a route is where it can be got wrong silently. Cutting the
    // page from the whole table and then filtering it draws a page that is
    // plausible, shorter than a page should be, and about the wrong names, and
    // no count on the screen contradicts it. That mutation survived the first
    // sweep at 5.8, which is why the two steps are one call.
    public static Shown Rows(
        IReadOnlyList<UniverseCell> cells,
        string? trendFilter,
        string? sectorFilter,
        int? asked)
    {
        var filtered = cells
            .Where(row => trendFilter is null || (row.TrendState ?? NotClassified) == trendFilter)
            .Where(row => sectorFilter is null || row.Sector == sectorFilter)
            .ToArray();

        var at = PageOf(filtered.Length, asked);

        return new Shown(Page(filtered, at), at, filtered.Length);
    }

    // What a name with no ladder row is filtered as. The same value the table
    // draws and the chips carry, named here so the filter and the cell cannot
    // come to disagree about it.
    public const string NotClassified = "not classified";

    // The page of rows a request draws, with the page it is and the number of
    // rows the filters left, which is what the nav states.
    public sealed record Shown(IReadOnlyList<UniverseCell> Page, int At, int Rows);

    // Each name's stored closes as the year its line draws, in session order, from the first
    // session it holds to the last. A name the closes hold nothing for is absent rather than an
    // empty year.
    public static IReadOnlyDictionary<string, PeerYear> Years(IReadOnlyList<CloseRow> closes) =>
        closes
            .GroupBy(row => row.Ticker, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => new PeerYear(group.First().SessionDate, group.Last().SessionDate, [.. group.Select(row => row.Close)]), StringComparer.Ordinal);

    // The rows a page draws, each carrying its year where the closes hold one. Read for the page
    // alone, since a year is drawn only beside a row on it.
    public static IReadOnlyList<UniverseCell> WithYears(IReadOnlyList<UniverseCell> page, IReadOnlyList<CloseRow> closes)
    {
        var years = Years(closes);

        return [.. page.Select(cell => cell with { Year = years.GetValueOrDefault(cell.Ticker) })];
    }

    // A cell with the name's swing readings as the swing reader stored them for the night, and
    // without them where it stored none.
    static UniverseCell WithReadings(UniverseCell cell, IReadOnlyDictionary<string, SwingReadingRow>? readings) =>
        readings is not null && readings.TryGetValue(cell.Ticker, out var read)
            ? cell with { Strength = read.Strength, Depth = read.Depth, DryUp = read.DryUp, Tightness = read.Tightness }
            : cell;

    static UniverseCell Cell(
        UniverseRow row,
        IReadOnlyDictionary<string, IReadOnlyList<ListingRow>>? history,
        IReadOnlyDictionary<string, DateOnly>? nextEventByTicker,
        DateOnly? night)
    {
        var toSupport = Distance(row.Close, row.NearestSupport, row.TypicalMove);
        var toResistance = Distance(row.Close, row.NearestResistance, row.TypicalMove);

        // The evenings this name was on the list, and the last of them. A name
        // that has never been on it has no date rather than one nobody has, and
        // neither column says anything about index membership.
        var listings = history is not null && history.TryGetValue(row.Ticker, out var found)
            ? found
            : [];

        var listed = listings.Where(listing => listing.IsListed).ToArray();

        // The name's next dated event, where the calendar holds one. A name with
        // none has no date rather than a date nobody has, which is the rule the
        // fact strip on the name page already follows.
        var nextEvent = nextEventByTicker is not null && nextEventByTicker.TryGetValue(row.Ticker, out var onCalendar)
            ? onCalendar
            : (DateOnly?)null;

        return new UniverseCell(
            row.Ticker,
            row.Sector ?? SectorNotOnFile,
            row.Close,
            row.TrendState,
            row.NearestSupport,
            row.NearestResistance,
            toSupport,
            toResistance,
            Nearest(toSupport, toResistance),
            listed.Length == 0 ? null : listed.Max(listing => listing.SessionDate),
            [.. listings.OrderBy(listing => listing.SessionDate).Select(listing => listing.IsListed)],
            // The sessions to it, section 15.8's column, counted where the
            // shortlist builder counts the same thing.
            nextEvent is { } dated && night is { } on ? ExchangeClosures.SessionsUntil(on, dated) : null,
            nextEvent,
            nextEvent is { } beyond && beyond > ExchangeClosures.CoveredThrough,
            row.Name,
            row.Researched,
            InSupport: Inside(row.Close, row.SupportFloor, row.NearestSupport),
            InResistance: Inside(row.Close, row.NearestResistance, row.ResistanceCeiling));
    }

    // Whether a close sits inside a band, both edges included, and not where the close or either edge is missing.
    static bool Inside(decimal? close, decimal? low, decimal? high) =>
        close is { } price && low is { } bottom && high is { } top && price >= bottom && price <= top;

    // Stated in one place, which a name's own level table reads too.
    // see: Distances are stated as typical days' moves
    static double? Distance(decimal? close, decimal? edge, double? typicalMove) =>
        Distances.InTypicalDays(close, edge, typicalMove);

    static double? Nearest(double? toSupport, double? toResistance) =>
        (toSupport, toResistance) switch
        {
            (null, null) => null,
            (null, { } resistance) => resistance,
            ({ } support, null) => support,
            var (support, resistance) => Math.Min(support!.Value, resistance!.Value),
        };

    // One line per sector: how many names it holds and how many are in an
    // uptrend, which is what section 15.8's strip states.
    //
    // Names with no sector on file are counted in their own line and never
    // folded into a real one, so the strip's lines sum to the index and no
    // sector's count is inflated by names that are not in it.
    public static IReadOnlyList<SectorLine> Sectors(IReadOnlyList<UniverseCell> rows) =>
    [
        .. rows
            .GroupBy(row => row.Sector, StringComparer.Ordinal)
            .Select(group => new SectorLine(
                group.Key,
                group.Count(),
                group.Count(row => row.TrendState == "uptrend"),
                group.Count(row => row.Evenings is { Count: > 0 } evenings && evenings[^1])))
            .OrderBy(line => line.Sector == SectorNotOnFile)
            .ThenBy(line => line.Sector, StringComparer.Ordinal),
    ];

    // A close within this many typical days' moves of its nearest band's edge is near it, one inside the band counting,
    // which section 17 states.
    public const double NearLevel = 1.0;

    // The page sizes the table offers, the first the default, and every row as the last.
    public static IReadOnlyList<int> PageSizes { get; } = [PageSize, 100];

    public const string AllRows = "all";

    // The columns the table sorts on, each by the key the link carries.
    public static IReadOnlyList<string> SortKeys { get; } = ["name", "sector", "close", "trend", "distance", "earnings", "strength", "pullback", "dry-up", "tightness", "listed"];

    // What the link asks of the table: the trend and the sector, the words searched for, the near-level chips and the one
    // gate short chip, the column sorted on and which way, the rows a page and the page.
    public sealed record Query(
        string? Trend,
        string? Sector,
        string? Search,
        string? Near,
        bool Short,
        string? Sort,
        bool Descending,
        string? Rows,
        int? Page);

    public const string NearSupport = "support";

    public const string NearResistance = "resistance";

    // The rows the query keeps, in the order it sorts them, the default by distance to the nearest level: the trend, the
    // sector, the words a ticker opens with or the company's name holds, a close near its support or its resistance, and
    // a member one gate short of a setup.
    public static IReadOnlyList<UniverseCell> Kept(IReadOnlyList<UniverseCell> cells, Query query, IReadOnlySet<string>? oneShort = null)
    {
        var words = query.Search?.Trim();

        IEnumerable<UniverseCell> kept = cells
            .Where(row => query.Trend is null || (row.TrendState ?? NotClassified) == query.Trend)
            .Where(row => query.Sector is null || row.Sector == query.Sector)
            .Where(row => words is not { Length: > 0 }
                || row.Ticker.StartsWith(words, StringComparison.OrdinalIgnoreCase)
                || (row.Name?.Contains(words, StringComparison.OrdinalIgnoreCase) ?? false))
            .Where(row => query.Near switch
            {
                NearSupport => row.InSupport || row.ToSupport <= NearLevel,
                NearResistance => row.InResistance || row.ToResistance <= NearLevel,
                _ => true,
            })
            .Where(row => !query.Short || (oneShort?.Contains(row.Ticker) ?? false));

        return Sorted([.. kept], query.Sort, query.Descending);
    }

    // The rows in the order a column sorts them, a row holding no value last whichever way, a tie by the ticker; none
    // asked keeps the projection's order, nearest a level first.
    public static IReadOnlyList<UniverseCell> Sorted(IReadOnlyList<UniverseCell> rows, string? sort, bool descending)
    {
        static IComparable? Of(UniverseCell row, string key) => key switch
        {
            "name" => row.Ticker,
            "sector" => row.Sector,
            "close" => row.Close,
            "trend" => row.TrendState ?? NotClassified,
            "distance" => row.Nearest,
            "earnings" => row.SessionsUntilEarnings,
            "strength" => row.Strength,
            "pullback" => row.Depth,
            "dry-up" => row.DryUp,
            "tightness" => row.Tightness,
            "listed" => row.LastListed,
            _ => null,
        };

        if (sort is null || !SortKeys.Contains(sort, StringComparer.Ordinal))
        {
            return rows;
        }

        var held = rows.Where(row => Of(row, sort) is not null);
        var ordered = descending
            ? held.OrderByDescending(row => Of(row, sort)).ThenBy(row => row.Ticker, StringComparer.Ordinal)
            : held.OrderBy(row => Of(row, sort)).ThenBy(row => row.Ticker, StringComparer.Ordinal);

        return [.. ordered, .. rows.Where(row => Of(row, sort) is null).OrderBy(row => row.Ticker, StringComparer.Ordinal)];
    }

    // The rows a page holds at the size asked, every row where the link asks for all.
    public static int SizeOf(string? rows) =>
        rows == AllRows ? int.MaxValue : int.TryParse(rows, out var size) && PageSizes.Contains(size) ? size : PageSize;

    // A page of the kept rows at the size asked, with the page it is.
    public static Shown Paged(IReadOnlyList<UniverseCell> kept, string? rows, int? asked)
    {
        var size = SizeOf(rows);

        if (size == int.MaxValue)
        {
            return new Shown(kept, 1, kept.Count);
        }

        var pages = Math.Max(1, (kept.Count + size - 1) / size);
        var at = Math.Clamp(asked ?? 1, 1, pages);

        return new Shown([.. kept.Skip((at - 1) * size).Take(size)], at, kept.Count);
    }

    // The members one gate short of a setup on the S&P 500's night, each with the first setup in the one family order it
    // missed by one gate and that gate's words: the pullback's from the swing filter's rows, a row failing one gate with
    // nothing excluding it, and every other family's from its stored answers missing exactly one gate.
    // see: A member that missed exactly one gate and no exclusion is drawn close to a buy point nearest first, and recommends nothing
    public static IReadOnlyDictionary<string, (string Family, string Gate)> OneShort(IReadOnlyList<GateResultRow> gates, IReadOnlyList<FamilyResultRow> results)
    {
        var shorts = new Dictionary<string, (string Family, string Gate)>(StringComparer.Ordinal);

        foreach (var gate in gates.Where(CloseScreen.MissedOne))
        {
            var missed = !gate.Market ? SwingGates.Market : !gate.Trend ? SwingGates.Trend : !gate.Setup ? SwingGates.Setup : !gate.Trigger ? SwingGates.Trigger : SwingGates.Trade;

            shorts.TryAdd(gate.Ticker, (EquityBrief.Core.Families.SetupFamilies.Pullback, missed));
        }

        foreach (var result in results
            .Where(result => !result.Passed && result.Missed == 1 && result.Exclusions.Count == 0)
            .OrderBy(result => EquityBrief.Core.Families.SetupFamilies.OrderOf(result.Family)))
        {
            if (EquityBrief.Core.Families.FamilyRule.GatesOf(result.Gates).FirstOrDefault(gate => !gate.Passed) is { } missed)
            {
                shorts.TryAdd(result.Ticker, (result.Family, missed.Name));
            }
        }

        return shorts;
    }

    // Each sector's tile: its members, the share of them in an uptrend where the night reads the trend on the index, how
    // many tonight's lists hold, and its fund's return over the live heavyweights rule's look-back to the night; members
    // with no sector filed counted in none.
    public static IReadOnlyList<SectorLine> Tiles(
        IReadOnlyList<UniverseCell> rows,
        bool trendRead,
        IReadOnlySet<string> listedTonight,
        Func<string, (string Fund, double? Return)?> fund) =>
    [
        .. rows
            .Where(row => row.Sector != SectorNotOnFile)
            .GroupBy(row => row.Sector, StringComparer.Ordinal)
            .Select(group => new SectorLine(
                group.Key,
                group.Count(),
                group.Count(row => row.TrendState == "uptrend"),
                group.Count(row => listedTonight.Contains(row.Ticker)),
                trendRead,
                fund(group.Key)?.Fund,
                fund(group.Key)?.Return))
            .OrderBy(line => line.Sector, StringComparer.Ordinal),
    ];
}
