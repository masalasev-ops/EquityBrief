using System.Globalization;
using EquityBrief.Core.Ledger;

namespace EquityBrief.Core.Loop;

// One condition a rule also requires: a reading of the ledger's catalogue at or above a level, or at or under it.
public sealed record AlsoRequires(int Reading, bool Above, double Level)
{
    public string Column => LedgerReadings.All[Reading].Column;

    public bool Holds(IReadOnlyList<double?> readings) =>
        readings[Reading] is { } value && (Above ? value >= Level : value <= Level);
}

// The settings every engine's proposal turns, read off a rule's own registered parameters and all off where none is
// stated: the exit of the menu its trades are walked under, nought for its own; the conditions a member passing the
// rule's own gates must also meet; and a score, a sum of the catalogue's readings each times its weight, its list
// ordered by it, highest first, and a member scoring under its floor left off. A rule stating none of them is walked,
// listed and ordered exactly as before they existed.
// see: Every engine's settings hooks land together and all default off, so the families' pins move once
public sealed record RuleHooks(int Exit, IReadOnlyList<AlsoRequires> Also, IReadOnlyDictionary<int, double> Score, double? ScoreFloor)
{
    public const string ExitParameter = "exit";

    public const string AlsoPrefix = "also_";

    public const string AboveSuffix = "_above";

    public const string BelowSuffix = "_below";

    public const string ScorePrefix = "score_";

    public const string ScoreFloorParameter = "score_floor";

    public static RuleHooks Off { get; } = new(0, [], new Dictionary<int, double>(), null);

    // The files a hooked rule's list and trades are read through, which every family rule's version pins beside its own.
    public static IReadOnlyList<string> Sources { get; } = ["src/EquityBrief.Core/Loop/RuleHooks.cs", "src/EquityBrief.Core/Loop/ExitMenu.cs"];

    public bool IsOff => Exit == 0 && Also.Count == 0 && Score.Count == 0 && ScoreFloor is null;

    // Whether a member's readings are needed: a condition or a score.
    public bool ReadsReadings => Also.Count > 0 || Score.Count > 0;

    public ExitChoice? ExitChoice => ExitMenu.Of(Exit);

    // Whether a parameter's name is one of the hooks', its reading named in the catalogue where it names one.
    public static bool IsHook(string name) =>
        name == ExitParameter
        || name == ScoreFloorParameter
        || (name.StartsWith(ScorePrefix, StringComparison.Ordinal) && Column(name[ScorePrefix.Length..]) >= 0)
        || (name.StartsWith(AlsoPrefix, StringComparison.Ordinal) && name.EndsWith(AboveSuffix, StringComparison.Ordinal) && Column(name[AlsoPrefix.Length..^AboveSuffix.Length]) >= 0)
        || (name.StartsWith(AlsoPrefix, StringComparison.Ordinal) && name.EndsWith(BelowSuffix, StringComparison.Ordinal) && Column(name[AlsoPrefix.Length..^BelowSuffix.Length]) >= 0);

    // The hooks a rule's parameters state, the rest of its parameters left alone; an exit the menu does not hold is
    // refused rather than read as the rule's own.
    public static RuleHooks Of(IReadOnlyDictionary<string, double> parameters)
    {
        var exit = 0;
        var also = new List<AlsoRequires>();
        var score = new Dictionary<int, double>();
        double? floor = null;

        foreach (var (name, value) in parameters.OrderBy(pair => pair.Key, StringComparer.Ordinal))
        {
            if (name == ExitParameter)
            {
                exit = (int)Math.Round(value);

                if (exit != 0 && ExitMenu.Of(exit) is null)
                {
                    throw new ArgumentException(FormattableString.Invariant($"The exit hook names {value}, and the menu numbers its exits 1 to {ExitMenu.Swing.Count}, 0 being the rule's own."), nameof(parameters));
                }
            }
            else if (name == ScoreFloorParameter)
            {
                floor = value;
            }
            else if (name.StartsWith(ScorePrefix, StringComparison.Ordinal) && Column(name[ScorePrefix.Length..]) is var weighted and >= 0)
            {
                score[weighted] = value;
            }
            else if (name.StartsWith(AlsoPrefix, StringComparison.Ordinal) && name.EndsWith(AboveSuffix, StringComparison.Ordinal) && Column(name[AlsoPrefix.Length..^AboveSuffix.Length]) is var above and >= 0)
            {
                also.Add(new AlsoRequires(above, true, value));
            }
            else if (name.StartsWith(AlsoPrefix, StringComparison.Ordinal) && name.EndsWith(BelowSuffix, StringComparison.Ordinal) && Column(name[AlsoPrefix.Length..^BelowSuffix.Length]) is var below and >= 0)
            {
                also.Add(new AlsoRequires(below, false, value));
            }
        }

        return new RuleHooks(exit, also, score, floor);
    }

