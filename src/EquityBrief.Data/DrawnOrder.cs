using System.Text.Json;
using EquityBrief.Core.Ladders;
using EquityBrief.Core.Prices;

namespace EquityBrief.Data;

// The order tonight's list is drawn in, and the reward to risk it breaks a tie in fired count
// by, held where both the read surface and the worker reach them: the surface draws the list in
// this order, and the night asks for a report on the first name it draws. Two statements of one
// order would come to disagree about which name is first.
// see: Tonight's list breaks a tie in fired count by the plan's reward to risk, and a row with none is drawn after every row with one and says why
public static class DrawnOrder
{
    // How many reasons fired, then the plan's reward to risk with a row holding none after every
    // row holding one, then the ticker in code-point order, so the order is total and two reads of
    // one night cannot disagree.
    public static IReadOnlyList<T> Ordered<T>(
        IEnumerable<T> rows,
        Func<T, int> fired,
        Func<T, decimal?> rewardToRisk,
        Func<T, string> ticker) =>
    [
        .. rows
            .OrderByDescending(fired)
            .ThenBy(row => rewardToRisk(row) is null)
            .ThenByDescending(rewardToRisk)
            .ThenBy(ticker, StringComparer.Ordinal),
    ];

    // The reward to risk the night's plan computes from its first tranche, read off the plan the
    // listing kept that night, or the arithmetic's own words for why it computes none, which are
    // none where the arithmetic states none.
    //
    // The listing keeps the first tranche and the first traded exit, which are all the ratio from
    // the first tranche reads, so it is worked by the ladder's own arithmetic rather than by a
    // second statement of it. Rounded as the ladder stores it, so the figure the list draws is the
    // figure the night's plan holds.
    // see: Code owns every number
    public static (decimal? RewardToRisk, string? Absent) FirstTranche(string planAtListing)
    {
        using var document = JsonDocument.Parse(planAtListing);
        var root = document.RootElement;

        // A plan that is not an object carries no tranche, which is what the arithmetic says of it.
        decimal? Price(string name) =>
            root.ValueKind == JsonValueKind.Object && root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
                ? Money.FromStorage(value.GetString()!)
                : null;

        var (low, high, stop, target) = (Price("entryLow"), Price("entryHigh"), Price("stop"), Price("firstTradedTarget"));

        var plan = new Ladder(
            low is { } entryLow && high is { } entryHigh ? [new Tranche(entryLow, entryHigh, TrancheCondition.ReachesTheZone, stop)] : [],
            target is { } exit ? [new Exit(exit, exit, Traded: true, Trailing: false, Fraction: string.Empty, Reason: null)] : [],
            null,
            null,
            []);

        var arithmetic = LadderSeries.ArithmeticFor(plan);

        return arithmetic.FirstRewardToRisk is { } ratio
            ? (Math.Round(ratio, PriceForm.Places, MidpointRounding.AwayFromZero), null)
            : (null, arithmetic.Absent);
    }
}
