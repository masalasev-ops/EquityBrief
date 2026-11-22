using EquityBrief.Core.Loop;
using EquityBrief.Web.Marks;

namespace EquityBrief.Api.Reading;

// The case for promotion's rows on one index, worked by the core from Paper trading's accounts as the page walks them, each
// book's own exit beside its running revision, so the page reads the rows the monthly run stores.
// see: A background version replaces a live rule at a monthly run only where it beat it over the same nights and over the long history and closed 50 trades or a book's 12 months
public static class CaseScreen
{
    public static IReadOnlyList<PromotionRowView> Rows(
        string index,
        string indexName,
        PaperAccounts accounts,
        IReadOnlyList<RuleTestYearRow> longYears,
        DateOnly night,
        IReadOnlyDictionary<string, OwnExitView>? ownExits = null,
        IReadOnlyList<RuleTestRow>? longRules = null) =>
        PromotionCases.Rows(
            index,
            indexName,
            accounts.Sources,
            accounts.Views,
            accounts.Sessions,
            longYears,
            night,
            ownExits?
                .Where(exit => exit.Value is { OwnMean: not null, LiveMean: not null })
                .ToDictionary(
                    exit => exit.Key,
                    exit => (exit.Value.OwnMean!.Value * 100 / PromotionRule.TradingMonths(exit.Value.Since, exit.Value.Through ?? night), exit.Value.LiveMean!.Value * 100 / PromotionRule.TradingMonths(exit.Value.Since, exit.Value.Through ?? night)),
                    StringComparer.Ordinal),
            longRules is null ? null : PromotionCases.LongTestOf(longRules));
}
