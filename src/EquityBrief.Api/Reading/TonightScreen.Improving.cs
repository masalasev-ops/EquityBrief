using EquityBrief.Core.Bars;
using EquityBrief.Core.Families;
using EquityBrief.Core.Ledger;
using EquityBrief.Web.Marks;

namespace EquityBrief.Api.Reading;

// The projection from the Fundamentals family's stored rows to its card on Tonight. It reads what the step stored, each
// change since a night read off the growth the step carried on each member's answer, so the card and the step say one
// thing; the scorecard's own checks for what a newly read filing changed; and the rule's own order for the improvers.
// see: A screen reads and renders, and each figure it works out at a price or from stored figures has one function in the core
// see: The Fundamentals family buys a company whose newly read filing lifts its scorecard to eight in nine, and flags one whose release reads better
public static partial class TonightScreen
{
    // Each check as a sentence of what changed names it.
    static readonly IReadOnlyDictionary<int, string> CheckChanges = new Dictionary<int, string>
    {
        [1] = "net income above zero",
        [2] = "cash from operations above zero",
        [3] = "a higher return on assets",
        [4] = "cash from operations above net income",
        [5] = "less long-term debt against its assets",
        [6] = "more current assets against current liabilities",
        [7] = "no new shares",
        [8] = "a wider gross margin",
        [9] = "more sales against its assets",
    };

    // The card on a night: tonight's improvers in the rule's order, each with what changed and its status; the index's ten
    // highest scores with their business, their price against their own past and what the quality book says of them; the
    // trades held with their change since bought beside the index's stocks over the same nights; and the stocks reporting
    // in the next five trading days.
    public static ImprovingCardView Improving(
        string index,
        DateOnly night,
        ImprovingNightRow? read,
        IReadOnlyList<ImprovingResultRow> tonight,
        IReadOnlyDictionary<DateOnly, IReadOnlyList<ImprovingResultRow>> answersOn,
        IReadOnlyList<ImprovingTradeRow> trades,
        IReadOnlyList<ImprovingScoreRow> scores,
        IReadOnlyDictionary<string, IReadOnlyList<double?>> readings,
        IReadOnlyDictionary<string, string> quality,
        IReadOnlyDictionary<string, UniverseCell> cells,
        IReadOnlyList<(string Ticker, DateOnly Date)>? reports)
    {
        var settings = ImprovingRule.Starting;
        var checksOf = scores.ToDictionary(score => score.Ticker, score => score.Checks, StringComparer.Ordinal);
        var byTicker = tonight.ToDictionary(row => row.Ticker, StringComparer.Ordinal);
        string? Company(string ticker) => cells.TryGetValue(ticker, out var cell) ? cell.Name : null;

        var ranked = ImprovingRule.Ranked(
            tonight.Where(row => row.Status is not null),
            row => (row is { BeforePassed: { } before, BeforeApplied: { } applied } && row.Status != ImprovingRule.Flagged ? ImprovingRule.Rise(row.PassedChecks, row.Applied, before, applied) : null, row.GrowthChange, row.Ticker));

        ImprovingRowCell[] improving =
        [
            .. ranked.Select(row => new ImprovingRowCell(
                row.Ticker,
                Company(row.Ticker),
                row.PassedChecks,
                row.Applied,
                row.BeforePassed,
                row.BeforeApplied,
                ImprovingChanged(row, checksOf.GetValueOrDefault(row.Ticker)),
                ImprovingStatusWords(row.Status),
                ImprovingStatusLine(row, checksOf.GetValueOrDefault(row.Ticker), trades, settings))),
        ];

        StrongestCell[] strongest =
        [
            .. scores
                .Where(score => score.Applied >= FundamentalScorecard.FewestRead)
                .OrderByDescending(score => ImprovingRule.Share(score.Passed, score.Applied))
                .ThenBy(score => score.GrossProfitability is null)
                .ThenByDescending(score => score.GrossProfitability ?? 0)
                .ThenBy(score => score.Ticker, StringComparer.Ordinal)
                .Take(MarkRenderer.StrongestShown)
                .Select(score => new StrongestCell(
                    score.Ticker,
                    Company(score.Ticker),
                    score.Passed,
                    score.Applied,
                    ImprovingBusiness(readings.GetValueOrDefault(score.Ticker), score.GrossProfitability),
                    ImprovingReading(readings.GetValueOrDefault(score.Ticker), "value_place") is { } place
                        ? "Its price is " + MarkRenderer.PlaceWords(place) + "."
                        : "Its price over earnings is not read.",
                    quality.GetValueOrDefault(score.Ticker))),
        ];

        // The trades held after the night, each its change since bought beside the members answered on the night it was
        // chosen over the same nights, both the mean of each trade's.
        var held = trades.Where(trade => trade.EndReason == ImprovingRule.Open && trade.ChosenOn < night).ToArray();
        var since = new List<double>();
        var members = new List<double>();

        foreach (var trade in held)
        {
            if (trade is not { BoughtOn: not null, Gap: { } gap, BenchGap: { } benchGap }
                || !answersOn.TryGetValue(trade.ChosenOn, out var then)
                || then.FirstOrDefault(row => row.Ticker == trade.Ticker) is not { Carried: > 0 } start
                || !byTicker.TryGetValue(trade.Ticker, out var now))
            {
                continue;
            }

            double[] grown = [.. then.Where(row => row.Carried > 0 && byTicker.ContainsKey(row.Ticker)).Select(row => byTicker[row.Ticker].Carried / row.Carried)];

            since.Add((now.Carried / start.Carried) - 1.0 - gap);

            if (grown.Length > 0)
            {
                members.Add(grown.Average() - 1.0 - benchGap);
            }
        }

        // The members reporting in the next five trading days.
        int? reporting = null;

        if (reports is not null)
        {
            var through = ReportingThrough(night);

            reporting = reports.Where(report => report.Date > night && report.Date <= through && cells.ContainsKey(report.Ticker)).Select(report => report.Ticker).Distinct(StringComparer.Ordinal).Count();
        }

        return new ImprovingCardView(
            index,
            night,
            read is not null,
            read?.Fault,
            tonight.Count(row => row.Status == ImprovingRule.Bought),
            tonight.Count(row => row.Status == ImprovingRule.Flagged),
            held.Length,
            since.Count > 0 ? since.Average() : null,
            members.Count > 0 ? members.Average() : null,
            reporting,
            improving,
            strongest,
            read?.Settings ?? ImprovingRule.Starting.Words);
    }

