using EquityBrief.Core.Prices;

namespace EquityBrief.Core.Cards;

// A pick's reward to risk worked from the plan its night stored, where its night stored no ratio of its own: how far the
// target stands above the entry against how far the stop stands beneath it, rounded to a stored price's places as a
// family's trade gate rounds the ratio it stores; none where the stop is not beneath the entry or the target not above it.
// see: A screen reads and renders, and each figure it works out at a price or from stored figures has one function in the core
public static class PlanRatio
{
    public static decimal? Of(decimal entry, decimal stop, decimal target) =>
        entry > stop && target > entry
            ? Math.Round((target - entry) / (entry - stop), PriceForm.Places, MidpointRounding.AwayFromZero)
            : null;
}
