using System.Text.Json;
using EquityBrief.Core.Prices;
using EquityBrief.Core.Shortlist;
using EquityBrief.Web.Marks;

namespace EquityBrief.Api.Reading;

// The projection from stored listing rows to what tonight's list draws.
//
// It sits beside `NameScreen` and `UniverseScreen` and in the same seam. Nothing
// here decides whether a reason fired: the builder decided that and wrote it,
// and this reads it back. What it does is order, count and cut, which is what
// section 15.7 states the page does.
// see: A screen reads and renders, and computes nothing
// see: The page shows twenty and states the true count
public static class TonightScreen
{
    // Section 17's list display. The strongest twenty are drawn and the true
    // fired count is always stated, because a page that shows twenty every night
    // cannot tell you how busy the night was.
    public const int Drawn = 20;

    // The order section 15.7 states: how many reasons fired, then band strength
    // as the tiebreaker.
    //
    // The strength score is the obligation this checkpoint discharges. The phase
    // 3 sign-off measured it over four names: touches are 72 to 79 per cent of
    // the members of each name's strongest band, anchor counts are bunched at 4
    // to 15 while touch counts run 0 to 41, and MSFT's top two bands carry the
    // same 4 anchors with the ranking decided by 15 touches against 9. So the
    // score is dominated by touches, and a tiebreaker dominated by touches is a
    // tiebreaker about how often a price has come back to a level, which is what
    // the ordering is for. It is read from the stored band rather than recomputed
    // here.
    // owes: The strength score read against four names
    public static IReadOnlyList<ListingCell> Rows(
        DateOnly night,
        IReadOnlyList<ListingRow> listings,
        IReadOnlyDictionary<string, int> strengthByTicker,
        IReadOnlyDictionary<string, UniverseCell> cellByTicker,
        IReadOnlyList<CloseRow> closesToTheNight)
    {
        // The two newest sessions each name holds at or before the night, newest
        // first, which is what both the close cell and the day change are read
        // from. One read for the pair, because the pair is the property: two
        // reads gave 5.8 a close from one session and a previous close from
        // another, and the subtraction of two sessions that are not consecutive
        // is a number rather than an absence.
        var sessions = closesToTheNight
            .GroupBy(row => row.Ticker, StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => (IReadOnlyList<CloseRow>)[.. group.OrderByDescending(row => row.SessionDate)],
                StringComparer.Ordinal);

        return
        [
            .. listings
                .Where(listing => listing.FiredCount > 0)
                .Select(listing => Cell(listing, night, strengthByTicker, cellByTicker, sessions))
                .OrderByDescending(cell => cell.FiredCount)
                .ThenByDescending(cell => cell.Strength)
                .ThenBy(cell => cell.Ticker, StringComparer.Ordinal),
        ];
    }

    // The day's change, as a signed percentage of the session before.
    //
    // Derived, and derived here for the reason `UniverseScreen` states about the
    // distance: it is not a stored column, the read surface computes nothing and
    // the page computes nothing, so the projection is the seam it belongs in.
    //
    // It takes the name's own sessions rather than two closes, because the two
    // closes are what went wrong: a change is only a day's change where the two
    // sessions behind it are the name's two newest at the night and the newer of
    // them is the night. A name with no bar on the night has no day change at
    // all, and drawing one from its last two stored sessions would be a figure
    // about a day the page is not showing. It read the newest bar against the
    // newest bar before the night until the sixth phase 5 sign-off review, which
    // is a wrong number on any past night and exactly 0.00 for a stale name.
    //
    // A previous close of zero or less gives no change rather than an infinite
    // one. That is a name the store holds a close of nothing for, and dividing
    // by it would report the price itself as a day's move, which is the shape
    // the earnings rule's own earlier-session guard was written against.
    // see: Code owns every number
    public static double? DayChange(DateOnly night, IReadOnlyList<CloseRow>? sessions)
    {
        if (sessions is not { Count: >= 2 } held || held[0].SessionDate != night || held[1].Close <= 0m)
        {
            return null;
        }

        return Statistic.FromPrice((held[0].Close - held[1].Close) / held[1].Close) * 100;
    }

