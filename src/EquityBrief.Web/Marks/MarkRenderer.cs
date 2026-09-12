using System.Globalization;
using System.Text;
using EquityBrief.Core.Components;

namespace EquityBrief.Web.Marks;

// One session, as a mark is given it. The renderer's own shape rather than the
// read API's, because the mark renderer is in the project the API references
// and not the other way round.
public sealed record ChartBar(
    DateOnly SessionDate,
    decimal Open,
    decimal High,
    decimal Low,
    decimal Close,
    long Volume);

// One moving average over the same sessions as the bars, as a mark is given it.
//
// Values are nullable and in session order, one per bar, so the line breaks
// where the average has no value rather than joining across the absence. A
// 200-day average has no value for the first 199 sessions of a stored year, and
// a line drawn straight from the first value it does have would claim the
// average was flat over sessions it did not exist for.
//
// Double rather than decimal, because an average of prices is a statistic and
// the two worlds do not mix. It arrives already across that boundary.
public sealed record ChartAverage(string Name, IReadOnlyList<double?> Values);

// One band of the volume profile, as a mark is given it.
//
// Edges decimal because they are prices, shares long, and the share of the
// period double because it is a fraction of a count. The mark draws the count
// and states the share, which is the pair the report reads out loud.
public sealed record ProfileBand(decimal Low, decimal High, long Shares, double ShareOfPeriod);

// One level band, as a mark is given it.
//
// The role is a word rather than a flag, because it is drawn as a word as well
// as a hue: hue is never the only channel that carries a meaning, and a reader
// who cannot separate green from orange still reads the band.
public sealed record ChartBand(decimal LowEdge, decimal HighEdge, string Role, bool Immediate, int Strength);

// One momentum reading, as a mark is given it.
//
// Neutral is the value the reading means nothing without. An RSI of 53 is a
// number; an RSI of 53 against a rule at 50 is a statement. Floor and Ceiling
// bound the axis where the reading has a fixed range and are absent where it
// does not: an RSI runs 0 to 100 whatever the stock does, and a MACD is in the
// stock's own money and has no bounds but its own.
public sealed record MomentumReading(
    string Name,
    IReadOnlyList<double?> Values,
    double Neutral,
    double? Floor,
    double? Ceiling);

// One row of the level summary table, as the surface is given it.
//
// Members arrive parsed, because the table's whole content is each band's
// members and their dates and a string would have to be read to draw them.
public sealed record SummaryMember(string Kind, decimal Price, DateOnly Date);

public sealed record SummaryBand(
    decimal LowEdge,
    decimal HighEdge,
    string Role,
    bool Immediate,
    int Strength,
    bool HasNonAverageAnchor,
    IReadOnlyList<SummaryMember> Members);

// One row of the plan column: a price, what happens there, and how it reads.
//
// `Kind` is what the reader is being told at that price, and the mark draws each
// kind differently: a purchase below the marker, a sale above it, a stop as a
// horizontal rule and the invalidation as the lowest rule of all. `Detail` is
// what the row says in words, because hue is never the only channel.
public sealed record PlanRow(
    decimal LowEdge,
    decimal HighEdge,
    string Kind,
    string Detail,
    bool Traded);

// The kinds a plan row takes, named once so the mark and the tables agree.
public static class PlanKind
{
    public const string Tranche = "tranche";
    public const string Exit = "exit";
    public const string Stop = "stop";
    public const string Invalidation = "invalidation";
}

// An average that anchors no band, with the count that explains it.
//
// Section 18's row says a name with fewer than two hundred bars records its long
// average as not available with the bar count, and that what a reader sees is
// "not available, nn bars". An average with no value cannot be a band member, so
// without this the table would simply not mention it, and an absence with no
// statement beside it is the failure that row describes.
public sealed record AbsentAverage(string Name, int BarCount);

// One row of the universe screen, already projected.
//
// `Nearest` is the smaller of the two distances and is what the screen orders
// on. Every nullable field is a name the night computed nothing for, and each is
// drawn as an absence rather than as a zero.
public sealed record UniverseCell(
    string Ticker,
    string Sector,
    decimal? Close,
    string? TrendState,
    decimal? NearestSupport,
    decimal? NearestResistance,
    double? ToSupport,
    double? ToResistance,
    double? Nearest,
    // The listing halves, which arrived at 5.4 with the store that feeds them.
    // `LastListed` is null for a name that has never been on the list, which is
    // an absence rather than a date nobody has.
    DateOnly? LastListed = null,
    IReadOnlyList<bool>? Evenings = null,
    // The sessions-until-earnings half, which arrived at 5.8 with the rest of
    // the parts section 15 states and the pages did not draw. Null for a name
    // with no dated event ahead of it, and for one whose event is past the end
    // of the exchange closure table, which are two absences and are stated as
    // two: a count nobody can take is not the same as no event to count to.
    // owes: The exchange closure table extended before the nights reach its end
    int? SessionsUntilEarnings = null,
    DateOnly? NextEvent = null,
    bool EventBeyondTheTable = false);

// One of a name's biggest moves, as the table is given it. No cause: it is a
// researched claim and arrives with the pass that writes it.
public sealed record MoveCell(DateOnly SessionDate, int Sessions, double ChangePct, int Rank);

// One row of tonight's list, already projected.
//
// `Fired` carries the same reasons as `Reasons` with the values that made each
// true, which is 15.7's reasons-per-row half and arrived at 5.6 with the record
// that sits beside them. It is optional because a caller that only needs the
// names of what fired, the watch list among them, should not have to carry the
// values to say so.
public sealed record ListingCell(
    string Ticker,
    DateOnly SessionDate,
    int FiredCount,
    int Strength,
    decimal? Close,
    IReadOnlyList<string> Reasons,
    IReadOnlyList<FiredReason>? Fired = null,
    // The three section 15.7 states beside the name, the close and the reasons,
    // drawn from 5.8. Each is absent rather than zero for a name the night
    // computed nothing for. `Distance` carries the mark's own input rather than
    // a copy of its three numbers, because the mark takes a universe cell and a
    // second shape holding the same values is a second place they can disagree.
    double? DayChangePct = null,
    string? TrendState = null,
    UniverseCell? Distance = null);

// One reason that fired for a name, with the values that made it true.
public sealed record FiredReason(string Name, IReadOnlyDictionary<string, string> Values);

// One reason and how many of tonight's names it fired on.
public sealed record ReasonTotal(string Reason, int Names);

// One reason's record, as the run page draws it.
//
// Counts and no rate. The share that reached target before stop and the
// break-even those setups demanded are the other half of 15.10's row and arrive
// at 7.5 with the verdicts; what this carries is how much has been scored and
// how far that is from the minimum a verdict needs.
// see: An unresolved setup is never a win
public sealed record ReasonRecord(
    string Reason,
    int Fired,
    int Won,
    int Lost,
    int Unresolved,
    int Minimum)
{
    // A setup that has done nothing is neither right nor wrong, so it is in
    // neither half of this.
    public int Resolved => Won + Lost;

    public bool HasEarnedAVerdict => Resolved >= Minimum;
}

// One row of the reason track: section 15.5's three states out of one
// denominator.
//
// `ResolvedUnsplit` is the resolved setups of a reason that has not earned a
// verdict, drawn as one segment rather than as a win segment beside a loss one.
// 15.11 gates the record column on the minimum, and a split drawn below it is
// the same figure through a second channel.
public sealed record ReasonTrackRow(
    string Reason,
    int Won,
    int Lost,
    int Unresolved,
    int ResolvedUnsplit = 0)
{
    public int Total => Won + Lost + Unresolved + ResolvedUnsplit;
}

// One window's universe base rate, as the run page pins it.
//
// `Rate` is null before the first fill has anything matured to count, which is a
// window nothing has measured rather than a rate of zero.
public sealed record BaseRateLine(string Window, double? Rate);

// The four verdict counts of the last phase report.
//
// Four fields and no total. Out of scope is counted apart from unexamined and
// only one of them is a defect, so a record that summed them would make the run
// page report a build that has not reached a claim as one that failed to check
// it.
public sealed record HarnessCounts(int Passed, int Failed, int Unexamined, int OutOfScope);

// One stage of a night, as the operational header draws it.
// `StartedAt` is the instant, not the duration, and the two are different
// questions. The duration answers how long the night took; the instant answers
// what time of day a stage ran, which is the only thing that can bound the hour
// the provider posts the day's bulk file. The row carried the duration alone
// until the phase 5 sign-off, so the surface two operating obligations name
// could answer one of them and not the other.
// owes: The provider's posting hour for the day's bulk file, measured from live fetches
public sealed record StageRow(
    string Stage,
    DateTimeOffset StartedAt,
    double Seconds,
    int RowsWritten,
    int ModelCalls,
    int NetworkRequests,
    string Spend,
    string Outcome,
    string Detail);

// One line of the sector strip.
public sealed record SectorLine(string Sector, int Names, int InUptrend, int OnTheList);

// The price scale a chart drew, so another mark can draw against it.
//
// Section 15.5 says the volume profile is drawn against the same price axis as
// the chart beside it. That is a claim about two pictures agreeing, and the only
// way to make it hold by construction rather than by coincidence is to compute
// the scale once and hand it to both. A profile that took its own low and high
// from its own bands would be a picture whose rows line up with nothing, and it
// would look entirely reasonable on its own.
public sealed record PriceAxis(double Low, double High)
{
    // The span the scale is drawn over. A flat series has none and is drawn
    // through the middle rather than refused, which is the rule the chart
    // already applies to its candles.
    public double Range => High - Low > 0 ? High - Low : 1;
}

// The marks, as SVG strings written server side.
//
// This is the level chart mark with one of its four elements absent. Section
// 15.5 names four: candles, the level bands, the moving averages and a volume
// pane. Candles and the volume pane are drawn here at 1.3 and the moving
// averages at 3.1. The bands arrive at 3.4 with the level builder, drawn into
// this file rather than into a second one.
//
// That is the whole reason this is not a temporary chart. A temporary chart
// becomes the second renderer, and one renderer for both the app and the export
// is what makes the two carry the same pictures from the same numbers.
// see: Marks are defined once and every screen draws from that list
//
// It touches no store and computes no figure, which is what its blank
// matrix cells claim. Geometry is not a figure: nothing here is reported to a
// reader as a number, and every price drawn arrives already computed.
public sealed class MarkRenderer : IComponent
{
    // The empty declaration, which is a claim and not an omission. Section
    // 15.4 puts the marks on the server, and the seam between rendering and
    // reading only means something if a check asserts the renderer reaches no
    // store of its own.
    public static ComponentAccess Access => ComponentAccess.Nothing;

    // Below this a chart says what it has rather than drawing through nothing.
    // Two, because one session has no range to scale against and a chart of one
    // candle is a picture of nothing. Section 15.5's note is the rule: a mark
    // degrades by stating its bar count, never by drawing a sparse series as a
    // quiet one.
    public const int FewestBars = 2;

    const int Width = 960;
    const int ProfileWidth = 150;
    const int PriceHeight = 340;
    const int VolumeHeight = 90;
    const int Gap = 18;
    const int Margin = 8;

    static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;

    // The same culture as a function, for the places a fragment is built
    // rather than appended. StringBuilder takes the provider directly; a
    // string does not.
    static string Formatted(FormattableString text) => text.ToString(Invariant);

