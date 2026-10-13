using System.Globalization;
using System.Text.Json;
using EquityBrief.Core.Candidates;
using EquityBrief.Core.Cards;
using EquityBrief.Core.Families;
using EquityBrief.Core.Filter;
using EquityBrief.Core.Loop;
using EquityBrief.Core.Sweep;
using EquityBrief.Web.Marks;

namespace EquityBrief.Api.Reading;

// The sector heavyweights' six settings of one design as an index's newest tester run judged them: the proposal, the
// month the run was for, its adjusted p and whether it passed.
public sealed record SixSettingsVerdict(string Proposal, string Month, double? Adjusted, bool Passed);

// One standing rule's row on a night, as the night or the record command's replay stored it.
public sealed record RuleNightRow(string Index, DateOnly Session, string Family, string Rule, bool Evaluated, int Listed, string? Gates, int? Stretch, int? Mark, bool Flagged, int? Completed, int? Sessions, string Source);

// One pick a rule of its own keeps on a night, or one a registered rule's list kept.
public sealed record RulePickRow(string Rule, string Ticker, DateOnly Session, int Place, decimal Entry, decimal Stop, decimal? Target, double? RewardToRisk, string Why);

// One member forming a breakout under a rule on a night.
public sealed record FormingRow(string Rule, int Place, string Ticker, decimal Close, decimal High, double MovesUnder, double VolumeNeeded, double Volume, double RangeRatio, string Missing, DateOnly? NextEarnings, int Forming);

// The selector's choices on a card, the band and the words of the rule chosen, its picks where it is a variant, its
// funnel, its stretch line and its forming list, read for the rule the link names or the live rule where it names
// none.
// see: A variant's picks are shown on its card when chosen and its results only under its tests
// see: The forming list advises and never lists a stock
// see: A card's stretch line counts its mark over past empty nights and draws none under thirty completed stretches
public static class RuleScreen
{
    // The card's selector: the live rule, then each variant by the number of its first registration.
    public const string LiveChoice = "Live rule";

    // The index's provisional rule, where no freeze has registered one.
    public const string ProvisionalChoice = "Provisional rule";

    public static string NoVariantLine(string index) => $"No variant is registered on the {index} before its freeze.";

    // The query key a card's choice is kept under in the link: the family's own word.
    public static string QueryKey(string family) => family;

    // Each card with the rule the link chooses for it, the live rule where the link names none or names one that
    // does not stand, read off the register as it stood at the night's end and the rows the night stored.
    public static IReadOnlyList<FamilyCardView> WithRules(
        IReadOnlyList<FamilyCardView> cards,
        string indexName,
        bool large,
        DateOnly night,
        IReadOnlyList<CandidateRow> register,
        Func<string, string?> chosen,
        IReadOnlyList<RuleNightRow> nights,
        IReadOnlyList<RulePickRow> picks,
        IReadOnlyList<FormingRow> forming,
        bool marketOpen,
        IndexRuleSettings? provisional = null) =>
    [
        .. cards.Select(card => card with
        {
            RuleChoice = Of(card, indexName, large, night, register, chosen(QueryKey(card.Family)), nights, picks, forming, marketOpen, provisional),
        }),
    ];

