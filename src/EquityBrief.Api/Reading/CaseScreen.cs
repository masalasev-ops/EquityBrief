using EquityBrief.Core.Cards;
using EquityBrief.Core.Loop;
using EquityBrief.Web.App;
using EquityBrief.Web.Marks;

namespace EquityBrief.Api.Reading;

// The case for promotion's rows on one index, read off Paper trading's accounts as its page walks them: each running
// revision with each variant of it, both records over the same nights from the trades the account bought, the long
// history's test from the variant's years judged from 2019, and the rule's three conditions. It computes no account of its
// own, so every figure is the one Paper trading draws.
// see: A background version replaces a live rule at a monthly run only where it beat it over the same nights and over the long history and closed 50 trades or a book's 12 months
public static class CaseScreen
{
    public static IReadOnlyList<PromotionRowView> Rows(
        string index,
        string indexName,
        PaperAccounts accounts,
        IReadOnlyList<RuleTestYearRow> longYears,
        DateOnly night,
        IReadOnlyDictionary<string, OwnExitView>? ownExits = null)
    {
        var sources = accounts.Sources.ToDictionary(account => account.Key, StringComparer.Ordinal);
        var sessions = accounts.Sessions ?? [];
        var rows = new List<PromotionRowView>();

        foreach (var head in accounts.Views.Where(view => view.Standing == PaperAccountView.Live))
        {
            var book = sources[head.Key].Book;
            var revision = head.Revisions.LastOrDefault();
            var running = Bought(head, sources[head.Key]);
            var runningFrom = revision?.From ?? head.Since ?? night;
            var variants = new List<PromotionCaseView>();

            foreach (var variant in accounts.Views.Where(view => view.Family == head.Family && view.Standing == PaperAccountView.Variant))
            {
                var source = sources[variant.Key];
                var own = Bought(variant, source);
                var from = PromotionRule.SameFrom(variant.VersionFrom ?? variant.Since ?? night, runningFrom);
                // Each figure is read over the trades the walk bought, as Paper trading's tiles read it, and the picks a night over
                // every trade the account listed.
                var same = PromotionRule.Record(book, [.. own.Select(Trade)], from, night) with { Listed = Listed(source, from, night) };
                var ran = PromotionRule.Record(book, [.. running.Select(Trade)], from, night) with { Listed = Listed(sources[head.Key], from, night) };
                (PromotionLong Variant, PromotionLong Running)? judged = source.Tested is { } tested
                    && longYears.Where(year => year.Family == variant.Family && year.Rule == tested).ToArray() is { Length: > 0 } years
                    ? PromotionRule.Long([.. years.Select(year => (year.Year, year.Units, year.Total, year.ComparedUnits, year.ComparedTotal))])
                    : null;
                var theirs = sources[head.Key].Trades.Where(one => one.Trade.ListedOn >= from).Select(one => (one.Trade.Ticker, one.Trade.ListedOn)).ToHashSet();
                var named = MarkRenderer.VariantName(variant);

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
                    Tonight(sources[head.Key].Trades, night)));
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
                ownExits?.GetValueOrDefault(head.Family) is { OwnMean: { } ownMean, LiveMean: { } liveMean } exit
                    ? (ownMean * 100 / PromotionRule.TradingMonths(exit.Since, exit.Through ?? night), liveMean * 100 / PromotionRule.TradingMonths(exit.Since, exit.Through ?? night))
                    : null));
        }

        return rows;
    }

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