    // The one place a price becomes a plot coordinate.
    //
    // Prices are decimal and statistics are double, and nothing crosses between
    // them implicitly. A coordinate is neither: it is a position on a surface,
    // computed in double because that is what geometry is, and it never travels
    // back. This helper is named for the crossing so the boundary is visible at
    // every call rather than hidden in an expression, which is what the money
    // rule in CLAUDE.md asks of anything that crosses it.
    static double PlotValue(decimal price) => (double)price;

    static string Number(double value) => value.ToString("0.##", Invariant);

    // The price scale the chart draws, computed here so the profile beside it
    // can be given the same one.
    //
    // It takes in the averages as well as the candles, for the reason stated
    // below: a 200-day average sits well under the price after a year of rising,
    // and a scale drawn from the candles alone pushes it off the bottom of the
    // pane where it reads as absent rather than as low. Neither the profile nor
    // the level bands widen it. A profile band lies inside the window's own high
    // and low by construction, and every level candidate but the averages does
    // too: a swing, a touch, a retracement and a shelf are all prices from
    // inside the window, and the averages are already taken in here. A scale
    // that stretched to fit a mark would make the two pictures disagree about
    // where a price is, which is the whole thing this method exists to prevent.
    public PriceAxis AxisFor(IReadOnlyList<ChartBar> bars, IReadOnlyList<ChartAverage>? averages = null)
    {
        var high = bars.Max(bar => PlotValue(bar.High));
        var low = bars.Min(bar => PlotValue(bar.Low));

        var drawn = (averages ?? []).SelectMany(line => line.Values).Where(value => value is not null).ToArray();

        if (drawn.Length > 0)
        {
            high = Math.Max(high, drawn.Max()!.Value);
            low = Math.Min(low, drawn.Min()!.Value);
        }

        return new PriceAxis(low, high);
    }

    // Where a price sits in the price pane, given the axis. One definition, used
    // by the candles, the averages and the profile, because two mappings on one
    // scale is a picture that lies about where the price is.
    static double At(PriceAxis axis, double value) =>
        PriceHeight - Margin - ((value - axis.Low) / axis.Range * (PriceHeight - (2 * Margin)));

    // The plan column: one vertical price axis with the current price marked in
    // the middle of it.
    //
    // Everything above the marker is a sale and everything below is a purchase,
    // which is legible without reading a caption, and it is one column rather
    // than two facing sides because a reader should not have to learn a
    // convention before reading it.
    // see: The plan figure is one vertical price column with the current price marked in it
    //
    // It draws nothing the ladder does not carry. Every row handed in is a
    // stored value, and the only arithmetic here is the axis, which is where a
    // price sits on a scale rather than what the price is.
    // see: A screen reads and renders, and computes nothing
    public string PlanColumn(string ticker, decimal close, IReadOnlyList<PlanRow> rows)
    {
        if (rows.Count == 0)
        {
            return $"<p class=\"degraded\" data-ticker=\"{Escaped(ticker)}\" data-rows=\"0\">" +
                $"{Escaped(ticker)} has no plan to draw, which is what a name with no eligible " +
                $"band gets.</p>";
        }

        var prices = rows
            .SelectMany(row => new[] { PlotValue(row.LowEdge), PlotValue(row.HighEdge) })
            .Append(PlotValue(close))
            .ToArray();

        var axis = new PriceAxis(prices.Min(), prices.Max());

        var svg = new StringBuilder();

        svg.Append(Invariant, $"<svg class=\"plan-column\" viewBox=\"0 0 {Width} {PriceHeight}\" ");
        svg.Append(Invariant, $"role=\"img\" data-ticker=\"{Escaped(ticker)}\" data-rows=\"{rows.Count}\" ");
        svg.Append(Invariant, $"data-close=\"{close}\" data-axis-low=\"{axis.Low}\" data-axis-high=\"{axis.High}\">");

        svg.Append(Invariant, $"<title>{Escaped(ticker)} plan column</title>");
        svg.Append(Invariant, $"<desc>One vertical price axis. Everything above the price marker is a sale, everything below is a purchase, stops are horizontal rules and the invalidation is the lowest.</desc>");

        // The column itself, and the price marker on it.
        const double column = Width / 2d;

        svg.Append(Invariant, $"<line class=\"axis\" x1=\"{column}\" y1=\"{Margin}\" x2=\"{column}\" y2=\"{PriceHeight - Margin}\" stroke=\"var(--rule, #d8d8d8)\" stroke-width=\"1\"/>");

        foreach (var row in rows)
        {
            var top = At(axis, PlotValue(row.HighEdge));
            var bottom = At(axis, PlotValue(row.LowEdge));
            var height = Math.Max(bottom - top, 1);

            var hue = row.Kind switch
            {
                PlanKind.Tranche => SupportHue,
                PlanKind.Exit => ResistanceHue,
                _ => "var(--muted, #6a6a6a)",
            };

            svg.Append(Invariant, $"<g class=\"plan-row\" data-kind=\"{Escaped(row.Kind)}\" ");
            svg.Append(Invariant, $"data-low-edge=\"{row.LowEdge}\" data-high-edge=\"{row.HighEdge}\" ");
            svg.Append(Invariant, $"data-traded=\"{(row.Traded ? "true" : "false")}\">");

            // A stop and the invalidation are rules rather than zones, because
            // each is one price a close is measured against. A tranche and an
            // exit are the band they sit on, which has width.
            if (row.Kind is PlanKind.Stop or PlanKind.Invalidation)
            {
                svg.Append(Invariant, $"<line class=\"{row.Kind}-rule\" x1=\"{Margin}\" y1=\"{bottom}\" x2=\"{Width - Margin}\" y2=\"{bottom}\" ");
                svg.Append(Invariant, $"stroke=\"{hue}\" stroke-width=\"1\" stroke-dasharray=\"4 3\"/>");
            }
            else
            {
                var left = row.Kind == PlanKind.Tranche ? Margin : column;

                svg.Append(Invariant, $"<rect class=\"{row.Kind}-zone\" x=\"{left}\" y=\"{top}\" ");
                svg.Append(Invariant, $"width=\"{(Width / 2d) - Margin}\" height=\"{height}\" ");
                svg.Append(Invariant, $"fill=\"{hue}\" fill-opacity=\"{(row.Traded ? 0.30 : 0.12)}\"/>");
            }

            // The row in words, because hue is never the only channel and a
            // reader who cannot separate the two loses nothing.
            svg.Append(Invariant, $"<text class=\"plan-label\" x=\"{Width - Margin}\" y=\"{bottom - 2}\" text-anchor=\"end\" ");
            svg.Append(Invariant, $"font-family=\"Segoe UI, Arial, sans-serif\" font-size=\"11\" fill=\"var(--muted, #6a6a6a)\">{Escaped(row.Detail)}</text>");

            svg.Append("</g>");
        }

        // The price marker last, so it is drawn over the zones rather than under
        // them: it is the one thing the whole figure is read against.
        var at = At(axis, PlotValue(close));

        svg.Append(Invariant, $"<g class=\"price-marker\" data-close=\"{close}\">");
        svg.Append(Invariant, $"<line x1=\"{Margin}\" y1=\"{at}\" x2=\"{Width - Margin}\" y2=\"{at}\" stroke=\"var(--ink, #1c1c1c)\" stroke-width=\"1.4\"/>");
        svg.Append(Invariant, $"<text x=\"{Margin}\" y=\"{at - 3}\" font-family=\"Segoe UI, Arial, sans-serif\" font-size=\"11\" fill=\"var(--ink, #1c1c1c)\">{close}</text>");
        svg.Append("</g>");

        svg.Append("</svg>");

        return svg.ToString();
    }

    // The tranche table and the exit table, which are what the plan column's
    // figure is read beside. Every cell is a stored value.
    public string PlanTables(string ticker, IReadOnlyList<PlanRow> rows)
    {
        var tranches = rows.Where(row => row.Kind == PlanKind.Tranche).ToArray();
        var exits = rows.Where(row => row.Kind == PlanKind.Exit).ToArray();

        var html = new StringBuilder();

        html.Append(Invariant, $"<table class=\"tranche-table\" data-ticker=\"{Escaped(ticker)}\" data-rows=\"{tranches.Length}\">");
        html.Append("<tr><th>Zone</th><th>Condition and stop</th></tr>");

        foreach (var row in tranches)
        {
            html.Append(Invariant, $"<tr data-low-edge=\"{row.LowEdge}\"><td>{row.LowEdge} to {row.HighEdge}</td>");
            html.Append(Invariant, $"<td>{Escaped(row.Detail)}</td></tr>");
        }

        html.Append("</table>");

        html.Append(Invariant, $"<table class=\"exit-table\" data-ticker=\"{Escaped(ticker)}\" data-rows=\"{exits.Length}\">");
        html.Append("<tr><th>Zone</th><th>Action</th></tr>");

        foreach (var row in exits)
        {
            html.Append(Invariant, $"<tr data-low-edge=\"{row.LowEdge}\" data-traded=\"{(row.Traded ? "true" : "false")}\">");
            html.Append(Invariant, $"<td>{row.LowEdge} to {row.HighEdge}</td><td>{Escaped(row.Detail)}</td></tr>");
        }

        html.Append("</table>");

        return html.ToString();
    }

