using System.Text.Json;
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
        IReadOnlyList<ListingRow> listings,
        IReadOnlyDictionary<string, int> strengthByTicker,
        IReadOnlyDictionary<string, decimal?> closeByTicker) =>
    [
        .. listings
            .Where(listing => listing.FiredCount > 0)
            .Select(listing => Cell(listing, strengthByTicker, closeByTicker))
            .OrderByDescending(cell => cell.FiredCount)
            .ThenByDescending(cell => cell.Strength)
            .ThenBy(cell => cell.Ticker, StringComparer.Ordinal),
    ];

    static ListingCell Cell(
        ListingRow listing,
        IReadOnlyDictionary<string, int> strengthByTicker,
        IReadOnlyDictionary<string, decimal?> closeByTicker)
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

        return new ListingCell(
            listing.Ticker,
            listing.SessionDate,
            listing.FiredCount,
            strengthByTicker.TryGetValue(listing.Ticker, out var strength) ? strength : 0,
            closeByTicker.TryGetValue(listing.Ticker, out var close) ? close : null,
            [.. fired.Select(reason => reason.Name)],
            fired);
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
