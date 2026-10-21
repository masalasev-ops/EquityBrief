using System.Globalization;
using System.Text.Json;
using EquityBrief.Core.Chart;
using EquityBrief.Core.Indicators;
using EquityBrief.Core.Ledger;
using EquityBrief.Core.Prices;
using EquityBrief.Web.App;
using EquityBrief.Web.Marks;

namespace EquityBrief.Api.Reading;

// What the chart page reads, each as the read API handed it: the stored bars and indicator rows, the chart's averages
// before the store's year, the pulled sessions a weekly or monthly chart reads, the night's bands and cards, the chart's
// marks, the stock's membership row and index, its filings and facts, the name page's report figures and year's
// extremes, its long view, its peers, its next dated event, and the day its newest report section was written.
public sealed record ChartInputs(
    string Ticker,
    ChartInterval Interval,
    IReadOnlyList<BarRow> Bars,
    IReadOnlyList<IndicatorRow> Indicators,
    IReadOnlyList<ChartAverageRow> Warmed,
    PulledSeries? Pulled,
    IReadOnlyList<LevelRow> Levels,
    IReadOnlyList<DecisionCardRow> Cards,
    IReadOnlyList<ChartEvent> Events,
    UniverseRow? Member,
    string? IndexName,
    IReadOnlyList<FilingRow> Fundamentals,
    IReadOnlyList<FiledFactRow> FiledFacts,
    ReportView Report,
    YearExtremes? Year,
    LongViewRow? LongView,
    IReadOnlyList<StoredPick> Peers,
    CalendarRow? NextEvent,
    DateOnly? ReportWritten,
    (DateOnly Quarter, decimal Shares)? ProviderShares = null);

// The chart page's figures, worked when the page is read from what the store holds: the candles at the interval shown,
// rolled up in the core, each with the averages the name page draws at its last session; and the grid, every figure
// the one the name page's function works for it, so the two pages never disagree.
// see: Every link that opens a stock opens its chart page first, and the chart page links to its report
// see: A screen reads and renders, and each figure it works out at a price or from stored figures has one function in the core
public static class ChartScreen
{
    public static ChartPageView View(ChartInputs inputs)
    {
        var stored = inputs.Bars
            .Select(bar => new SessionBar(bar.SessionDate, bar.Open, bar.High, bar.Low, bar.Close, bar.Volume))
            .ToArray();

        // The daily chart reads the store alone; the weekly and monthly charts read the pulled sessions before the
        // store's first as well, at the store's scale, where the read API handed them.
        var series = inputs.Interval != ChartInterval.Daily && inputs.Pulled is { Pull: not null, PulledClose: { } pulledClose } pulled && stored.Length > 0
            ? ChartSeries.Joined(stored, pulled.Sessions, ChartSeries.Ratio(stored[0].Close, pulledClose))
            : stored;

        var candles = ChartSeries.Candles(series, inputs.Interval);
        var averages = AveragesBySession(inputs.Indicators, inputs.Warmed);

        double? At(string name, DateOnly session) =>
            averages.TryGetValue(name, out var values) && values.TryGetValue(session, out var value) ? value : null;

        var drawn = candles
            .Select(candle => new ChartCandle(
                candle.First,
                candle.Last,
                candle.Open,
                candle.High,
                candle.Low,
                candle.Close,
                candle.Volume,
                At(IndicatorSeries.Sma20, candle.Last),
                At(IndicatorSeries.Sma50, candle.Last),
                At(IndicatorSeries.Sma200, candle.Last)))
            .ToArray();

        var plans = inputs.Cards
            .Where(card => card.Entry is not null)
            .OrderBy(card => EquityBrief.Core.Families.SetupFamilies.OrderOf(card.Family))
            .Select(card => new ChartPlan(FamilyLabel(card.Family), card.Entry!.Value, card.Stop, card.Target))
            .ToArray();

        return new ChartPageView(
            inputs.Ticker,
            ChartSeries.Word(inputs.Interval),
            drawn,
            [.. inputs.Levels.Select(level => new ChartBand(level.LowEdge, level.HighEdge, level.Role, level.Immediate, level.Strength))],
            plans,
            inputs.Events,
            [.. ChartSeries.Holes(candles, inputs.Interval).Select(hole => new ChartGap(hole.After, hole.Before))],
            BeforeLine(inputs, stored),
            Grid(inputs, stored),
            inputs.ReportWritten);
    }

