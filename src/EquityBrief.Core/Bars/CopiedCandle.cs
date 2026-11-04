namespace EquityBrief.Core.Bars;

// A candle that holds no trading: no volume, and its open, high, low and close each the close of the session before. The
// provider has served such a row for a session it held no prices for, which on 2026-07-21, 07-22 and 07-31 left six
// members' stored years with a day the exchange traded and they did not.
// see: A member holding a copied candle or behind the night is asked for its year again, and stays suspect until an answer clears it
public static class CopiedCandle
{
    public static bool Is(long volume, decimal open, decimal high, decimal low, decimal close, decimal previousClose) =>
        volume == 0 && open == previousClose && high == previousClose && low == previousClose && close == previousClose;
}
