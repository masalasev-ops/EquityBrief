using EquityBrief.Core.Prices;

namespace EquityBrief.Core.Judge;

// One way the judge may hold a stock: its words as the model chooses them, the sessions it is held at most, and the
// typical daily moves its stop sits under the buy.
public sealed record JudgeHold(string Words, int Sessions, double StopMoves);

// What one of the judge's picks sells at, worked from the price it buys at: the stop, the take-profit for the whole or
// the half, and where the shape holds the other half, the distance its give-up price keeps behind the highest close.
public sealed record JudgePrices(decimal Buy, decimal Stop, decimal TakeProfit, decimal? GiveUpDistance);

// The menu the operator approved with the judge's mockup on 2026-11-19: how long to hold, a week, two, four or three
// months, the stop set by code at 1, 1.5, 2 or 3 typical daily moves under the buy by the holding period, and the take
// profit the whole at 2 times the risk or half there with the rest following a give-up price that rises behind the
// highest close, its stop at the buy until then. The model chooses a hold and a shape and nothing else; code works every
// price from the price the stock buys at and its typical daily move, from the last close until it is bought.
// see: Every pick of any rule or account states the price it buys at with its stop and its take-profit
public static class JudgeMenu
{
    public const string Whole = "the whole at 2 times the risk";

    public const string HalfThenGiveUp = "half at 2 times the risk, the rest on a rising give-up price";

    public const decimal TargetRisks = 2m;

    public static readonly IReadOnlyList<JudgeHold> Holds =
    [
        new("1 week", 5, 1),
        new("2 weeks", 10, 1.5),
        new("4 weeks", 20, 2),
        new("3 months", 63, 3),
    ];

    public static readonly IReadOnlyList<string> Shapes = [Whole, HalfThenGiveUp];

    public static JudgeHold? HoldOf(string? words) =>
        Holds.FirstOrDefault(hold => string.Equals(hold.Words, words, StringComparison.Ordinal));

    public static bool IsShape(string? words) => Shapes.Contains(words ?? string.Empty, StringComparer.Ordinal);

    // The prices of a plan from the price it buys at, none where the hold or the shape is not on the menu, so a pick with
    // no take-profit is never stored, or where no typical move is held to place the stop by.
    public static JudgePrices? Prices(decimal buy, double? typicalMove, string? hold, string? shape)
    {
        if (HoldOf(hold) is not { } held || !IsShape(shape) || typicalMove is not > 0 || buy <= 0)
        {
            return null;
        }

        var risk = Statistic.ToPrice(typicalMove.Value * held.StopMoves);

        if (risk <= 0 || risk >= buy)
        {
            return null;
        }

        return new JudgePrices(
            buy,
            buy - risk,
            buy + TargetRisks * risk,
            string.Equals(shape, HalfThenGiveUp, StringComparison.Ordinal) ? risk : null);
    }
}