    // The averages the name page draws at each session: the indicator rows, and where they are empty the chart's
    // averages the night read through the sessions before the store's year.
    // see: The chart's averages are read over the sessions before the store's year from the pulled history at the store's scale, by a step whose figures only the name and chart pages draw
    static Dictionary<string, Dictionary<DateOnly, double?>> AveragesBySession(IReadOnlyList<IndicatorRow> indicators, IReadOnlyList<ChartAverageRow> warmed)
    {
        var bySession = new Dictionary<string, Dictionary<DateOnly, double?>>(StringComparer.Ordinal);

        foreach (var average in warmed)
        {
            var values = new Dictionary<DateOnly, double?>();

            foreach (var (session, value) in average.Values)
            {
                values[session] = value;
            }

            bySession[average.Name] = values;
        }

        foreach (var row in indicators.Where(row => ChartAverages.Contains(row.Name, StringComparer.Ordinal)))
        {
            if (!bySession.TryGetValue(row.Name, out var values))
            {
                bySession[row.Name] = values = [];
            }

            if (row.Value is not null || !values.ContainsKey(row.SessionDate))
            {
                values[row.SessionDate] = row.Value ?? values.GetValueOrDefault(row.SessionDate);
            }
        }

        return bySession;
    }

    static readonly string[] ChartAverages = [IndicatorSeries.Sma20, IndicatorSeries.Sma50, IndicatorSeries.Sma200];

    static string FamilyLabel(string family) =>
        EquityBrief.Core.Families.SetupFamilies.Named(family)?.Label
        ?? (string.Equals(family, EquityBrief.Core.Families.SetupFamilies.SectorHeavyweights.Name, StringComparison.Ordinal)
            ? EquityBrief.Core.Families.SetupFamilies.SectorHeavyweights.Label
            : family);

    // What the weekly and monthly charts read before the store's year, or why they start at its first session.
    static string? BeforeLine(ChartInputs inputs, IReadOnlyList<SessionBar> stored)
    {
        if (inputs.Interval == ChartInterval.Daily || stored.Count == 0)
        {
            return null;
        }

        var first = stored[0].Session;

        return inputs.Pulled is { Pull: { } pull, PulledClose: { } pulledClose, Sessions.Count: > 0 } pulled
            ? FormattableString.Invariant($"The candles before {first:yyyy-MM-dd} are read from the history pulled before the stored year, the pull {pull}, from {pulled.Sessions[0].Session:yyyy-MM-dd}, brought to the stored prices by the two closes of {first:yyyy-MM-dd}, a ratio of {ChartSeries.Ratio(stored[0].Close, pulledClose).ToString("0.0000", CultureInfo.InvariantCulture)}.")
            : FormattableString.Invariant($"The chart starts at {first:yyyy-MM-dd}: {inputs.Pulled?.Reason ?? ReadApi.NothingPulled}.");
    }

    // The grid's groups, in the order the page draws them.
    static IReadOnlyList<GridGroup> Grid(ChartInputs inputs, IReadOnlyList<SessionBar> stored)
    {
        decimal? close = stored.Count > 0 ? stored[^1].Close : null;
        var latest = inputs.Indicators
            .GroupBy(row => row.Name, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.OrderBy(row => row.SessionDate).Last().Value, StringComparer.Ordinal);
        var newest = inputs.Fundamentals.Count > 0 ? JsonDocument.Parse(inputs.Fundamentals[0].Payload) : null;

        try
        {
            return
            [
                Company(inputs, newest),
                Valuation(inputs, newest),
                Earnings(inputs),
                Dividend(inputs),
                Technical(inputs, close, latest, stored),
                Performance(inputs, stored),
                Insiders(inputs),
            ];
        }
        finally
        {
            newest?.Dispose();
        }
    }

    static GridFigure Missing(string label) => new(label, SinglePageApp.NotStored);

    static string? Text(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value) && value.ValueKind is JsonValueKind.String or JsonValueKind.Number
            ? value.ToString()
            : null;

