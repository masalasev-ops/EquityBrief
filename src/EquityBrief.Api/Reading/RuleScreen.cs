using System.Globalization;
using System.Text.Json;
using EquityBrief.Core.Candidates;
using EquityBrief.Core.Cards;
using EquityBrief.Core.Families;
using EquityBrief.Core.Filter;
using EquityBrief.Web.Marks;

namespace EquityBrief.Api.Reading;

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
        bool marketOpen) =>
    [
        .. cards.Select(card => card with
        {
            RuleChoice = Of(card, indexName, large, night, register, chosen(QueryKey(card.Family)), nights, picks, forming, marketOpen),
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
        bool marketOpen)
    {
        var standing = Standing(card.Family, large, indexName, register, night);
        var live = standing.FirstOrDefault(row => FamilyRecords.IsLive(row.Candidate));
        var variants = standing.Where(row => !FamilyRecords.IsLive(row.Candidate)).OrderBy(row => row.RegisteredAt).ThenBy(row => row.Id).ToArray();
        var choices = new List<RuleChoiceView> { new(live?.Candidate ?? (large ? LiveChoice : ProvisionalChoice), RuleScreenWords.LiveSlug, null, true) };

        choices.AddRange(variants.Select((row, at) => new RuleChoiceView(row.Candidate, RunScreen.Slug(row.Candidate), at + 1, false)));

        var chosen = choices.FirstOrDefault(choice => !choice.Live && choice.Slug == asked) ?? choices[0];
        var chosenRow = chosen.Live ? live : variants.First(row => RunScreen.Slug(row.Candidate) == chosen.Slug);
        var ruleName = chosen.Live ? live?.Candidate ?? RuleRows.ProvisionalRule : chosenRow!.Candidate;
        var liveWords = live is null ? card.Rule : Words(card.Family, live) ?? card.Rule;
        var words = chosen.Live ? card.Rule : Words(card.Family, chosenRow!) ?? chosenRow!.Candidate;
        var row = nights.FirstOrDefault(one => one.Family == card.Family && one.Rule == ruleName && one.Session == night);

        return new RuleView(
            chosen,
            choices,
            Parts(words, liveWords),
            chosen.Live ? null : [.. picks.Where(pick => pick.Rule == ruleName && pick.Session == night).OrderBy(pick => pick.Place).Select(pick => new VariantPickView(pick.Place, pick.Ticker, pick.Entry, pick.Stop, pick.Target, pick.RewardToRisk, pick.Why))],
            row?.Gates is { } gates ? Funnel(gates) : null,
            row is { Evaluated: true, Stretch: { } stretch } && card.Family != HeavyweightRule.Name ? new StretchLineView(stretch, row.Mark, row.Flagged, row.Completed ?? 0, row.Sessions ?? 0) : null,
            card.Family == BreakoutRule.Name && row is not null ? FormingOf(forming.Where(one => one.Rule == ruleName).ToArray(), marketOpen) : null,
            row?.Evaluated ?? false,
            large || variants.Length > 0 ? null : NoVariantLine(indexName));
    }

    // The sector heavyweights' card's selector on an index, as a swing card's: the live rule, or the provisional rule
    // where no freeze registered one, then each variant by the number of its first registration, and the rule the link
    // chooses under the family's own key, the live rule where it names none or one that does not stand; with the chosen
    // variant's name, whose own book the card is then drawn from.
    // see: A variant's picks are shown on its card when chosen and its results only under its tests
    public static (RuleView Rule, string? Variant) Heavyweights(string indexName, bool large, DateOnly night, IReadOnlyList<CandidateRow> register, string? asked, string liveWords)
    {
        var standing = Standing(HeavyweightRule.Name, large, indexName, register, night);
        var live = standing.FirstOrDefault(row => FamilyRecords.IsLive(row.Candidate));
        var variants = standing.Where(row => !FamilyRecords.IsLive(row.Candidate)).OrderBy(row => row.RegisteredAt).ThenBy(row => row.Id).ToArray();
        var choices = new List<RuleChoiceView> { new(live?.Candidate ?? (large ? LiveChoice : ProvisionalChoice), RuleScreenWords.LiveSlug, null, true) };

        choices.AddRange(variants.Select((row, at) => new RuleChoiceView(row.Candidate, RunScreen.Slug(row.Candidate), at + 1, false)));

        var chosen = choices.FirstOrDefault(choice => !choice.Live && choice.Slug == asked) ?? choices[0];
        var chosenRow = chosen.Live ? null : variants.First(row => RunScreen.Slug(row.Candidate) == chosen.Slug);
        var words = chosenRow is null ? liveWords : Words(HeavyweightRule.Name, chosenRow) ?? chosenRow.Candidate;

        return (
            new RuleView(chosen, choices, Parts(words, liveWords), null, null, null, null, true, large || variants.Length > 0 ? null : NoVariantLine(indexName)),
            chosenRow?.Candidate);
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

    // The live rules over their mark on the night, which the Run page carries among the things to worry about.
    public static IReadOnlyList<(string Family, string Rule, int Stretch, int Mark)> Flagged(IReadOnlyList<RuleNightRow> nights, DateOnly night) =>
    [
        .. nights
            .Where(row => row.Session == night && row.Flagged && row.Mark is not null && (FamilyRecords.IsLive(row.Rule) || row.Rule == RuleRows.ProvisionalRule))
            .Select(row => (row.Family, row.Rule, row.Stretch ?? 0, row.Mark!.Value)),
    ];
}
