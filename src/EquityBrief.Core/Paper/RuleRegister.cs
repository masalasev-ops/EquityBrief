using System.Globalization;
using System.Text.Json;
using EquityBrief.Core.Candidates;
using EquityBrief.Core.Cards;
using EquityBrief.Core.Families;
using EquityBrief.Core.Filter;

namespace EquityBrief.Core.Paper;

// The candidate register as the pages and the paper accounts read a family on an index: its rules standing past a night,
// its live rule and each variant with its tab, every registration of it, a rule's chips off its parameters or an S&P 400's
// or 600's provisional rule's off its night's settings, and a heavyweights rule's settings.
public static class RuleRegister
{
    // The instant past a night from which nothing recorded counts on its page: a freeze or an answer written after the
    // night is read on no night before it.
    public static DateTimeOffset PastTheNight(DateOnly night) => new(night.AddDays(1).ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);

    // A rule's name as a key a page's link and an account carry: lower case, each run of other characters one hyphen.
    public static string Slug(string candidate) =>
        System.Text.RegularExpressions.Regex.Replace(candidate.ToLowerInvariant(), "[^a-z0-9]+", "-").Trim('-');

    // A variant's tab on a card: its number and its one change as a sentence, the setting other than the plan where it
    // differs in more; the same settings where it differs in none.
    // see: A rule's revision is a run of its registrations stating the same settings, and every trade belongs to the revision that listed it
    public static string TabSentence(int number, IReadOnlyList<RuleChip> liveChips, CandidateRow row)
    {
        var changes = RuleSentences.Between(liveChips, ChipsOf(row));
        var named = changes.FirstOrDefault(change => change.Key != "hooks") ?? changes.FirstOrDefault();

        return named is null
            ? RuleChips.TabWords(number, [])
            : FormattableString.Invariant($"V{number} · ") + (named.Sentence.Length > 1 && char.IsUpper(named.Sentence[1]) ? named.Sentence : char.ToLowerInvariant(named.Sentence[0]) + named.Sentence[1..]);
    }

    // A family's rules standing on an index past a night as its card names them: its live rule where one stands, and each
    // variant with its tab's words, numbered by its first registration, which paper trading names its accounts by.
    public static (CandidateRow? Live, IReadOnlyList<(CandidateRow Row, string Tab)> Variants) Accounts(string family, bool large, string indexName, IReadOnlyList<CandidateRow> register, DateOnly night, IndexRuleSettings? provisional = null, DateTimeOffset? asOf = null)
    {
        var (live, variants) = LiveAndVariants(Standing(family, large, indexName, register, night, asOf));
        var liveChips = live is not null ? ChipsOf(live) : !large && provisional is not null ? ProvisionalChips(family, provisional) : [];

        return (live, [.. variants.Select((row, at) => (row, TabSentence(at + 1, liveChips, row)))]);
    }

    // A registered rule's chips, read off its parameters through its kind's table of short words; none for a rule whose
    // parameters cannot be read or whose evaluator no table is written for.
    public static IReadOnlyList<RuleChip> ChipsOf(CandidateRow row)
    {
        try
        {
            // A dip rule's chips are its setting's own, its market setting in the words of its index.
            // see: A dip rule is registered as the family on its index and kept by the dip's own step alone
            return CandidateEvaluators.Find(row.Evaluator) is DipCandidate dip
                ? [.. dip.SettingOf(CandidateEvaluator.Read(row.Parameters)).KeyedChipsOn(dip.Index).Select(chip => new RuleChip(chip.Key, chip.Words))]
                : CandidateEvaluators.Find(row.Evaluator) is { } evaluator && KindOf(evaluator) is { } kind
                ? RuleChips.Of(kind, CandidateEvaluator.Read(row.Parameters))
                : [];
        }
        catch (Exception failure) when (failure is ArgumentException or JsonException or InvalidCastException or KeyNotFoundException)
        {
            return [];
        }
    }

