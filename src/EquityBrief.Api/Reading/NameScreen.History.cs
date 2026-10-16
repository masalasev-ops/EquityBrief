using System.Globalization;
using System.Text.Json;
using EquityBrief.Core.Cards;
using EquityBrief.Core.Ladders;
using EquityBrief.Core.Levels;
using EquityBrief.Core.Moves;
using EquityBrief.Core.News;
using EquityBrief.Core.Providers;
using EquityBrief.Core.Quotes;
using EquityBrief.Core.Report;
using EquityBrief.Web.App;
using EquityBrief.Web.Marks;

namespace EquityBrief.Api.Reading;

// What the name page's history parts read, as the read API handed it: the typical earnings move and the long view the chart
// averages step stored, the filings the filings refresh stored over the year's sessions, the stored closes beside their
// raw closes, the stored stories with their labels, the zone a session's close is read in, and whether the page draws the
// newest night, the only night the typical earnings move and the long view are stored for.
public sealed record HistoryInputs(
    EarningsMoveRow? Earnings,
    LongViewRow? LongView,
    IReadOnlyList<FiledRow> Filed,
    IReadOnlyList<RawCloseRow> RawCloses,
    IReadOnlyList<NewsArticleRow> Stories,
    TimeZoneInfo Zone,
    bool Newest);

// The name page's typical earnings move, long view, chart marks, moves' causes, ladder figure and plan shares, each worked
// by the core from what the read API handed over; nothing here computes a figure of its own.
// see: A screen reads and renders, and each figure it works out at a price or from stored figures has one function in the core
public static partial class NameScreen
{
    // The view of the history parts, at the price the masthead draws.
    public static HistoryView History(
        HistoryInputs inputs,
        IReadOnlyList<BarRow> bars,
        IReadOnlyList<LevelRow> levels,
        double? typicalMove,
        LadderRow? ladder,
        IReadOnlyList<ReactionRow> reactions,
        IReadOnlyList<KeptDividend> dividends,
        YearExtremes? year,
        LiveQuoteView? live)
    {
        var (tranches, exits, invalidation) = Plan(ladder);

        // The plan's levels sit on the long view's prices only where both were read on one night: a refetch between them
        // moves the stored year's scale, and a level drawn across a line at another scale stands at the wrong price. The
        // year's high and low are read off the bars the long view's year is, so they are drawn whichever plan is stored.
        var apart = ladder is null
            ? "no plan is stored for the night, the plan being read for the S&P 500's members alone"
            : inputs.LongView is { } view && view.Night != ladder.AsOf
                ? FormattableString.Invariant($"the plan was read on {ladder.AsOf:yyyy-MM-dd} and the long view on {view.Night:yyyy-MM-dd}, so the plan's levels are drawn once both are read on one night")
                : null;

        LadderBand[] bands =
        [
            .. levels.Select(level =>
            {
                var (touches, last) = BandTurns.Of(Members(level.Members).Select(member => (member.Kind, member.Date)));

                return new LadderBand(level.LowEdge, level.HighEdge, level.Role, AwayFromThePrice(bars, level, typicalMove, live), touches, last, PlanUse(level, tranches, exits));
            }),
        ];
        var price = live?.Price ?? (bars.Count > 0 ? bars[^1].Close : null);
        var unplanned = PlanReason(ladder, tranches.Count);

        return new HistoryView(
            inputs.Newest,
            inputs.Earnings?.Move,
            inputs.Earnings?.Pull,
            inputs.Earnings?.Reason,
            inputs.LongView?.Closes ?? [],
            inputs.LongView?.Pull,
            inputs.LongView?.Ratio,
            inputs.LongView?.Reason,
            apart is null ? LongViewLevels(tranches, exits, invalidation, year) : LongViewLevels([], [], null, year),
            apart,
            Events(bars, reactions, dividends, inputs.RawCloses),
            bands,
            price,
            live is not null,
            PlanSizes.Of(tranches, exits),
            ladder is null ? "no plan is stored for the night, the plan being read for the S&P 500's members alone" : null,
            tranches.Count == 0 ? null : Trail(exits),
            MoveCauses.LabelsCover(Stories(inputs.Stories)),
            RiskLinesAt(bands, price, invalidation),
            unplanned,
            unplanned is { } why ? PlanForms(why, bands, price) : null);
    }