    // The volume profile, drawn horizontally against a price axis it is given.
    //
    // The width of a row is its share of the busiest band rather than of the
    // period, because a profile whose rows were scaled to the period would be
    // twenty short stubs on a name whose volume is evenly spread. The share of
    // the period is on the row as a number instead, which is the figure the
    // report quotes and the one the shelf threshold is read against.
    public string VolumeProfile(string ticker, IReadOnlyList<ProfileBand> bands, PriceAxis axis)
    {
        if (bands.Count == 0)
        {
            return $"<p class=\"degraded\" data-ticker=\"{Escaped(ticker)}\" data-bands=\"0\">" +
                $"{Escaped(ticker)} has no volume profile, which is what a name with fewer than " +
                $"sixty stored sessions gets.</p>";
        }

        foreach (var band in bands)
        {
            if (band.High <= band.Low)
            {
                throw new ArgumentException(
                    $"A profile band runs from {band.Low} to {band.High}, which is not a band. A row " +
                    "of no height would draw nothing and would take its share of the period with it.",
                    nameof(bands));
            }
        }

        var loudest = bands.Max(band => band.Shares);
        var busiest = loudest > 0 ? loudest : 1;

        var svg = new StringBuilder();

        svg.Append(Invariant, $"<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 {ProfileWidth} {PriceHeight}\" ");
        svg.Append(Invariant, $"role=\"img\" class=\"volume-profile\" data-ticker=\"{Escaped(ticker)}\" ");
        svg.Append(Invariant, $"data-bands=\"{bands.Count}\" data-axis-low=\"{Number(axis.Low)}\" data-axis-high=\"{Number(axis.High)}\">");
        svg.Append(Invariant, $"<title>{Escaped(ticker)}, shares traded in {bands.Count} price bands</title>");
        svg.Append("<desc>Shares traded in each price band, drawn against the price axis of the chart beside it.</desc>");

        foreach (var band in bands)
        {
            var top = At(axis, PlotValue(band.High));
            var bottom = At(axis, PlotValue(band.Low));
            var width = (double)band.Shares / busiest * (ProfileWidth - (2 * Margin));

            // A band whose whole span sits outside the axis draws nothing rather
            // than being clamped to an edge, where it would read as a band at a
            // price it is not at.
            var height = bottom - top;

            svg.Append(Invariant, $"<rect class=\"band\" data-band-low=\"{band.Low.ToString(Invariant)}\" ");
            svg.Append(Invariant, $"data-band-high=\"{band.High.ToString(Invariant)}\" data-shares=\"{band.Shares}\" ");
            svg.Append(Invariant, $"data-share-of-period=\"{band.ShareOfPeriod.ToString("0.#####", Invariant)}\" ");
            svg.Append(Invariant, $"x=\"{Margin}\" y=\"{Number(top)}\" width=\"{Number(Math.Max(0, width))}\" ");
            svg.Append(Invariant, $"height=\"{Number(Math.Max(0, height))}\" fill=\"var(--muted, #6a6a6a)\"/>");
        }

        svg.Append("</svg>");

        return svg.ToString();
    }

    // Support is green and resistance is orange, and this is the one place in
    // the whole system those two hues are used. Every other mark is neutral ink
    // or one hue in steps.
    // see: Support and resistance own two hues and nothing else uses them
    const string SupportHue = "var(--support, #2f7d4f)";
    const string ResistanceHue = "var(--resistance, #b5651d)";

    const int ReadingHeight = 64;
    const int ReadingGap = 10;

    // The momentum panel. One small axis per reading, each with its neutral rule
    // drawn across it.
    //
    // The rule is the point of the mark rather than decoration. Section 5 says
    // an RSI near 50 is balanced and above 70 is stretched, so a reading drawn
    // without its rule is a line whose height means nothing, and the panel would
    // be four squiggles a reader has to bring their own conventions to.
    //
    // Each reading is scaled on its own axis. A MACD is in the stock's money and
    // an RSI is a score out of a hundred, so one shared scale would flatten
    // whichever of them has the smaller numbers into a straight line.
    public string MomentumPanel(string ticker, IReadOnlyList<MomentumReading> readings)
    {
        if (readings.Count == 0)
        {
            return $"<p class=\"degraded\" data-ticker=\"{Escaped(ticker)}\" data-readings=\"0\">" +
                $"{Escaped(ticker)} has no momentum readings stored.</p>";
        }

        var height = (readings.Count * ReadingHeight) + ((readings.Count - 1) * ReadingGap);
        var svg = new StringBuilder();

        svg.Append(Invariant, $"<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 {Width} {height}\" ");
        svg.Append(Invariant, $"width=\"100%\" role=\"img\" class=\"momentum-panel\" data-ticker=\"{Escaped(ticker)}\" ");
        svg.Append(Invariant, $"data-readings=\"{readings.Count}\">");
        svg.Append(Invariant, $"<title>{Escaped(ticker)}, {readings.Count} momentum reading(s)</title>");
        svg.Append(Invariant, $"<desc>Each reading on its own small axis with its neutral rule drawn across it.</desc>");

        for (var index = 0; index < readings.Count; index++)
        {
            var reading = readings[index];
            var top = index * (ReadingHeight + ReadingGap);
            var drawn = reading.Values.Where(value => value is not null).Select(value => value!.Value).ToArray();

            // The axis takes in the neutral rule as well as the values, because
            // a rule outside the scale is a rule drawn off the pane, and a
            // reading that never crossed its rule is exactly the case a reader
            // most wants to see.
            var low = reading.Floor ?? Math.Min(drawn.Length > 0 ? drawn.Min() : reading.Neutral, reading.Neutral);
            var high = reading.Ceiling ?? Math.Max(drawn.Length > 0 ? drawn.Max() : reading.Neutral, reading.Neutral);
            var span = high - low > 0 ? high - low : 1;

            double Y(double value) => top + ReadingHeight - 2 - ((value - low) / span * (ReadingHeight - 4));

            var slot = (double)(Width - (2 * Margin)) / reading.Values.Count;

            svg.Append(Invariant, $"<g class=\"reading\" data-name=\"{Escaped(reading.Name)}\" ");
            svg.Append(Invariant, $"data-neutral=\"{Number(reading.Neutral)}\" data-values=\"{drawn.Length}\">");

            // The rule first, so the reading is drawn over it.
            svg.Append(Invariant, $"<line class=\"neutral-rule\" x1=\"{Margin}\" y1=\"{Number(Y(reading.Neutral))}\" ");
            svg.Append(Invariant, $"x2=\"{Width - Margin}\" y2=\"{Number(Y(reading.Neutral))}\" ");
            svg.Append(Invariant, $"stroke=\"var(--rule, #d8d8d8)\" stroke-width=\"1\" stroke-dasharray=\"3 3\"/>");
            svg.Append(Invariant, $"<text x=\"{Margin}\" y=\"{Number(top + 10)}\" fill=\"var(--muted, #6a6a6a)\" font-size=\"10\">");
            svg.Append(Invariant, $"{Escaped(reading.Name)}, neutral at {Number(reading.Neutral)}</text>");

            // One path per unbroken run, for the reason the averages break: a
            // reading has no value until its warm-up ends.
            var run = new StringBuilder();

            for (var at = 0; at <= reading.Values.Count; at++)
            {
                var value = at < reading.Values.Count ? reading.Values[at] : null;

                if (value is { } point)
                {
                    run.Append(run.Length == 0 ? 'M' : 'L')
                        .Append(Number(Margin + (slot * at) + (slot / 2)))
                        .Append(' ')
                        .Append(Number(Y(point)))
                        .Append(' ');

                    continue;
                }

                if (run.Length > 0)
                {
                    svg.Append(Invariant, $"<path d=\"{run.ToString().Trim()}\" fill=\"none\" ");
                    svg.Append(Invariant, $"stroke=\"var(--ink, #1c1c1c)\" stroke-width=\"1.2\"/>");
                    run.Clear();
                }
            }

            svg.Append("</g>");
        }

        svg.Append("</svg>");

        return svg.ToString();
    }

    // The level summary table. Each band with its members and their dates.
    //
    // A table rather than a mark, and that is section 15.5's own arithmetic: it
    // states seven marks and this is not one of them. It is a region of the name
    // screen, listed in 15.9 beside the chart, and it is written here because
    // the marks and the regions that read them are drawn by the same server.
    public string LevelSummary(
        string ticker,
        IReadOnlyList<SummaryBand> bands,
        IReadOnlyList<AbsentAverage>? absent = null)
    {
        var missing = absent ?? [];

        if (bands.Count == 0)
        {
            return $"<p class=\"degraded\" data-ticker=\"{Escaped(ticker)}\" data-bands=\"0\">" +
                $"{Escaped(ticker)} has no level bands stored.</p>";
        }

        var table = new StringBuilder();

        table.Append(Invariant, $"<table class=\"level-summary\" data-ticker=\"{Escaped(ticker)}\" data-bands=\"{bands.Count}\">");
        table.Append("<caption>Level summary, each band with its members and their dates</caption>");
        table.Append("<thead><tr><th>Band</th><th>Role</th><th>Strength</th><th>Members</th></tr></thead><tbody>");

        foreach (var band in bands)
        {
            // A band of one price is written as one price rather than as a range
            // from a number to itself, because the second reads as a mistake.
            var edges = band.LowEdge == band.HighEdge
                ? band.LowEdge.ToString(Invariant)
                : $"{band.LowEdge.ToString(Invariant)} to {band.HighEdge.ToString(Invariant)}";

            var role = band.Immediate ? $"{band.Role}, immediate" : band.Role;

            table.Append(Invariant, $"<tr class=\"band\" data-low-edge=\"{band.LowEdge.ToString(Invariant)}\" ");
            table.Append(Invariant, $"data-role=\"{Escaped(band.Role)}\" data-immediate=\"{(band.Immediate ? 1 : 0)}\" ");
            table.Append(Invariant, $"data-members=\"{band.Members.Count}\" data-anchored=\"{(band.HasNonAverageAnchor ? 1 : 0)}\">");
            table.Append(Invariant, $"<td>{Escaped(edges)}</td><td>{Escaped(role)}</td><td>{band.Strength}</td><td><ul>");

            foreach (var member in band.Members)
            {
                table.Append(Invariant, $"<li class=\"member\" data-kind=\"{Escaped(member.Kind)}\" data-date=\"{member.Date:yyyy-MM-dd}\">");
                table.Append(Invariant, $"{Escaped(member.Kind)} at {member.Price.ToString(Invariant)} on {member.Date:yyyy-MM-dd}</li>");
            }

            table.Append("</ul></td></tr>");
        }

        table.Append("</tbody>");

        // The averages that anchor nothing, each saying why. Section 18's row
        // asks for the string and this is the surface it is read on: an average
        // with no value cannot be a member of any band above, so without this
        // row it would be absent from the table with nothing saying so.
        if (missing.Count > 0)
        {
            table.Append(Invariant, $"<tfoot data-absent=\"{missing.Count}\">");

            foreach (var average in missing)
            {
                table.Append(Invariant, $"<tr class=\"absent-average\" data-name=\"{Escaped(average.Name)}\" ");
                table.Append(Invariant, $"data-bar-count=\"{average.BarCount}\"><td>{Escaped(average.Name)}</td>");
                table.Append(Invariant, $"<td colspan=\"3\">not available, {average.BarCount} bars</td></tr>");
            }

            table.Append("</tfoot>");
        }

        table.Append("</table>");

        return table.ToString();
    }

    public string LevelChart(
        string ticker,
        IReadOnlyList<ChartBar> bars,
        IReadOnlyList<ChartAverage>? averages = null,
        IReadOnlyList<ChartBand>? bands = null)
    {
        if (bars.Count < FewestBars)
        {
            return Degraded(ticker, bars.Count);
        }

        // An average whose length does not match the bars is refused rather
        // than drawn against the wrong sessions. A line one session short would
        // draw every point one slot to the left and look entirely plausible,
        // which is the failure that reports green.
        var lines = averages ?? [];
        var shading = bands ?? [];

        foreach (var band in shading)
        {
            if (band.HighEdge < band.LowEdge)
            {
                throw new ArgumentException(
                    $"A level band runs from {band.LowEdge} to {band.HighEdge}, which is inverted. Drawn " +
                    "as given it would be a rectangle of negative height, which renders as nothing at " +
                    "all rather than as a fault.",
                    nameof(bands));
            }
        }

        foreach (var line in lines)
        {
            if (line.Values.Count != bars.Count)
            {
                throw new ArgumentException(
                    $"The average '{line.Name}' carries {line.Values.Count} values against " +
                    $"{bars.Count} sessions. A mark draws one value per session, and a line of a " +
                    "different length would be drawn against the wrong dates rather than refused.",
                    nameof(averages));
            }
        }

        // The one scale, computed by the method the profile beside this chart is
        // given. A flat series has no span and is drawn through the middle
        // rather than refused, which PriceAxis.Range carries.
        var axis = AxisFor(bars, lines);
        var loudest = bars.Max(bar => bar.Volume);
        var busiest = loudest > 0 ? loudest : 1;

        var slot = (double)(Width - (2 * Margin)) / bars.Count;
        var body = Math.Max(1, Math.Min(11, slot * 0.62));

        var svg = new StringBuilder();

        svg.Append(Invariant, $"<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 {Width} {PriceHeight + Gap + VolumeHeight}\" ");
        svg.Append(Invariant, $"width=\"100%\" role=\"img\" class=\"level-chart\" data-ticker=\"{Escaped(ticker)}\" data-sessions=\"{bars.Count}\" ");
        svg.Append(Invariant, $"data-axis-low=\"{Number(axis.Low)}\" data-axis-high=\"{Number(axis.High)}\">");
        svg.Append(Invariant, $"<title>{Escaped(ticker)}, {bars.Count} sessions from {bars[0].SessionDate:yyyy-MM-dd} to {bars[^1].SessionDate:yyyy-MM-dd}</title>");

        // Stated for a reader who cannot see the picture, and it says which
        // elements are here rather than describing the finished mark.
        var drawn = lines.Count > 0
            ? $"with {lines.Count} moving average(s) "
            : "with no moving average given ";

        var shaded = shading.Count > 0
            ? $"{shading.Count} level band(s) shaded behind them, "
            : "no level bands given, ";

        svg.Append(Invariant, $"<desc>Daily candles {drawn}over a volume pane on a shared time axis, with {shaded}support below the price and resistance above it.</desc>");

        // The bands first, so everything else reads on top of them. A band drawn
        // over the candles hides the price it is a statement about, which is the
        // one thing the picture exists to show.
        //
        // Full width, because a band is a price and not an event: it holds for
        // the whole chart rather than for the sessions that happened to touch it.
        if (shading.Count > 0)
        {
            svg.Append("<g class=\"level-bands\">");

            foreach (var band in shading)
            {
                var top = At(axis, PlotValue(band.HighEdge));
                var bottom = At(axis, PlotValue(band.LowEdge));
                var hue = band.Role == "support" ? SupportHue : ResistanceHue;

                // A zero-width band is a real band: a single price with one
                // member. It becomes a rule rather than a rectangle nothing
                // draws, the same repair a zero-height candle body gets.
                var height = Math.Abs(bottom - top);

                svg.Append(Invariant, $"<rect class=\"level-band\" data-role=\"{Escaped(band.Role)}\" ");
                svg.Append(Invariant, $"data-low-edge=\"{band.LowEdge.ToString(Invariant)}\" data-high-edge=\"{band.HighEdge.ToString(Invariant)}\" ");
                svg.Append(Invariant, $"data-immediate=\"{(band.Immediate ? 1 : 0)}\" data-strength=\"{band.Strength}\" ");
                svg.Append(Invariant, $"x=\"{Margin}\" y=\"{Number(Math.Min(top, bottom))}\" width=\"{Width - (2 * Margin)}\" ");
                svg.Append(Invariant, $"height=\"{Number(Math.Max(height, 1))}\" fill=\"{hue}\" fill-opacity=\"{Number(band.Immediate ? 0.22 : 0.12)}\"/>");
            }

            svg.Append("</g>");
        }

        double Centre(int index) => Margin + (slot * index) + (slot / 2);

        // The averages are drawn before the candles so the price reads on top of
        // them. They carry no hue of their own: green and orange belong to
        // support and resistance on every screen, and a set of averages is a
        // magnitude rather than a set of categories, so it is one hue in steps
        // with the longer average darker, which is section 15.6's rule that
        // magnitude is one ramp and never a rainbow. That rule has no decision
        // behind it and is cited by section rather than by name, because the
        // architecture states it once with its reasoning and a decision would be
        // a second place holding one fact.
        // see: Support and resistance own two hues and nothing else uses them
        for (var line = 0; line < lines.Count; line++)
        {
            var average = lines[line];
            var shade = 0.34 + (0.22 * Math.Min(line, 3));

            svg.Append(Invariant, $"<g class=\"moving-average\" data-average=\"{Escaped(average.Name)}\" ");
            svg.Append(Invariant, $"data-values=\"{average.Values.Count(value => value is not null)}\">");

            // One path per unbroken run of values. A 200-day average has no
            // value for the first 199 sessions of a stored year, and joining
            // across an absence would draw a line over sessions the average did
            // not exist for.
            var run = new StringBuilder();

            for (var index = 0; index <= average.Values.Count; index++)
            {
                var value = index < average.Values.Count ? average.Values[index] : null;

                if (value is { } point)
                {
                    run.Append(run.Length == 0 ? 'M' : 'L')
                        .Append(Number(Centre(index)))
                        .Append(' ')
                        .Append(Number(At(axis, point)))
                        .Append(' ');

                    continue;
                }

                if (run.Length > 0)
                {
                    svg.Append(Invariant, $"<path d=\"{run.ToString().Trim()}\" fill=\"none\" ");
                    svg.Append(Invariant, $"stroke=\"var(--ink, #1c1c1c)\" stroke-opacity=\"{Number(shade)}\" stroke-width=\"1.4\"/>");
                    run.Clear();
                }
            }

            svg.Append("</g>");
        }

        for (var index = 0; index < bars.Count; index++)
        {
            var bar = bars[index];
            var centre = Margin + (slot * index) + (slot / 2);

            double Y(decimal price) => At(axis, PlotValue(price));

            var top = Y(bar.High);
            var bottom = Y(bar.Low);
            var openY = Y(bar.Open);
            var closeY = Y(bar.Close);

            // Hollow for a close above the open and filled for below, in
            // neutral ink. Green and orange belong to support and resistance on
            // every screen and section 15.6 names a candle as the case it
            // forbids them in.
            // see: Support and resistance own two hues and nothing else uses them
            var rising = bar.Close > bar.Open;
            var fill = rising ? "none" : "var(--ink, #1c1c1c)";

            svg.Append(Invariant, $"<g class=\"candle\" data-session=\"{bar.SessionDate:yyyy-MM-dd}\">");
            svg.Append(Invariant, $"<line x1=\"{Number(centre)}\" y1=\"{Number(top)}\" x2=\"{Number(centre)}\" y2=\"{Number(bottom)}\" stroke=\"var(--ink, #1c1c1c)\" stroke-width=\"1\"/>");

            // A session that opened and closed at one price has no body, and a
            // zero-height rectangle draws nothing, so it becomes a rule.
            var height = Math.Abs(openY - closeY);
            var left = centre - (body / 2);

            if (height < 1)
            {
                svg.Append(Invariant, $"<line x1=\"{Number(left)}\" y1=\"{Number(closeY)}\" x2=\"{Number(left + body)}\" y2=\"{Number(closeY)}\" stroke=\"var(--ink, #1c1c1c)\" stroke-width=\"1\"/>");
            }
            else
            {
                svg.Append(Invariant, $"<rect x=\"{Number(left)}\" y=\"{Number(Math.Min(openY, closeY))}\" width=\"{Number(body)}\" height=\"{Number(height)}\" fill=\"{fill}\" stroke=\"var(--ink, #1c1c1c)\" stroke-width=\"1\"/>");
            }

            svg.Append("</g>");
        }

        var volumeTop = PriceHeight + Gap;

        svg.Append(Invariant, $"<g class=\"volume-pane\" data-sessions=\"{bars.Count}\">");
        svg.Append(Invariant, $"<line x1=\"{Margin}\" y1=\"{volumeTop}\" x2=\"{Width - Margin}\" y2=\"{volumeTop}\" stroke=\"var(--rule, #d8d8d8)\" stroke-width=\"1\"/>");

        for (var index = 0; index < bars.Count; index++)
        {
            var bar = bars[index];
            var centre = Margin + (slot * index) + (slot / 2);
            var height = (double)bar.Volume / busiest * (VolumeHeight - Margin);

            svg.Append(Invariant, $"<rect class=\"volume\" data-session=\"{bar.SessionDate:yyyy-MM-dd}\" ");
            svg.Append(Invariant, $"x=\"{Number(centre - (body / 2))}\" y=\"{Number(volumeTop + VolumeHeight - Margin - height)}\" ");
            svg.Append(Invariant, $"width=\"{Number(body)}\" height=\"{Number(height)}\" fill=\"var(--muted, #6a6a6a)\"/>");
        }

        svg.Append("</g>");

        // The shared time axis, which is what makes the two panes one picture.
        svg.Append(Invariant, $"<g class=\"time-axis\">");
        svg.Append(Invariant, $"<text x=\"{Margin}\" y=\"{PriceHeight + Gap + VolumeHeight - 1}\" fill=\"var(--muted, #6a6a6a)\" font-size=\"11\">{bars[0].SessionDate:yyyy-MM-dd}</text>");
        svg.Append(Invariant, $"<text x=\"{Width - Margin}\" y=\"{PriceHeight + Gap + VolumeHeight - 1}\" text-anchor=\"end\" fill=\"var(--muted, #6a6a6a)\" font-size=\"11\">{bars[^1].SessionDate:yyyy-MM-dd}</text>");
        svg.Append("</g>");

        svg.Append("</svg>");

        return svg.ToString();
    }

    // What a mark returns instead of a drawing. It states the count rather than
    // apologising, because the reader's next question is how many there were.
    // Why it is here, section 15.9's region that is present only when the name
    // is on tonight's list.
    //
    // Each reason in a full sentence rather than a label, with the values that
    // made it true. A label is what the list row shows; a sentence is what the
    // name page owes, because this is the page a reader acts from.
    // see: Every figure carries a plain-language key
    public string WhyItIsHere(string ticker, IReadOnlyList<FiredReason> reasons)
    {
        var why = new StringBuilder();

        why.Append(Invariant, $"<section class=\"why-it-is-here\" data-ticker=\"{Escaped(ticker)}\" data-reasons=\"{reasons.Count}\">");

        if (reasons.Count == 0)
        {
            why.Append("<p class=\"degraded\" data-listed=\"false\">this name is not on tonight's list</p></section>");

            return why.ToString();
        }

        foreach (var reason in reasons)
        {
            why.Append(Invariant, $"<p class=\"reason\" data-reason=\"{Escaped(reason.Name)}\">");
            why.Append(Invariant, $"{Escaped(Sentence(reason.Name))}");
            why.Append(Invariant, $" <span class=\"values\" data-values=\"{Escaped(string.Join(", ", reason.Values.Select(value => $"{value.Key} {value.Value}")))}\">");
            why.Append(Invariant, $"{Escaped(string.Join(", ", reason.Values.Select(value => $"{value.Key} {value.Value}")))}</span></p>");
        }

        why.Append("</section>");

        return why.ToString();
    }

    // Each reason as a sentence. Every reason the shortlist carries has its own
    // arm and anything else throws, for the reason the plan column's mapping
    // does: a sentence a reader acts on that was produced by a value nobody
    // wrote is what a catch-all arm makes invisible.
    static string Sentence(string reason) => reason switch
    {
        "at entry zone" => "tonight's close is inside a tranche zone, so the plan's first step is available at tonight's price.",
        "crossed a level" => "the close moved through a band edge it was on the other side of yesterday, so the level either held or failed today.",
        "breakout on volume" => "the close is above a resistance band on volume above the fifty-day average.",
        "trend state changed" => "tonight's trend label differs from last night's, so the ladder changes shape and the whole plan is different from yesterday's.",
        "unusual volume" => "volume is above twice the fifty-day average, so something happened the price may not have shown yet.",
        "earnings soon" => "the next dated event is inside the twenty-session horizon, which is a calendar fact rather than a setup.",
        _ => throw new InvalidOperationException(
            $"The stored listing carries the reason '{reason}', which this mapping has no sentence for. " +
            "A sentence a reader acts on that was produced by a value nobody wrote is what a catch-all arm " +
            "makes invisible, so the page fails rather than rendering a default."),
    };

    // The walk, section 15.9's last region: previous and next on tonight's list,
    // so an evening's reading is one pass through with no return to the list.
    //
    // A name that is not on the list has no neighbours and says so, rather than
    // linking to the ends of a list it is not in.
    public string Walk(string ticker, string? previous, string? next)
    {
        var walk = new StringBuilder();

        walk.Append(Invariant, $"<nav class=\"walk\" data-ticker=\"{Escaped(ticker)}\" ");
        walk.Append(Invariant, $"data-previous=\"{Escaped(previous ?? "none")}\" data-next=\"{Escaped(next ?? "none")}\">");

        if (previous is null)
        {
            walk.Append("<span class=\"degraded\">no previous name on tonight's list</span>");
        }
        else
        {
            walk.Append(Invariant, $"<a href=\"#/name/{Escaped(previous)}\">previous: {Escaped(previous)}</a>");
        }

        if (next is null)
        {
            walk.Append("<span class=\"degraded\">no next name on tonight's list</span>");
        }
        else
        {
            walk.Append(Invariant, $"<a href=\"#/name/{Escaped(next)}\">next: {Escaped(next)}</a>");
        }

        walk.Append("</nav>");

        return walk.ToString();
    }

    // The listing strip, section 15.5's mark: the evenings a name was on the
    // list over a window.
    //
    // It says nothing about index membership, which every name in the table it
    // sits in has by definition. That sentence is in section 15.8's note because
    // the columns were misread that way once.
    public string ListingStrip(string ticker, IReadOnlyList<bool> evenings)
    {
        const int Cell = 4;
        const int Height = 14;

        var strip = new StringBuilder();

        strip.Append(Invariant, $"<svg class=\"listing-strip\" role=\"img\" viewBox=\"0 0 {Math.Max(evenings.Count, 1) * Cell} {Height}\" ");
        strip.Append(Invariant, $"width=\"{Math.Max(evenings.Count, 1) * Cell}\" height=\"{Height}\" ");
        strip.Append(Invariant, $"data-ticker=\"{Escaped(ticker)}\" data-evenings=\"{evenings.Count}\" ");
        strip.Append(Invariant, $"data-listed=\"{evenings.Count(listed => listed)}\">");

        for (var at = 0; at < evenings.Count; at++)
        {
            // A listed evening is inked and a quiet one is a rule, so the strip
            // reads as a pattern rather than as two colours a reader has to
            // learn. Hue is never the only channel that carries a meaning.
            if (evenings[at])
            {
                strip.Append(Invariant, $"<rect x=\"{at * Cell}\" y=\"2\" width=\"{Cell - 1}\" height=\"{Height - 4}\" fill=\"var(--ink, #1c1c1c)\" />");
            }
            else
            {
                strip.Append(Invariant, $"<rect x=\"{at * Cell}\" y=\"{Height / 2}\" width=\"{Cell - 1}\" height=\"1\" fill=\"var(--rule, #d8d8d8)\" />");
            }
        }

        strip.Append(Invariant, $"<title>{Escaped(ticker)}: on the list on {evenings.Count(listed => listed)} of {evenings.Count} evening(s)</title>");
        strip.Append("</svg>");

        return strip.ToString();
    }

    // Tonight's list, section 15.7's third region.
    //
    // One row per name that fired, ordered by how many fired then by band
    // strength, at most twenty drawn. The true count is in the header rather
    // than here, because a page that shows twenty every night cannot tell you
    // how busy the night was.
    // see: The page shows twenty and states the true count
    public string TonightList(
        IReadOnlyList<ListingCell> rows,
        int drawn,
        IReadOnlyList<ReasonRecord>? records = null)
    {
        var shown = rows.Take(drawn).ToArray();
        var list = new StringBuilder();

        list.Append(Invariant, $"<section class=\"tonight-list\" data-fired=\"{rows.Count}\" data-drawn=\"{shown.Length}\">");

        if (rows.Count == 0)
        {
            list.Append("<p class=\"degraded\" data-fired=\"0\">no name fired a reason tonight</p></section>");

            return list.ToString();
        }

        list.Append(Invariant, $"<table class=\"list-table\" data-rows=\"{shown.Length}\">");
        list.Append("<tr><th>Name</th><th>Close</th><th>Day</th><th>Trend</th><th>Distance</th><th>Reasons</th></tr>");

        foreach (var row in shown)
        {
            list.Append(Invariant, $"<tr data-ticker=\"{Escaped(row.Ticker)}\" data-fired-count=\"{row.FiredCount}\" data-strength=\"{row.Strength}\" ");
            list.Append(Invariant, $"data-day-change=\"{Change(row.DayChangePct)}\" data-trend-state=\"{Escaped(row.TrendState ?? NotClassified)}\">");

            // The name, and the name is the link that selects this row. Section
            // 15.7's selected-name region is for whichever row is selected, and
            // a row a reader cannot select is a row the region can never be
            // about. The href carries the night as well as the name, so a
            // selected view of an earlier night is a link like every other view.
            list.Append(Invariant, $"<td><a class=\"select\" data-selects=\"{Escaped(row.Ticker)}\" ");
            list.Append(Invariant, $"href=\"#/night/{row.SessionDate:yyyy-MM-dd}?name={Uri.EscapeDataString(row.Ticker)}\">{Escaped(row.Ticker)}</a></td>");

            list.Append(Invariant, $"<td>{(row.Close is { } close ? close.ToString(Invariant) : "not computed")}</td>");

            // The day's change, signed and in words as well as by its sign. The
            // two hues are support's and resistance's and a day is not allowed
            // either of them, so the direction is the sign on the number and
            // nothing else carries it.
            // see: Support and resistance own two hues and nothing else uses them
            list.Append(Invariant, $"<td class=\"day-change\">{ChangeReads(row.DayChangePct)}</td>");

            // The trend state in a word, read off the ladder row rather than
            // worked out here, and a name with no row says so rather than
            // showing an empty cell.
            list.Append(Invariant, $"<td class=\"trend-state\">{Escaped((row.TrendState ?? NotClassified).Replace('_', ' '))}</td>");

            // The distance row mark, the same mark the universe table draws, so
            // a shape means one thing on both screens.
            list.Append(Invariant, $"<td>{(row.Distance is { } cell ? DistanceRow(cell) : "<span class=\"degraded\" data-distance=\"none\">no bands stored for this name</span>")}</td>");

            list.Append(Invariant, $"<td data-reasons=\"{Escaped(string.Join(", ", row.Reasons))}\">{ReasonsForRow(row, records)}</td>");
            list.Append("</tr>");
        }

        list.Append("</table>");

        // What the drawn rows leave out, stated rather than left to arithmetic
        // a reader would have to do.
        if (rows.Count > shown.Length)
        {
            list.Append(Invariant, $"<p class=\"more\" data-undrawn=\"{rows.Count - shown.Length}\">{rows.Count} name(s) fired and {shown.Length} are drawn</p>");
        }

        list.Append("</section>");

        return list.ToString();
    }