    // The kind of rule an evaluator's registrations are, whose table of short words its chips are written by.
    public static RuleKind? KindOf(CandidateEvaluator evaluator) => evaluator switch
    {
        IndexPullbackCandidate => RuleKind.IndexPullback,
        IndexBreakoutCandidate => RuleKind.IndexBreakout,
        IndexDriftCandidate => RuleKind.IndexDrift,
        IndexHeavyweightCandidate => RuleKind.IndexHeavyweights,
        QualityCandidate => RuleKind.Quality,
        ImprovingCandidate => RuleKind.Improving,
        SwingFilterRule => RuleKind.Pullback,
        BreakoutCandidate => RuleKind.Breakout,
        DriftCandidate => RuleKind.Drift,
        BookEvaluator => RuleKind.Heavyweights,
        _ => null,
    };

    // An S&P 400's or 600's provisional rule's chips, read off the settings its night stored as a registration would
    // state them: its family's dials and the levels every provisional rule holds, the profit gate where the night read
    // one, its own breadth at the floor its night stored and the dollar volume floor at one times; and the
    // fundamentals-first family's on any index, off the setting its night stored.
    // see: Each frozen rule on the S&P 400 and 600 reads its market check at the level its own sweep chose
    public static IReadOnlyList<RuleChip> ProvisionalChips(string family, IndexRuleSettings settings)
    {
        var p = new Dictionary<string, double>(StringComparer.Ordinal)
        {
            [IndexRuleCandidate.FloorsParameter] = 1,
            [IndexRuleCandidate.LowestCloseParameter] = Prices.Statistic.FromPrice(settings.LowestPrice),
            [IndexRuleCandidate.QualityParameter] = settings.ProfitQuarters is null ? 0 : 1,
            [IndexRuleCandidate.LargeBreadthParameter] = 0,
            [IndexRuleCandidate.BreadthFloorParameter] = settings.MarketFloor,
        };

        static double? Dial(IReadOnlyDictionary<string, string> dials, string name) =>
            dials.TryGetValue(name, out var value) ? value is "off" or "none" ? 0 : double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var number) ? number : null : null;

        void Set(string key, double? value)
        {
            if (value is { } held)
            {
                p[key] = held;
            }
        }

        // The change an approval set on the family where the night read it at one, which no dial states.
        IReadOnlyList<RuleChip> WithApproved(IReadOnlyList<RuleChip> chips) =>
            settings.Approved is { } approved && approved.TryGetValue(family, out var change) ? [.. chips, new RuleChip("hooks", change)] : chips;