    // The reason a stored ladder gives for placing no tranche, as the ladder's rule wrote it, or none where it placed one.
    static string? PlanReason(LadderRow? ladder, int tranches)
    {
        if (ladder is null || tranches > 0)
        {
            return null;
        }

        using var plan = JsonDocument.Parse(ladder.Plan);

        return plan.RootElement.TryGetProperty("reason", out var reason) && reason.ValueKind == JsonValueKind.String ? reason.GetString() : null;
    }

    // What would bring a plan to a name whose ladder placed none, read off the reason the ladder stored, in the words the
    // ladder's rule and the night's builder write it, and the bands about the price: the trend no longer reading down and a
    // support band beneath the price, which a close above the nearest band above would give, a band beneath it anchored on
    // more than an average, the sessions or swings the trend is read from, or the night's bars. A reason none of these
    // opens gives none.
    // see: A name with no plan is drawn with the reason its ladder stored and what would bring a plan, and no empty table
    public static string? PlanForms(string reason, IReadOnlyList<LadderBand> bands, decimal? price)
    {
        var above = price is { } now ? bands.Where(band => band.LowEdge > now).OrderBy(band => band.LowEdge).FirstOrDefault() : null;
        var reclaim = above is { } nearest
            ? FormattableString.Invariant($" A close above {Figures.Price(nearest.HighEdge)} would put the nearest band above{Named(nearest)}, beneath the price.")
            : string.Empty;

        bool Opens(string words) => reason.StartsWith(words, StringComparison.Ordinal);

        return Opens("the trend is down")
            ? "A plan forms on a night the trend no longer reads down and a support band sits beneath the price." + reclaim
            : Opens("no support band sits below the price")
                ? "A plan forms on a night a support band sits beneath the price." + reclaim
                : Opens("no support band below the price has an anchor other than a moving average")
                    ? "A plan forms on a night a band beneath the price holds a swing, a touch, a retracement or a volume shelf and not an average alone."
                    : Opens("no 50-day average") || Opens("no 200-day average") || Opens("no stored bars")
                        ? "A plan forms once the name holds the sessions its averages need."
                        : Opens("fewer than")
                            ? "A plan forms once the name's chart holds the swings its trend is read from."
                            : Opens("the trend state could not be classified")
                                ? "A plan forms once its trend can be read."
                                : Opens("no bar for this session")
                                    ? "A plan forms on the next night its bars arrive."
                                    : null;
    }

    // The lines the risks' two columns open on, read off the bands about the price the masthead draws and the plan's
    // invalidation: a close above the nearest resistance band above the price, and a close below the invalidation or,
    // where no plan is stored, below the nearest support band beneath the price.
    // see: The risks close on what would turn the stock up and what would make it worse, each column opening on a line the plan computes
    public static RiskLines RiskLinesAt(IReadOnlyList<LadderBand> bands, decimal? price, decimal? invalidation)
    {
        var above = price is { } now
            ? bands.Where(band => band.Role == "resistance" && band.LowEdge > now).OrderBy(band => band.LowEdge).FirstOrDefault()
            : null;
        var below = price is { } then
            ? bands.Where(band => band.Role == "support" && band.HighEdge < then).OrderByDescending(band => band.HighEdge).FirstOrDefault()
            : null;

        return new RiskLines(
            above is { } resistance
                ? FormattableString.Invariant($"A close above the nearest resistance band{Named(resistance)}, {MarkRenderer.Away(resistance.Away, above: true)}.")
                : "No resistance band stands above the price, so no close above one is read.",
            invalidation is { } floor
                ? FormattableString.Invariant($"A close below the plan's invalidation at {Figures.Price(floor)}, where the whole position is sold.")
                : below is { } support
                    ? FormattableString.Invariant($"A close below the nearest support band{Named(support)}, {MarkRenderer.Away(support.Away, above: false)}.")
                    : "No support band stands beneath the price, so no close below one is read.");
    }

    // A band as its line names it: the one price a band of one price stands at, and otherwise its two edges.
    static string Named(LadderBand band) =>
        band.LowEdge == band.HighEdge
            ? " at " + Figures.Price(band.LowEdge)
            : ", " + Figures.Price(band.LowEdge) + " to " + Figures.Price(band.HighEdge);

