using EquityBrief.Core.Families;
using EquityBrief.Core.Prices;
using EquityBrief.Web.Marks;

namespace EquityBrief.Api.Reading;

// The projection from the sector heavyweights' book to the card tonight's page draws and the name page's line. It
// reads what the book stored and reads the rule's own functions for what the card states of a close and of the next
// rebalance, so the card and the book say one thing.
// see: A screen reads and renders, and computes nothing
// see: The sector heavyweights hold the largest companies leading their sectors, rotated on the first session of each month
public static partial class TonightScreen
{
    // The card on a night: the holdings open at its close in the order of their sectors, each with its lead at the last
    // rebalance on or before the night and its close there against its 200-session average; what that rebalance
    // bought; what ended at it or since; and the next rebalance on the exchange's calendar.
    public static HeavyweightCardView Heavyweights(
        DateOnly night,
        IReadOnlyList<HeavyweightHoldingRow> holdings,
        IReadOnlyList<HeavyweightReadRow> read,
        IReadOnlyList<HeavyweightCloseRow> closes,
        IReadOnlyDictionary<string, UniverseCell> cellByTicker)
    {
        var words = SetupFamilies.SectorHeavyweights;
        DateOnly? last = read.Count > 0 ? read[0].Session : null;
        var closeOf = closes.ToDictionary(close => close.Ticker, StringComparer.Ordinal);
        var open = holdings
            .Where(holding => holding.EndedOn is null)
            .OrderBy(holding => holding.Sector, StringComparer.Ordinal)
            .ThenBy(holding => holding.Ticker, StringComparer.Ordinal)
            .ToArray();

        HeavyweightHoldingCell[] cells =
        [
            .. open.Select(holding =>
            {
                var reading = read.FirstOrDefault(row => row.Ticker == holding.Ticker);
                var close = closeOf.GetValueOrDefault(holding.Ticker);

                return new HeavyweightHoldingCell(
                    holding.Ticker,
                    cellByTicker.TryGetValue(holding.Ticker, out var cell) ? cell.Name : null,
                    holding.Sector,
                    holding.EnteredOn,
                    reading?.Lead,
                    reading?.Session,
                    close?.Close,
                    close?.Average200,
                    close is { Close: { } shut, Average200: { } average } && HeavyweightRule.Broken(Statistic.FromPrice(shut), average));
            }),
        ];

        string[] entered = last is { } on
            ? [.. holdings.Where(holding => holding.EnteredOn == on).OrderBy(holding => holding.Sector, StringComparer.Ordinal).ThenBy(holding => holding.Ticker, StringComparer.Ordinal).Select(holding => holding.Ticker)]
            : [];

        HeavyweightEndedCell[] ended = last is { } since
            ? [.. holdings
                .Where(holding => holding.EndedOn is { } end && end >= since)
                .OrderBy(holding => holding.EndedOn)
                .ThenBy(holding => holding.Ticker, StringComparer.Ordinal)
                .Select(holding => new HeavyweightEndedCell(holding.Ticker, holding.EndedOn!.Value, holding.Reason ?? "ended"))]
            : [];

        var empty = cells.Length > 0
            ? null
            : last is not { } rebalance
                ? "No rebalance has been read yet. The book reads its first on the first night every member's company and share count are stored, which the quarters fetch writes from the answer it already asks for."
                : entered.Length == 0
                    ? FormattableString.Invariant($"No sector's largest company led its sector above nothing while passing the trend gate at the rebalance of {rebalance:yyyy-MM-dd}, so the book holds nothing until the next.")
                    : FormattableString.Invariant($"Every holding the rebalance of {rebalance:yyyy-MM-dd} bought has been sold since, as the notes beneath say.");

        return new HeavyweightCardView(words.Heading, words.Eyebrow, words.Rule, HeavyweightRule.LookBack, last, HeavyweightRule.NextRebalance(night), cells, entered, ended, empty);
    }

    // What a name's page says where the sector heavyweights hold the name at its night's close, and nothing where
    // they do not.
    public static string? HeldAsAHeavyweight(string ticker, IReadOnlyList<HeavyweightHoldingRow> holdings) =>
        holdings.FirstOrDefault(holding => holding.Ticker == ticker && holding.EndedOn is null) is { } held
            ? FormattableString.Invariant($"Held by the sector heavyweights since {held.EnteredOn:yyyy-MM-dd} as the leader of {held.Sector}, while it leads.")
            : null;
}