    // The close the row shows: the name's close on the night, and nothing where
    // the name has no bar on it.
    //
    // Read from the same pair the change is, rather than from the universe row,
    // whose close column is the name's newest bar whatever night is asked for.
    // That column is what made a past night's row carry today's close beside a
    // change computed from that night's, and the two disagreeing on one row is
    // what the sixth review demonstrated. The universe screen still reads it, and
    // a past night's bands and plan are the same shape and are carried at 6.0.
    // owes: The phase 5 sign-off's remaining store-shape findings ruled or fixed
    public static decimal? CloseOn(DateOnly night, IReadOnlyList<CloseRow>? sessions) =>
        sessions is { Count: > 0 } held && held[0].SessionDate == night ? held[0].Close : null;

    // Which row the selected-name region is drawn for: the one the reader asked
    // for, and the first when they have asked for none or for a name that is not
    // on tonight's list.
    //
    // Section 15.7 says the region is for whichever row is selected. A
    // composition that always took the first would satisfy every count on this
    // page and answer a question the reader did not ask, which is what it did
    // until 5.8. The fallback is the first row rather than nothing, because the
    // page opens with no name in the hash and a region that were absent then
    // would make the common case the empty one.
    public static ListingCell? Selected(IReadOnlyList<ListingCell> rows, string? asked) =>
        rows.FirstOrDefault(row => string.Equals(row.Ticker, asked, StringComparison.Ordinal))
            ?? rows.FirstOrDefault();

    static ListingCell Cell(
        ListingRow listing,
        DateOnly night,
        IReadOnlyDictionary<string, int> strengthByTicker,
        IReadOnlyDictionary<string, UniverseCell> cellByTicker,
        IReadOnlyDictionary<string, IReadOnlyList<CloseRow>> sessionsByTicker)
    {
        using var document = JsonDocument.Parse(listing.Reasons);

        // The values that made each reason true, carried beside the names since
        // 5.6, because 15.7's reasons-per-row half shows them on hover and a row
        // holding only the names could not.
        var fired = document.RootElement.EnumerateArray()
            .Where(reason => reason.GetProperty("fired").GetBoolean())
            .Select(reason => new FiredReason(
                reason.GetProperty("name").GetString()!,
                reason.GetProperty("values").EnumerateObject()
                    .ToDictionary(value => value.Name, value => value.Value.GetString()!, StringComparer.Ordinal)))
            .ToArray();

        var cell = cellByTicker.GetValueOrDefault(listing.Ticker);
        var sessions = sessionsByTicker.GetValueOrDefault(listing.Ticker);

        return new ListingCell(
            listing.Ticker,
            listing.SessionDate,
            listing.FiredCount,
            strengthByTicker.TryGetValue(listing.Ticker, out var strength) ? strength : 0,
            CloseOn(night, sessions),
            [.. fired.Select(reason => reason.Name)],
            fired,
            // The three the row states beside the name, the close and the
            // reasons. Each is absent rather than zero for a name the night
            // computed nothing for, which is the rule every other column on
            // every other screen already follows.
            DayChange(night, sessions),
            cell?.TrendState,
            cell);
    }

    // The true fired count over the whole index, which is the headline. It is
    // the market's mood and it is the one number the twenty drawn rows cannot
    // tell you.
    public static int Fired(IReadOnlyList<ListingRow> listings) =>
        listings.Count(listing => listing.FiredCount > 0);

    // Which of the six fired, and how often, across tonight's list. Whether the
    // evening is one thing happening to many names or many things happening to a
    // few.
    public static IReadOnlyList<ReasonTotal> Totals(IReadOnlyList<ListingRow> listings)
    {
        var counted = ShortlistSeries.Reasons.ToDictionary(name => name, _ => 0, StringComparer.Ordinal);

        foreach (var listing in listings)
        {
            using var document = JsonDocument.Parse(listing.Reasons);

            foreach (var reason in document.RootElement.EnumerateArray())
            {
                if (reason.GetProperty("fired").GetBoolean())
                {
                    counted[reason.GetProperty("name").GetString()!]++;
                }
            }
        }

        // Every reason is a row whether or not it fired, in section 11's own
        // order, so a reason that fires on no night is still visible as one that
        // fires on no night.
        return [.. ShortlistSeries.Reasons.Select(name => new ReasonTotal(name, counted[name]))];
    }

    // The evenings a name was on the list over a window, which is what the
    // listing strip draws and what the universe screen's two right-hand columns
    // count. They say nothing about index membership, which every name in that
    // table has by definition.
    public static IReadOnlyList<bool> Strip(IReadOnlyList<ListingRow> history) =>
        [.. history.OrderBy(listing => listing.SessionDate).Select(listing => listing.FiredCount > 0)];
}
