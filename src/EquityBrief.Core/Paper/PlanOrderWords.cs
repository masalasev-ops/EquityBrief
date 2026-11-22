using System.Globalization;
using EquityBrief.Core.Bars;
using EquityBrief.Core.Loop;

namespace EquityBrief.Core.Paper;

// The words a live rule's order under a plan buying at a price is drawn in while it waits for its price, with that price
// and the last session it waits, and once its window passed unfilled; one place, so every page and the paper accounts say
// the same and the counts read them. In the operator's words: an order buys if the price falls to a set level, and is
// cancelled if not filled by its last day.
// see: A live rule's trade under a plan is drawn on every page as its plan made it, and its waiting orders are cards of their own nearest their buy price first
public static class PlanOrderWords
{
    public const string WaitingOpening = "buys if it falls to";

    public const string NotFilled = "not filled, no trade";

    // The group the counts name the orders from earlier nights still waiting in.
    public const string StillWaiting = "waiting to buy";

    // Past picks' count headings for the three groups.
    public const string NewTonightHeading = "New tonight, waiting to buy";

    public const string StillWaitingHeading = "From earlier nights, waiting to buy";

    public const string NotFilledHeading = "Not filled, no trade";

    static string Day(DateOnly day) => day.ToString("d MMM yyyy", CultureInfo.InvariantCulture);

    public static string Waiting(decimal? price, DateOnly lastDay) =>
        price is { } at
            ? FormattableString.Invariant($"{WaitingOpening} ${at.ToString("0.00", CultureInfo.InvariantCulture)}; cancelled if not filled by {Day(lastDay)}")
            : FormattableString.Invariant($"{WaitingOpening} a level its plan could not place tonight, so it will not fill; cancelled by {Day(lastDay)}");

    public static bool IsWaiting(string? why) => why is not null && why.StartsWith(WaitingOpening, StringComparison.Ordinal);

    // An order its levels placed no price for is not placed, and says so in the operator's words.
    public const string NoPrice = "No price to buy at from its levels tonight, so no order was placed.";

    public static string NotFilledOf(PlanOrder order) => order.PlacesNoPrice() ? NoPrice : NotFilled;

    public static bool IsNotFilled(string? why) => string.Equals(why, NotFilled, StringComparison.Ordinal) || string.Equals(why, NoPrice, StringComparison.Ordinal);

    // A count's words for the orders waiting, tonight's new and earlier nights' each named as its group, each led by a comma
    // and none where none waits.
    public static string Counts(int newTonight, int stillWaiting) =>
        (newTonight > 0 ? FormattableString.Invariant($", {newTonight} new tonight {StillWaiting}") : string.Empty)
        + (stillWaiting > 0 ? FormattableString.Invariant($", {stillWaiting} from earlier nights {StillWaiting}") : string.Empty);

    // The group's heading, in the operator's words.
    public static string Heading(int orders) =>
        FormattableString.Invariant($"Waiting to buy: {orders} {(orders == 1 ? "order that buys" : "orders that buy")} only if the price falls to a set level. If it does not get there by the last day, nothing is bought.");

    // How far tonight's close stands above an order's price, over the close, none where either is not read or the close is none.
    public static decimal? Gap(decimal? price, decimal? close) =>
        price is { } at && close is { } last && last > 0 ? (last - at) / last : null;

    // The last session an order waits: the window's sessions on the exchange calendar after the night that listed it.
    public static DateOnly LastDay(DateOnly listed, int window)
    {
        var day = listed;

        for (var counted = 0; counted < window;)
        {
            day = day.AddDays(1);

            if (ExchangeClosures.IsSession(day))
            {
                counted++;
            }
        }

        return day;
    }

    // The sessions an order still waits after a night, its last day among them.
    public static int SessionsLeft(DateOnly night, DateOnly lastDay)
    {
        var left = 0;

        for (var day = night.AddDays(1); day <= lastDay; day = day.AddDays(1))
        {
            if (ExchangeClosures.IsSession(day))
            {
                left++;
            }
        }

        return left;
    }
}
