using EquityBrief.Core.Bars;
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
// see: A screen reads and renders, and computes nothing
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
    public const string SectorNotOnFile = "not on file";

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
        DateOnly? night = null) =>
    [
        .. rows
            .Select(row => Cell(row, history, nextEventByTicker, night))
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

    // The sessions between a night and a name's next dated event, which is what
    // section 15.8's column states.
    //
    // Counted off the exchange's own calendar rather than off stored bars,
    // because the event is ahead of the night and no bar exists for a session
    // that has not happened. It is the sessions strictly after the night up to
    // and including the event's own day where that day is a session, so an event
    // on tomorrow's session reads as 1 and one on tonight's reads as 0.
    //
    // An event past the end of the closure table gives no count rather than a
    // wrong one. The table covers three years and a night says so on its closing
    // line within a quarter of the end, and a column that guessed weekdays past
    // it would be the guess that row exists to refuse.
    // owes: The exchange closure table extended before the nights reach its end
    public static int? SessionsUntil(DateOnly night, DateOnly eventDate)
    {
        if (eventDate < night || eventDate > ExchangeClosures.CoveredThrough || night < ExchangeClosures.CoveredFrom)
        {
            return null;
        }

        return eventDate == night
            ? 0
            : ExchangeClosures.SessionsBetween(night, eventDate).Count
                + (ExchangeClosures.IsSession(eventDate) ? 1 : 0);
    }

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

        var listed = listings.Where(listing => listing.FiredCount > 0).ToArray();

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
            [.. listings.OrderBy(listing => listing.SessionDate).Select(listing => listing.FiredCount > 0)],
            nextEvent is { } dated && night is { } on ? SessionsUntil(on, dated) : null,
            nextEvent,
            nextEvent is { } beyond && beyond > ExchangeClosures.CoveredThrough);
    }

    // The gap to a band edge, in typical days' moves, as a distance rather than
    // a direction: what the screen says is how far a name is from an edge, and
    // a name three days below its resistance and one above its support is near
    // the support.
    //
    // A typical move of zero or less gives no distance rather than an infinite
    // one. That is a name whose chart has not moved over the window the average
    // is taken across, and dividing by it would put it at the top of the screen
    // for having been still.
    static double? Distance(decimal? close, decimal? edge, double? typicalMove) =>
        close is { } price && edge is { } band && typicalMove is > 0
            ? Statistic.FromPrice(Math.Abs(price - band)) / typicalMove.Value
            : null;

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
}
