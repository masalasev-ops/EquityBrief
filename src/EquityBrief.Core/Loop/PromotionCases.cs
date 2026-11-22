using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using EquityBrief.Core.Cards;
using EquityBrief.Core.Paper;

namespace EquityBrief.Core.Loop;

// One variant against its setup's running revision on an index, as the case for promotion reads it: the index's code and
// name, the setup and whether it is a book, the running revision's number and first session, the variant's account key, its
// one change as a sentence and the settings it differs in, both records over the same nights and over the long history, the
// rule's three conditions, the picks each listed a night over the same nights and how many of the variant's were the
// revision's too, tonight's picks of each, and the long test the long history's figures come from, its month and its run, none
// where no long test judged the variant.
public sealed record PromotionCaseView(
    string Index,
    string IndexName,
    string Family,
    bool Book,
    int Revision,
    DateOnly RevisionFrom,
    string Variant,
    string VariantName,
    IReadOnlyList<string> Changes,
    PromotionRecord Same,
    PromotionRecord RunningSame,
    PromotionLong? Long,
    PromotionLong? RunningLong,
    PromotionConditions Conditions,
    int Sessions,
    int Shared,
    IReadOnlyList<string> Tonight,
    IReadOnlyList<string> RunningTonight,
    string? LongTestMonth = null,
    string? LongTestRun = null);

// One running revision on an index as the table under the cases reads it, with each variant beside it, and for a book its
// own exit's points a month beside the book's over the same nights, none where none is walked.
public sealed record PromotionRowView(
    string Index,
    string IndexName,
    string Family,
    bool Book,
    int? Revision,
    DateOnly? RevisionFrom,
    PromotionRecord? Running,
    IReadOnlyList<PromotionCaseView> Variants,
    (double Own, double Book)? OwnExit = null);

// The case for promotion's rows on one index, read off Paper trading's accounts as the core works them: each running revision
// with each variant of it, both records over the same nights from the trades the account bought, the long history's test
// from the variant's years judged from 2019, and the rule's three conditions. It computes no account of its own, so every
// figure is the one Paper trading draws, and the page and the monthly run read the same rows. A case judged over the long
// history names the long test it read, its month and its run, given with the years.
// see: A background version replaces a live rule at a monthly run only where it beat it over the same nights and over the long history and closed 50 trades or a book's 12 months
// see: The monthly run stores each case standing from Paper trading's accounts in Core, and the local run writes its words
public static class PromotionCases
{
    public static IReadOnlyList<PromotionRowView> Rows(
        string index,
        string indexName,
        IReadOnlyList<PaperAccountSource> accountSources,
        IReadOnlyList<PaperAccountView> views,
        IReadOnlyList<DateOnly>? accountSessions,
        IReadOnlyList<RuleTestYearRow> longYears,
        DateOnly night,
        IReadOnlyDictionary<string, (double Own, double Book)>? ownExits = null,
        (string Month, string Run)? longTest = null)
    {
        var sources = accountSources.ToDictionary(account => account.Key, StringComparer.Ordinal);
        var sessions = accountSessions ?? [];
        var rows = new List<PromotionRowView>();

        foreach (var head in views.Where(view => view.Standing == PaperAccountView.Live))
        {
            var book = sources[head.Key].Book;
            var revision = head.Revisions.LastOrDefault();
            var running = Bought(head, sources[head.Key]);
            var runningFrom = revision?.From ?? head.Since ?? night;
            var variants = new List<PromotionCaseView>();

            foreach (var variant in views.Where(view => view.Family == head.Family && view.Standing == PaperAccountView.Variant))
            {
                var source = sources[variant.Key];
                var own = Bought(variant, source);
                var from = PromotionRule.SameFrom(variant.VersionFrom ?? variant.Since ?? night, runningFrom);

                // Each figure is read over the trades the walk bought, as Paper trading's tiles read it, and the picks a night
                // over every trade the account listed.
                var same = PromotionRule.Record(book, [.. own.Select(Trade)], from, night) with { Listed = Listed(source, from, night) };
                var ran = PromotionRule.Record(book, [.. running.Select(Trade)], from, night) with { Listed = Listed(sources[head.Key], from, night) };
                (PromotionLong Variant, PromotionLong Running)? judged = source.Tested is { } tested
                    && longYears.Where(year => year.Family == variant.Family && year.Rule == tested).ToArray() is { Length: > 0 } years
                    ? PromotionRule.Long([.. years.Select(year => (year.Year, year.Units, year.Total, year.ComparedUnits, year.ComparedTotal))])
                    : null;
                var theirs = sources[head.Key].Trades.Where(one => one.Trade.ListedOn >= from).Select(one => (one.Trade.Ticker, one.Trade.ListedOn)).ToHashSet();
                var named = PaperNames.VariantName(variant);

                variants.Add(new PromotionCaseView(
                    index,
                    indexName,
                    head.Family,
                    book,
                    revision?.Number ?? 1,
                    runningFrom,
                    variant.Key,
                    named,
                    variant.Differences.Count > 0 ? [.. variant.Differences.Select(change => change.Sentence)] : [named],
                    same,
                    ran,
                    judged?.Variant,
                    judged?.Running,
                    PromotionRule.Judge(book, same, ran, judged?.Variant, judged?.Running),
                    sessions.Count(session => session >= from && session <= night),
                    source.Trades.Count(one => one.Trade.ListedOn >= from && one.Trade.ListedOn <= night && theirs.Contains((one.Trade.Ticker, one.Trade.ListedOn))),
                    Tonight(source.Trades, night),
                    Tonight(sources[head.Key].Trades, night),
                    judged is null ? null : longTest?.Month,
                    judged is null ? null : longTest?.Run));
            }

            rows.Add(new PromotionRowView(
                index,
                indexName,
                head.Family,
                book,
                revision?.Number,
                revision?.From,
                PromotionRule.Record(book, [.. running.Select(Trade)], PromotionRule.SameFrom(runningFrom, runningFrom), night),
                variants,
                ownExits is not null && ownExits.TryGetValue(head.Family, out var exit) ? exit : null));
        }

        return rows;
    }