    static RuleView Of(
        FamilyCardView card,
        string indexName,
        bool large,
        DateOnly night,
        IReadOnlyList<CandidateRow> register,
        string? asked,
        IReadOnlyList<RuleNightRow> nights,
        IReadOnlyList<RulePickRow> picks,
        IReadOnlyList<FormingRow> forming,
        bool marketOpen,
        IndexRuleSettings? provisional)
    {
        var standing = Standing(card.Family, large, indexName, register, night);
        var (live, variants) = LiveAndVariants(standing);
        var liveChips = live is not null ? ChipsOf(live) : !large && provisional is not null ? ProvisionalChips(card.Family, provisional) : [];
        var choices = new List<RuleChoiceView> { new(live?.Candidate ?? (large ? LiveChoice : ProvisionalChoice), RuleScreenWords.LiveSlug, null, true, LiveTab(live)) };

        choices.AddRange(variants.Select((row, at) => new RuleChoiceView(row.Candidate, RunScreen.Slug(row.Candidate), at + 1, false, RuleChips.TabWords(at + 1, RuleChips.Differing(liveChips, ChipsOf(row))))));

        var chosen = choices.FirstOrDefault(choice => !choice.Live && choice.Slug == asked) ?? choices[0];
        var chosenRow = chosen.Live ? live : variants.First(row => RunScreen.Slug(row.Candidate) == chosen.Slug);
        var ruleName = chosen.Live ? live?.Candidate ?? RuleRows.ProvisionalRule : chosenRow!.Candidate;
        var liveWords = live is null ? card.Rule : Words(card.Family, live) ?? card.Rule;
        var words = chosen.Live ? card.Rule : Words(card.Family, chosenRow!) ?? chosenRow!.Candidate;
        var row = nights.FirstOrDefault(one => one.Family == card.Family && one.Rule == ruleName && one.Session == night);
        var own = chosen.Live ? null : picks.Where(pick => pick.Rule == ruleName && pick.Session == night).OrderBy(pick => pick.Place).ToArray();

        return new RuleView(
            chosen,
            choices,
            Parts(words, chosen.Live ? words : liveWords),
            own is null ? null : [.. own.Select(pick => new VariantPickView(pick.Place, pick.Ticker, pick.Entry, pick.Stop, pick.Target, pick.RewardToRisk, pick.Why))],
            row?.Gates is { } gates ? Funnel(gates) : null,
            row is { Evaluated: true, Stretch: { } stretch } && card.Family != HeavyweightRule.Name ? new StretchLineView(stretch, row.Mark, row.Flagged, row.Completed ?? 0, row.Sessions ?? 0) : null,
            card.Family == BreakoutRule.Name && row is not null ? FormingOf(forming.Where(one => one.Rule == ruleName).ToArray(), marketOpen) : null,
            row?.Evaluated ?? false,
            large || variants.Length > 0 ? null : NoVariantLine(indexName),
            Chips: ChipViews(liveChips, chosen.Live ? liveChips : ChipsOf(chosenRow!), !chosen.Live),
            Share: own is null ? null : ShareOf(large || live is not null, card.Passes ?? [], [.. own.Select(pick => pick.Ticker)]),
            Words: words);
    }

    // The live or provisional rule's tab: live where a freeze registered one, provisional otherwise, as the card's pill
    // reads, on the S&P 500 as on the S&P 400 and 600.
    static string LiveTab(CandidateRow? live) => live is not null ? "Live" : "Provisional";

    // A chosen rule's chips, each marked where a chosen variant differs from the live rule on it, and the live rule's
    // settings a variant states none of drawn as without them.
    static IReadOnlyList<RuleChipView> ChipViews(IReadOnlyList<RuleChip> live, IReadOnlyList<RuleChip> chosen, bool variant)
    {
        if (!variant)
        {
            return [.. chosen.Select(chip => new RuleChipView(chip.Words, false))];
        }

        var differing = RuleChips.Differing(live, chosen).Select(chip => chip.Words).ToHashSet(StringComparer.Ordinal);

        return
        [
            .. chosen.Select(chip => new RuleChipView(chip.Words, differing.Contains(chip.Words))),
            .. RuleChips.Differing(live, chosen).Where(chip => chosen.All(own => own.Key != chip.Key)).Select(chip => new RuleChipView(chip.Words, true)),
        ];
    }

    // A chosen variant's picks or holdings beside the live or provisional rule's passes tonight: those both hold, those
    // the variant adds and those it drops, each in the order given.
    public static RuleShareView ShareOf(bool live, IReadOnlyList<string> passes, IReadOnlyList<string> chosen) =>
        new(
            live ? "live" : "provisional",
            [.. chosen.Where(ticker => passes.Contains(ticker, StringComparer.Ordinal))],
            [.. chosen.Where(ticker => !passes.Contains(ticker, StringComparer.Ordinal))],
            [.. passes.Where(ticker => !chosen.Contains(ticker, StringComparer.Ordinal))]);

    // A registered rule's chips, read off its parameters through its kind's table of short words; none for a rule whose
    // parameters cannot be read or whose evaluator no table is written for.
    public static IReadOnlyList<RuleChip> ChipsOf(CandidateRow row)
    {
        try
        {
            return CandidateEvaluators.Find(row.Evaluator) is { } evaluator && KindOf(evaluator) is { } kind
                ? RuleChips.Of(kind, CandidateEvaluator.Read(row.Parameters))
                : [];
        }
        catch (Exception failure) when (failure is ArgumentException or JsonException or InvalidCastException)
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
        SwingFilterRule => RuleKind.Pullback,
        BreakoutCandidate => RuleKind.Breakout,
        DriftCandidate => RuleKind.Drift,
        BookEvaluator => RuleKind.Heavyweights,
        _ => null,
    };

