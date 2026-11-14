namespace EquityBrief.Worker.Sweep;

// On the operator's ruling of 2026-11-14 on the books, item 3, a book's half exit: half a holding's shares sell at the first close
// at or above its take-profit, the stop on the other half then moving up to the buy's close, and the other half sells by the
// book's own exit or a close under the buy. Returns are fractions of the sum a span opens on, as a book's holdings read them.
// see: Every pick takes profit at its own sale's price, a price it turned down at before or a multiple of its risk
public static class BookHalf
{
    // What a close does to a holding with its stop and take-profit placed: under a half exit, its first close at or above the
    // take-profit with no half sold yet sells half and moves the stop up to the buy with no take-profit left; otherwise a close
    // under the stop or at or above the take-profit ends it, as a full exit does.
    public static (string? Ended, (double Stop, double? TakeProfit) Placed, bool SoldHalf) Step(double close, double buy, (double Stop, double? TakeProfit) placed, bool half, bool halfSold)
    {
        if (half && !halfSold && placed.TakeProfit is { } aim && close >= aim && close >= placed.Stop)
        {
            return (null, (buy, null), true);
        }

        return (Core.Loop.TakeProfit.EndOf(close, placed.Stop, placed.TakeProfit), placed, false);
    }

    // A holding's result from its buy's close, half sold at one close and half at another.
    public static double Result(double buy, double half, double sold) => ((0.5 * half) + (0.5 * sold)) / buy - 1.0;

    // A holding's return over a span as a fraction of the sum the span opens on: from its base (the buy's fill on the session
    // it was bought, the span's first close after), over the sum, to the close it ends at; where half sold within the span at a
    // close, that half counts at its own close. Half sold before the span has left the book as a holding sold whole leaves it,
    // its cash counted in no month, so the span reads the half still held alone.
    public static double Over(double basePrice, double sum, double to, double? half, bool halfBefore) =>
        half is not { } sold || halfBefore ? (to - basePrice) / sum
        : ((0.5 * sold) + (0.5 * to) - basePrice) / sum;

    // A holding's return over a span of the tape, from the open after its buy where the span opens on the buy; none where a close
    // or the fill is not read.
    public static double? Return(HeavyweightTape tape, int name, int entry, int from, int to, int half, Func<int, int, double?> fillOver)
    {
        var close = tape.Close[name];

        if (to < from || !(close[from] > 0) || double.IsNaN(close[to]))
        {
            return null;
        }

        var basePrice = close[from];

        if (from == entry)
        {
            if (fillOver(name, from) is not { } fill)
            {
                return null;
            }

            basePrice = fill * close[from];
        }

        return half <= to && !double.IsNaN(close[half]) ? Over(basePrice, close[from], close[to], close[half], half <= from) : Over(basePrice, close[from], close[to], null, false);
    }
}