        switch (family)
        {
            case SetupFamilies.Pullback:
                foreach (var (key, value) in PullbackDials(settings.Pullback))
                {
                    p[key] = value;
                }

                return WithApproved(RuleChips.Of(RuleKind.IndexPullback, p));

            case BreakoutRule.Name:
                Set(IndexBreakoutCandidate.HighSessionsParameter, Dial(settings.Breakout, "high"));
                Set(IndexBreakoutCandidate.VolumeMultipleParameter, Dial(settings.Breakout, "volume"));
                Set(IndexBreakoutCandidate.RangeCeilingParameter, Dial(settings.Breakout, "ceiling"));
                Set(IndexBreakoutCandidate.StopMovesParameter, Dial(settings.Breakout, "stop"));

                return WithApproved(RuleChips.Of(RuleKind.IndexBreakout, p));

            case DriftRule.Name:
                Set(IndexDriftCandidate.WindowSessionsParameter, Dial(settings.Drift, "window"));
                Set(IndexDriftCandidate.ReactionMovesParameter, Dial(settings.Drift, "reaction"));
                Set(IndexDriftCandidate.VolumeMultipleParameter, Dial(settings.Drift, "volume"));
                Set(IndexDriftCandidate.TargetMultipleParameter, Dial(settings.Drift, "target"));

                return WithApproved(RuleChips.Of(RuleKind.IndexDrift, p));

            case HeavyweightRule.Name:
                p.Remove(IndexRuleCandidate.LargeBreadthParameter);
                p.Remove(IndexRuleCandidate.BreadthFloorParameter);
                p.Remove(IndexRuleCandidate.LowestCloseParameter);
                p[IndexHeavyweightCandidate.DesignParameter] = IndexHeavyweightCandidate.DesignA;
                Set(IndexHeavyweightCandidate.LargestParameter, settings.Heavyweights.GetValueOrDefault("size") == "every" ? 0 : Dial(settings.Heavyweights, "size"));
                Set(IndexHeavyweightCandidate.LookBackParameter, Dial(settings.Heavyweights, "look-back"));
                Set(IndexHeavyweightCandidate.LeadersParameter, Dial(settings.Heavyweights, "leaders"));
                Set(IndexHeavyweightCandidate.BetaParameter, settings.Heavyweights.TryGetValue("beta", out var beta) ? beta == "off" ? 0 : 1 : null);
                Set(IndexHeavyweightCandidate.LeadingExitParameter, settings.Heavyweights.GetValueOrDefault("exit") is "drop" or "both" ? 1 : 0);
                Set(IndexHeavyweightCandidate.AverageExitParameter, settings.Heavyweights.GetValueOrDefault("exit") is "break" or "both" ? 1 : 0);

                return RuleChips.Of(RuleKind.IndexHeavyweights, p);

            case FundamentalsRule.Name:
                return FundamentalsSetting.FromShort(settings.FundamentalsSetting) is { } business ? RuleChips.Of(business) : [];

            default:
                return [];
        }
    }

    // The pullback's dials off the setting its night stored, each as a registration states it: its strength, its depth,
    // its dry-up, its freshness, its reward to risk, its stop, its market dial and its band strength, none for a part the
    // words do not carry.
    static IEnumerable<(string Key, double Value)> PullbackDials(string setting)
    {
        static double? Number(string text) =>
            double.TryParse(text.TrimEnd('%'), NumberStyles.Float, CultureInfo.InvariantCulture, out var value) ? text.EndsWith('%') ? value / 100 : value : null;

        foreach (var part in setting.Split(", ", StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var words = part.Split(' ');
            var last = Number(words[^1]);

            switch (words[0])
            {
                case "strength" when last is { } strength:
                    yield return (IndexPullbackCandidate.StrengthParameter, strength);
                    break;
                case "depth" when words.Length == 4 && Number(words[1]) is { } low && last is { } high:
                    yield return (IndexPullbackCandidate.DepthLowParameter, low);
                    yield return (IndexPullbackCandidate.DepthHighParameter, high);
                    break;
                case "dry-up":
                    yield return (IndexPullbackCandidate.DryUpParameter, last ?? 0);
                    break;
                case "fresh" when last is { } fresh:
                    yield return (IndexPullbackCandidate.FreshnessParameter, fresh);
                    break;
                case "reward" when last is { } reward:
                    yield return (IndexPullbackCandidate.RewardToRiskParameter, reward);
                    break;
                case "stop" when words.Length == 4 && Number(words[1]) is { } low && last is { } high:
                    yield return (IndexPullbackCandidate.StopLowParameter, low);
                    yield return (IndexPullbackCandidate.StopHighParameter, high);
                    break;
                case "market":
                    yield return (IndexPullbackCandidate.MarketParameter, last ?? 0);
                    break;
                case "band" when last is { } band:
                    yield return (IndexPullbackCandidate.BandParameter, band);
                    break;
            }
        }
    }

    // A registered heavyweights rule's settings as its registration states them, none for a rule the register does not
    // hold or whose parameters cannot be read; and whether it is a design (b) book, which reads no lead over a sector.
    public static HeavyweightSettings? HeavyweightSettingsOf(string? candidate, IReadOnlyList<CandidateRow> register) =>
        BookParameters(candidate, register) is { } found && CandidateEvaluators.Find(found.Row.Evaluator) is BookEvaluator book ? book.SettingsOf(found.Parameters) : null;

    public static bool DesignB(string? candidate, IReadOnlyList<CandidateRow> register) =>
        BookParameters(candidate, register) is { } found && found.Parameters.GetValueOrDefault(IndexHeavyweightCandidate.DesignParameter) == IndexHeavyweightCandidate.DesignB;

    static (CandidateRow Row, IReadOnlyDictionary<string, double> Parameters)? BookParameters(string? candidate, IReadOnlyList<CandidateRow> register)
    {
        if (candidate is null || register.LastOrDefault(row => row.Candidate == candidate) is not { } row)
        {
            return null;
        }

        try
        {
            return (row, CandidateEvaluator.Read(row.Parameters));
        }
        catch (Exception failure) when (failure is ArgumentException or JsonException or InvalidCastException or KeyNotFoundException)
        {
            return null;
        }
    }

    // The rules of a family standing on an index at the night's end, or as of the instant given where the page reads the
    // register later: on the S&P 500 the family's own evaluators' rows, and on the S&P 400 or 600 the index's rules of the
    // family. The rule of the day before an S&P 400 or 600 freeze, kept in the background, is on no card's tabs and in no
    // account: its record is read on the Run page alone.
    // see: A freeze on the S&P 400 or 600 registers a market variant beside each swing rule and keeps the rule it replaces in the background for 63 sessions
    public static IReadOnlyList<CandidateRow> Standing(string family, bool large, string indexName, IReadOnlyList<CandidateRow> register, DateOnly night, DateTimeOffset? asOf = null)
    {
        var at = asOf ?? PastTheNight(night);
        var rows = CandidateFamily.StandingBefore(
            [
                .. register.Select(row => new RegisterRow(
                    row.Id, row.Candidate, string.Empty, string.Empty, row.Evaluator,
                    row.Parameters, row.EvaluatorVersion, row.Event, row.Retires, row.RegisteredAt, row.Evidence)),
            ],
            at);
        var byName = register.ToDictionary(row => row.Id);

        return
        [
            .. rows
                .Where(row => !FamilyRecords.IsBackground(row.Candidate) && CandidateEvaluators.Find(row.Evaluator) is { } evaluator && OfTheFamily(evaluator, family, large, indexName))
                .Select(row => byName[row.Id]),
        ];
    }

    // Every registration of a family's rules on an index, retired or standing, which names the version that listed a trade.
    public static IReadOnlyList<CandidateRow> Registrations(string family, bool large, string indexName, IReadOnlyList<CandidateRow> register) =>
    [
        .. register.Where(row => row.Event == CandidateFamily.Registered && CandidateEvaluators.Find(row.Evaluator) is { } evaluator && OfTheFamily(evaluator, family, large, indexName)),
    ];

    static bool OfTheFamily(CandidateEvaluator evaluator, string family, bool large, string indexName) =>
        evaluator switch
        {
            IndexRuleCandidate index => !large && index.SetupFamily == family && IndexRuleCandidate.NameOf(index.Index) == indexName,
            OwnStepCandidate own => own.SetupFamily == family && OwnStepCandidate.NameOf(own.Index) == indexName,
            FamilyRuleEvaluator rule => large && rule.Family == family,
            BookEvaluator book => large && book.Family == family,
            SwingFilterRule => large && family == SetupFamilies.Pullback,
            _ => false,
        };

    // A family's live rule among its standing rules, its newest registration carrying the live rule's name, and every
    // other in the order each first registered, the rule an approved change replaced among them.
    // see: An approved change registers the family's live rule again at the change on every index, and the rule it replaces runs beside it for 63 sessions or in the background where the family holds nine
    public static (CandidateRow? Live, CandidateRow[] Variants) LiveAndVariants(IReadOnlyList<CandidateRow> standing)
    {
        var live = FamilyRecords.LiveOf(standing, row => row.Candidate, row => row.RegisteredAt, row => row.Id);

        return (live, [.. standing.Where(row => row != live).OrderBy(row => row.RegisteredAt).ThenBy(row => row.Id)]);
    }
}
