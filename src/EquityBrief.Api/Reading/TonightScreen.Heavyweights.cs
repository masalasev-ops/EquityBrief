using EquityBrief.Core.Bars;
using EquityBrief.Core.Families;
using EquityBrief.Core.Prices;
using EquityBrief.Web.Marks;

namespace EquityBrief.Api.Reading;

// The projection from the sector heavyweights' book to the card tonight's page draws and the name page's line. It
// reads what the book stored and reads the rule's own functions for what the card states of a close and of the next
// rebalance, so the card and the book say one thing.
// see: A screen reads and renders, and computes only the plan in the operator's money and a pick's open trades in its sector
// see: The sector heavyweights hold the largest companies leading their sectors, rotated on the first session of each month whose stored year holds the closes their readings need
public static partial class TonightScreen
{
    // The card on a night: the holdings open at its close in the order of their sectors, each with its lead at the last
    // rebalance on or before the night and its close there against its 200-session average; what that rebalance
    // bought; what ended at it or since; the next rebalance on the exchange's calendar, the next session where the book
    // has read none in that session's month and the store's year holds the closes the live setting's readings need,
    // and on a night whose month's rebalance has not been read, that it waits; and the day the family's live rule
    // registered with the variants standing beside it, read off the register as it stood at the night's end.
    // see: The sector heavyweights freeze at their sweep's proposal, the proposal's three passing neighbours registered beside them as variants
    public static HeavyweightCardView Heavyweights(
        DateOnly night,
        IReadOnlyList<HeavyweightHoldingRow> holdings,
        IReadOnlyList<HeavyweightReadRow> read,
        IReadOnlyList<HeavyweightCloseRow> closes,
        IReadOnlyDictionary<string, UniverseCell> cellByTicker,
        IReadOnlyList<CandidateRow>? register = null)
    {
        var words = SetupFamilies.SectorHeavyweights;
        var (liveSince, variants) = Standing(HeavyweightRule.Name, register ?? [], night);
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
                ? FormattableString.Invariant($"No rebalance has been read yet. The book reads its first on the next night the store holds that night's closes of the index and of each sector's fund and the {HeavyweightRule.SessionsNeeded(HeavyweightRule.Live)} sessions its readings need, ranking each member by the company and share counts the quarters fetch stores from the answer it already asks for.")
                : entered.Length == 0
                    ? FormattableString.Invariant($"No sector's largest companies led their sector's fund above nothing while passing the trend gate with a beta of at least one at the rebalance of {rebalance:yyyy-MM-dd}, so the book holds nothing until the next.")
                    : FormattableString.Invariant($"Every holding the rebalance of {rebalance:yyyy-MM-dd} bought has been sold since, as the notes beneath say.");
        var next = HeavyweightRule.NextRebalance(night, last, HeavyweightRule.Live);

        return new HeavyweightCardView(words.Heading, words.Eyebrow, RuleWords.Heavyweights(HeavyweightRule.Live), HeavyweightRule.Live.LookBack, last, next, cells, entered, ended, empty, liveSince, variants, Waits: Waiting(night, last, next, HeavyweightRule.Live));
    }

    // What the card says on a night in a month whose rebalance a book that has read one before has not read: that the
    // store's year to the night holds fewer sessions than the readings need where it does, and the session the book reads
    // it on; none on any other night.
    static string? Waiting(DateOnly night, DateOnly? last, DateOnly? next, HeavyweightSettings settings)
    {
        if (last is null || !HeavyweightRule.Rebalances(night, last, settings.Weekly))
        {
            return null;
        }

        var on = next is { } day ? FormattableString.Invariant($"on {day:yyyy-MM-dd}") : "past the exchange calendar's table";
        var need = HeavyweightRule.SessionsNeeded(settings);

        return BarRetention.SessionsTo(night) is { } held && held < need
            ? FormattableString.Invariant($"The rebalance of this month waits: the year of closes the store keeps to {night:yyyy-MM-dd} holds {held} sessions and its readings need {need}, so the book reads it {on}.")
            : $"The rebalance of this month has not been read, and the book tries again {on}.";
    }

    // What a name's page says where the sector heavyweights hold the name at its night's close, and nothing where
    // they do not.
    public static string? HeldAsAHeavyweight(string ticker, IReadOnlyList<HeavyweightHoldingRow> holdings) =>
        holdings.FirstOrDefault(holding => holding.Ticker == ticker && holding.EndedOn is null) is { } held
            ? FormattableString.Invariant($"Held by the sector heavyweights since {held.EnteredOn:yyyy-MM-dd} as the leader of {held.Sector}, while it leads.")
            : null;
}