    static GridGroup Company(ChartInputs inputs, JsonDocument? newest)
    {
        var member = inputs.Member;
        var capitalisation = newest is null ? null : Text(newest.RootElement, "marketCapitalisation");
        var count = inputs.FiledFacts
            .Where(fact => FiledFacts.SharesOutstanding.Contains(fact.Concept, StringComparer.Ordinal))
            .OrderBy(fact => fact.End)
            .ThenBy(fact => fact.Filed)
            .LastOrDefault();

        return new GridGroup("company", "The company",
        [
            inputs.IndexName is { Length: > 0 } index ? new GridFigure("Index", index) : Missing("Index"),
            member?.Sector is { Length: > 0 } sector ? new GridFigure("GICS sector", sector, Href: "#/universe?sector=" + Uri.EscapeDataString(sector)) : Missing("GICS sector"),
            member?.Industry is { Length: > 0 } industry ? new GridFigure("Industry", industry) : Missing("Industry"),
            capitalisation is { } stated && decimal.TryParse(stated, NumberStyles.Float, CultureInfo.InvariantCulture, out var value)
                ? new GridFigure("Market value, the provider's", Figures.Money(value), stated)
                : Missing("Market value, the provider's"),
            count is { } shares
                ? new GridFigure("Shares outstanding", FormattableString.Invariant($"{Figures.Shares(shares.Value)} as of {shares.End:yyyy-MM-dd}"), shares.Value.ToString(CultureInfo.InvariantCulture))
                : inputs.ProviderShares is { } provided
                    ? new GridFigure("Shares outstanding", FormattableString.Invariant($"{Figures.Shares(provided.Shares)} at {provided.Quarter:yyyy-MM-dd}, the provider's balance sheet, the filer filing none"), provided.Shares.ToString(CultureInfo.InvariantCulture))
                    : Missing("Shares outstanding"),
            inputs.Peers.Count > 0
                ? new GridFigure("Peers", string.Join(", ", inputs.Peers.Select(peer => peer.Ticker)), Links: [.. inputs.Peers.Select(peer => (peer.Ticker, SinglePageApp.ChartRoute + Uri.EscapeDataString(peer.Ticker)))])
                : Missing("Peers"),
        ]);
    }

    static GridGroup Valuation(ChartInputs inputs, JsonDocument? newest)
    {
        var valuation = inputs.Report.Valuation;
        var multiples = newest is not null && newest.RootElement.TryGetProperty("valuation", out var stated) && stated.ValueKind == JsonValueKind.Object ? stated : default;
        var quarter = valuation?.Quarters.MaxBy(multiple => multiple.Quarter);

        static GridFigure Multiple(string label, string? stored) =>
            stored is { } text && decimal.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value)
                ? new GridFigure(label, Figures.Multiple(value), text)
                : Missing(label);

