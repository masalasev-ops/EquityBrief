namespace EquityBrief.Core.Bars;

// A refetch's answer held to the sessions the store already holds, before it replaces them: a day's move over a quarter in the
// answer that the stored closes over the same two sessions miss by over 25 points is not this stock's year, as a ticker the
// provider has passed to another listing serves. A move the store shows too stands, as does a split the store has not applied,
// where the answer is the smooth side, and a stored session with no volume is no witness, since it may be a copied candle.
// see: A refetch answer whose day's move the stored closes do not show is refused, and the stock keeps its year and is asked again
public static class RefetchAnswer
{
    public const decimal MoveLimit = 0.25m;
    public const decimal ApartLimit = 0.25m;

    public sealed record Disagreement(DateOnly Before, DateOnly Session, decimal AnswerMove, decimal StoredMove);

    public static Disagreement? Refused(
        IReadOnlyDictionary<DateOnly, (decimal Close, long Volume)> stored,
        IReadOnlyList<(DateOnly Session, decimal Close)> answer)
    {
        var ordered = answer.OrderBy(session => session.Session).ToArray();

        for (var at = 1; at < ordered.Length; at++)
        {
            var (before, session) = (ordered[at - 1], ordered[at]);

            if (before.Close <= 0
                || !stored.TryGetValue(before.Session, out var heldBefore) || heldBefore.Close <= 0 || heldBefore.Volume == 0
                || !stored.TryGetValue(session.Session, out var held) || held.Volume == 0)
            {
                continue;
            }

            var answerMove = (session.Close / before.Close) - 1;
            var storedMove = (held.Close / heldBefore.Close) - 1;

            if (Math.Abs(answerMove) > MoveLimit && Math.Abs(answerMove - storedMove) > ApartLimit)
            {
                return new Disagreement(before.Session, session.Session, answerMove, storedMove);
            }
        }

        return null;
    }
}