    // The reasons on one row of tonight's list, section 15.7's fourth region.
    //
    // Each reason named, with its measured record beside it under 15.11 and the
    // values that made it true on hover. The record is the reason's and not the
    // name's: it says how this reason has done across every name it ever fired
    // for, and it is not a statement about the name in this row. That is why it
    // is drawn inside the reason's own span rather than in a column of its own,
    // where a reader would take it for a property of the row.
    // see: A reason's record is displayed, beside the reason and never beside the name
    static string ReasonsForRow(ListingCell row, IReadOnlyList<ReasonRecord>? records)
    {
        var byReason = records?.ToDictionary(record => record.Reason, StringComparer.Ordinal);
        var cell = new StringBuilder();

        // The values arrive with `Fired` and the names without it, so a caller
        // that has only the names still draws the reasons rather than nothing.
        var fired = row.Fired
            ?? [.. row.Reasons.Select(name => new FiredReason(name, new Dictionary<string, string>(StringComparer.Ordinal)))];

        foreach (var reason in fired)
        {
            var values = string.Join(
                ", ",
                reason.Values.OrderBy(value => value.Key, StringComparer.Ordinal).Select(value => $"{value.Key} {value.Value}"));

            cell.Append(Invariant, $"<span class=\"reason\" data-reason=\"{Escaped(reason.Name)}\" ");
            cell.Append(Invariant, $"title=\"{Escaped(values.Length == 0 ? "no values stored for this reason" : values)}\">");
            cell.Append(Invariant, $"{Escaped(reason.Name)}");

            if (byReason is not null && byReason.TryGetValue(reason.Name, out var record))
            {
                cell.Append(record.HasEarnedAVerdict
                    ? Formatted($"<span class=\"record\" data-verdict=\"due\" data-resolved=\"{record.Resolved}\">{record.Resolved} resolved</span>")
                    : Formatted($"<span class=\"record not-measured\" data-outline=\"dashed\" data-verdict=\"none\" data-resolved=\"{record.Resolved}\" data-minimum=\"{record.Minimum}\">{record.Resolved} of {record.Minimum} resolved</span>"));
            }

            cell.Append("</span>");
        }

        return cell.ToString();
    }