    // What a stock's page says where the Fundamentals family bought it on its night, keeps a trade in it from an earlier
    // night or flagged it on its results release, and nothing where it did none of these.
    public static string? OnTheFundamentalsCard(string ticker, DateOnly night, IReadOnlyList<ImprovingResultRow> tonight, IReadOnlyList<ImprovingTradeRow> trades)
    {
        var settings = ImprovingRule.Starting;
        var row = tonight.FirstOrDefault(one => one.Ticker == ticker);

        if (row is { Status: ImprovingRule.Bought, Filed: { } filed })
        {
            return FormattableString.Invariant($"Fundamentals buys it at the next open: its filing of {filed:yyyy-MM-dd} lifted its score to {row.PassedChecks} of {row.Applied}") +
                (row is { BeforePassed: { } before, BeforeApplied: { } applied } ? FormattableString.Invariant($", from {before} of {applied}.") : ".");
        }

        if (trades.FirstOrDefault(one => one.Ticker == ticker && one.EndReason == ImprovingRule.Open && one.ChosenOn < night) is { } open)
        {
            return (open.BoughtOn is { } bought
                    ? FormattableString.Invariant($"Fundamentals bought it on {bought:yyyy-MM-dd}")
                    : FormattableString.Invariant($"Fundamentals chose it on {open.ChosenOn:yyyy-MM-dd}"))
                + FormattableString.Invariant($" and keeps it while its score stays at {settings.HoldInNine} of 9 or more.");
        }

        return row is { Status: ImprovingRule.Flagged }
            ? (row.Released is { } released
                ? FormattableString.Invariant($"Fundamentals flagged it: its results release of {released:yyyy-MM-dd} reads results better than expected. ")
                : "Fundamentals flagged it: its results release reads results better than expected. ")
              + FormattableString.Invariant($"It is bought only if its filing lifts it to {settings.BuyInNine} of 9.")
            : null;
    }

    // The last day "reporting this week" counts: the fifth trading day after the night on the exchange's calendar.
    public static DateOnly ReportingThrough(DateOnly night)
    {
        var through = night;

        for (var counted = 0; counted < MarkRenderer.ReportingDays && through < ExchangeClosures.CoveredThrough;)
        {
            through = through.AddDays(1);

            if (ExchangeClosures.IsSession(through))
            {
                counted++;
            }
        }

        return through;
    }

    static string ImprovingStatusWords(string? status) => status switch
    {
        ImprovingRule.Bought => MarkRenderer.BoughtWords,
        ImprovingRule.Flagged => MarkRenderer.FlaggedWords,
        ImprovingRule.Held => MarkRenderer.HeldWords,
        _ => MarkRenderer.ImprovedWords,
    };

