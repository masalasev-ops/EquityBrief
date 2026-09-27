using EquityBrief.Core.Bars;
using EquityBrief.Core.Prices;
using EquityBrief.Core.Returns;
using EquityBrief.Web.Marks;

namespace EquityBrief.Api.Reading;

// The projection from stored rows to what the Past picks screen and a name's own region draw.
//
// It sits beside `UniverseScreen` and derives for the reason that one does: four figures the screen
// draws are stored nowhere. What became of a trade as of the night drawn, which on a page for an earlier
// night is open where the store says it finished later. The sessions it was held, counted on the exchange
// calendar from the night it was listed. Its result in multiples of the risk it took, the stored return
// over the risk as a share of the buy. And where the price stood against the buy, read as ratios so a
// split since the listing moves neither side. Not in the read API, whose claim is that it computes
// nothing, and not in the page, whose claim is the same.
// see: A screen reads and renders, and computes nothing
// see: Every trade the live list recommended is shown, and their share waits for the minimum the reason records wait for
public static class PicksScreen
{
    // Every trade as of a night, in the order the rows came: newest night first, and within a night the
    // order that night's list was drawn in, improving businesses first where it stored its readings and the
    // filter's own order where it stored none.
    // see: Tonight's list is the swing filter's with improving businesses drawn first, and an evening is listed and ordered by the rule that listed it
    public static IReadOnlyList<PickCell> Cells(IReadOnlyList<PickRow> rows, DateOnly asOf) =>
        [.. rows.Select(row => Cell(row, asOf))];

    // A name's trades listed before the night its page draws, which is what "On the list before" states:
    // the night's own listing is the page's subject rather than one before it.
    public static IReadOnlyList<PickCell> Before(IReadOnlyList<PickRow> rows, DateOnly night) =>
        [.. Cells(rows, night).Where(cell => cell.Night < night)];

    // The trades a filter keeps. A status the screen does not filter by keeps every trade, as all does.
    public static IReadOnlyList<PickCell> Filtered(IReadOnlyList<PickCell> cells, string? status) =>
        status is { } asked && PickStatus.Filters.Contains(asked, StringComparer.Ordinal)
            ? [.. cells.Where(cell => cell.Status == asked)]
            : cells;

    // The trades counted, and the three figures that wait on the minimum computed only once both of its
    // counts are met. The counts are the run page's: the trades decided at their target or their stop
    // that set a bar, and the nights they were listed on, against the two minimums its reason records
    // wait on. A trade that ran out of time is finished and never in the share, as an unresolved setup is
    // never in a reason's, and it counts in the average result, which is what each trade earned. A share
    // over fewer is not drawn because it is not computed.
    // see: An unresolved setup is never a win
    public static PicksSummary Summary(IReadOnlyList<PickCell> cells)
    {
        var finished = cells.Where(cell => cell.Status is PickStatus.Target or PickStatus.Stopped or PickStatus.Time).ToArray();
        var decided = finished.Where(cell => ForwardReturnSeries.IsScored(Outcome(cell.Status), cell.BreakEven)).ToArray();
        var nights = decided.Select(cell => cell.Night).Distinct().Count();
        var met = decided.Length >= ReasonVerdict.MinimumResolved && nights >= ReasonVerdict.MinimumSessions;

        var results = finished.Where(cell => cell.Result is not null).Select(cell => cell.Result!.Value).ToArray();

        return new PicksSummary(
            cells.Count,
            cells.Select(cell => cell.Night).Distinct().Count(),
            cells.Count(cell => cell.Status == PickStatus.Open),
            finished.Count(cell => cell.Status == PickStatus.Target),
            finished.Count(cell => cell.Status == PickStatus.Stopped),
            finished.Count(cell => cell.Status == PickStatus.Time),
            cells.Count(cell => cell.Status == PickStatus.Missing),
            decided.Length,
            nights,
            ReasonVerdict.MinimumResolved,
            ReasonVerdict.MinimumSessions,
            met ? 100.0 * decided.Count(cell => cell.Status == PickStatus.Target) / decided.Length : null,
            met ? decided.Average(cell => cell.BreakEven!.Value) : null,
            met && results.Length > 0 ? results.Average() : null);
    }

    // The stored outcome a status stands for, which the run page's own test of a scored setup reads.
    static string? Outcome(string status) => status switch
    {
        PickStatus.Target => ForwardReturnSeries.Win,
        PickStatus.Stopped => ForwardReturnSeries.Loss,
        _ => null,
    };

    static PickCell Cell(PickRow row, DateOnly asOf)
    {
        // A trade the store says finished after the night drawn was open on it.
        var finishedBy = row.ResolvedOn is { } resolved && resolved <= asOf;

        var status = !row.OutcomeStored
            ? PickStatus.Missing
            : row.Outcome is null || !finishedBy
                ? PickStatus.Open
                : row.Outcome switch
                {
                    ForwardReturnSeries.Win => PickStatus.Target,
                    ForwardReturnSeries.Loss => PickStatus.Stopped,
                    ForwardReturnSeries.Unresolved => PickStatus.Time,
                    _ => PickStatus.Missing,
                };

        var finished = status is PickStatus.Target or PickStatus.Stopped or PickStatus.Time;

        // The risk as a percentage of the buy, which a finished trade's return is stated in multiples of.
        double? risk = row.Entry is { } buy && row.Stop is { } stop && buy > 0 && stop < buy
            ? Statistic.FromRatio((buy - stop) / buy) * 100
            : null;

        double? along = finished
            ? row.ReturnPct is { } made ? 1 + (made / 100) : null
            : status == PickStatus.Open && row.NowClose is { } now && row.ListingClose is { } then && then > 0
                ? Statistic.FromRatio(now / then)
                : null;

        return new PickCell(
            row.Ticker,
            row.Company,
            row.Night,
            row.Plan,
            row.Entry,
            row.Stop,
            row.Target,
            status,
            ExchangeClosures.SessionsUntil(row.Night, finished ? row.ResolvedOn!.Value : asOf),
            finished && row.ReturnPct is { } returned && risk is { } risked ? returned / risked : null,
            along,
            finished ? row.ResolvedOn : null,
            finished ? row.ReturnPct : null,
            status == PickStatus.Open ? row.NowOn : null,
            status == PickStatus.Open ? row.NowClose : null,
            row.BreakEven,
            row.State);
    }
}
