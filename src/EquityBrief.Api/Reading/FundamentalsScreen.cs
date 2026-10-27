using System.Globalization;
using EquityBrief.Core.Insiders;
using EquityBrief.Core.Ledger;
using EquityBrief.Core.Quarters;
using EquityBrief.Core.Report;
using EquityBrief.Web.Marks;

namespace EquityBrief.Api.Reading;

// A stock's fundamentals card and the brief read's line from it, written by code from templates over the scorecard the
// night stored: the word and the count, each check a sentence with its figures, a newer quarter reported and not yet
// filed, and four lines not scored, its sales against a year before off the ledger's readings of the night, gross profit
// over assets with its place on its index, its price over earnings against its own last twelve quarters and its
// industry's median, and the analysts' changes to their estimates over 30 days with the insiders' open-market trades over
// 90. A figure not stored says so in its line.
// see: The fundamentals scorecard scores every member on nine checks of its filings as first filed, and only the quality family's rule reads it
public static class FundamentalsScreen
{
    // The checks' names, as each sentence not scored opens.
    static readonly IReadOnlyDictionary<int, string> Names = new Dictionary<int, string>
    {
        [1] = "Net income",
        [2] = "Cash from operations",
        [3] = "Return on assets",
        [4] = "Cash from operations against net income",
        [5] = "Long-term debt against assets",
        [6] = "Current assets against current liabilities",
        [7] = "Shares outstanding",
        [8] = "Gross margin",
        [9] = "Sales against assets",
    };

    public static FundamentalsView Of(FundamentalScoreRow? score, string? indexName, IReadOnlyList<double?>? readings, AnalystReading? analysts, InsiderView? insiders)
    {
        var index = indexName ?? "index";

        return new FundamentalsView(
            Summary(score),
            score?.Word,
            score is null ? [] : [.. score.Checks.Select(Check)],
            score is { Awaited: { } awaited, Quarter: { } quarter }
                ? Invariant($"Its quarter to {awaited:yyyy-MM-dd} has been reported, but its filing is not stored yet; these checks read the quarter to {quarter:yyyy-MM-dd}.")
                : null,
            [Sales(readings), Gross(score, index), Price(score, readings, index), Estimates(analysts, insiders)]);
    }

    // The brief read's line in its business panel.
    public static BriefLine Line(FundamentalScoreRow? score) =>
        new("fundamentals", score switch
        {
            { Word: { } word, Quarter: { } quarter } => Invariant($"Fundamentals: {word}, {score.Passed} of {score.Applied} checks on its filings to {quarter:yyyy-MM-dd}, in the card below."),
            _ => "Fundamentals: " + Lowered(Summary(score)),
        }, score?.Word);

    static string Summary(FundamentalScoreRow? score) => score switch
    {
        null => "Not scored, as no scorecard is stored for it on the night.",
        { Quarter: null } => "Not scored, as none of its quarters' filings is stored.",
        { Word: { } word, Quarter: { } quarter } => Invariant($"{Capitalised(word)}: {score.Passed} of {score.Applied} checks, on its filings to {quarter:yyyy-MM-dd}."),
        { Applied: 0, Quarter: { } quarter } => Invariant($"Not scored, as none of its checks could be read from its filings to {quarter:yyyy-MM-dd}."),
        { Quarter: { } quarter } => Invariant($"Not scored, as only {score.Applied} of its checks could be read from its filings to {quarter:yyyy-MM-dd}."),
    };

    static FundamentalsCheckLine Check(ScorecardCheck check)
    {
        var answer = ScorecardText.Word(check.Answer);
        var name = Names[check.Number];

        if (check.Answer == CheckAnswer.NotApplying)
        {
            return new(check.Number, answer, $"{name}: not scored, as it does not apply to a financial company.");
        }

        if (check.Answer == CheckAnswer.NotFiled)
        {
            return new(check.Number, answer, $"{name}: not scored, as its {check.Missing ?? "figure"} is not stored for the periods it reads.");
        }

        return new(check.Number, answer, check.Number switch
        {
            1 => check.Amount is { } income && income > 0m
                ? $"Net income over the last four quarters, {Figures.Money(income)}, was above zero."
                : $"Net income over the last four quarters was {(check.Amount is { } loss && loss < 0m ? "a loss of " + Figures.Money(-loss) : "zero")}.",
            2 => check.Amount is { } cash && cash > 0m
                ? $"Cash from operations over the last four quarters, {Figures.Money(cash)}, was above zero."
                : $"Cash from operations over the last four quarters was {Figures.Money(check.Amount ?? 0m)}, not above zero.",
            3 => $"Return on assets was {Share(check.Now)}, {Compared(check.Now, check.YearBefore)} a year ago's {Share(check.YearBefore)}.",
            4 => $"Cash from operations, {Figures.Money(check.Amount ?? 0m)}, was {(check.Answer == CheckAnswer.Passed ? "above" : "not above")} net income, {Figures.Money(check.Against ?? 0m)}.",
            5 => $"Long-term debt was {Share(check.Now)} of its average assets, {Compared(check.Now, check.YearBefore)} a year ago's {Share(check.YearBefore)}.",
            6 => $"Current assets were {Times(check.Now)} current liabilities, {Compared(check.Now, check.YearBefore)} a year ago's {Times(check.YearBefore)}.",
            7 => Shares(check),
            8 => $"Gross margin was {Share(check.Now)}, {Compared(check.Now, check.YearBefore)} a year ago's {Share(check.YearBefore)}.",
            _ => $"Sales were {Times(check.Now)} assets, {Compared(check.Now, check.YearBefore)} a year ago's {Times(check.YearBefore)}.",
        });
    }