    // The long test an index's rule rows were read from: the month it ran for and its run, none where no long test is stored.
    public static (string Month, string Run)? LongTestOf(IReadOnlyList<RuleTestRow> rules) =>
        rules.Count == 0 ? null : (rules[0].Month, rules[0].RunId);

    // The cases standing among an index's rows: each variant meeting the rule.
    public static IReadOnlyList<PromotionCaseView> Standing(IReadOnlyList<PromotionRowView> rows) =>
        [.. rows.SelectMany(row => row.Variants).Where(one => one.Conditions.Meets)];

    // A case's figures as the monthly run stores them, and the key a later run's figures are read against: a case whose
    // figures are the same keeps its words, and one whose figures changed is asked for them again.
    public static string Figures(PromotionCaseView one) => JsonSerializer.Serialize(one);

    public static string FiguresKey(string figures) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(figures)))[..32];

    public static PromotionCaseView? FromFigures(string figures) => JsonSerializer.Deserialize<PromotionCaseView>(figures);

    // The trades an account's walk bought, with their stored results, as Paper trading's tiles count them; every trade where
    // no walk ran.
    static IReadOnlyList<(PaperTrade Trade, double? Result)> Bought(PaperAccountView view, PaperAccountSource source) =>
        view.Walk is { } walk
            ? [.. walk.Trades.Where(one => one.Skipped is null).Select(one => (one.Trade, source.Trades.FirstOrDefault(own => own.Trade == one.Trade).Result))]
            : source.Trades;

    static int Listed(PaperAccountSource source, DateOnly from, DateOnly night) =>
        source.Trades.Count(one => one.Trade.ListedOn >= from && one.Trade.ListedOn <= night);

    static PromotionTrade Trade((PaperTrade Trade, double? Result) one) => new(one.Trade.ListedOn, one.Trade.Ended, one.Result);

    static IReadOnlyList<string> Tonight(IReadOnlyList<(PaperTrade Trade, double? Result)> trades, DateOnly night) =>
        [.. trades.Where(one => one.Trade.ListedOn == night).Select(one => one.Trade.Ticker).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)];
}
