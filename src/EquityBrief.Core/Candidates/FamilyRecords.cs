using System.Globalization;
using System.Text.Json;
using EquityBrief.Core.Bars;
using EquityBrief.Core.Returns;

namespace EquityBrief.Core.Candidates;

// One trade a registered family rule kept, as its record reads it: the night it was listed, and once it ended
// its result in multiples of its risk and the benchmark of the same plan on every member that night.
public sealed record FamilyTradeRow(string Candidate, DateOnly Session, DateOnly? EndedOn, double? Result, double? Benchmark);

// A replay of a registered family rule at a change of its code, as its row on the run log states it: the rule, the
// code version it was replayed under, the session its record counted from when it ran, whether every trade it
// would have kept is the one its record stored, what differed first where one was not, and when it ran.
public sealed record FamilyReplayRow(string Candidate, string Version, DateOnly From, bool Reproduced, string Said, DateTimeOffset At);

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
    bool Crossed,
    string? Restarted = null);

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

    // A replay's rows on the run log: the stage a rule's row is written under, the words and its name, read back by
    // the words, and the two outcomes a rule's row takes.
    public const string ReplayStage = "family-replay";

    public static string ReplayStageOf(string candidate) => ReplayStage + ": " + candidate;

    public const string ReplayStages = ReplayStage + ": %";

    public const string ReplayReproduced = "reproduced";

    public const string ReplayDiffers = "differs";

    // A replay's row read back off the run log, or none where its detail is not a replay's.
    public static FamilyReplayRow? ReplayOf(string outcome, string? detail, DateTimeOffset at)
    {
        if (detail is null)
        {
            return null;
        }

        try
        {
            using var read = JsonDocument.Parse(detail);
            var root = read.RootElement;

            return new FamilyReplayRow(
                root.GetProperty("candidate").GetString()!,
                root.GetProperty("version").GetString()!,
                DateOnly.ParseExact(root.GetProperty("from").GetString()!, "yyyy-MM-dd", CultureInfo.InvariantCulture),
                outcome == ReplayReproduced,
                root.GetProperty("said").GetString() ?? string.Empty,
                at);
        }
        catch (Exception unread) when (unread is JsonException or KeyNotFoundException or FormatException or InvalidOperationException)
        {
            return null;
        }
    }

    // A replay's row detail: the rule, the version it was replayed under, the session it replayed from, how many
    // nights it read, how many trades the record stored and the replay kept, and what it found.
    public static string ReplayDetail(string candidate, string version, DateOnly from, int nights, int stored, int replayed, string said) =>
        JsonSerializer.Serialize(new
        {
            candidate,
            version,
            from = from.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            nights,
            stored,
            replayed,
            said,
        });

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

    // Where a standing rule's record counts from, and why it restarted where it did: from the session a replay
    // under the version it stands at found it counting from, where that replay reproduced every trade; from the
    // registration it stands by, with what the replay found, where that replay did not; and from the registration
    // it stands by where no replay was run under its version, which a rule registered once is.
    // see: A family rule registered again keeps its record from its first registration where a replay of its stored nights reproduces every trade, and restarts at the change otherwise
    public static (DateOnly First, string? Restarted) StartOf(RegisterRow standing, IReadOnlyList<FamilyReplayRow> replays) =>
        Replayed(standing, replays) switch
        {
            { Reproduced: true } carried => (carried.From, null),
            { } restarted => (FirstSession(standing.RegisteredAt), restarted.Said),
            null => (FirstSession(standing.RegisteredAt), null),
        };

    // The session a replay of a standing rule replays from: where its record counts from now, or its first
    // registration's where the registration it stands by is not its first and no replay was run under its
    // version, since a change made before any replay is replayed with the next one.
    public static DateOnly ReplayFrom(IReadOnlyList<RegisterRow> register, RegisterRow standing, IReadOnlyList<FamilyReplayRow> replays)
    {
        if (Replayed(standing, replays) is not null)
        {
            return StartOf(standing, replays).First;
        }

        var first = register
            .Where(row => row.Event == CandidateFamily.Registered && string.Equals(row.Candidate, standing.Candidate, StringComparison.Ordinal))
            .MinBy(row => (row.RegisteredAt, row.Id));

        return FirstSession(first?.RegisteredAt ?? standing.RegisteredAt);
    }

    // The newest replay of a rule under the version it stands at.
    static FamilyReplayRow? Replayed(RegisterRow standing, IReadOnlyList<FamilyReplayRow> replays) =>
        replays
            .Where(replay => string.Equals(replay.Candidate, standing.Candidate, StringComparison.Ordinal) && string.Equals(replay.Version, standing.EvaluatorVersion, StringComparison.Ordinal))
            .MaxBy(replay => replay.At);

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
        int trials,
        IReadOnlyList<FamilyReplayRow>? replays = null)
    {
        var starts = rules.ToDictionary(rule => rule.Candidate, rule => StartOf(rule, replays ?? []), StringComparer.Ordinal);
        var firsts = starts.ToDictionary(start => start.Key, start => start.Value.First, StringComparer.Ordinal);
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
                    levels[rule.Candidate].Crossed,
                    starts[rule.Candidate].Restarted);
            }),
        ];
    }

    static bool Decided(FamilyTradeRow trade) => trade.Result is not null && trade.Benchmark is not null;
}
