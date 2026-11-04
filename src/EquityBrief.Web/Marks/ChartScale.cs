using System.Globalization;

namespace EquityBrief.Web.Marks;

// The chart page's vertical scale and the prices its gridlines are labelled at: the arithmetic of where a price sits, kept
// apart so it can be worked by hand.
// see: The chart page's scale fits the candles, the plan's lines and the levels shown, and an average never widens it
public static class ChartScale
{
    // The room left above and below what the scale fits, as a share of what it spans, so the highest wick and a plan's line
    // do not sit on the pane's edge.
    public const double Margin = 0.03;

    // The most lines a chart labels; the step is the smallest round one that keeps within it.
    public const int MostLines = 7;

    static readonly double[] RoundSteps = [1, 2, 2.5, 5, 10];

    public static PriceAxis Fit(IReadOnlyCollection<double> prices)
    {
        var (low, high) = (prices.Min(), prices.Max());
        var span = high - low;
        var room = span > 0 ? span * Margin : (Math.Abs(high) > 0 ? Math.Abs(high) * Margin : 1);

        return new PriceAxis(low - room, high + room);
    }

    // The smallest of 1, 2, 2.5 and 5 times a power of ten that labels no more than the most lines over the span.
    public static double Step(double low, double high)
    {
        var span = high - low;

        if (span <= 0)
        {
            return 1;
        }

        var power = Math.Pow(10, Math.Floor(Math.Log10(span)) - 2);

        foreach (var decade in new[] { 1d, 10d, 100d, 1000d })
        {
            foreach (var round in RoundSteps)
            {
                var step = round * power * decade;

                if (Lines(low, high, step).Count <= MostLines)
                {
                    return step;
                }
            }
        }

        return span;
    }

    // Each whole multiple of the step inside the scale.
    public static IReadOnlyList<double> Lines(double low, double high, double step)
    {
        var lines = new List<double>();

        for (var at = Math.Ceiling(low / step); at * step <= high; at++)
        {
            lines.Add(at * step);
        }

        return lines;
    }

    // A line's price with the places its step needs and no more.
    public static string Label(double price, double step)
    {
        var places = step >= 1 && Math.Abs(step - Math.Round(step)) < 1e-9 ? 0 : step >= 0.1 && Math.Abs((step * 10) - Math.Round(step * 10)) < 1e-9 ? 1 : 2;

        return price.ToString("N" + places.ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture);
    }
}