    // Each move with the cause the core took for it in its stated order, over the sessions the move spans.
    // see: A move's cause is research's where written, then the labelled news inside it and then the filings dated inside it
    public static IReadOnlyList<MoveCell> TakenCauses(IReadOnlyList<MoveCell> cells, IReadOnlyList<BarRow> bars, IReadOnlyList<ReactionRow> reactions, HistoryInputs inputs)
    {
        var at = new Dictionary<DateOnly, int>();

        for (var index = 0; index < bars.Count; index++)
        {
            at[bars[index].SessionDate] = index;
        }

        var stories = Stories(inputs.Stories);
        CausePrint[] prints = [.. reactions.Select(reaction => new CausePrint(reaction.ReportDate, reaction.Timing, reaction.Session))];
        CauseFiling[] filings =
        [
            .. inputs.Filed.Select(filed => new CauseFiling(
                filed.Filed,
                filed.Form,
                SecEdgarArchive.Address(SecEdgarArchive.Request(ArchiveDocument.FilingIndex, filed.Cik, filed.Accession, filed.Accession + "-index.htm")))),
        ];

        return
        [
            .. cells.Select(cell =>
            {
                if (!at.TryGetValue(cell.SessionDate, out var last) || last - cell.Sessions < 0)
                {
                    return cell;
                }

                var first = bars[last - cell.Sessions + 1].SessionDate;
                var from = bars[last - cell.Sessions].SessionDate;
                var window = new MoveWindow(first, cell.SessionDate, CloseOf(from, inputs.Zone), CloseOf(cell.SessionDate, inputs.Zone));

                return cell with { Taken = MoveCauses.For(window, cell.Cause, stories, prints, filings) };
            }),
        ];
    }