    // An S&P 400's or 600's provisional rule's chips, read off the settings its night stored as a registration would
    // state them: its family's dials and the levels every provisional rule holds, the profit gate where the night read
    // one, its own breadth and the dollar volume floor at one times.
    public static IReadOnlyList<RuleChip> ProvisionalChips(string family, IndexRuleSettings settings)
    {
        var p = new Dictionary<string, double>(StringComparer.Ordinal)
        {
            [IndexRuleCandidate.FloorsParameter] = 1,
            [IndexRuleCandidate.LowestCloseParameter] = EquityBrief.Core.Prices.Statistic.FromPrice(settings.LowestPrice),
            [IndexRuleCandidate.QualityParameter] = settings.ProfitQuarters is null ? 0 : 1,
            [IndexRuleCandidate.LargeBreadthParameter] = 0,
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
                p.Remove(IndexRuleCandidate.LowestCloseParameter);
                p[IndexHeavyweightCandidate.DesignParameter] = IndexHeavyweightCandidate.DesignA;
                Set(IndexHeavyweightCandidate.LargestParameter, settings.Heavyweights.GetValueOrDefault("size") == "every" ? 0 : Dial(settings.Heavyweights, "size"));
                Set(IndexHeavyweightCandidate.LookBackParameter, Dial(settings.Heavyweights, "look-back"));
                Set(IndexHeavyweightCandidate.LeadersParameter, Dial(settings.Heavyweights, "leaders"));
                Set(IndexHeavyweightCandidate.BetaParameter, settings.Heavyweights.TryGetValue("beta", out var beta) ? beta == "off" ? 0 : 1 : null);
                Set(IndexHeavyweightCandidate.LeadingExitParameter, settings.Heavyweights.GetValueOrDefault("exit") is "drop" or "both" ? 1 : 0);
                Set(IndexHeavyweightCandidate.AverageExitParameter, settings.Heavyweights.GetValueOrDefault("exit") is "break" or "both" ? 1 : 0);

                return RuleChips.Of(RuleKind.IndexHeavyweights, p);

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

    // The sector heavyweights' card's selector on an index, as a swing card's: the live rule, or the provisional rule
    // where no freeze registered one, then each variant by the number of its first registration, and the rule the link
    // chooses under the family's own key, the live rule where it names none or one that does not stand; with the chosen
    // variant's name, whose own book the card is then drawn from.
    // see: A variant's picks are shown on its card when chosen and its results only under its tests
    public static (RuleView Rule, string? Variant) Heavyweights(string indexName, bool large, DateOnly night, IReadOnlyList<CandidateRow> register, string? asked, string liveWords, IReadOnlyList<SixSettingsVerdict>? sixSettings = null, IndexRuleSettings? provisional = null)
    {
        var standing = Standing(HeavyweightRule.Name, large, indexName, register, night);
        var (live, variants) = LiveAndVariants(standing);
        var liveChips = live is not null ? ChipsOf(live) : !large && provisional is not null ? ProvisionalChips(HeavyweightRule.Name, provisional) : [];
        var choices = new List<RuleChoiceView> { new(live?.Candidate ?? (large ? LiveChoice : ProvisionalChoice), RuleScreenWords.LiveSlug, null, true, LiveTab(live)) };

        choices.AddRange(variants.Select((row, at) => new RuleChoiceView(row.Candidate, RunScreen.Slug(row.Candidate), at + 1, false, RuleChips.TabWords(at + 1, RuleChips.Differing(liveChips, ChipsOf(row))))));

        var chosen = choices.FirstOrDefault(choice => !choice.Live && choice.Slug == asked) ?? choices[0];
        var chosenRow = chosen.Live ? null : variants.First(row => RunScreen.Slug(row.Candidate) == chosen.Slug);
        var words = chosenRow is null ? liveWords : Words(HeavyweightRule.Name, chosenRow) ?? chosenRow.Candidate;
        var untested = !large && live is null && chosenRow is not null ? Untested(chosenRow, sixSettings ?? []) : null;

        return (
            new RuleView(
                chosen,
                choices,
                Parts(words, liveWords),
                null,
                null,
                null,
                null,
                true,
                large || variants.Length > 0 ? null : NoVariantLine(indexName),
                untested,
                ChipViews(liveChips, chosenRow is null ? liveChips : ChipsOf(chosenRow), chosenRow is not null),
                Words: words),
            chosenRow?.Candidate);
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

    // The line beside a heavyweights book registered before its family's freeze: how the index's newest tester run judged
    // its design's six settings year by year, at the adjusted p it stored against the bar, so a book that did not pass
    // does not read as better than the provisional rule; and, for a book reading twelve months' strength, that it reads
    // from the oldest close the night holds, one session short of the search's setting. None where no run judged them.
    // see: An index family's variants may stand registered before its freeze, its provisional rule staying the live one
    public static UntestedLineView? Untested(CandidateRow book, IReadOnlyList<SixSettingsVerdict> sixSettings)
    {
        IReadOnlyDictionary<string, double> parameters;

        try
        {
            parameters = CandidateEvaluator.Read(book.Parameters);
        }
        catch (Exception failure) when (failure is ArgumentException or JsonException or InvalidCastException)
        {
            return null;
        }

        var design = parameters.GetValueOrDefault(IndexHeavyweightCandidate.DesignParameter);

        if (sixSettings.FirstOrDefault(verdict => verdict.Proposal == SixSettings.Of(design)) is not { } verdict)
        {
            return null;
        }

        var named = design == IndexHeavyweightCandidate.DesignA ? "design (a)'s" : "design (b)'s";
        var line = verdict switch
        {
            { Passed: true, Adjusted: { } p } => FormattableString.Invariant(
                $"This book's design passed the year-by-year test: the run for {verdict.Month} read {named} six settings at an adjusted p of {p:0.00} against the bar of {LoopGate.Bar:0.0000}."),
            { Adjusted: { } p } => FormattableString.Invariant(
                $"This book did not pass the year-by-year test: the run for {verdict.Month} read {named} six settings at an adjusted p of {p:0.00} against the bar of {LoopGate.Bar:0.0000}, so it does not read as better than the provisional rule."),
            _ => FormattableString.Invariant(
                $"This book did not pass the year-by-year test: the run for {verdict.Month} held too few blocks to read {named} six settings, so it does not read as better than the provisional rule."),
        };

        if (parameters.GetValueOrDefault(IndexHeavyweightCandidate.TwelveOneParameter) == 1)
        {
            line += FormattableString.Invariant(
                $" It reads twelve months' strength from {TwelveMonthStrength.From} sessions back, the oldest close the night holds, one session short of the {SweepAxes.TwelveMonthSessions} the search read.");
        }

        return new UntestedLineView(line, verdict.Adjusted);
    }

    // The rules of a family standing on an index at the night's end: on the S&P 500 the family's own evaluators' rows,
    // and on the S&P 400 or 600 the index's rules of the family.
    static IReadOnlyList<CandidateRow> Standing(string family, bool large, string indexName, IReadOnlyList<CandidateRow> register, DateOnly night)
    {
        var at = TonightScreen.PastTheNight(night);
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
                .Where(row => CandidateEvaluators.Find(row.Evaluator) is { } evaluator && OfTheFamily(evaluator, family, large, indexName))
                .Select(row => byName[row.Id]),
        ];
    }

    static bool OfTheFamily(CandidateEvaluator evaluator, string family, bool large, string indexName) =>
        evaluator switch
        {
            IndexRuleCandidate index => !large && index.SetupFamily == family && IndexRuleCandidate.NameOf(index.Index) == indexName,
            FamilyRuleEvaluator rule => large && rule.Family == family,
            BookEvaluator book => large && book.Family == family,
            SwingFilterRule => large && family == SetupFamilies.Pullback,
            _ => false,
        };

    // A registered rule's words, written by code from its parameters as the card writes the live rule's; none for a
    // rule whose parameters do not state what its words need, which is then named by its registration alone.
    static string? Words(string family, CandidateRow row)
    {
        try
        {
            var parameters = CandidateEvaluator.Read(row.Parameters);

            return CandidateEvaluators.Find(row.Evaluator) switch
            {
                SwingFilterRule => RuleWords.Pullback(SwingFilterRule.SettingsOf(parameters)),
                BreakoutCandidate => RuleWords.Breakout(BreakoutCandidate.SettingsOf(parameters)),
                DriftCandidate => RuleWords.Drift(DriftCandidate.SettingsOf(parameters)),
                BookEvaluator book => RuleWords.Heavyweights(book.SettingsOf(parameters)),
                _ => null,
            };
        }
        catch (Exception failure) when (failure is KeyNotFoundException or ArgumentException or JsonException or InvalidCastException)
        {
            return null;
        }
    }

    // A rule's words as clauses, each marked where the live rule's words do not carry it, so the lines that differ
    // stand out; the live rule's own carry no mark.
    public static IReadOnlyList<RulePartView> Parts(string words, string liveWords)
    {
        var live = Clauses(liveWords).ToHashSet(StringComparer.Ordinal);

        return [.. Clauses(words).Select(clause => new RulePartView(clause, !live.Contains(clause)))];
    }

    static IReadOnlyList<string> Clauses(string words) =>
        [.. words.Split([". ", ", ", "; "], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Select(clause => clause.TrimEnd('.'))];

    public static IReadOnlyList<(string Gate, int Passed)> Funnel(string gates) => RuleRows.ReadGates(gates);

    static FormingListView FormingOf(IReadOnlyList<FormingRow> rows, bool marketOpen) =>
        new(
            [.. rows.OrderBy(row => row.Place).Select(row => new FormingRowView(row.Place, row.Ticker, row.Close, row.High, row.MovesUnder, row.VolumeNeeded, row.Volume, row.RangeRatio, Missing(row.Missing), row.NextEarnings))],
            rows.Count > 0 ? rows[0].Forming : 0,
            marketOpen);

    static IReadOnlyList<string> Missing(string json)
    {
        using var document = JsonDocument.Parse(json);

        return [.. document.RootElement.EnumerateArray().Select(gate => gate.GetString() ?? string.Empty)];
    }

    // A family's live rule among its standing rules, its newest registration carrying the live rule's name, and every
    // other in the order each first registered, the rule an approved change replaced among them.
    // see: An approved change reaches the S&P 500's breakout and drift through the stored settings and a registration, and the rule it replaces runs beside it for 63 sessions
    static (CandidateRow? Live, CandidateRow[] Variants) LiveAndVariants(IReadOnlyList<CandidateRow> standing)
    {
        var live = FamilyRecords.LiveOf(standing, row => row.Candidate, row => row.RegisteredAt, row => row.Id);

        return (live, [.. standing.Where(row => row != live).OrderBy(row => row.RegisteredAt).ThenBy(row => row.Id)]);
    }

    // The live rules over their mark on the night, which the Run page carries among the things to worry about: a family's
    // live rule as the register stood past the night, the rule an approved change replaced being a variant.
    public static IReadOnlyList<(string Family, string Rule, int Stretch, int Mark)> Flagged(IReadOnlyList<RuleNightRow> nights, DateOnly night, IReadOnlyList<CandidateRow> register)
    {
        var standing = CandidateFamily.StandingBefore(
            [
                .. register.Select(row => new RegisterRow(
                    row.Id, row.Candidate, string.Empty, string.Empty, row.Evaluator,
                    row.Parameters, row.EvaluatorVersion, row.Event, row.Retires, row.RegisteredAt, row.Evidence)),
            ],
            TonightScreen.PastTheNight(night));
        var live = standing
            .GroupBy(row => CandidateFamily.SetupFamilyOf(row.Evaluator) + "|" + (CandidateEvaluators.Find(row.Evaluator) is IndexRuleCandidate rule ? rule.Index : string.Empty), StringComparer.Ordinal)
            .Select(family => FamilyRecords.LiveOf(family)?.Candidate)
            .OfType<string>()
            .ToHashSet(StringComparer.Ordinal);

        return
        [
            .. nights
                .Where(row => row.Session == night && row.Flagged && row.Mark is not null && (live.Contains(row.Rule) || row.Rule == RuleRows.ProvisionalRule))
                .Select(row => (row.Family, row.Rule, row.Stretch ?? 0, row.Mark!.Value)),
        ];
    }
}