    // The parameters a rule states without its hooks, which its own evaluation reads.
    public static IReadOnlyDictionary<string, double> Without(IReadOnlyDictionary<string, double> parameters) =>
        parameters.Where(pair => !IsHook(pair.Key)).ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);

    // Whether a member's readings meet every condition; a reading the member does not hold meets none.
    public bool Passes(IReadOnlyList<double?> readings) => Also.All(condition => condition.Holds(readings));

    // A member's score, none where a reading it weighs is not held.
    public double? ScoreOf(IReadOnlyList<double?> readings)
    {
        var sum = 0.0;

        foreach (var (reading, weight) in Score)
        {
            if (readings[reading] is not { } value)
            {
                return null;
            }

            sum += weight * value;
        }

        return sum;
    }

    // A rule's candidates for tonight in its own order, kept where they meet the conditions and, where it scores, kept
    // where their score holds and is not under the floor and ordered by it, highest first, its own order breaking a tie;
    // a rule with no condition and no score keeps its own list as it stands.
    public IReadOnlyList<T> Order<T>(IReadOnlyList<T> own, Func<T, IReadOnlyList<double?>?> readings)
    {
        if (!ReadsReadings)
        {
            return own;
        }

        var kept = own
            .Select((candidate, place) => (Candidate: candidate, Place: place, Readings: readings(candidate)))
            .Where(one => one.Readings is not null && Passes(one.Readings))
            .ToList();

        if (Score.Count == 0)
        {
            return [.. kept.Select(one => one.Candidate)];
        }

        return
        [
            .. kept
                .Select(one => (one.Candidate, one.Place, Score: ScoreOf(one.Readings!)))
                .Where(one => one.Score is { } value && (ScoreFloor is not { } least || value >= least))
                .OrderByDescending(one => one.Score)
                .ThenBy(one => one.Place)
                .Select(one => one.Candidate),
        ];
    }

    // The words of the hooks a rule states, none where it states none.
    public string? Words()
    {
        if (IsOff)
        {
            return null;
        }

        var parts = new List<string>();

        if (ExitChoice is { } exit)
        {
            parts.Add("exit: " + exit.Words);
        }

        parts.AddRange(Also.Select(condition => FormattableString.Invariant($"also requires {condition.Column} {(condition.Above ? "at or above" : "at or under")} {condition.Level.ToString("0.####", CultureInfo.InvariantCulture)}")));

        if (Score.Count > 0)
        {
            parts.Add("ordered by a score over " + string.Join(", ", Score.Keys.Order().Select(reading => LedgerReadings.All[reading].Column))
                + (ScoreFloor is { } least ? FormattableString.Invariant($", a score under {least.ToString("0.####", CultureInfo.InvariantCulture)} left off") : string.Empty));
        }

        return string.Join("; ", parts);
    }

    static int Column(string column)
    {
        for (var at = 0; at < LedgerReadings.All.Count; at++)
        {
            if (string.Equals(LedgerReadings.All[at].Column, column, StringComparison.Ordinal))
            {
                return at;
            }
        }

        return -1;
    }
}