    // The reason track, section 15.5's sixth mark.
    //
    // Per reason, out of one denominator: setups resolved as a win, resolved as
    // a loss, and unresolved. The third state is a dashed outline and never a
    // third colour, because unresolved is not a smaller amount of losing and
    // must not read as one.
    // see: Not yet measured is drawn as a dashed outline, never as a pale value
    //
    // One denominator means one across the whole mark rather than one per row.
    // Scaled per row every bar would be full width and the mark would say
    // nothing about which reason fired on more names, which is the question it
    // exists to answer.
    //
    // Hue is not a channel here at all. The three states are one ink at two
    // steps plus an outline, and each row carries its counts in words on its own
    // title, so a reader who cannot separate two greys loses nothing.
    // see: Support and resistance own two hues and nothing else uses them
    public string ReasonTrack(IReadOnlyList<ReasonTrackRow> rows)
    {
        const int Row = 18;
        const int Width = 220;
        const int Label = 4;

        var most = rows.Count == 0 ? 0 : rows.Max(row => row.Total);
        var height = Math.Max(rows.Count, 1) * Row;
        var track = new StringBuilder();

        track.Append(Invariant, $"<svg class=\"reason-track\" role=\"img\" viewBox=\"0 0 {Width} {height}\" ");
        track.Append(Invariant, $"width=\"{Width}\" height=\"{height}\" data-reasons=\"{rows.Count}\" data-denominator=\"{most}\">");

        for (var at = 0; at < rows.Count; at++)
        {
            var row = rows[at];
            var top = (at * Row) + 3;

            track.Append(Invariant, $"<g data-reason=\"{Escaped(row.Reason)}\" data-total=\"{row.Total}\" ");
            track.Append(Invariant, $"data-won=\"{row.Won}\" data-lost=\"{row.Lost}\" ");
            track.Append(Invariant, $"data-resolved-unsplit=\"{row.ResolvedUnsplit}\" data-unresolved=\"{row.Unresolved}\">");

            // A reason that fired on nothing draws its rule rather than nothing,
            // so a reason that never fires is visible as one that never fires
            // rather than absent from the picture.
            if (row.Total == 0 || most == 0)
            {
                track.Append(Invariant, $"<rect x=\"{Label}\" y=\"{top + (Row / 2)}\" width=\"{Width - Label - 2}\" height=\"1\" fill=\"var(--rule, #d8d8d8)\" />");
            }

            var span = most == 0 ? 0d : (double)(Width - Label - 2) / most;
            var left = (double)Label;

            left = Segment(track, "won", row.Won, left, top, span, "var(--ink, #1c1c1c)", 1);
            left = Segment(track, "lost", row.Lost, left, top, span, "var(--ink, #1c1c1c)", 0.45);
            left = Segment(track, "resolved", row.ResolvedUnsplit, left, top, span, "var(--ink, #1c1c1c)", 0.7);

            // The unresolved segment, drawn as an outline with nothing inside
            // it. Its own segment of the track and never folded into the rate.
            if (row.Unresolved > 0 && span > 0)
            {
                track.Append(Invariant, $"<rect data-state=\"unresolved\" data-count=\"{row.Unresolved}\" ");
                track.Append(Invariant, $"x=\"{left:0.##}\" y=\"{top}\" width=\"{row.Unresolved * span:0.##}\" height=\"{Row - 6}\" ");
                track.Append(Invariant, $"fill=\"none\" stroke=\"var(--ink, #1c1c1c)\" stroke-dasharray=\"3 2\" />");
            }

            track.Append(Invariant, $"<title>{Escaped(row.Reason)}: {Words(row)}</title>");
            track.Append("</g>");
        }

        track.Append("</svg>");

        return track.ToString();
    }

