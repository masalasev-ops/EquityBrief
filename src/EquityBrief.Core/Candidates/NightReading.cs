using EquityBrief.Core.Shortlist;

namespace EquityBrief.Core.Candidates;

// The arithmetic behind a night's values that is more than reading a column.
//
// Here rather than in the night's stage for the reason the reason outcomes are:
// a stage reads a store and writes a row, and what it computes on the way is a
// thing constructed input can be put to with no store at all.
public static class NightReading
{
    // How many of the name's bands the close crossed since the previous session,
    // and how far past the crossed edge the furthest crossing finished.
    //
    // A crossing is read against the whole band and not one of its edges: a rise
    // that finished inside a band has reached the level and not gone through it,
    // so the edge a rise is measured from is the high one and the edge a fall is
    // measured from is the low one. The furthest rather than the nearest, because
    // the condition asks whether the close cleared any band by the margin, and
    // the nearest crossing would answer about a different band each night.
    public static (int Count, decimal? Past) Crossings(
        decimal close,
        decimal previousClose,
        IReadOnlyList<Band> bands)
    {
        var count = 0;
        decimal? past = null;

        foreach (var band in bands)
        {
            decimal? distance = previousClose < band.HighEdge && close >= band.HighEdge
                ? close - band.HighEdge
                : previousClose > band.LowEdge && close <= band.LowEdge
                    ? band.LowEdge - close
                    : null;

            if (distance is not { } cleared)
            {
                continue;
            }

            count++;

            if (past is null || cleared > past)
            {
                past = cleared;
            }
        }

        return (count, past);
    }

    // Where a volume sits inside its own window, 1 being the largest.
    //
    // Ties share the better rank, so two identical sessions are both second
    // rather than second and third, which is what keeps the rank a statement
    // about the volume and not about the order the rows arrived in.
    public static int RankOf(long volume, IReadOnlyList<long> window) =>
        1 + window.Count(other => other > volume);
}
