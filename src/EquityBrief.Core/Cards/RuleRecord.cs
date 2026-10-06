using System.Globalization;
using System.Text.Json;

namespace EquityBrief.Core.Cards;

// One trade a rule's replay kept: its result after its cost, in multiples of its risk or in per cent of its buy for a rule
// with no stop; the sessions it was held, from the session after its buy to the one it ended on; the lowest close it was
// held through against its buy, in the same unit and nothing where it never closed under its buy; and whether it ended at
// its target, none for a rule that sets no target.
public sealed record RecordTrade(double Result, int Sessions, double WorstClose, bool? Won);

// A rule's record over the trades its replay kept: how many, the share that ended at its target, none for a rule setting
// no target, the mean result, the median sessions held, how many trades had ended by each session held, and the median of
// the lowest close each was held through.
// see: A rule's record is replayed at its one setting by the sweep's own code over the pulled history, after costs on every index
public sealed record RuleRecordFigures(int Trades, double? Won, double? Average, int? MedianSessions, IReadOnlyList<int> EndedBy, double? WorstClose)
{
    public static RuleRecordFigures Of(IReadOnlyList<RecordTrade> trades)
    {
        if (trades.Count == 0)
        {
            return new RuleRecordFigures(0, null, null, null, [], null);
        }

        var targets = trades.Where(trade => trade.Won is not null).ToArray();
        var longest = trades.Max(trade => trade.Sessions);
        var ended = new int[longest];

        foreach (var trade in trades)
        {
            ended[Math.Max(1, trade.Sessions) - 1]++;
        }

        return new RuleRecordFigures(
            trades.Count,
            targets.Length == trades.Count ? 1.0 * targets.Count(trade => trade.Won == true) / trades.Count : null,
            trades.Average(trade => trade.Result),
            Median([.. trades.Select(trade => trade.Sessions)]),
            ended,
            Median([.. trades.Select(trade => trade.WorstClose)]));
    }

    // The sessions by which a share of the trades had ended: the fewest sessions held such that at least that share of the
    // trades ended in them, none for a record of no trades.
    public static int? HeldBy(IReadOnlyList<int> endedBy, double share)
    {
        var trades = endedBy.Sum();

        if (trades == 0)
        {
            return null;
        }

        var reached = 0;

        for (var session = 0; session < endedBy.Count; session++)
        {
            reached += endedBy[session];

            if (reached >= share * trades)
            {
                return session + 1;
            }
        }

        return endedBy.Count;
    }

    // The trades ended by each session held, as the store holds them.
    public static string EndedByJson(IReadOnlyList<int> endedBy) => JsonSerializer.Serialize(endedBy);

    public static IReadOnlyList<int> EndedByFrom(string json) => JsonSerializer.Deserialize<int[]>(json) ?? [];

    static int? Median(int[] values)
    {
        if (values.Length == 0)
        {
            return null;
        }

        Array.Sort(values);

        return values[(values.Length - 1) / 2];
    }

    static double? Median(double[] values)
    {
        if (values.Length == 0)
        {
            return null;
        }

        Array.Sort(values);

        var middle = values.Length / 2;

        return values.Length % 2 == 1 ? values[middle] : (values[middle - 1] + values[middle]) / 2;
    }

    // A figure as a stored settings key states it.
    public static string Number(double value) => value.ToString("R", CultureInfo.InvariantCulture);
}
