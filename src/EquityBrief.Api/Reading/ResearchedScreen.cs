using EquityBrief.Core.Prices;
using EquityBrief.Web.App;

namespace EquityBrief.Api.Reading;

// The researched list from 19.7: why each report was written, read off the request a pass settled on the day its newest
// section was written or the day after; how far its stock has moved since, as the ratio of the store's closes; and the
// list sorted by the day written or by staleness, a stale report first. It reads what the store holds and writes nothing.
public static class ResearchedScreen
{
    public const string SortQuery = "sort";

    public const string ByWritten = "written";

    public const string ByStaleness = "stale";

    public const string TonightsList = "tonight's list";

    public const string AskedByYou = "asked by you";

    public const string Regenerated = "a regeneration you asked for";

    public const string ByHand = "by hand";

    // Why a report was written: the newest request a pass settled on the day its newest section was written or the day
    // after, the night's own request being tonight's list and a press asking for every section again a regeneration; by
    // hand where no request was settled then, a pass run from the worker's command.
    public static string Why(DateOnly written, IReadOnlyList<WrittenRequestRow> requests)
    {
        var settled = requests
            .Where(request => DateOnly.FromDateTime(request.SettledAt.UtcDateTime) is var day && (day == written || day == written.AddDays(1)))
            .MaxBy(request => request.SettledAt);

        return settled switch
        {
            null => ByHand,
            { Refresh: true } => Regenerated,
            { AskedFrom: "night" } => TonightsList,
            _ => AskedByYou,
        };
    }

    // The move since a report was written: the newest close on or before the day it was written against the newest close
    // the store holds, less one; none where either is not held.
    public static double? Move(DateOnly written, IReadOnlyDictionary<(string Ticker, DateOnly Session), decimal> closes, string ticker)
    {
        var mine = closes.Where(close => close.Key.Ticker == ticker).OrderBy(close => close.Key.Session).ToArray();

        return mine.LastOrDefault(close => close.Key.Session <= written) is { Key.Ticker: not null } then && then.Value > 0m && mine.Length > 0
            ? Statistic.FromRatio((mine[^1].Value / then.Value) - 1m)
            : null;
    }

    // The list in the order the link asks: the newest written first, or a stale report first and then the newest written.
    public static IReadOnlyList<ResearchedCell> Sorted(IReadOnlyList<ResearchedCell> cells, string? sort) =>
        sort == ByStaleness
            ? [.. cells.OrderByDescending(cell => cell.Stale == true).ThenByDescending(cell => cell.Written).ThenBy(cell => cell.Ticker, StringComparer.Ordinal)]
            : [.. cells.OrderByDescending(cell => cell.Written).ThenBy(cell => cell.Ticker, StringComparer.Ordinal)];
}
