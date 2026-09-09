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
// It touches no store and computes no figure, which is what its eleven blank
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
    // pane where it reads as absent rather than as low. The profile does not
    // widen it. A profile band lies inside the window's own high and low by
    // construction, and a scale that stretched to fit a mark would make the two
    // pictures disagree about where a price is, which is the whole thing this
    // method exists to prevent.
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

    public string LevelChart(
        string ticker,
        IReadOnlyList<ChartBar> bars,
        IReadOnlyList<ChartAverage>? averages = null)
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

        svg.Append(Invariant, $"<desc>Daily candles {drawn}over a volume pane on a shared time axis. The level bands are not drawn yet.</desc>");

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
    public string Degraded(string ticker, int bars) =>
        $"<p class=\"degraded\" data-ticker=\"{Escaped(ticker)}\" data-sessions=\"{bars}\">" +
        $"{Escaped(ticker)} has {bars} stored session{(bars == 1 ? string.Empty : "s")}, " +
        $"and a chart needs at least {FewestBars}.</p>";

    static string Escaped(string text) =>
        text.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;").Replace("\"", "&quot;");
}
