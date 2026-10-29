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
// rule's own gates must also meet; and a score, a sum of the catalogue's readings each times its weight or from 17.15 a
// stored tree model named by its key, its list ordered by it, highest first, and a member scoring under its floor left
// off. From 21.4 three more forms: the exposure, which in the market check's place reads the index's breadth against
// the floor the hook states and the VIX's close against the median of its closes over the 252 sessions before, and lets
// the rule list five picks where both hold, two where one holds and none where neither; the ranking, under which the
// rule's candidates are its family's setups at the ledger's loose gates on the night, ordered by the index's newest
// learned score or by the expected value of the setups like each, the best five taken; and an order by that expected
// value over the rule's own candidates, highest first, a candidate with no figure after every one with one. A rule
// stating none of them is walked, listed and ordered exactly as before they existed. A tree model is found by the reader
// that holds the stored models, and one not found scores no member, so the rule lists none; the ranking's score and the
// expected value are handed in by the step that lists, since neither is a registered parameter, and a ranking handed
// no score lists none.
// see: Every engine's settings hooks land together and all default off, so the families' pins move once
// see: The learned score's challenger is a tree ensemble fitted by FastTree and scored by code from its stored trees, put only where it passes and earns more than the ridge score
public sealed record RuleHooks(
    int Exit,
    IReadOnlyList<AlsoRequires> Also,
    IReadOnlyDictionary<int, double> Score,
    double? ScoreFloor,
    long? Trees = null,
    TreeModel? Model = null,
    double? Exposure = null,
    int Ranking = 0,
    bool ByExpected = false,
    IScoreModel? Ranker = null)
{
    public const string ExitParameter = "exit";

    public const string AlsoPrefix = "also_";

    public const string AboveSuffix = "_above";

    public const string BelowSuffix = "_below";

    public const string ScorePrefix = "score_";

    public const string ScoreFloorParameter = "score_floor";

    public const string TreesParameter = "score_trees";

    // From 21.4: the exposure, stated as the breadth floor it reads, over 0 and at most 1; the ranking, 1 by the index's
    // newest learned score and 2 by the expected value of the setups like each candidate; and the expected-value order, 1
    // where it is on.
    public const string ExposureParameter = "exposure";

    public const string RankingParameter = "ranking";

    public const string ExpectedOrderParameter = "order_expected";

    public const int RankingByScore = 1;

    public const int RankingByExpected = 2;

    // The picks a night the exposure allows where both of its switches hold, and where one does.
    public const int ExposureFull = Families.SetupFamilies.ListedANight;

    public const int ExposureHalf = 2;

    // The best candidates a ranking takes.
    public const int RankingPicks = Families.SetupFamilies.ListedANight;

    // The VIX's close over its median, at or under which the exposure's second switch holds.
    public const double VixAtItsMedian = 1;

    // The catalogue's places of the index's breadth and of the VIX's close over its median, which the exposure reads.
    public static int BreadthColumn { get; } = Column("breadth");

    public static int VixOverMedianColumn { get; } = Column("vix_over_median");

    public static RuleHooks Off { get; } = new(0, [], new Dictionary<int, double>(), null);

    // The files a hooked rule's list and trades are read through, which every family rule's version pins beside its own.
    public static IReadOnlyList<string> Sources { get; } = ["src/EquityBrief.Core/Loop/RuleHooks.cs", "src/EquityBrief.Core/Loop/ExitMenu.cs", "src/EquityBrief.Core/Loop/TreeScore.cs"];

    public bool IsOff => Exit == 0 && Also.Count == 0 && Score.Count == 0 && ScoreFloor is null && Trees is null && Exposure is null && Ranking == 0 && !ByExpected;

    // Whether a member's readings are needed: a condition, a score, the exposure's two switches or a ranking by the
    // learned score.
    public bool ReadsReadings => Also.Count > 0 || Score.Count > 0 || Trees is not null || Exposure is not null || Ranking == RankingByScore;

    // Whether the expected value of the setups like each candidate is needed: the order by it, or a ranking by it.
    public bool ReadsExpected => ByExpected || Ranking == RankingByExpected;

    // Whether the rule's candidates are its family's setups at the ledger's loose gates rather than its own list.
    public bool Ranks => Ranking != 0;

    // Whether the hooks stated are the ones a book's rule reads: its conditions and its score, and none of the exit, the
    // exposure, the ranking or the expected-value order, which pick a night's trades.
    public bool AreABooks => Exit == 0 && Exposure is null && Ranking == 0 && !ByExpected;

    public const string NotABooks = "a book reads conditions on the catalogue's readings and an order by a score alone";

    public ExitChoice? ExitChoice => ExitMenu.Of(Exit);

    // Whether a parameter's name is one of the hooks', its reading named in the catalogue where it names one.
    public static bool IsHook(string name) =>
        name == ExitParameter
        || name == ScoreFloorParameter
        || name == TreesParameter
        || name == ExposureParameter
        || name == RankingParameter
        || name == ExpectedOrderParameter
        || (name.StartsWith(ScorePrefix, StringComparison.Ordinal) && Column(name[ScorePrefix.Length..]) >= 0)
        || (name.StartsWith(AlsoPrefix, StringComparison.Ordinal) && name.EndsWith(AboveSuffix, StringComparison.Ordinal) && Column(name[AlsoPrefix.Length..^AboveSuffix.Length]) >= 0)
        || (name.StartsWith(AlsoPrefix, StringComparison.Ordinal) && name.EndsWith(BelowSuffix, StringComparison.Ordinal) && Column(name[AlsoPrefix.Length..^BelowSuffix.Length]) >= 0);

    // Whether a parameter's name states an order: a score's weight, floor or trees, a ranking or the expected-value
    // order. A change stating one replaces the whole order the rule stands at.
    public static bool IsOrder(string name) =>
        name == ScoreFloorParameter
        || name == RankingParameter
        || name == ExpectedOrderParameter
        || name.StartsWith(ScorePrefix, StringComparison.Ordinal);

    // The hooks a rule's parameters state, the rest of its parameters left alone; an exit the menu does not hold, an
    // exposure floor outside 0 to 1, a ranking the two forms do not name, an expected-value order other than 1 and two
    // orders stated together are each refused rather than read as the rule's own.
    public static RuleHooks Of(IReadOnlyDictionary<string, double> parameters)
    {
        var exit = 0;
        var also = new List<AlsoRequires>();
        var score = new Dictionary<int, double>();
        double? floor = null;
        long? trees = null;
        double? exposure = null;
        var ranking = 0;
        var byExpected = false;

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
            else if (name == TreesParameter)
            {
                trees = (long)value;
            }
            else if (name == ExposureParameter)
            {
                if (!(value > 0) || value > 1)
                {
                    throw new ArgumentException(FormattableString.Invariant($"The exposure hook states {value}, and it states the breadth floor it reads, over 0 and at most 1; a rule reading no exposure states none."), nameof(parameters));
                }

                exposure = value;
            }
            else if (name == RankingParameter)
            {
                ranking = (int)Math.Round(value);

                if (ranking != 0 && ranking != RankingByScore && ranking != RankingByExpected)
                {
                    throw new ArgumentException(FormattableString.Invariant($"The ranking hook states {value}, and it states {RankingByScore} for the index's newest learned score or {RankingByExpected} for the expected value of the setups like each candidate, 0 being no ranking."), nameof(parameters));
                }
            }
            else if (name == ExpectedOrderParameter)
            {
                if (value != 0 && value != 1)
                {
                    throw new ArgumentException(FormattableString.Invariant($"The expected-value order states {value}, and it states 1 where it is on and 0 where it is not."), nameof(parameters));
                }

                byExpected = value == 1;
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

        var orders = (score.Count > 0 || trees is not null || floor is not null ? 1 : 0) + (ranking != 0 ? 1 : 0) + (byExpected ? 1 : 0);

        if (orders > 1)
        {
            throw new ArgumentException("A rule states one order: a score over the catalogue's readings or the learned trees, a ranking, or the expected-value order.", nameof(parameters));
        }

        return new RuleHooks(exit, also, score, floor, trees, null, exposure, ranking, byExpected);
    }

    // The hooks with the tree model they name found by the reader given, none where they name none.
    public RuleHooks With(Func<long, TreeModel?>? models) =>
        Trees is { } key && models?.Invoke(key) is { } model ? this with { Model = model } : this;

    // The hooks with the score a ranking by the learned score orders by, the index's newest fitted on all finished data
    // on the night and the fold's own in the tester, handed in by the step that lists; a ranking by anything else, and
    // one handed none, is left as it is.
    public RuleHooks WithRanker(IScoreModel? model) =>
        Ranking == RankingByScore && model is not null ? this with { Ranker = model } : this;

    // The picks a night the exposure allows on a session, read off the session's readings, which every member of the
    // index shares: five where the breadth is at or above the floor and the VIX's close at or under its median, two
    // where one holds and none where neither or where the night holds no readings; none at all where the rule states no
    // exposure, its list capped as its family caps it.
    public int? PicksUnder(IReadOnlyList<double?>? readings) =>
        Exposure is not { } floor
            ? null
            : ((BreadthHolds(readings, floor) ? 1 : 0) + (VixHolds(readings) ? 1 : 0)) switch
            {
                2 => ExposureFull,
                1 => ExposureHalf,
                _ => 0,
            };

    public static bool BreadthHolds(IReadOnlyList<double?>? readings, double floor) =>
        BreadthColumn >= 0 && readings is not null && readings[BreadthColumn] is { } breadth && breadth >= floor;

    public static bool VixHolds(IReadOnlyList<double?>? readings) =>
        VixOverMedianColumn >= 0 && readings is not null && readings[VixOverMedianColumn] is { } over && over <= VixAtItsMedian;

    // The parameters a rule states without its hooks, which its own evaluation reads.
    public static IReadOnlyDictionary<string, double> Without(IReadOnlyDictionary<string, double> parameters) =>
        parameters.Where(pair => !IsHook(pair.Key)).ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);

    // Whether a member's readings meet every condition; a reading the member does not hold meets none.
    public bool Passes(IReadOnlyList<double?> readings) => Also.All(condition => condition.Holds(readings));

    // A member's score, none where a reading it weighs is not held or the tree model it names was not found.
    public double? ScoreOf(IReadOnlyList<double?> readings)
    {
        if (Trees is not null)
        {
            return Model?.Score(readings);
        }

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
    // a rule with no condition and no score keeps its own list as it stands. From 21.4: under a ranking the candidates
    // handed in are the family's setups at the ledger's loose gates, each scored by the ranker or by its expected value,
    // one with neither left off, and the best five taken; under the expected-value order a candidate with a figure
    // comes before every one without, by the figure and then by the rule's own order; and under the exposure the list
    // is cut to the picks the night's switches allow, read off the first kept candidate's readings, which every member
    // of the index shares on a session. The expected value is handed in by the step that lists, none where it has none.
    public IReadOnlyList<T> Order<T>(IReadOnlyList<T> own, Func<T, IReadOnlyList<double?>?> readings, Func<T, double?>? expected = null)
    {
        if (!ReadsReadings && !ReadsExpected)
        {
            return own;
        }

        var rows = own
            .Select((candidate, place) => (Candidate: candidate, Place: place, Readings: ReadsReadings ? readings(candidate) : null))
            .Where(one => !ReadsReadings || (one.Readings is not null && Passes(one.Readings)))
            .ToList();

        IEnumerable<T> ordered;

        if (Ranks)
        {
            ordered = rows
                .Select(one => (one.Candidate, one.Place, Rank: Ranking == RankingByScore ? Ranker?.Score(one.Readings!) : expected?.Invoke(one.Candidate)))
                .Where(one => one.Rank is not null)
                .OrderByDescending(one => one.Rank)
                .ThenBy(one => one.Place)
                .Take(RankingPicks)
                .Select(one => one.Candidate);
        }
        else if (Score.Count > 0 || Trees is not null)
        {
            ordered = rows
                .Select(one => (one.Candidate, one.Place, Score: ScoreOf(one.Readings!)))
                .Where(one => one.Score is { } value && (ScoreFloor is not { } least || value >= least))
                .OrderByDescending(one => one.Score)
                .ThenBy(one => one.Place)
                .Select(one => one.Candidate);
        }
        else if (ByExpected)
        {
            ordered = rows
                .Select(one => (one.Candidate, one.Place, Expected: expected?.Invoke(one.Candidate)))
                .OrderByDescending(one => one.Expected is not null)
                .ThenByDescending(one => one.Expected ?? 0)
                .ThenBy(one => one.Place)
                .Select(one => one.Candidate);
        }
        else
        {
            ordered = rows.Select(one => one.Candidate);
        }

        return PicksUnder(rows.Count > 0 ? rows[0].Readings : null) is { } picks ? [.. ordered.Take(picks)] : [.. ordered];
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

        var floor = ScoreFloor is { } least ? FormattableString.Invariant($", a score under {least.ToString("0.####", CultureInfo.InvariantCulture)} left off") : string.Empty;

        if (Trees is { } key)
        {
            parts.Add(FormattableString.Invariant($"ordered by the learned trees {key:x12}") + floor);
        }
        else if (Score.Count > 0)
        {
            parts.Add("ordered by a score over " + string.Join(", ", Score.Keys.Order().Select(reading => LedgerReadings.All[reading].Column)) + floor);
        }
        else if (Ranking == RankingByScore)
        {
            parts.Add(FormattableString.Invariant($"ranked over the family's setups at the ledger's loose gates by the index's newest learned score, the best {RankingPicks}"));
        }
        else if (Ranking == RankingByExpected)
        {
            parts.Add(FormattableString.Invariant($"ranked over the family's setups at the ledger's loose gates by the expected value of the setups like each, the best {RankingPicks}"));
        }
        else if (ByExpected)
        {
            parts.Add("ordered by the expected value of the setups like each, highest first");
        }

        if (Exposure is { } breadth)
        {
            parts.Add(FormattableString.Invariant($"exposure: {ExposureFull} picks where the index's breadth is at or above {breadth.ToString("0.####", CultureInfo.InvariantCulture)} and the VIX at or under its median of the {LedgerReadings.VixMedianSessions} sessions before, {ExposureHalf} where one holds and none where neither, in the market check's place"));
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
