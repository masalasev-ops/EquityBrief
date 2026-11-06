namespace EquityBrief.Core.Loop;

// One unit of a rule's walk as an index's per-rule test kept it: the session it was entered on, or a book's month opened
// on, that session's place counted from the test span's first session, the rule's figure and the figure of what it was
// read against, each none where that side held nothing there.
public readonly record struct PooledUnit(DateOnly Session, int Place, double? Proposed, double? Current);

// One rule as an index's per-rule test judged it, read by the pooled step: its index, family and name, whether it is the
// family's live or provisional rule on the index, whether its unit is a book's month, the sessions a block of its waits,
// the sessions from the test span's first to the history's newest, that newest session, the year its verdict was judged
// from where it reads a release reading, its units over the test years, and for a variant the kind of change it makes.
public sealed record PooledRule(
    string Index,
    string Family,
    string Rule,
    bool Head,
    bool Book,
    int Cap,
    int Span,
    DateOnly Through,
    int? JudgedFrom,
    IReadOnlyList<PooledUnit> Units,
    string? Kind = null);

// One verdict across the indices: the family, the kind it reads, its family's own rules or a kind of change, the indices
// and the rules it summed, the gate's and the screens' verdict, the year it was judged from, and for a pass the one index
// that carried it, none where the others together stand above nothing.
public sealed record PooledVerdict(
    string Family,
    string Kind,
    IReadOnlyList<string> Indices,
    IReadOnlyList<string> Rules,
    LoopVerdict Verdict,
    int? JudgedFrom,
    string? CarriedBy)
{
    public bool Passed => Verdict.Passes;
}

// The one verdict across the three indices. Each family's live or provisional rules on the indices, each keeping its own
// settings, are summed into blocks of 63 sessions on the one exchange calendar before the sign flip, every block counted
// from the test span's first session as each index's own test counts it, and read by the same gate at the same bar; a kind
// of change registered on two or more indices is summed the same way beside it, and a family's kinds are read together by
// the step-down. The count, the stability and the trimmed screens read the summed units, a book's months counted once a
// calendar month. A rule reading a release reading is judged from its cutoff's year alone, and an index whose first test
// year falls later adds nothing to the blocks before it. A pass one index carried says so, and that changes no verdict.
// see: One verdict across the three indices stands beside each index's own, and an approval still changes one index's rule
// see: A proposal passes the tester on a block sign-flip test of its total edge after costs against the current rule, corrected within a run and held to a fixed bar across runs
public static class PooledJudge
{
    // The kind a family's own live or provisional rules are read as.
    public const string Own = "its rules";

    // A kind of change is summed across the indices where it is registered on at least this many.
    public const int FewestIndices = 2;

    // Which way a variant moves a setting from its live rule.
    public const string Raised = "raised";

    public const string Lowered = "lowered";

    public const string Off = "off";

    public const string On = "on";

    // Every family's verdicts in the order its rules were given: its own rules first where any stands, then its kinds
    // together.
    public static IReadOnlyList<PooledVerdict> Judge(IReadOnlyList<PooledRule> rules)
    {
        var verdicts = new List<PooledVerdict>();

        foreach (var family in rules.GroupBy(rule => rule.Family, StringComparer.Ordinal))
        {
            var heads = OnePerIndex(family.Where(rule => rule.Head));

            if (heads.Count > 0)
            {
                verdicts.AddRange(Read(family.Key, [(Own, heads)], heads.Max(rule => rule.Cap)));
            }

            (string Kind, IReadOnlyList<PooledRule> Rules)[] kinds =
            [
                .. family
                    .Where(rule => !rule.Head && rule.Kind is not null)
                    .GroupBy(rule => rule.Kind!, StringComparer.Ordinal)
                    .Select(kind => (kind.Key, OnePerIndex(kind)))
                    .Where(kind => kind.Item2.Count >= FewestIndices),
            ];

            if (kinds.Length > 0)
            {
                verdicts.AddRange(Read(family.Key, kinds, kinds.SelectMany(kind => kind.Rules).Concat(heads).Max(rule => rule.Cap)));
            }
        }

        return verdicts;
    }

    // One rule an index, the first given.
    static IReadOnlyList<PooledRule> OnePerIndex(IEnumerable<PooledRule> rules) =>
        [.. rules.GroupBy(rule => rule.Index, StringComparer.Ordinal).Select(index => index.First())];

    // Sets of rules read together by the step-down, each summed across its indices; their blocks are read once the cap's
    // sessions have traded after a block's last on the index whose history ends soonest.
    static IEnumerable<PooledVerdict> Read(string family, IReadOnlyList<(string Kind, IReadOnlyList<PooledRule> Rules)> sets, int cap)
    {
        var all = sets.SelectMany(set => set.Rules).ToArray();
        var span = all.MinBy(rule => rule.Through)!.Span;
        var evidences = sets.Select(set => Evidence(set.Rules)).ToArray();
        var judged = LoopJudge.Judge([.. evidences.Select(one => one.Evidence)], 0, span, cap);

        return sets.Select((set, at) => new PooledVerdict(
            family,
            set.Kind,
            [.. set.Rules.Select(rule => rule.Index)],
            [.. set.Rules.Select(rule => rule.Rule)],
            judged[at],
            evidences[at].From,
            judged[at].Passes ? CarriedBy(set.Rules, evidences[at].From) : null));
    }

