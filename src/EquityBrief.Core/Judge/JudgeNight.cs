using System.Globalization;
using EquityBrief.Core.Providers;

namespace EquityBrief.Core.Judge;

// Where a picker version stands in its index: the version that picks there, the one it most recently replaced, or a version
// waiting on paper for its case.
public enum JudgeStanding
{
    Running = 1,
    Replaced = 2,
    Paper = 3,
}

// One picker version a night may run: its index, where it stands there, and the order it was made in.
public sealed record JudgeSlot(string Index, JudgeStanding Standing, int Made);

// From 24.24 the picker's night, run once for each index on that index's members alone. Each index's running version runs
// first, then the one it most recently replaced, then the rest in the order they were made; a version past its index's
// running one starts only while the night can hold it, before the off-peak run's first slot, the next scheduled run that
// needs the model or the store, and one that cannot is written "not run" for the night, never filled in later. "A fixed 5"
// is each version's variant: it takes the first 5 its version's second round ranked, where its version takes the best
// quarter of the marked, and asks nothing of its own.
// see: The AI stock picker picks within each index, the best quarter of the stocks it marks and at most 5
// see: A running picker version trailing its index's fund and random picks is marked failing, and its index's paper versions are bounded by the survivors' rules
public static class JudgeNight
{
    // The indices in the order a night runs their versions of one standing.
    public static IReadOnlyList<string> Indices { get; } = ["GSPC", FundHoldings.MidCapIndex, FundHoldings.SmallCapIndex];

    public const string NotRun = "not run";

    // The off-peak run's first slot, in UTC, on the morning after a night: the most the night can hold its paper versions to.
    public const int HoldsUntilHourUtc = 4;

    public const int HoldsUntilMinuteUtc = 5;

    public const string BestQuarter = "the best quarter";

    public const string FixedFive = "a fixed 5";

    // Each picker version's two ways of taking its picks, the version's own first.
    public static IReadOnlyList<string> Variants { get; } = [BestQuarter, FixedFive];

    // The night's versions in the order they run.
    public static IReadOnlyList<JudgeSlot> Order(IReadOnlyList<JudgeSlot> slots) =>
    [
        .. slots
            .OrderBy(slot => slot.Standing)
            .ThenBy(slot => slot.Standing == JudgeStanding.Paper ? slot.Made : 0)
            .ThenBy(slot => Place(slot.Index))
            .ThenBy(slot => slot.Made),
    ];

    // The night's versions where none waits on paper yet: each index's running version.
    public static IReadOnlyList<JudgeSlot> RunningOnly { get; } = [.. Indices.Select(index => new JudgeSlot(index, JudgeStanding.Running, 0))];

    // The instant a night's paper versions must have started by: the off-peak run's first slot on the morning after it.
    public static DateTimeOffset HoldsUntil(DateOnly session) =>
        new(session.AddDays(1).ToDateTime(new TimeOnly(HoldsUntilHourUtc, HoldsUntilMinuteUtc)), TimeSpan.Zero);

    // Whether a version may start at an instant: a running version always, until the next open bounds its run; any other
    // only while the night can hold it.
    public static bool MayStart(JudgeSlot slot, DateTimeOffset now, DateTimeOffset holdsUntil) =>
        slot.Standing == JudgeStanding.Running || now < holdsUntil;

    // The picks a variant takes of its version's ranked list: the best quarter of the marked, or a fixed 5, each in rank order.
    public static IReadOnlyList<T> Taken<T>(IReadOnlyList<T> ranked, int marked, string variant) =>
        [.. ranked.Take(string.Equals(variant, FixedFive, StringComparison.Ordinal) ? JudgePrompt.Ranked(marked) : JudgePrompt.Quarter(marked))];

    // What the card says of a night's marks: how many of the index's stocks it rated worth buying and how many it picked.
    public static string MarkedWords(string indexName, int marked, int picked) =>
        marked == 0
            ? $"It rated none of the {indexName}'s stocks worth buying tonight."
            : string.Create(CultureInfo.InvariantCulture, $"It rated {marked} of the {indexName}'s stocks worth buying tonight and picked the best quarter: {picked}.");

    static int Place(string index) => Indices.ToList().IndexOf(index) is var at && at >= 0 ? at : Indices.Count;
}
