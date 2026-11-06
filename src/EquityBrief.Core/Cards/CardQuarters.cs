using EquityBrief.Core.Families;
using EquityBrief.Core.Ledger;
using EquityBrief.Core.Readings;
using EquityBrief.Core.Report;

namespace EquityBrief.Core.Cards;

// One quarter a pick's card's business line reads: the provider's label and the day it says the quarter was filed, where its
// figures are drawn from, and its net income, operating income and interest expense from there.
public sealed record CardQuarter(DateOnly PeriodEnd, DateOnly FilingDate, QuarterSource Source, decimal? NetIncome, decimal? OperatingIncome, decimal? InterestExpense);

// A pick's card's quarters as the pages draw them: the provider's newest fetch before the night keeps the quarters and the
// days they were filed, and each quarter's net income and operating income are its SEC filing's as first filed where a
// filing stating it is stored, else for the newest quarter its results release's own rows, else none, the quarter not on
// file until its filing; its interest expense is its filing's alone, none where the filing states none.
// see: A pick's card reads its quarters as the pages draw them, from the filing and then the release and otherwise not on file
public static class CardQuarters
{
    public static IReadOnlyList<CardQuarter> Of(IReadOnlyList<FiledIncome> fetched, IReadOnlyList<FiledFactRow> filed, string? releaseText = null, DateOnly? releasedOn = null)
    {
        QuarterRow[] labels =
        [
            .. fetched
                .GroupBy(quarter => quarter.PeriodEnd)
                .Select(quarter => new QuarterRow(quarter.Key, null, null, null, null, null, null, null, null, null, null, null, null, false)),
        ];

        var sourced = QuarterSources.Of(labels, filed, releaseText, releasedOn);
        var interest = FirstFiledRevenue.Quarters(
            filed.Select(fact => new FiledRevenue(fact.Concept, fact.Start, fact.End, fact.Value, fact.Filed, fact.Form, fact.Accession)),
            FiledFacts.InterestExpense);
        var ends = labels.Select(label => label.PeriodEnd).SelectMany(end => new[] { end, end.AddYears(-1) }).Distinct().ToArray();

        int Apart(DateOnly one, DateOnly other) => Math.Abs(one.DayNumber - other.DayNumber);

        // The filed quarter nearest a label within a month, where no other label stands nearer to it, as the sourced lines
        // are matched.
        decimal? InterestAt(DateOnly end) => interest
            .Where(quarter => Apart(quarter.End, end) <= QuarterSources.LabelWithin && ends.All(label => Apart(quarter.End, label) >= Apart(quarter.End, end)))
            .OrderBy(quarter => Apart(quarter.End, end))
            .FirstOrDefault()?.Value;

        return
        [
            .. fetched.Select(quarter =>
            {
                var source = sourced.SourceOf(quarter.PeriodEnd) ?? QuarterSource.NotOnFile;
                var lines = sourced.Quarters.FirstOrDefault(row => row.PeriodEnd == quarter.PeriodEnd);

                return new CardQuarter(
                    quarter.PeriodEnd,
                    quarter.FilingDate,
                    source,
                    lines?.NetIncome,
                    lines?.OperatingIncome,
                    source == QuarterSource.Filing ? InterestAt(quarter.PeriodEnd) : null);
            }),
        ];
    }
}
