using EquityBrief.Core.Prices;

namespace EquityBrief.Api.Reading;

// How far a price sits from a band edge, in typical days' moves.
//
// One place, because tonight's list and a name's own page both state it and two
// implementations of one measure drift apart without anything noticing. The universe
// screen stated it first and the name page's level table reads the same measure.
// see: Distances are stated as typical days' moves
public static class Distances
{
    // The gap to a band edge, in typical days' moves, as a distance rather than
    // a direction: what a screen says is how far a name is from an edge, and
    // a name three days below its resistance and one above its support is near
    // the support.
    //
    // A typical move of zero or less gives no distance rather than an infinite
    // one. That is a name whose chart has not moved over the window the average
    // is taken across, and dividing by it would put it at the top of the screen
    // for having been still.
    public static double? InTypicalDays(decimal? close, decimal? edge, double? typicalMove) =>
        close is { } price && edge is { } band && typicalMove is > 0
            ? Statistic.FromPrice(Math.Abs(price - band)) / typicalMove.Value
            : null;
}