    static double Segment(
        StringBuilder track,
        string state,
        int count,
        double left,
        int top,
        double span,
        string fill,
        double opacity)
    {
        const int Row = 18;

        if (count <= 0 || span <= 0)
        {
            return left;
        }

        track.Append(Invariant, $"<rect data-state=\"{state}\" data-count=\"{count}\" ");
        track.Append(Invariant, $"x=\"{left:0.##}\" y=\"{top}\" width=\"{count * span:0.##}\" height=\"{Row - 6}\" ");
        track.Append(Invariant, $"fill=\"{fill}\" fill-opacity=\"{Number(opacity)}\" />");

        return left + (count * span);
    }

    // The counts in words, so the picture is never the only channel.
    static string Words(ReasonTrackRow row) =>
        row.Total == 0
            ? "no setup on this reason yet"
            : row.ResolvedUnsplit > 0
                ? Formatted($"{row.ResolvedUnsplit} resolved and {row.Unresolved} unresolved, of {row.Total}")
                : Formatted($"{row.Won} won, {row.Lost} lost and {row.Unresolved} unresolved, of {row.Total}");

    // The reason records, section 15.10's second region, in the state this build
    // is in for its first year.
    //
    // A reason below the minimum shows a dashed outline carrying its resolved
    // count against that minimum, and no rate. A pale number reads as a small
    // one, and a rate over a handful of cases reads as evidence and is not.
    // see: Not yet measured is drawn as a dashed outline, never as a pale value
    // see: The record column stays empty until it has earned a number
    // see: A reason's record is displayed, beside the reason and never beside the name
    //
    // The base rate is the pinned first row rather than a figure beside one of
    // them, which is what makes it impossible to read a forward-return figure on
    // this page without it.
    // see: Every forward-return figure is shown against the universe base rate
    public string ReasonRecords(
        IReadOnlyList<ReasonRecord> records,
        IReadOnlyList<ReasonTrackRow> tracks,
        IReadOnlyList<BaseRateLine> baseRates,
        int nights)
    {
        // The guard that makes the rule a property of the code rather than a
        // habit of this method. A region that can be drawn with no base rate is
        // one that will be, on the evening somebody passes an empty list, and
        // the figure that appears without it looks exactly like one that has it.
        // see: Every forward-return figure is shown against the universe base rate
        if (baseRates.Count == 0)
        {
            throw new InvalidOperationException(
                "The reason records were asked for with no base rate line at all. Every " +
                "forward-return figure on this page is shown against the universe base rate " +
                "for the same window, so a region with no pinned line is a region drawing " +
                "figures a reader cannot judge.");
        }

        var table = new StringBuilder();

        table.Append(Invariant, $"<section class=\"reason-records\" data-reasons=\"{records.Count}\" ");
        table.Append(Invariant, $"data-nights=\"{nights}\" data-base-rates=\"{baseRates.Count}\" ");
        table.Append(Invariant, $"data-minimum=\"{(records.Count == 0 ? 0 : records[0].Minimum)}\">");

        // The base rates, pinned above every row rather than beside one of them,
        // one per window that has one. The population is stated in the same
        // breath: every name-night the store holds, and not the nights a reason
        // fired, which would compare a signal against itself.
        foreach (var line in baseRates)
        {
            table.Append(Invariant, $"<p class=\"base-rate\" data-pinned=\"true\" data-window=\"{Escaped(line.Window)}\" ");
            table.Append(Invariant, $"data-base-rate=\"{(line.Rate is { } rate ? Number(rate) : "none")}\">");

            table.Append(line.Rate is { } shown
                ? Formatted($"the universe base rate over the {Escaped(line.Window)} session window is {Number(shown)} per cent, ")
                : Formatted($"the universe base rate over the {Escaped(line.Window)} session window is not yet measured, "));

            table.Append("computed over every name-night the store holds rather than over the nights a reason fired</p>");
        }

        // The setup horizon has no universe figure of the same kind, and the
        // page says why rather than leaving a window without a line and letting
        // a reader wonder which of the three is missing.
        // see: The `setup` horizon has no universe base rate, and the column is null for it
        table.Append("<p class=\"base-rate\" data-window=\"setup\" data-base-rate=\"none by rule\">");
        table.Append("the setup horizon has no universe base rate: target before stop is a question about a plan, ");
        table.Append("and a name with no plan that night has no answer to it. A setup is judged against the ");
        table.Append("break-even its own plan demanded</p>");

        table.Append(Invariant, $"<p class=\"nights\" data-nights=\"{nights}\">the record below stands on {nights} night(s) of listings</p>");

        table.Append("<table class=\"records-table\">");
        table.Append("<tr><th>Reason</th><th>Track</th><th>Fired</th><th>Resolved</th><th>Record</th></tr>");

        var byReason = tracks.ToDictionary(track => track.Reason, StringComparer.Ordinal);

        foreach (var record in records)
        {
            table.Append(Invariant, $"<tr data-reason=\"{Escaped(record.Reason)}\" data-fired=\"{record.Fired}\" ");
            table.Append(Invariant, $"data-resolved=\"{record.Resolved}\" data-minimum=\"{record.Minimum}\">");
            table.Append(Invariant, $"<td>{Escaped(record.Reason)}</td>");
            table.Append(Invariant, $"<td>{(byReason.TryGetValue(record.Reason, out var track) ? ReasonTrack([track]) : string.Empty)}</td>");
            table.Append(Invariant, $"<td>{record.Fired}</td>");
            table.Append(Invariant, $"<td>{record.Resolved}</td>");

            // The count against the minimum, inside the dashed outline. The
            // count is what makes the absence readable: a reader sees how far
            // off a verdict is rather than only that there is none.
            table.Append(record.HasEarnedAVerdict
                ? Formatted($"<td data-verdict=\"due\">{record.Resolved} resolved, and the share that reached target before stop arrives with the verdicts at 7.5</td>")
                : Formatted($"<td class=\"not-measured\" data-outline=\"dashed\" data-verdict=\"none\">{record.Resolved} of {record.Minimum} resolved</td>"));

            table.Append("</tr>");
        }

        table.Append("</table>");
        table.Append("<p class=\"degraded\" data-verdicts=\"absent\">no rate is shown for a reason below the minimum, because a rate over a handful of resolved setups is consistent with almost any truth</p>");
        table.Append("</section>");

        return table.ToString();
    }

    // The operational header, section 15.10's first region: what ran, how long
    // each stage took, model calls, network requests, spend and the outcome.
    //
    // Per stage rather than in one total, because a night that landed inside its
    // limit by one step doing nothing is legible only if the steps are apart.
    public string OperationalHeader(DateOnly night, IReadOnlyList<StageRow> stages)
    {
        var header = new StringBuilder();

        header.Append(Invariant, $"<header class=\"operational\" data-night=\"{night:yyyy-MM-dd}\" data-stages=\"{stages.Count}\">");

        if (stages.Count == 0)
        {
            header.Append(Invariant, $"<p class=\"degraded\" data-stages=\"0\">the run log carries no stage for {night:yyyy-MM-dd}</p></header>");

            return header.ToString();
        }

        header.Append("<table class=\"stage-table\">");
        header.Append("<tr><th>Stage</th><th>Started (UTC)</th><th>Took</th><th>Rows</th><th>Model calls</th><th>Requests</th><th>Spend</th><th>Outcome</th><th>Detail</th></tr>");

        foreach (var stage in stages)
        {
            // The instant in UTC and stated as UTC in the column heading, for
            // the reason every schedule in this repository is written that way:
            // a local rendering walks an hour against the provider twice a year,
            // and the figure this column exists to bound is the provider's.
            // The cell carries the clock time and the attribute the whole
            // instant, because a night that starts after the close in New York
            // carries tomorrow's UTC date and a bare time would hide it.
            header.Append(Invariant, $"<tr data-stage=\"{Escaped(stage.Stage)}\" ");
            header.Append(Invariant, $"data-started=\"{stage.StartedAt.UtcDateTime:yyyy-MM-ddTHH:mm:ssZ}\" data-seconds=\"{Number(stage.Seconds)}\" ");
            header.Append(Invariant, $"data-rows=\"{stage.RowsWritten}\" data-model-calls=\"{stage.ModelCalls}\" ");
            header.Append(Invariant, $"data-requests=\"{stage.NetworkRequests}\" data-spend=\"{Escaped(stage.Spend)}\" ");
            header.Append(Invariant, $"data-outcome=\"{Escaped(stage.Outcome)}\">");
            header.Append(Invariant, $"<td>{Escaped(stage.Stage)}</td>");
            header.Append(Invariant, $"<td>{stage.StartedAt.UtcDateTime:HH:mm:ss}</td>");
            header.Append(Invariant, $"<td>{Number(stage.Seconds)}s</td><td>{stage.RowsWritten}</td>");
            header.Append(Invariant, $"<td>{stage.ModelCalls}</td><td>{stage.NetworkRequests}</td>");
            header.Append(Invariant, $"<td>{Escaped(stage.Spend)}</td><td>{Escaped(stage.Outcome)}</td>");

            // What the stage said about itself, in a cell rather than a hover.
            // Until the phase 5 sign-off it was the stage cell's title, which a
            // person reads only by pointing at it and a phone cannot show, so the
            // names a night could not compare, the rows the fetch passed over and
            // the members a file carried nothing for were on the page and not
            // readable on it.
            header.Append(Invariant, $"<td class=\"detail\">{Escaped(stage.Detail)}</td></tr>");
        }

        header.Append("</table>");

        // The night's own total beneath the steps rather than above them, so the
        // steps are what is read first.
        header.Append(Invariant, $"<p class=\"total\" data-seconds=\"{Number(stages.Sum(stage => stage.Seconds))}\">");
        header.Append(Invariant, $"{stages.Count} stage(s), {Number(stages.Sum(stage => stage.Seconds))} second(s) of stage time</p>");
        header.Append("</header>");

        return header.ToString();
    }

    // Stale and failed, section 15.10's fourth region: names carrying
    // yesterday's bars, and what failed in which component.
    //
    // The names are listed rather than counted. A page that says four names are
    // stale and does not say which is a page nobody can act on. The research
    // halves, being the sections that fell back and the documents refused by
    // admissibility, arrive with the pass that produces them and are stated as
    // absent rather than drawn as empty lists.
    public string StaleAndFailed(IReadOnlyList<string> stale, IReadOnlyList<StageRow> failed)
    {
        var region = new StringBuilder();

        region.Append(Invariant, $"<section class=\"stale-and-failed\" data-stale=\"{stale.Count}\" data-failed=\"{failed.Count}\">");

        region.Append(stale.Count == 0
            ? "<p data-stale=\"none\">no name is carrying yesterday's bars</p>"
            : Formatted($"<p data-stale=\"{stale.Count}\">{stale.Count} name(s) carrying yesterday's bars: {Escaped(string.Join(", ", stale))}</p>"));

        region.Append(failed.Count == 0
            ? "<p data-failed=\"none\">no stage of this night failed</p>"
            : Formatted($"<p data-failed=\"{failed.Count}\">{failed.Count} stage(s) failed</p>"));

        foreach (var stage in failed)
        {
            region.Append(Invariant, $"<p class=\"failed\" data-stage=\"{Escaped(stage.Stage)}\" data-outcome=\"{Escaped(stage.Outcome)}\">");
            region.Append(Invariant, $"{Escaped(stage.Stage)}: {Escaped(stage.Outcome)}. {Escaped(stage.Detail)}</p>");
        }

        region.Append("<p class=\"degraded\" data-research=\"absent\">the sections that fell back and the documents refused by admissibility arrive with the research pass that produces them</p>");
        region.Append("</section>");

        return region.ToString();
    }