    // The summed evidence of rules on several indices: every unit of each from the year its verdict is judged from, the
    // latest any of them is, a book's months summed into one unit a calendar month; each test year's pair over them; and
    // each side's results over the units.
    public static (LoopEvidence Evidence, int? From) Evidence(IReadOnlyList<PooledRule> rules)
    {
        var book = rules[0].Book;
        var from = rules.Max(rule => rule.JudgedFrom);
        var units = rules.SelectMany(rule => rule.Units).Where(unit => from is not { } year || unit.Session.Year >= year);

        if (book)
        {
            units = units
                .GroupBy(unit => (unit.Session.Year, unit.Session.Month))
                .Select(month => new PooledUnit(month.Min(unit => unit.Session), month.Min(unit => unit.Place), Sum(month.Select(unit => unit.Proposed)), Sum(month.Select(unit => unit.Current))));
        }

        PooledUnit[] held = [.. units.OrderBy(unit => unit.Place).ThenBy(unit => unit.Session)];
        var through = rules.Min(rule => rule.Through);
        var first = Math.Max(LoopFolds.FirstTestYear, from ?? LoopFolds.FirstTestYear);

        LoopYear[] years =
        [
            .. Enumerable.Range(first, Math.Max(0, through.Year - first + 1)).Select(year =>
            {
                var inYear = held.Where(unit => unit.Session.Year == year).ToArray();

                return new LoopYear(
                    year,
                    year < through.Year,
                    inYear.Count(unit => unit.Current is not null),
                    inYear.Count(unit => unit.Proposed is not null),
                    inYear.Sum(unit => unit.Current ?? 0),
                    inYear.Sum(unit => unit.Proposed ?? 0));
            }),
        ];

        return (
            new LoopEvidence(
                book,
                [.. held.Select(unit => new PairedUnit(unit.Place, unit.Proposed, unit.Current))],
                years,
                [.. held.Where(unit => unit.Current is not null).Select(unit => unit.Current!.Value)],
                [.. held.Where(unit => unit.Proposed is not null).Select(unit => unit.Proposed!.Value)]),
            from);
    }

    static double? Sum(IEnumerable<double?> figures)
    {
        double[] held = [.. figures.Where(figure => figure is not null).Select(figure => figure!.Value)];

        return held.Length == 0 ? null : held.Sum();
    }

    // The index that carried a pass: the one whose units hold the largest paired total where the other indices' together
    // stand at or under nothing; none where the others together stand above nothing.
    public static string? CarriedBy(IReadOnlyList<PooledRule> rules, int? from)
    {
        (string Index, double Total)[] totals =
        [
            .. rules.GroupBy(rule => rule.Index, StringComparer.Ordinal).Select(index => (index.Key, index
                .SelectMany(rule => rule.Units)
                .Where(unit => from is not { } year || unit.Session.Year >= year)
                .Sum(unit => (unit.Proposed ?? 0) - (unit.Current ?? 0)))),
        ];
        var whole = totals.Sum(one => one.Total);

        return totals.Where(one => whole - one.Total <= 0).OrderByDescending(one => one.Total).Select(one => one.Index).FirstOrDefault();
    }

    // The kind of change a variant makes to its family's live rule on its index: each setting it states at another level
    // than the rule does, a setting it does not state read as nothing, named with which way it moved, in the settings'
    // order; none where it states every setting as the rule does.
    public static string? KindOf(IReadOnlyDictionary<string, double> rule, IReadOnlyDictionary<string, double> variant)
    {
        string[] moves =
        [
            .. rule.Keys.Union(variant.Keys).Order(StringComparer.Ordinal)
                .Select(setting => (Setting: setting, From: rule.GetValueOrDefault(setting), To: variant.GetValueOrDefault(setting)))
                .Where(move => move.From != move.To)
                .Select(move => move.Setting + " " + (move.To == 0 ? Off : move.From == 0 ? On : move.To > move.From ? Raised : Lowered)),
        ];

        return moves.Length == 0 ? null : string.Join(", ", moves);
    }

    // The passes luck alone would bring a year across the families the step read: each family's verdict held to the bar a
    // run, twelve runs a year, so the level a year a family.
    public static double FalsePassesAYear(int families) => families * LoopGate.Level;

    // The years in which luck alone would bring one pass, to the nearest whole year; none where no family is read.
    public static int? YearsAPass(int families) => families <= 0 ? null : (int)Math.Round(1 / FalsePassesAYear(families));
}
