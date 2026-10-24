using System.Globalization;
using EquityBrief.Core.Bars;
using EquityBrief.Core.Families;
using EquityBrief.Core.Prices;
using EquityBrief.Web.Marks;

namespace EquityBrief.Api.Reading;

// The projection from the sector heavyweights' book to the card tonight's page draws and the name page's line. It
// reads what the book stored and reads the rule's own functions for what the card states of a close and of the next
// rebalance, so the card and the book say one thing.
// see: A screen reads and renders, and each figure it works out at a price or from stored figures has one function in the core
// see: The sector heavyweights hold the largest companies leading their sectors, rotated on the first session of each month whose stored year and the pulled history before it hold the closes their readings need
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
        IReadOnlyList<CandidateRow>? register = null,
        bool readsBefore = false)
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
                ? FormattableString.Invariant($"No reshuffle has been read yet. The first is read on the next night the store holds that night's closes of the index and of each sector's fund, and {HeavyweightRule.SessionsNeeded(HeavyweightRule.Live)} trading days of closes in the store's year with the history pulled before it, each company ranked by its value from the share counts stored with its quarterly reports.")
                : entered.Length == 0
                    ? FormattableString.Invariant($"At the reshuffle of {rebalance:d MMM yyyy}, none of the sectors' largest companies beat its sector's fund while in an uptrend and moving at least as much as the market, so it holds nothing until the next.")
                    : FormattableString.Invariant($"Everything the reshuffle of {rebalance:d MMM yyyy} bought has been sold since, as the lines beneath say.");
        var next = HeavyweightRule.NextRebalance(night, last, HeavyweightRule.Live, readsBefore);

        return new HeavyweightCardView(words.Heading, words.Eyebrow, RuleWords.Heavyweights(HeavyweightRule.Live), HeavyweightRule.Live.LookBack, last, next, cells, entered, ended, empty, liveSince, variants, Waits: Waiting(night, last, next, HeavyweightRule.Live, readsBefore));
    }

    // What the card says on a night in a month whose rebalance a book that has read one before has not read: that the
    // store's year to the night holds fewer sessions than the readings need where it does and the pulled history reaches
    // no session before it, and the session the book reads it on; none on any other night.
    // see: A night's reading needing more closes than the store's year holds reads the sessions before it from the pulled history at the store's scale
    static string? Waiting(DateOnly night, DateOnly? last, DateOnly? next, HeavyweightSettings settings, bool readsBefore = false)
    {
        if (last is null || !HeavyweightRule.Rebalances(night, last, settings.Weekly))
        {
            return null;
        }

        var on = next is { } day ? FormattableString.Invariant($"on {day:d MMM yyyy}") : "past the exchange calendar's table";
        var need = HeavyweightRule.SessionsNeeded(settings);

        return !readsBefore && BarRetention.SessionsTo(night) is { } held && held < need
            ? FormattableString.Invariant($"This month's reshuffle waits: the year of closes the store keeps to {night:d MMM yyyy} holds {held} trading days and its readings need {need}, so it is read {on}.")
            : $"This month's reshuffle has not been read yet, and it is tried again {on}.";
    }

    // The line an S&P 400's or 600's book draws for a sector it holds nothing in, since its rebalance stores its holdings
    // alone.
    public const string StoresNoSectorReading = "This book stores no reading for a sector it bought nothing in.";

    // A stock's or a fund's return over the look-back to tonight off its closes, none where either is not stored.
    public static double? ReturnOf(StandingCloses closes) =>
        HeavyweightStandings.Return(closes.Now is { } now ? Statistic.FromPrice(now) : null, closes.Back is { } back ? Statistic.FromPrice(back) : null);

    // A heavyweights card with its standing tonight: each holding's return since bought off its own book's growth carried
    // to the night and its standing off tonight's closes by the rule's own functions, design (b)'s read for no design of
    // its own; the eleven sectors in order with each fund's return over the look-back and why a sector holding nothing
    // holds none; and the four tiles.
    // see: Paper trading's accounts and a heavyweight's standing tonight are worked by one function in the core when a page is read
    public static HeavyweightCardView WithStanding(
        HeavyweightCardView card,
        DateOnly night,
        IReadOnlyDictionary<string, StandingCloses> stocks,
        IReadOnlyDictionary<string, StandingCloses> funds,
        IReadOnlyDictionary<string, double?> growth,
        Func<string, double?> sectorReturn,
        Func<string, string?> reason,
        bool designB = false,
        bool largeFunds = false)
    {
        HeavyweightHoldingCell[] holdings =
        [
            .. card.Holdings.Select(holding =>
            {
                var closes = stocks.GetValueOrDefault(holding.Ticker) ?? new StandingCloses(null, null, null);
                var standing = designB
                    ? new HeavyweightStanding(null, null, null, closes.Now is { } shut && holding.Average200 is { } line && line > 0 ? (Statistic.FromPrice(shut) / line) - 1.0 : null, HeavyweightStandings.NotForThisDesign)
                    : HeavyweightStandings.Of(closes.Now is { } now ? Statistic.FromPrice(now) : null, closes.Back is { } back ? Statistic.FromPrice(back) : null, holding.Average200, sectorReturn(holding.Sector));

                return holding with
                {
                    SinceBought = growth.TryGetValue(holding.Ticker, out var carried) && carried is { } grown ? grown - 1.0 : null,
                    Standing = standing,
                };
            }),
        ];

        double? FundReturn(string? fund) =>
            fund is not null && funds.TryGetValue(fund, out var closes) ? ReturnOf(closes) : null;

        // A close tonight and at the last rebalance, as the tiles' returns since it read them.
        static (double?, double?) SinceRebalance(StandingCloses closes) =>
            (closes.Now is { } now ? Statistic.FromPrice(now) : null, closes.AtRebalance is { } then ? Statistic.FromPrice(then) : null);

        HeavyweightSectorView[] sectors =
        [
            .. GicsSectors.Eleven
                .Concat(holdings.Select(holding => holding.Sector).Where(sector => !GicsSectors.Eleven.Contains(sector, StringComparer.Ordinal)).Distinct(StringComparer.Ordinal))
                .Select(sector =>
                {
                    var fund = GicsSectors.Funds.GetValueOrDefault(sector);

                    return new HeavyweightSectorView(
                        sector,
                        fund is null ? null : largeFunds ? fund + ", the S&P 500's fund," : fund,
                        FundReturn(fund),
                        holdings.Any(holding => holding.Sector == sector) ? null : reason(sector));
                }),
        ];

        string[] heldFunds = [.. holdings.Select(holding => GicsSectors.Funds.GetValueOrDefault(holding.Sector)).OfType<string>().Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)];

        return card with
        {
            Holdings = holdings,
            Sectors = sectors,
            Tiles = new HeavyweightTilesView(
                holdings.Length,
                GicsSectors.Eleven.Count(sector => holdings.Any(holding => holding.Sector == sector)),
                GicsSectors.Eleven.Count,
                card.LastRebalance is null ? null : HeavyweightStandings.EqualWeighted(holdings.Select(holding => stocks.GetValueOrDefault(holding.Ticker) is { } closes ? SinceRebalance(closes) : (null, null))),
                card.LastRebalance is null ? null : HeavyweightStandings.EqualWeighted(heldFunds.Select(fund => funds.GetValueOrDefault(fund) is { } closes ? SinceRebalance(closes) : (null, null))),
                heldFunds,
                card.NextRebalance is { } next ? EquityBrief.Core.Bars.ExchangeClosures.SessionsUntil(night, next) : null),
        };
    }

    // Why a sector the S&P 500's book, or one of its registered books, bought nothing in holds nothing, in words a reader
    // needs no rule to follow, read off the book's own rows of its last rebalance: none of its largest companies beat the
    // sector's return, naming the closest; none of those that beat it was in an uptrend, naming the strongest; those that
    // were moved less than the market, naming the strongest and its beta; or what it bought was sold since, naming it.
    // Each closes on the next rebalance where the card knows it.
    // see: A heavyweight is bought where it leads its sector above nothing and passes the trend gate, and sold where the rule would not buy it
    public static string? EmptySectorReason(
        string sector,
        IReadOnlyList<HeavyweightReadRow> read,
        bool fundReturn,
        bool highBeta,
        int lookBack = HeavyweightRule.LookBack,
        IReadOnlyList<HeavyweightHoldingRow>? holdings = null,
        DateOnly? next = null)
    {
        var rows = read.Where(row => row.Sector == sector).ToArray();

        if (rows.Length == 0)
        {
            return read.Count == 0 ? "No rebalance has been read yet." : "Its last rebalance read none of its companies.";
        }

        static string Day(DateOnly day) => day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        static string Points(double lead) => string.Create(CultureInfo.InvariantCulture, $"{Math.Abs(lead) * 100:0.0} points");

        var on = Day(rows[0].Session);
        var against = !fundReturn ? "its members' average" : GicsSectors.Funds.TryGetValue(sector, out var fund) ? fund : "its fund";
        var over = string.Create(CultureInfo.InvariantCulture, $"over the {lookBack} sessions to the {on} rebalance");
        var uptrend = "in an uptrend, the close above the 50-day average and that above the 200-day";
        var leading = rows.Where(row => row.Lead > 0).OrderByDescending(row => row.Lead).ThenBy(row => row.Ticker, StringComparer.Ordinal).ToArray();
        var trending = leading.Where(row => row.Trend).ToArray();
        var after = next is { } nextOn ? $" Next rebalance {Day(nextOn)}." : string.Empty;

        if (leading.Length == 0)
        {
            var closest = rows.Where(row => row.Lead is not null).OrderByDescending(row => row.Lead).ThenBy(row => row.Ticker, StringComparer.Ordinal).FirstOrDefault();

            return $"Nothing held: none of its {rows.Length} largest companies beat {against} {over}"
                + (closest is { Lead: { } behind } ? behind == 0 ? $"; the closest, {closest.Ticker}, matched it." : $"; the closest, {closest.Ticker}, trailed it by {Points(behind)}." : ".")
                + after;
        }

        if (trending.Length == 0)
        {
            return (leading.Length == 1
                ? $"Nothing held: only {leading[0].Ticker} beat {against} {over}, by {Points(leading[0].Lead!.Value)}, and it was not {uptrend}, which the rule requires."
                : string.Create(CultureInfo.InvariantCulture, $"Nothing held: {leading.Length} of its {rows.Length} largest beat {against} {over}, led by {leading[0].Ticker} by {Points(leading[0].Lead!.Value)}, but none was {uptrend}, which the rule requires."))
                + after;
        }

        if (highBeta && trending.All(row => row.Beta is not { } beta || beta < HeavyweightRule.BetaFloor))
        {
            var strongest = trending[0];
            var moves = strongest.Beta is { } its
                ? string.Create(CultureInfo.InvariantCulture, $"its beta was {its:0.00}, so it moves about {its * 100:0}% as much as the market")
                : "its beta could not be read";

            return (trending.Length == 1
                ? $"Nothing held: {strongest.Ticker} beat {against} {over}, by {Points(strongest.Lead!.Value)}, and was {uptrend}, but {moves}, and the rule buys only stocks that move at least as much as the market, a beta of 1 or more."
                : string.Create(CultureInfo.InvariantCulture, $"Nothing held: {trending.Length} beat {against} {over} and were {uptrend}, the strongest {strongest.Ticker} by {Points(strongest.Lead!.Value)}, but {moves}, the others less, and the rule buys only stocks that move at least as much as the market, a beta of 1 or more."))
                + after;
        }

        var sold = (holdings ?? []).Where(holding => holding.Sector == sector && holding.EnteredOn == rows[0].Session && holding.EndedOn is not null).OrderBy(holding => holding.Ticker, StringComparer.Ordinal).ToArray();

        return (sold.Length == 0
            ? $"Nothing held: what the {on} rebalance bought here has been sold since."
            : $"Nothing held: the {on} rebalance bought {string.Join(" and ", sold.Select(holding => holding.Ticker))}, sold since on {string.Join(" and ", sold.Select(holding => Day(holding.EndedOn!.Value)).Distinct(StringComparer.Ordinal))}.")
            + after;
    }

    // The stocks a book holds open at the night's close, in the order of their sectors and then by ticker, which a chosen
    // variant's book is read beside.
    public static IReadOnlyList<string> OpenHoldings(IReadOnlyList<HeavyweightHoldingRow> holdings) =>
        [.. holdings.Where(holding => holding.EndedOn is null).OrderBy(holding => holding.Sector, StringComparer.Ordinal).ThenBy(holding => holding.Ticker, StringComparer.Ordinal).Select(holding => holding.Ticker)];

    // What a name's page says where the sector heavyweights hold the name at its night's close, and nothing where
    // they do not.
    public static string? HeldAsAHeavyweight(string ticker, IReadOnlyList<HeavyweightHoldingRow> holdings) =>
        holdings.FirstOrDefault(holding => holding.Ticker == ticker && holding.EndedOn is null) is { } held
            ? FormattableString.Invariant($"Held by the sector heavyweights since {held.EnteredOn:yyyy-MM-dd} as the leader of {held.Sector}, while it leads.")
            : null;
}