    static string Shares(ScorecardCheck check)
    {
        var allowed = Figures.Share(FundamentalScorecard.ShareTolerance);
        var change = check.Now ?? 0;
        var moved = change > 0 ? "rose " + Figures.Share(change) : change < 0 ? "fell " + Figures.Share(-change) : "were unchanged";

        return check.Answer == CheckAnswer.Passed
            ? $"Shares outstanding {moved} over the year, within the {allowed} allowed."
            : $"Shares outstanding {moved} over the year, more than the {allowed} allowed.";
    }

    static BriefLine Sales(IReadOnlyList<double?>? readings)
    {
        if (Reading(readings, "revenue_growth") is not { } growth)
        {
            return new BriefLine("sales", "Sales against a year before: not read from its filings.");
        }

        var moved = growth >= 0 ? "grew " + Figures.Share(growth) : "fell " + Figures.Share(-growth);
        var pace = Reading(readings, "growth_change") switch
        {
            > 0 => ", faster than in the quarter before",
            < 0 => ", slower than in the quarter before",
            0 => ", as fast as in the quarter before",
            _ => string.Empty,
        };

        return new BriefLine("sales", $"Sales {moved} on a year in the newest quarter{pace}.", growth.ToString("R", CultureInfo.InvariantCulture));
    }

    static BriefLine Gross(FundamentalScoreRow? score, string index) =>
        score is { GrossProfitability: { } gross, GrossRank: { } rank, GrossRanked: { } ranked }
            ? new BriefLine("gross", Invariant($"Gross profit was {Figures.Share(gross)} of its assets, {Ordinal(rank)} of the {ranked} {index} stocks with the figure."), gross.ToString("R", CultureInfo.InvariantCulture))
            : new BriefLine("gross", "Gross profit against assets: not read from its filings.");

    static BriefLine Price(FundamentalScoreRow? score, IReadOnlyList<double?>? readings, string index)
    {
        if (score?.Multiple is not { } multiple)
        {
            return new BriefLine("price", "Price against earnings: not read, as it earned nothing over its last four quarters or no share count is filed.");
        }

        var own = Reading(readings, "value_place") is { } place
            ? Invariant($", cheaper than in {(int)Math.Round(place * Core.Ledger.ValuationReading.Quarters, MidpointRounding.AwayFromZero)} of its own last {Core.Ledger.ValuationReading.Quarters} quarters")
            : string.Empty;
        var industry = score.IndustryMultiple is { } median
            ? Invariant($"; its industry's median on the {index} is {median:0.0}")
            : "; its industry's median is not read";

        return new BriefLine(
            "price",
            $"It trades at {MarkRenderer.EarningsMultiple(multiple, filed: true)}{own}{industry}.",
            multiple.ToString("R", CultureInfo.InvariantCulture),
            Concept: score.IndustryMultiple is null || score.Industry is null ? null : EquityBrief.Core.Report.ConceptCheck.MedianOf(score.Industry),
            ConceptValue: score.IndustryMultiple is { } drawnMedian ? drawnMedian.ToString("0.0", CultureInfo.InvariantCulture) : null);
    }

    static BriefLine Estimates(AnalystReading? analysts, InsiderView? insiders)
    {
        var year = analysts?.Revisions.FirstOrDefault(row => row.Period == EstimatePeriod.CurrentYear);
        var estimates = year is { Up30: { } up, Down30: { } down }
            ? Invariant($"Over 30 days {up} analysts raised and {down} cut their estimate for the year")
            : "The analysts' changes to their estimates over 30 days are not stored";
        var trades = insiders is null
            ? "insiders' open-market trades over 90 days are not read"
            : insiders.Ninety is { Bought: 0m, Sold: 0m }
                ? "insiders made no open-market trade over 90 days"
                : $"over 90 days insiders bought {Figures.Money(insiders.Ninety.Bought)} and sold {Figures.Money(insiders.Ninety.Sold)} in the open market";

        return new BriefLine("estimates", $"{estimates}; {trades}.");
    }

    static double? Reading(IReadOnlyList<double?>? readings, string column) =>
        readings is not null && LedgerReadings.IndexOf(column) is var at && at < readings.Count ? readings[at] : null;

    static string Compared(double? now, double? before) =>
        now is { } current && before is { } then
            ? current > then ? "up from" : current < then ? "down from" : "the same as"
            : "against";

    static string Share(double? value) => value is { } held ? Figures.Share(held) : "none";

    static string Times(double? value) => value is { } held ? held.ToString("0.00", CultureInfo.InvariantCulture) + " times" : "none";

    static string Ordinal(int place) =>
        (place % 100) is 11 or 12 or 13 ? Invariant($"{place}th")
        : (place % 10) switch
        {
            1 => Invariant($"{place}st"),
            2 => Invariant($"{place}nd"),
            3 => Invariant($"{place}rd"),
            _ => Invariant($"{place}th"),
        };

    static string Capitalised(string word) => word.Length == 0 ? word : char.ToUpperInvariant(word[0]) + word[1..];

    static string Lowered(string sentence) => sentence.Length == 0 ? sentence : char.ToLowerInvariant(sentence[0]) + sentence[1..];

    static string Invariant(FormattableString text) => text.ToString(CultureInfo.InvariantCulture);
}