    // The harness, section 15.10's last region: the verdict counts from the last
    // phase report, each separately.
    //
    // Separately because out of scope is counted apart from unexamined and only
    // one of them is a defect, and a page that summed them would report a build
    // that has not reached a claim as one that failed to check it.
    public string HarnessVerdicts(HarnessCounts? counts)
    {
        if (counts is not { } read)
        {
            return "<section class=\"harness\" data-report=\"none\">" +
                "<p class=\"degraded\">no phase report has been written on this machine</p></section>";
        }

        var region = new StringBuilder();

        region.Append(Invariant, $"<section class=\"harness\" data-passed=\"{read.Passed}\" data-failed=\"{read.Failed}\" ");
        region.Append(Invariant, $"data-unexamined=\"{read.Unexamined}\" data-out-of-scope=\"{read.OutOfScope}\">");
        region.Append(Invariant, $"<p>{read.Passed} passed, {read.Failed} failed, {read.Unexamined} unexamined, ");
        region.Append(Invariant, $"{read.OutOfScope} out of scope</p>");
        region.Append("<p class=\"degraded\">out of scope is counted apart from unexamined and never added to it, because only unexamined is a defect</p>");
        region.Append("</section>");

        return region.ToString();
    }

    // Tonight's reason totals, section 15.7's last region: the reason track
    // across tonight's fired names, which says whether the evening is one thing
    // happening to many names or many things happening to a few.
    public string ReasonTotals(IReadOnlyList<ReasonTrackRow> tracks)
    {
        var region = new StringBuilder();

        region.Append(Invariant, $"<section class=\"reason-totals\" data-reasons=\"{tracks.Count}\" ");
        region.Append(Invariant, $"data-names=\"{tracks.Sum(track => track.Total)}\">");
        region.Append(ReasonTrack(tracks));
        region.Append("<table class=\"totals-table\"><tr><th>Reason</th><th>Names</th></tr>");

        foreach (var track in tracks)
        {
            region.Append(Invariant, $"<tr data-reason=\"{Escaped(track.Reason)}\" data-names=\"{track.Total}\">");
            region.Append(Invariant, $"<td>{Escaped(track.Reason)}</td><td>{track.Total}</td></tr>");
        }

        region.Append("</table>");
        region.Append("<p class=\"degraded\" data-unresolved=\"all\">every name listed tonight is a setup nothing has scored yet, so the whole of tonight's track is the unresolved state</p>");
        region.Append("</section>");

        return region.ToString();
    }

    // The night header, section 15.7's first region.
    //
    // The fired count is the headline, because it is the market's mood and it is
    // the one number the twenty drawn rows cannot tell you. The quantities phase
    // 6 supplies are absent and say so rather than being drawn as zero, which
    // would read as a night that spent nothing because it did nothing.
    public string NightHeader(DateOnly night, int index, int fired, string? duration, HarnessCounts? harness)
    {
        var header = new StringBuilder();

        header.Append(Invariant, $"<header class=\"night-header\" data-night=\"{night:yyyy-MM-dd}\" ");
        header.Append(Invariant, $"data-index=\"{index}\" data-fired=\"{fired}\">");
        header.Append(Invariant, $"<p class=\"fired\">{fired} of {index} name(s) fired on {night:yyyy-MM-dd}</p>");
        header.Append(Invariant, $"<p class=\"duration\" data-duration=\"{Escaped(duration ?? "not recorded")}\">the night took {Escaped(duration ?? "a time the run log does not record")}</p>");

        // The harness verdict, which section 15.7 states in this header and
        // which stood only on the run page until 5.8. It is the same four
        // counts from the same report, drawn here in one line rather than in a
        // region of its own: this header answers how the evening went, and
        // whether the build that produced it is checked is part of that answer.
        //
        // Four counts and no total, for the reason the run page's own region
        // gives: out of scope is counted apart from unexamined and only one of
        // them is a defect.
        if (harness is { } counts)
        {
            header.Append(Invariant, $"<p class=\"harness-verdict\" data-passed=\"{counts.Passed}\" data-failed=\"{counts.Failed}\" ");
            header.Append(Invariant, $"data-unexamined=\"{counts.Unexamined}\" data-out-of-scope=\"{counts.OutOfScope}\">");
            header.Append(Invariant, $"harness: {counts.Passed} passed, {counts.Failed} failed, {counts.Unexamined} unexamined, ");
            header.Append(Invariant, $"{counts.OutOfScope} out of scope</p>");
        }
        else
        {
            header.Append("<p class=\"harness-verdict degraded\" data-report=\"none\">harness: no phase report has been written on this machine</p>");
        }

        header.Append("<p class=\"degraded\" data-prose=\"absent\">fresh prose against reused, and spend, arrive with the research pass that produces them</p>");
        header.Append("</header>");

        return header.ToString();
    }

    // The watch list, section 15.7's second region: the two or three names shown
    // every evening whether or not a reason fired, above the list rather than
    // inside it.
    //
    // No store holds a watch list, and none is invented here. The region states
    // that rather than being absent, because a region a reader cannot find is
    // indistinguishable from one that is empty.
    public string WatchList(IReadOnlyList<ListingCell> watched)
    {
        var watch = new StringBuilder();

        watch.Append(Invariant, $"<section class=\"watch-list\" data-watched=\"{watched.Count}\">");

        watch.Append(watched.Count == 0
            ? "<p class=\"degraded\" data-watch=\"none\">no watch list is on file, so none is shown</p>"
            : string.Empty);

        foreach (var name in watched)
        {
            watch.Append(Invariant, $"<span class=\"watched\" data-ticker=\"{Escaped(name.Ticker)}\">{Escaped(name.Ticker)}</span>");
        }

        watch.Append("</section>");

        return watch.ToString();
    }

    // The how-it-got-here table's rows, section 15.9's second region.
    //
    // The biggest moves of the stored year, largest first, each saying how many
    // sessions it spans so a five-day run reads as one and not as a day that
    // moved twelve per cent.
    //
    // The cause column is absent and the table says so once, rather than drawn
    // as an empty cell in every row. A cause is a researched claim and lives in
    // `research_section` with its source, so it arrives at 6.5 with the pass
    // that writes it. An absence stated and an absence drawn as emptiness are
    // different things, and only the first is readable.
    public string MovesTable(string ticker, IReadOnlyList<MoveCell> moves, IReadOnlyList<ChartBar> year)
    {
        var table = new StringBuilder();

        table.Append(Invariant, $"<section class=\"how-it-got-here\" data-ticker=\"{Escaped(ticker)}\" data-moves=\"{moves.Count}\" ");
        table.Append(Invariant, $"data-picture-sessions=\"{year.Count}\">");

        // The twelve-month picture, which section 15.9 puts in this region above
        // the table of the biggest moves. It is the level chart mark given the
        // year and no bands and no averages, rather than a drawing of its own:
        // nothing on any screen is a one-off drawing, and what this region asks
        // is what the year did, not where the levels are. The chart region below
        // is the one about levels, and it draws the same mark with them.
        // see: Marks are defined once and every screen draws from that list
        //
        // It degrades the way every mark does, by saying what it has: a name
        // with fewer sessions than a chart needs gets the sentence stating the
        // count rather than a picture drawn through nothing.
        table.Append(Invariant, $"<figure class=\"twelve-months\" data-sessions=\"{year.Count}\">");
        table.Append(LevelChart(ticker, year, [], []));
        table.Append(Invariant, $"<figcaption>the twelve months to {(year.Count > 0 ? year[^1].SessionDate.ToString("yyyy-MM-dd", Invariant) : "no stored session")}</figcaption>");
        table.Append("</figure>");

        if (moves.Count == 0)
        {
            table.Append("<p class=\"degraded\" data-moves=\"none\">no moves are stored for this name yet</p></section>");

            return table.ToString();
        }

        table.Append(Invariant, $"<table class=\"moves-table\" data-rows=\"{moves.Count}\" data-cause-column=\"absent\">");
        table.Append("<tr><th>Session</th><th>Over</th><th>Change</th></tr>");

        foreach (var move in moves)
        {
            table.Append(Invariant, $"<tr data-session-date=\"{move.SessionDate:yyyy-MM-dd}\" data-sessions=\"{move.Sessions}\" ");
            table.Append(Invariant, $"data-change-pct=\"{Number(move.ChangePct)}\" data-rank=\"{move.Rank}\">");
            table.Append(Invariant, $"<td>{move.SessionDate:yyyy-MM-dd}</td>");
            table.Append(Invariant, $"<td>{(move.Sessions == 1 ? "one session" : $"{move.Sessions} sessions")}</td>");
            table.Append(Invariant, $"<td>{Number(move.ChangePct)}%</td>");
            table.Append("</tr>");
        }

        table.Append("</table>");
        table.Append("<p class=\"degraded\" data-cause=\"absent\">the cause of each move arrives with the research pass that writes it, and is not stored here</p>");
        table.Append("</section>");

        return table.ToString();
    }

    // The distance row, section 15.5's mark for a table cell.
    //
    // A name's close between its nearest support and its nearest resistance,
    // with the distances in typical days. It turns a column of numbers into a
    // shape, so a scan down five hundred rows shows which names are near an edge
    // without reading any of them
    // (see: Distances are stated as typical days' moves).
    //
    // The two hues are the ones support and resistance own everywhere else, and
    // nothing else on this mark uses them
    // (see: Support and resistance own two hues and nothing else uses them).
    //
    // A name with neither edge draws a rule and says so. An absence drawn as a
    // shape at one end is a shape a reader will read.
    public string DistanceRow(UniverseCell row)
    {
        const int Width = 120;
        const int Height = 18;
        const int Middle = Height / 2;

        // The scale is fixed across every row rather than fitted to each, which
        // is the whole point of a mark meant to be scanned down a column: a
        // shape that means one thing on one row and another on the next says
        // nothing about the column. Four typical days either side, clamped, so a
        // name far from everything sits at the edge rather than off it.
        const double Span = 4;

        double Offset(double? days, int direction) => days is { } value
            ? (Width / 2.0) + (direction * Math.Min(value, Span) / Span * (Width / 2.0))
            : Width / 2.0;

        var mark = new StringBuilder();

        // Appended fragment by fragment with the provider on each, rather than
        // concatenated and formatted once. Two interpolated strings joined with
        // a plus are formatted in the current culture before anything sees them,
        // which is the coercion the compiler refuses here and the one this
        // repository bans everywhere else.
        mark.Append(Invariant, $"<svg class=\"distance-row\" role=\"img\" viewBox=\"0 0 {Width} {Height}\" width=\"{Width}\" height=\"{Height}\" ");
        mark.Append(Invariant, $"data-ticker=\"{Escaped(row.Ticker)}\" ");
        mark.Append(Invariant, $"data-to-support=\"{Days(row.ToSupport)}\" data-to-resistance=\"{Days(row.ToResistance)}\" ");
        mark.Append(Invariant, $"data-nearest=\"{Days(row.Nearest)}\">");

        mark.Append(Formatted(
            $"<line x1=\"0\" y1=\"{Middle}\" x2=\"{Width}\" y2=\"{Middle}\" stroke=\"var(--rule, #d8d8d8)\" stroke-width=\"1\" />"));

        if (row.ToSupport is not null)
        {
            mark.Append(Invariant, $"<line class=\"support-edge\" x1=\"{Offset(row.ToSupport, -1):0.##}\" y1=\"2\" ");
            mark.Append(Invariant, $"x2=\"{Offset(row.ToSupport, -1):0.##}\" y2=\"{Height - 2}\" stroke=\"{SupportHue}\" stroke-width=\"2\" />");
        }

        if (row.ToResistance is not null)
        {
            mark.Append(Invariant, $"<line class=\"resistance-edge\" x1=\"{Offset(row.ToResistance, 1):0.##}\" y1=\"2\" ");
            mark.Append(Invariant, $"x2=\"{Offset(row.ToResistance, 1):0.##}\" y2=\"{Height - 2}\" stroke=\"{ResistanceHue}\" stroke-width=\"2\" />");
        }

        // The close, always drawn, because the mark is about where the price
        // sits between the two and a row with no marker is a row with no
        // subject.
        mark.Append(Formatted(
            $"<circle class=\"close\" cx=\"{Width / 2}\" cy=\"{Middle}\" r=\"2.5\" fill=\"var(--ink, #1c1c1c)\" />"));

        mark.Append(row.ToSupport is null && row.ToResistance is null
            ? Formatted($"<title>{Escaped(row.Ticker)}: no band on either side yet</title>")
            : Formatted($"<title>{Escaped(row.Ticker)}: {Reads(row.ToSupport, "support")}, {Reads(row.ToResistance, "resistance")}</title>"));

        mark.Append("</svg>");

        return mark.ToString();
    }