        return new GridGroup("valuation", "Valuation",
        [
            valuation?.Multiple is { } night
                ? new GridFigure("Price to its four quarters' earnings, the night's", night.ToString("0.0", CultureInfo.InvariantCulture)
                    + (valuation.Low is { } low && valuation.High is { } high ? FormattableString.Invariant($", against {low:0.0} to {high:0.0} over its own quarters") : string.Empty),
                    night.ToString(CultureInfo.InvariantCulture))
                : Missing("Price to its four quarters' earnings, the night's"),
            quarter is { } newestQuarter
                ? new GridFigure("Its newest quarter's multiple", FormattableString.Invariant($"{Figures.Multiple(newestQuarter.Multiple)} at {newestQuarter.Quarter:yyyy-MM-dd}"), newestQuarter.Multiple.ToString(CultureInfo.InvariantCulture))
                : Missing("Its newest quarter's multiple"),
            Multiple("Price to earnings, the provider's trailing", Text(multiples, "trailingPe")),
            Multiple("Price to earnings, the provider's forward", Text(multiples, "forwardPe")),
        ]);
    }

    static GridGroup Earnings(ChartInputs inputs)
    {
        var quarter = inputs.Report.Quarter;
        var perShare = quarter?.Lines.FirstOrDefault(line => line.PerShare);
        var revenue = quarter?.Lines.FirstOrDefault(line => !line.PerShare);

        static GridFigure AgainstItsEstimate(string label, EquityBrief.Core.Report.QuarterLine? line, Func<decimal, string> shown) =>
            line?.Reported is { } reported
                ? new GridFigure(
                    label,
                    shown(reported) + (line.Estimate is { } estimate ? FormattableString.Invariant($" against an estimate of {shown(estimate)}") : ", no estimate filed"),
                    reported.ToString(CultureInfo.InvariantCulture))
                : Missing(label);

        return new GridGroup("earnings", "Earnings",
        [
            quarter is { } latest
                ? new GridFigure("Latest quarter", FormattableString.Invariant($"ended {latest.Quarter:yyyy-MM-dd}") + (latest.Reported is { } reported ? FormattableString.Invariant($", reported {reported:yyyy-MM-dd}") : string.Empty))
                : Missing("Latest quarter"),
            AgainstItsEstimate("Earnings a share", perShare, Figures.PerShare),
            perShare?.AgainstEstimate is { } surprise
                ? new GridFigure("The surprise", SurpriseWords(surprise), surprise.ToString(CultureInfo.InvariantCulture), MarkRenderer.Hue(surprise))
                : Missing("The surprise"),
            AgainstItsEstimate(revenue?.Line ?? "Revenue", revenue, value => Figures.Money(value, inputs.Report.Currency)),
            inputs.NextEvent is { } next
                ? new GridFigure("Next report", FormattableString.Invariant($"{next.EventDate:yyyy-MM-dd}"), FormattableString.Invariant($"{next.EventDate:yyyy-MM-dd}"))
                : Missing("Next report"),
        ]);
    }

    static string SurpriseWords(double surprise) =>
        (Math.Round(surprise, 1) == 0 ? 0 : surprise).ToString("+0.0;-0.0;0.0", CultureInfo.InvariantCulture) + "% against the estimate";

    static GridGroup Dividend(ChartInputs inputs)
    {
        var dividend = inputs.Report.Dividend;
        var year = dividend?.Years.LastOrDefault(paid => paid.Whole) ?? (inputs.Report.PaysNone ? inputs.Report.PaidLastYear : null);

        // A company paying none says so in each figure, the year it last paid in still drawn where it is the last whole one,
        // and not stored is left for one storing neither a rate nor a dividend.
        // see: A company filing a dividend rate of nought with no dividend kept in the year is drawn as paying none, and one storing neither as not stored
        GridFigure Unpaid(string label) => inputs.Report.PaysNone ? new GridFigure(label, SinglePageApp.PaysNone) : Missing(label);

        return new GridGroup("dividend", "Dividend",
        [
            dividend?.Yield is { } yield
                ? new GridFigure("Yield", FormattableString.Invariant($"{yield:0.00}% on {(dividend.RateIsForward ? "the forward rate" : "the last year's payments")} of {Figures.PerShare(dividend.Rate)}"), yield.ToString(CultureInfo.InvariantCulture))
                : Unpaid("Yield"),
            year is { } whole
                ? new GridFigure("Paid in its last whole year", FormattableString.Invariant($"{Figures.PerShare(whole.Total)} in {whole.Year}, {whole.Payments} {(whole.Payments == 1 ? "payment" : "payments")}"), whole.Total.ToString(CultureInfo.InvariantCulture))
                : Unpaid("Paid in its last whole year"),
            dividend?.PayoutOnEarnings is { } payout
                ? new GridFigure("Paid out of earnings", FormattableString.Invariant($"{payout:0.0}%"), payout.ToString(CultureInfo.InvariantCulture))
                : Unpaid("Paid out of earnings"),
            dividend?.RaisedInARow is { } raised
                ? new GridFigure("Raised in a row", FormattableString.Invariant($"{raised} {(raised == 1 ? "year" : "years")}"), raised.ToString(CultureInfo.InvariantCulture))
                : Unpaid("Raised in a row"),
        ]);
    }

    static GridGroup Technical(ChartInputs inputs, decimal? close, IReadOnlyDictionary<string, double?> latest, IReadOnlyList<SessionBar> stored)
    {
        var high = close is { } price && inputs.Year is { } extremes
            ? EquityBrief.Core.Tiles.NameTiles.High(price, extremes.High, extremes.HighOn, extremes.Low, extremes.LowOn)
            : null;

        GridFigure Away(string label, string average) =>
            close is { } price && latest.TryGetValue(average, out var value) && value is { } level && level > 0
                ? Signed(label, (Statistic.FromPrice(price) / level) - 1)
                : Missing(label);

        GridFigure Signed(string label, double fraction) =>
            new(label, (Math.Round(fraction * 100, 2) == 0 ? 0 : fraction * 100).ToString("+0.00;-0.00;0.00", CultureInfo.InvariantCulture) + "%", fraction.ToString(CultureInfo.InvariantCulture), MarkRenderer.Hue(fraction * 100));

        double? volume = stored.Count > 0 && latest.TryGetValue(IndicatorSeries.VolAvg50, out var mean) && mean is > 0 ? Statistic.FromVolume(stored[^1].Volume) / mean.Value : null;

        return new GridGroup("technical", "Technical",
        [
            high is { } tile
                ? new GridFigure("Year's high", FormattableString.Invariant($"{Figures.Price(tile.High)} on {tile.HighOn:yyyy-MM-dd}"), tile.High.ToString(CultureInfo.InvariantCulture))
                : Missing("Year's high"),
            high is { } under
                ? new GridFigure("From the year's high", (Math.Round(under.FromHigh, 1) == 0 ? 0 : under.FromHigh).ToString("+0.0;-0.0;0.0", CultureInfo.InvariantCulture) + "%", under.FromHigh.ToString(CultureInfo.InvariantCulture), MarkRenderer.Hue(under.FromHigh))
                : Missing("From the year's high"),
            high is { } low
                ? new GridFigure("Year's low", FormattableString.Invariant($"{Figures.Price(low.Low)} on {low.LowOn:yyyy-MM-dd}"), low.Low.ToString(CultureInfo.InvariantCulture))
                : Missing("Year's low"),
            latest.TryGetValue(IndicatorSeries.Atr14, out var move) && move is { } typical
                ? new GridFigure("Typical daily move", typical.ToString("#,##0.00", CultureInfo.InvariantCulture), typical.ToString(CultureInfo.InvariantCulture))
                : Missing("Typical daily move"),
            latest.TryGetValue(IndicatorSeries.Rsi14, out var strength) && strength is { } rsi
                ? new GridFigure("Relative strength", rsi.ToString("0.0", CultureInfo.InvariantCulture), rsi.ToString(CultureInfo.InvariantCulture))
                : Missing("Relative strength"),
            Away("From its 20-session average", IndicatorSeries.Sma20),
            Away("From its 50-session average", IndicatorSeries.Sma50),
            Away("From its 200-session average", IndicatorSeries.Sma200),
            volume is { } ratio
                ? new GridFigure("Volume against its 50-session mean", ratio.ToString("0.00", CultureInfo.InvariantCulture) + " times", ratio.ToString(CultureInfo.InvariantCulture))
                : Missing("Volume against its 50-session mean"),
        ]);
    }

    static GridGroup Performance(ChartInputs inputs, IReadOnlyList<SessionBar> stored)
    {
        var monthEnds = inputs.LongView?.Closes.Select(end => (end.Session, end.Close)).ToArray() ?? [];

        return new GridGroup("performance", "Performance",
        [
            .. ChartSeries.Performance(stored, monthEnds).Select(span => span.Return is { } fraction && span.From is { } from
                ? new GridFigure(Capitalised(span.Span), (Math.Round(fraction * 100, 2) == 0 ? 0 : fraction * 100).ToString("+0.00;-0.00;0.00", CultureInfo.InvariantCulture) + FormattableString.Invariant($"% since {from:yyyy-MM-dd}"), fraction.ToString(CultureInfo.InvariantCulture), MarkRenderer.Hue(fraction * 100))
                : Missing(Capitalised(span.Span))),
        ]);
    }

    // From 19.13 the insiders' open-market buying as the report's region works it: the net over 90 days and over the year,
    // the insiders who bought, and the newest purchase's day; each not stored where the store holds no insider history
    // reaching the night.
    // see: Insiders' trades are read from their Form 4 filings, and only open-market purchases and sales are counted
    static GridGroup Insiders(ChartInputs inputs)
    {
        var view = inputs.Report.Insiders;

        GridFigure Net(string label, EquityBrief.Core.Insiders.InsiderTotals? totals) =>
            totals is { } held
                ? new GridFigure(label, (held.Net > 0 ? "+" : string.Empty) + Figures.Money(held.Net), held.Net.ToString(CultureInfo.InvariantCulture), MarkRenderer.Hue(Statistic.FromPrice(held.Net), 0))
                : Missing(label);

        return new GridGroup("insiders", "Insiders",
        [
            Net("Net bought in the open market over 90 days", view?.Ninety),
            Net("Net bought in the open market over the year", view?.Year),
            view is { } buyers
                ? new GridFigure("Insiders who bought", FormattableString.Invariant($"{buyers.Year.Buyers} in the year, {buyers.Ninety.Buyers} in 90 days"), buyers.Year.Buyers.ToString(CultureInfo.InvariantCulture))
                : Missing("Insiders who bought"),
            view is { } newest
                ? new GridFigure("Newest purchase", newest.NewestPurchase is { } day ? day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) : "none in the year", newest.NewestPurchase?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture))
                : Missing("Newest purchase"),
        ]);
    }

    static string Capitalised(string words) => char.ToUpperInvariant(words[0]) + words[1..];
}
