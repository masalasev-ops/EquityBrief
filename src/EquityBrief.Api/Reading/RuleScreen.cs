using System.Globalization;
using System.Text.Json;
using EquityBrief.Core.Candidates;
using EquityBrief.Core.Cards;
using EquityBrief.Core.Families;
using EquityBrief.Core.Filter;
using EquityBrief.Core.Loop;
using EquityBrief.Core.Sweep;
using EquityBrief.Web.Marks;
using static EquityBrief.Core.Paper.RuleRegister;

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
// see: A variant's picks are shown on its card when chosen and its results on paper trading beside its verdict, never on Tonight
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
    // does not stand, read off the register as it stood at the night's end, or as of the instant given where the page
    // reads it later, and the rows the night stored.
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
        IndexRuleSettings? provisional = null,
        DateTimeOffset? asOf = null,
        IReadOnlyDictionary<string, IReadOnlyList<string>>? qualifiers = null) =>
    [
        .. cards.Select(card => card with
        {
            RuleChoice = Of(card, indexName, large, night, register, chosen(QueryKey(card.Family)), nights, picks, forming, marketOpen, provisional, asOf, qualifiers),
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
        IndexRuleSettings? provisional,
        DateTimeOffset? asOf,
        IReadOnlyDictionary<string, IReadOnlyList<string>>? qualifiers)
    {
        var standing = Standing(card.Family, large, indexName, register, night, asOf);
        var (live, variants) = LiveAndVariants(standing);
        var liveChips = live is not null ? ChipsOf(live) : !large && provisional is not null ? ProvisionalChips(card.Family, provisional) : [];
        var choices = new List<RuleChoiceView> { new(live?.Candidate ?? (large ? LiveChoice : ProvisionalChoice), RuleScreenWords.LiveSlug, null, true, LiveTab(live)) };

        choices.AddRange(variants.Select((row, at) => new RuleChoiceView(row.Candidate, RunScreen.Slug(row.Candidate), at + 1, false, TabSentence(at + 1, liveChips, row))));

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
            large || live is not null || variants.Length > 0 ? null : NoVariantLine(indexName),
            Chips: ChipViews(liveChips, chosen.Live ? liveChips : ChipsOf(chosenRow!), !chosen.Live),
            Share: own is null ? null : ShareOf(large || live is not null, card, ruleName, [.. own.Select(pick => pick.Ticker)], qualifiers),
            Words: words,
            Reached: row?.Gates is { } stored ? RuleRows.ReachedOf(stored) : null);
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

    // A chosen version beside the live or provisional rule tonight, like with like: where the night kept the version's
    // answers, the stocks it qualified, its picks first in their places, against the live rule's qualifying stocks; where
    // it did not, its picks against the live rule's picks; and a book's holdings against the live book's, as they stand.
    // see: A version's line holds its qualifying stocks against the live rule's where the night stores both, and its picks against the live rule's picks where it does not
    static RuleShareView ShareOf(bool live, FamilyCardView card, string rule, IReadOnlyList<string> picks, IReadOnlyDictionary<string, IReadOnlyList<string>>? qualifiers)
    {
        if (card.Family == HeavyweightRule.Name)
        {
            return ShareOf(live, card.Passes ?? [], picks);
        }

        if (qualifiers?.GetValueOrDefault(rule) is { } fired)
        {
            return ShareOf(live, card.Passes ?? [], [.. picks.Where(fired.Contains), .. fired.Where(ticker => !picks.Contains(ticker, StringComparer.Ordinal))]) with { Of = RuleShareView.Qualifying };
        }

        return ShareOf(live, [.. card.Picks.Select(pick => pick.Row.Ticker)], picks) with { Of = RuleShareView.Picks };
    }

    // A chosen variant's picks or holdings beside the live or provisional rule's passes tonight: those both hold, those
    // the variant adds and those it drops, each in the order given.
    public static RuleShareView ShareOf(bool live, IReadOnlyList<string> passes, IReadOnlyList<string> chosen) =>
        new(
            live ? "live" : "provisional",
            [.. chosen.Where(ticker => passes.Contains(ticker, StringComparer.Ordinal))],
            [.. chosen.Where(ticker => !passes.Contains(ticker, StringComparer.Ordinal))],
            [.. passes.Where(ticker => !chosen.Contains(ticker, StringComparer.Ordinal))]);

    // The sector heavyweights' card's selector on an index, as a swing card's: the live rule, or the provisional rule
    // where no freeze registered one, then each variant by the number of its first registration, and the rule the link
    // chooses under the family's own key, the live rule where it names none or one that does not stand; with the chosen
    // variant's name, whose own book the card is then drawn from.
    // see: A variant's picks are shown on its card when chosen and its results on paper trading beside its verdict, never on Tonight
    public static (RuleView Rule, string? Variant) Heavyweights(string indexName, bool large, DateOnly night, IReadOnlyList<CandidateRow> register, string? asked, string liveWords, IReadOnlyList<SixSettingsVerdict>? sixSettings = null, IndexRuleSettings? provisional = null, DateTimeOffset? asOf = null)
    {
        var standing = Standing(HeavyweightRule.Name, large, indexName, register, night, asOf);
        var (live, variants) = LiveAndVariants(standing);
        var liveChips = live is not null ? ChipsOf(live) : !large && provisional is not null ? ProvisionalChips(HeavyweightRule.Name, provisional) : [];
        var choices = new List<RuleChoiceView> { new(live?.Candidate ?? (large ? LiveChoice : ProvisionalChoice), RuleScreenWords.LiveSlug, null, true, LiveTab(live)) };

        choices.AddRange(variants.Select((row, at) => new RuleChoiceView(row.Candidate, RunScreen.Slug(row.Candidate), at + 1, false, TabSentence(at + 1, liveChips, row))));

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
                large || live is not null || variants.Length > 0 ? null : NoVariantLine(indexName),
                untested,
                ChipViews(liveChips, chosenRow is null ? liveChips : ChipsOf(chosenRow), chosenRow is not null),
                Words: words),
            chosenRow?.Candidate);
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
                $"This variant's design passed the year-by-year test: the run for {verdict.Month} put the chance that {named} six settings did as well by luck at {p:0.00}, within the {LoopGate.Bar:0.0000} it needs."),
            { Adjusted: { } p } => FormattableString.Invariant(
                $"This variant did not pass the year-by-year test: the run for {verdict.Month} put the chance that {named} six settings did as well by luck at {p:0.00}, against the {LoopGate.Bar:0.0000} it needs, so it does not read as better than the starting setup."),
            _ => FormattableString.Invariant(
                $"This variant did not pass the year-by-year test: the run for {verdict.Month} held too little history to read {named} six settings, so it does not read as better than the starting setup."),
        };

        if (parameters.GetValueOrDefault(IndexHeavyweightCandidate.TwelveOneParameter) == 1)
        {
            line += FormattableString.Invariant(
                $" It reads twelve months' strength from {TwelveMonthStrength.From} trading days back, the oldest close the night holds, one trading day short of the {SweepAxes.TwelveMonthSessions} the search read.");
        }

        return new UntestedLineView(line, verdict.Adjusted);
    }

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
                DipCandidate dip => dip.SettingOf(parameters).WordsOn(dip.Index),
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

    // The live rules over their mark on the night, which the Run page carries among the things to worry about: a family's
    // live rule as the register stood past the night, or as of the instant given where the page reads it later, the rule
    // an approved change replaced being a variant.
    public static IReadOnlyList<(string Family, string Rule, int Stretch, int Mark)> Flagged(IReadOnlyList<RuleNightRow> nights, DateOnly night, IReadOnlyList<CandidateRow> register, DateTimeOffset? asOf = null)
    {
        var standing = CandidateFamily.StandingBefore(
            [
                .. register.Select(row => new RegisterRow(
                    row.Id, row.Candidate, string.Empty, string.Empty, row.Evaluator,
                    row.Parameters, row.EvaluatorVersion, row.Event, row.Retires, row.RegisteredAt, row.Evidence)),
            ],
            asOf ?? TonightScreen.PastTheNight(night));
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