    static string Days(double? days) =>
        days is { } value ? value.ToString("0.##", CultureInfo.InvariantCulture) : "none";

    // A day's change as an attribute, and as the words a reader takes it from.
    // The sign is always drawn, because the sign is the only channel the
    // direction has: hue is spoken for by support and resistance, and a change
    // written without its sign is a magnitude.
    static string Change(double? change) =>
        change is { } value ? value.ToString("+0.00;-0.00;0.00", CultureInfo.InvariantCulture) : "none";

    static string ChangeReads(double? change) =>
        change is { } value
            ? Formatted($"{value.ToString("+0.00;-0.00;0.00", CultureInfo.InvariantCulture)}%")
            : "<span class=\"degraded\">no earlier close stored</span>";

    static string Reads(double? days, string side) =>
        days is { } value
            ? Formatted($"{value:0.#} typical days to {side}")
            : Formatted($"no {side} band");

    // The sector strip, section 15.8's first region.
    //
    // One line per sector with how many names it holds and how many are in an
    // uptrend. The count of names on tonight's list is the half this cannot draw
    // until 5.4 creates the store it would read, and it is absent rather than
    // shown as zero: a zero here would say nothing fired tonight.
    public string SectorStrip(IReadOnlyList<SectorLine> lines)
    {
        var strip = new StringBuilder();

        strip.Append(Formatted($"<section class=\"sector-strip\" data-sectors=\"{lines.Count}\">"));

        foreach (var line in lines)
        {
            strip.Append(Invariant, $"<div class=\"sector\" data-sector=\"{Escaped(line.Sector)}\" ");
            strip.Append(Invariant, $"data-names=\"{line.Names}\" data-uptrend=\"{line.InUptrend}\" data-listed=\"{line.OnTheList}\">");
            strip.Append(Invariant, $"{Escaped(line.Sector)}: {line.Names} name(s), {line.OnTheList} on the list, {line.InUptrend} in an uptrend</div>");
        }

        strip.Append("</section>");

        return strip.ToString();
    }

    // The universe table, section 15.8's second region.
    //
    // Every name in the index, in the order the projection put them, which is by
    // distance to the nearest level ascending. The columns the listings store
    // carries, being the evening a name was last on the list and the listing
    // strip, are absent rather than blank, and the table says so once rather
    // than in every row.
    public string UniverseTable(IReadOnlyList<UniverseCell> rows)
    {
        var table = new StringBuilder();

        table.Append(Formatted($"<table class=\"universe-table\" data-rows=\"{rows.Count}\">"));
        table.Append("<tr><th>Name</th><th>Sector</th><th>Close</th><th>Trend</th><th>Distance</th>");
        table.Append("<th>Sessions to earnings</th><th>Last on the list</th><th>Sixty evenings</th></tr>");

        foreach (var row in rows)
        {
            table.Append(Invariant, $"<tr data-ticker=\"{Escaped(row.Ticker)}\" data-sector=\"{Escaped(row.Sector)}\" ");
            table.Append(Invariant, $"data-trend-state=\"{Escaped(row.TrendState ?? NotClassified)}\">");

            table.Append(Formatted($"<td>{Escaped(row.Ticker)}</td>"));
            table.Append(Formatted($"<td>{Escaped(row.Sector)}</td>"));
            // The close, interpolated with the provider rather than converted
            // through the storage helper, which lives in the project that
            // stores things and is not one this project references. A name the
            // night computed nothing for says so rather than showing a zero.
            table.Append(Invariant, $"<td>{(row.Close is { } close ? close.ToString(Invariant) : "not computed")}</td>");
            table.Append(Formatted(
                $"<td>{Escaped((row.TrendState ?? NotClassified).Replace('_', ' '))}</td>"));
            table.Append(Formatted($"<td>{DistanceRow(row)}</td>"));

            // The sessions until the name's next dated event, which section 15.8
            // states as a column of this table. Two absences and they are stated
            // as two: a name the calendar holds nothing for has no event to
            // count to, and one whose event is past the end of the exchange
            // closure table has an event nobody can count the sessions to. A
            // column that drew both the same way would say the table had ended
            // in the voice of a name with no earnings date.
            // owes: The exchange closure table extended before the nights reach its end
            table.Append(Invariant, $"<td class=\"to-earnings\" data-sessions=\"{(row.SessionsUntilEarnings is { } until ? until.ToString(Invariant) : "none")}\" ");
            table.Append(Invariant, $"data-next-event=\"{(row.NextEvent is { } dated ? dated.ToString("yyyy-MM-dd", Invariant) : "none")}\">");
            table.Append(row.SessionsUntilEarnings is { } sessions
                ? Formatted($"{sessions}")
                : row.EventBeyondTheTable
                    ? "<span class=\"degraded\">past the end of the closure table</span>"
                    : "<span class=\"degraded\">no dated event</span>");
            table.Append("</td>");

            // The two right-hand columns count evenings a name appeared on the
            // list. They say nothing about index membership, which every name in
            // this table has by definition.
            table.Append(Invariant, $"<td data-last-listed=\"{(row.LastListed is { } listed ? listed.ToString("yyyy-MM-dd", Invariant) : "never")}\">");
            table.Append(Invariant, $"{(row.LastListed is { } shown ? shown.ToString("yyyy-MM-dd", Invariant) : "never")}</td>");
            table.Append(Formatted($"<td>{ListingStrip(row.Ticker, row.Evenings ?? [])}</td>"));
            table.Append("</tr>");
        }

        table.Append("</table>");

        return table.ToString();
    }

    // What a name with no ladder row is shown as. Its own value rather than an
    // empty cell, so it can be filtered for and counted.
    const string NotClassified = "not classified";

    // The filters, section 15.8's third region: trend state and sector as chips,
    // in the hash so a filtered view is a link.
    //
    // Drawn from the rows rather than from a list written here, so a state or a
    // sector the store holds and this file has never heard of still gets a chip.
    // The paging nav, section 15.8's "paged".
    //
    // The page is in the hash beside the filters, so a page of a filtered view
    // is a link, and each link carries the filters it was drawn under rather
    // than dropping them: a next-page link that cleared the chips would take a
    // reader from a filtered page 1 to an unfiltered page 2 and look like paging.
    //
    // It states which page of how many over how many rows. A nav that drew only
    // arrows says nothing about where a reader is or how much is left, which on
    // five hundred rows is the only question paging raises.
    public string UniversePaging(int rows, int page, int pageSize, string? trend, string? sector)
    {
        var pages = Math.Max(1, (rows + pageSize - 1) / pageSize);
        var at = Math.Clamp(page, 1, pages);

        var nav = new StringBuilder();

        nav.Append(Invariant, $"<nav class=\"universe-paging\" data-page=\"{at}\" data-pages=\"{pages}\" ");
        nav.Append(Invariant, $"data-rows=\"{rows}\" data-page-size=\"{pageSize}\">");

        string Link(int to, string label, string rel) =>
            Formatted($"<a class=\"page\" rel=\"{rel}\" data-page=\"{to}\" href=\"{Query(to, trend, sector)}\">{Escaped(label)}</a>");

        nav.Append(at > 1
            ? Link(at - 1, "previous", "prev")
            : "<span class=\"page degraded\" data-page=\"none\" rel=\"prev\">previous</span>");

        nav.Append(Invariant, $"<span class=\"page-of\">page {at} of {pages}, {rows} name(s)</span>");

        nav.Append(at < pages
            ? Link(at + 1, "next", "next")
            : "<span class=\"page degraded\" data-page=\"none\" rel=\"next\">next</span>");

        nav.Append("</nav>");

        return nav.ToString();
    }

    // The universe route's hash, with the filters it was drawn under kept.
    static string Query(int page, string? trend, string? sector)
    {
        var parts = new List<string> { Formatted($"page={page}") };

        if (trend is { Length: > 0 })
        {
            parts.Add(Formatted($"trend={Uri.EscapeDataString(trend)}"));
        }

        if (sector is { Length: > 0 })
        {
            parts.Add(Formatted($"sector={Uri.EscapeDataString(sector)}"));
        }

        return "#/universe?" + string.Join("&amp;", parts);
    }

    public string UniverseFilters(IReadOnlyList<UniverseCell> rows)
    {
        var states = rows
            .Select(row => row.TrendState ?? NotClassified)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(state => state, StringComparer.Ordinal)
            .ToArray();

        var sectors = rows
            .Select(row => row.Sector)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(sector => sector, StringComparer.Ordinal)
            .ToArray();

        var filters = new StringBuilder();

        filters.Append(Invariant, $"<nav class=\"universe-filters\" data-states=\"{states.Length}\" data-sector-chips=\"{sectors.Length}\">");

        foreach (var state in states)
        {
            filters.Append(Invariant, $"<a class=\"chip\" data-filter=\"trend\" data-value=\"{Escaped(state)}\" ");
            filters.Append(Invariant, $"href=\"#/universe?trend={Uri.EscapeDataString(state)}\">{Escaped(state.Replace('_', ' '))}</a>");
        }

        foreach (var sector in sectors)
        {
            filters.Append(Invariant, $"<a class=\"chip\" data-filter=\"sector\" data-value=\"{Escaped(sector)}\" ");
            filters.Append(Invariant, $"href=\"#/universe?sector={Uri.EscapeDataString(sector)}\">{Escaped(sector)}</a>");
        }

        filters.Append("</nav>");

        return filters.ToString();
    }

    public string Degraded(string ticker, int bars) =>
        $"<p class=\"degraded\" data-ticker=\"{Escaped(ticker)}\" data-sessions=\"{bars}\">" +
        $"{Escaped(ticker)} has {bars} stored session{(bars == 1 ? string.Empty : "s")}, " +
        $"and a chart needs at least {FewestBars}.</p>";

    static string Escaped(string text) =>
        text.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;").Replace("\"", "&quot;");
}