    // The instant a session's regular close was set, the closures table's and its early closes', and the end of the day
    // in the session's zone for a day the table does not place.
    static DateTimeOffset CloseOf(DateOnly session, TimeZoneInfo zone) =>
        RegularSession.On(session, zone)?.Close
            ?? new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(session.ToDateTime(TimeOnly.MaxValue), zone), TimeSpan.Zero);

    // The stories the labeller labelled, as a cause reads them, each with the newest label the read API handed over.
    static IReadOnlyList<CauseStory> Stories(IReadOnlyList<NewsArticleRow> articles) =>
    [
        .. articles
            .Where(article => string.Equals(article.Outcome, NewsLabelling.Labelled, StringComparison.Ordinal))
            .Select(article => new CauseStory(
                DateTimeOffset.Parse(article.PublishedAt, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal),
                article.Title,
                article.Source,
                article.Link,
                article.Direction ?? "neutral",
                article.Kind)),
    ];

    // The stored plan read back as the ladder's own records: its tranches, its exits and its invalidation.
    static (IReadOnlyList<Tranche> Tranches, IReadOnlyList<Exit> Exits, decimal? Invalidation) Plan(LadderRow? ladder) =>
        ladder is null ? ([], [], null) : StoredPlan.Read(ladder.Plan);

    // What the plan does at a band, in the words its own rows use: the tranche whose zone lies inside it, the share a
    // traded exit at its edges sells or that the trail begins there, or that an exit there is not traded; none where the
    // plan neither buys nor sells at it.
    // see: Each traded exit sells an equal fraction of what is held, and the top of the ladder trails
    static string? PlanUse(LevelRow level, IReadOnlyList<Tranche> tranches, IReadOnlyList<Exit> exits)
    {
        for (var at = 0; at < tranches.Count; at++)
        {
            if (tranches[at].LowEdge >= level.LowEdge && tranches[at].HighEdge <= level.HighEdge)
            {
                return FormattableString.Invariant($"tranche {at + 1} buys here");
            }
        }

        return exits.FirstOrDefault(exit => exit.LowEdge == level.LowEdge && exit.HighEdge == level.HighEdge) switch
        {
            null => null,
            { Traded: false } => "too near the entry to sell at",
            { Trailing: true, Fraction: "1/1" } => "the trail begins here",
            { Trailing: true, Fraction: var last } => FormattableString.Invariant($"the last {last} trails from here"),
            { Fraction: var share } => FormattableString.Invariant($"sells {share} here"),
        };
    }

    // What the long view draws across its line: each tranche's zone where the plan buys, each traded exit's where it
    // sells or the trail begins, the invalidation, and the year's high and low with the sessions they were made on.
    static IReadOnlyList<LongViewLevel> LongViewLevels(IReadOnlyList<Tranche> tranches, IReadOnlyList<Exit> exits, decimal? invalidation, YearExtremes? year)
    {
        var levels = new List<LongViewLevel>();

        for (var at = 0; at < tranches.Count; at++)
        {
            levels.Add(new LongViewLevel(tranches[at].LowEdge, tranches[at].HighEdge, "support", FormattableString.Invariant($"Tranche {at + 1}, {Figures.Price(tranches[at].LowEdge)} to {Figures.Price(tranches[at].HighEdge)}"), FormattableString.Invariant($"Tranche {at + 1}")));
        }

        foreach (var exit in exits.Where(exit => exit.Traded))
        {
            var named = exit.Trailing ? "Trail begins" : "Exit";

            levels.Add(new LongViewLevel(exit.LowEdge, exit.HighEdge, "resistance", FormattableString.Invariant($"{named}, {Figures.Price(exit.LowEdge)} to {Figures.Price(exit.HighEdge)}"), named));
        }

        if (invalidation is { } floor)
        {
            levels.Add(new LongViewLevel(floor, floor, "rule", FormattableString.Invariant($"The invalidation, {Figures.Price(floor)}"), "Invalidation"));
        }

        if (year is { } extremes)
        {
            levels.Add(new LongViewLevel(extremes.High, extremes.High, "rule", FormattableString.Invariant($"The year's high, {Figures.Price(extremes.High)} on {extremes.HighOn:yyyy-MM-dd}"), "Year's high"));
            levels.Add(new LongViewLevel(extremes.Low, extremes.Low, "rule", FormattableString.Invariant($"The year's low, {Figures.Price(extremes.Low)} on {extremes.LowOn:yyyy-MM-dd}"), "Year's low"));
        }

        return levels;
    }

    // The chart's marks: each print on the session its reaction is read on, and each ex-dividend date, a kept dividend's
    // and before the first kept one the session the adjusted close's step shows, over the sessions the chart draws.
    // see: The chart marks each report and each ex-dividend date over its sessions, the ex-dates a kept dividend's and before the first kept one the steps the dividends leave in the bars
    internal static IReadOnlyList<ChartEvent> Events(IReadOnlyList<BarRow> bars, IReadOnlyList<ReactionRow> reactions, IReadOnlyList<KeptDividend> dividends, IReadOnlyList<RawCloseRow> rawCloses)
    {
        if (bars.Count == 0)
        {
            return [];
        }

        var (first, last) = (bars[0].SessionDate, bars[^1].SessionDate);
        var events = new List<ChartEvent>();

        foreach (var reaction in reactions.Where(reaction => reaction.Session >= first && reaction.Session <= last))
        {
            var when = reaction.Timing switch
            {
                "before" => "before the open",
                "after" => "after the close",
                _ => "its timing not filed",
            };

            events.Add(new ChartEvent(reaction.Session, ChartEvent.Report, FormattableString.Invariant($"Results reported {reaction.ReportDate:yyyy-MM-dd}, {when}: this session moved {reaction.MovePct.ToString("+0.0;-0.0;0.0", CultureInfo.InvariantCulture)}%")));
        }

        KeptDividend[] kept = [.. dividends.Where(dividend => dividend.ExDate >= first && dividend.ExDate <= last)];
        var firstKept = dividends.Count > 0 ? dividends.Min(dividend => dividend.ExDate) : DateOnly.MaxValue;

        foreach (var dividend in kept)
        {
            events.Add(new ChartEvent(dividend.ExDate, ChartEvent.ExDividend, FormattableString.Invariant($"Ex-dividend {dividend.ExDate:yyyy-MM-dd}, {Figures.Price(dividend.Amount)} a share")));
        }

        foreach (var step in CardHitsReading.Steps([.. rawCloses.Select(row => (row.Session, row.Close, row.RawClose))]).Where(step => step >= first && step <= last && step < firstKept))
        {
            events.Add(new ChartEvent(step, ChartEvent.ExDividend, FormattableString.Invariant($"Ex-dividend {step:yyyy-MM-dd}, read from the step its dividend leaves in the adjusted prices")));
        }

        return events;
    }

    // The trail in the words of its rule, and the share the top exit holds on it where the plan trades one.
    // see: The trailing stop is the higher of the band beneath and the last swing low, and a stop inside a support band moves to that band's low edge
    static string? Trail(IReadOnlyList<Exit> exits)
    {
        var top = exits.FirstOrDefault(exit => exit.Traded && exit.Trailing);

        return "In an uptrend each tranche's stop is raised to the higher of the band beneath it and the last swing low beneath it, and a stop that would sit inside a support band moves to that band's low edge"
            + (top is null
                ? "."
                : FormattableString.Invariant($"; from {Figures.Price(top.LowEdge)}, the top exit, {(top.Fraction == "1/1" ? "the whole position" : "the last " + top.Fraction)} is held on a stop trailed under the price rather than sold at a price."));
    }
}