    // What changed, in one sentence from the scorecard's own checks: the checks the newly read filing passes that the one
    // before did not and those it no longer passes; or for a stock flagged from its release, what the release read.
    public static string ImprovingChanged(ImprovingResultRow row, string? checks)
    {
        if (row.Status == ImprovingRule.Flagged)
        {
            return row.Released is { } released
                ? FormattableString.Invariant($"Its results release of {released:d MMM} reads results better than expected, its outlook {row.Outlook ?? "given"}.")
                : "Its results release reads results better than expected.";
        }

        if (row.BeforeChecks is not { } stored || checks is null)
        {
            return FormattableString.Invariant($"Its new filing scores {row.PassedChecks} of {row.Applied}.");
        }

        var before = ScorecardText.ChecksOf(stored).ToDictionary(check => check.Number, check => check.Answer);
        var now = ScorecardText.ChecksOf(checks);
        string[] gained = [.. now.Where(check => check.Answer == CheckAnswer.Passed && before.GetValueOrDefault(check.Number) != CheckAnswer.Passed).Select(check => CheckChanges[check.Number])];
        string[] lost = [.. now.Where(check => check.Answer == CheckAnswer.Failed && before.GetValueOrDefault(check.Number) == CheckAnswer.Passed).Select(check => CheckChanges[check.Number])];

        if (gained.Length == 0)
        {
            return FormattableString.Invariant($"Its new filing scores {row.PassedChecks} of {row.Applied}, where the one before scored {row.BeforePassed} of {row.BeforeApplied}.");
        }

        return lost.Length == 0
            ? $"Its new filing shows {ImprovingListed(gained)}, which the one before did not."
            : $"Its new filing shows {ImprovingListed(gained)}, which the one before did not; it no longer shows {ImprovingListed(lost)}.";
    }

    // The line beneath a row's status: the filing and the release behind a buy, what a flag waits for, the check a stock
    // short of the bar misses, or when a stock already held was chosen.
    static string ImprovingStatusLine(ImprovingResultRow row, string? checks, IReadOnlyList<ImprovingTradeRow> trades, ImprovingSettings settings)
    {
        var trade = trades.FirstOrDefault(one => one.Ticker == row.Ticker && one.EndReason == ImprovingRule.Open);

        switch (row.Status)
        {
            case ImprovingRule.Bought:
                return trade is { Released: { } released }
                    ? FormattableString.Invariant($"Its filing of {trade.Filed:d MMM} confirmed the release of {released:d MMM}.")
                    : row.Filed is { } filed
                        ? FormattableString.Invariant($"Its filing of {filed:d MMM} lifted it to {row.PassedChecks} of {row.Applied}.")
                        : "Its new filing lifted it over the bar.";
            case ImprovingRule.Flagged:
                return FormattableString.Invariant($"Bought only if its filing lifts it to {settings.BuyInNine} of 9.");
            case ImprovingRule.Held:
                return trade is { } open
                    ? FormattableString.Invariant($"Chosen on {open.ChosenOn:d MMM}; it holds one trade a stock.")
                    : "It holds one trade a stock.";
        }

        return row.Reason switch
        {
            ImprovingRule.UnderTheBar => checks is null
                ? FormattableString.Invariant($"It needs {settings.BuyInNine} of 9.")
                : FormattableString.Invariant($"It needs {settings.BuyInNine} of 9 and misses {ImprovingListed([.. ScorecardText.ChecksOf(checks).Where(check => check.Answer == CheckAnswer.Failed).Select(check => CheckChanges[check.Number])])}."),
            ImprovingRule.NotFaster => "Its sales growth did not speed up on the quarter before.",
            ImprovingRule.GrowthNotRead => "Its sales growth could not be read from its filings.",
            ImprovingRule.NoWord => "Too few of its checks could be read for a score.",
            _ => "It falls short of the bar.",
        };
    }

    // A stock's business in one sentence: its sales over a year and how that pace moved, and its gross profit over assets.
    static string ImprovingBusiness(IReadOnlyList<double?>? readings, double? gross)
    {
        var sales = ImprovingReading(readings, "revenue_growth") is { } growth
            ? (growth >= 0 ? "Sales grew " + Figures.Share(growth) : "Sales fell " + Figures.Share(-growth)) + " on a year" + ImprovingReading(readings, "growth_change") switch
            {
                > 0 => ", faster than in the quarter before",
                < 0 => ", slower than in the quarter before",
                _ => string.Empty,
            }
            : "Its sales against a year before are not read";

        return gross is { } profit ? $"{sales}; gross profit was {Figures.Share(profit)} of its assets." : sales + ".";
    }

    static double? ImprovingReading(IReadOnlyList<double?>? readings, string column) =>
        readings is not null && LedgerReadings.IndexOf(column) is var at && at < readings.Count ? readings[at] : null;

    static string ImprovingListed(IReadOnlyList<string> names) =>
        names.Count switch
        {
            0 => "none of its checks",
            1 => names[0],
            _ => string.Join(", ", names.Take(names.Count - 1)) + " and " + names[^1],
        };
}
