

namespace EquityBrief.Core.Prices;

// The one way a price becomes a statistic.
//
// It lived in `EquityBrief.Data` until the phase 5 sign-off, which put it out of
// reach of `EquityBrief.Core`: Data references Core and not the other way round,
// so the two Core components that cross this boundary could not call it and cast
// inline instead. A helper the rule requires and half the tree cannot see is a
// helper that gets bypassed, so it sits beside `PriceForm` now, which is the
// other half of the same boundary and was always in Core.
//
// CLAUDE.md's rule is that prices are decimal in code and TEXT in storage, that
// statistics are double, and that there is no implicit conversion between the
// two worlds: a helper that crosses the boundary does so explicitly and is named
// for it. Money is the storage crossing. This is the other one, and it had no
// helper until an indicator needed it.
//
// The name says what is being given up. A decimal carries more significant
// digits than a double, so the crossing loses precision, and it is acceptable
// here for the reason the rule permits statistics to be double at all: an
// average, a ratio and a standard deviation are approximations of a population
// and their last digits are noise. A price's last digits are not.
//
// It was one way until 3.4. The comment here said a statistic never becomes a
// price again, which was true of everything that existed at 3.1 and stopped
// being true when the level builder had to put a moving average on the chart as
// a band edge. The rule in CLAUDE.md is that no conversion is implicit and that
// a helper crossing the boundary is named for it, in either direction, and the
// restriction to one direction was this file's own and not the rule's.
public static class Statistic
{
    // The store's price precision, and the reason the crossing back is safe:
    // every price in this store is written at four places or fewer, so a
    // statistic rounded to four places has lost nothing a price could have
    // carried. Read off PriceForm rather than restated, because a precision
    // written twice is two places one fact lives.
    public const int Places = PriceForm.Places;

    public static double FromPrice(decimal price) => (double)price;

    public static double FromVolume(long shares) => shares;

    // A ratio of two prices becoming a statistic.
    //
    // Named apart from `FromPrice` because what arrives is no longer money: a
    // percentage change is one price divided by another, so the units have
    // cancelled and nothing about it is a price any more. `FromPrice` would
    // accept it and would be the wrong name on the call, which matters here more
    // than it usually does, since the rule this file exists for is about telling
    // the two worlds apart rather than about the cast itself.
    //
    // The division is done in decimal by the caller and the crossing happens
    // once, at the end. Dividing after the crossing would be two approximations
    // where one will do.
    public static double FromRatio(decimal ratio) => (double)ratio;

    // A statistic that is itself a price, becoming one.
    //
    // Only for a statistic in the units of a price: a moving average of closes,
    // an average true range, a band edge. Never for a ratio, an index or a
    // count, and never as a way to quote an arbitrary double as money. An RSI of
    // 53 handed to this would produce 53 and mean nothing.
    //
    // Rounded to the store's own precision rather than cast, which is the whole
    // safety argument. A double holding an average may render as
    // 287.33999999999997, and casting it to decimal preserves that; a price
    // column holds four places, so the rounding is what makes the crossing
    // lossless in the only direction that matters. The last digits of an average
    // are noise, which is why the rule lets it be a double at all.
    public static decimal ToPrice(double statistic) => PriceForm.Round((decimal)statistic, Places);
}
