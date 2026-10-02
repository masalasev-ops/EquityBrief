using EquityBrief.Core.Bars;
using EquityBrief.Core.Returns;

namespace EquityBrief.Core.Candidates;

// One trade a registered family rule kept, as its record reads it: the night it was listed, and once it ended
// its result in multiples of its risk and the benchmark of the same plan on every member that night.
public sealed record FamilyTradeRow(string Candidate, DateOnly Session, DateOnly? EndedOn, double? Result, double? Benchmark);

// A registered family rule's record as the run page draws it: whether it is the family's live rule, the session
// its record counts from, its trades, those ended with a result and a benchmark and the edge over them, the
// whole blocks of 63 sessions holding a trade, the look those reach and the next one, the p-value its last look
// read and the level it was read at, and whether a look crossed.
public sealed record FamilyRecordView(
    string Candidate,
    bool Live,
    DateOnly First,
    int Trades,
    int Decided,
    double? Edge,
    int Blocks,
    int NextLook,
    int LooksTaken,
    double? PValue,
    double Level,
    bool Crossed);

// A registered family rule's record: its trades' edge, the result less the benchmark, summed over blocks of 63
// sessions from the first session on or after the day it registered, a block whole once its last session and
// the family's cap after it have passed, and read at the looks the register's candidates are read at, by the
// sign-flip test over its whole blocks holding a trade, at the level the family's own Holm graph gives it.
// see: A registered family rule is evaluated every night at its own settings and keeps its own list, its trades stored with their benchmark when they end
// see: Each setup family's correction for luck counts its own rules alone, at most nine a family
public static class FamilyRecords
{
    // The words a family's live rule is named with, ahead of the rest of its name.
    public const string LivePrefix = "the live ";

    public static bool IsLive(string candidate) => candidate.StartsWith(LivePrefix, StringComparison.Ordinal);

    // The first session a rule's record counts from: the first exchange session on or after the day it
    // registered, the night it was first evaluated on.
    public static DateOnly FirstSession(DateTimeOffset registeredAt)
    {
        var day = DateOnly.FromDateTime(registeredAt.UtcDateTime);

        while (!ExchangeClosures.IsSession(day))
        {
            day = day.AddDays(1);
        }

        return day;
    }

    // The sums of the whole blocks holding a trade ended with a result and a benchmark, in order, each the
    // edges of those trades.
    public static IReadOnlyList<double> BlockSums(IReadOnlyList<FamilyTradeRow> trades, DateOnly first, DateOnly asOf, int cap) =>
    [
        .. trades
            .Where(trade => trade.Session >= first && Decided(trade))
            .GroupBy(trade => Blocks.Of(first, trade.Session))
            .Where(block => Blocks.Complete(first, block.Key, asOf, cap))
            .OrderBy(block => block.Key)
            .Select(block => block.Sum(trade => trade.Result!.Value - trade.Benchmark!.Value)),
    ];

    // Each registered rule of one family read over its own trades, the level each look is read at the one the
    // family's graph gives it over the family's own distinct trials.
    public static IReadOnlyList<FamilyRecordView> Family(
        IReadOnlyList<RegisterRow> rules,
        IReadOnlyList<FamilyTradeRow> trades,
        DateOnly asOf,
        int cap,
        int trials)
    {
        var firsts = rules.ToDictionary(rule => rule.Candidate, rule => FirstSession(rule.RegisteredAt), StringComparer.Ordinal);
        var own = rules.ToDictionary(
            rule => rule.Candidate,
            rule => (IReadOnlyList<FamilyTradeRow>)[.. trades.Where(trade => string.Equals(trade.Candidate, rule.Candidate, StringComparison.Ordinal))],
            StringComparer.Ordinal);
        var sums = rules.ToDictionary(rule => rule.Candidate, rule => BlockSums(own[rule.Candidate], firsts[rule.Candidate], asOf, cap), StringComparer.Ordinal);

        bool CrossesAt(IReadOnlyList<double> blocks, double level) =>
            Looks.Taken(blocks.Count) is var taken and > 0
            && Looks.CrossedAt([.. blocks.Take(Looks.At[taken - 1])], look => Looks.Spent(level, Looks.Fraction(look))) is not null;

        var levels = HolmGraph.Levels(
                [.. rules.Select(rule => new GraphMember(rule.Candidate, false, false, level => CrossesAt(sums[rule.Candidate], level)))],
                ReasonVerdict.Significance,
                Math.Max(1, trials))
            .ToDictionary(level => level.Candidate, StringComparer.Ordinal);

        return
        [
            .. rules.Select(rule =>
            {
                var mine = own[rule.Candidate];
                var decided = mine.Where(Decided).ToArray();
                var blocks = sums[rule.Candidate];
                var taken = Looks.Taken(blocks.Count);

                return new FamilyRecordView(
                    rule.Candidate,
                    IsLive(rule.Candidate),
                    firsts[rule.Candidate],
                    mine.Count,
                    decided.Length,
                    decided.Length > 0 ? decided.Average(trade => trade.Result!.Value - trade.Benchmark!.Value) : null,
                    blocks.Count,
                    taken < Looks.At.Count ? Looks.At[taken] : Looks.Maximum,
                    taken,
                    taken > 0 ? SignFlip.PValue([.. blocks.Take(Looks.At[taken - 1])]) : null,
                    levels[rule.Candidate].Level,
                    levels[rule.Candidate].Crossed);
            }),
        ];
    }

    static bool Decided(FamilyTradeRow trade) => trade.Result is not null && trade.Benchmark is not null;
}
