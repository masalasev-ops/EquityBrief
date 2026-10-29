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
        IReadOnlyList<(string Ticker, DateOnly Date)>? reports,
        DateOnly? liveSince = null,
        QualityFixedView? fixedView = null)
    {
        var setting = SettingOf(read?.Settings);
        var settings = new ImprovingSettings(setting.BuyInNine, setting.HoldInNine, setting.Sessions);
        var checksOf = scores.ToDictionary(score => score.Ticker, score => score.Checks, StringComparer.Ordinal);
        var byTicker = tonight.ToDictionary(row => row.Ticker, StringComparer.Ordinal);
        string? Company(string ticker) => cells.TryGetValue(ticker, out var cell) ? cell.Name : null;

        // A member whose scorecard read no check is never ranked: a trade held in one is drawn after the ranked rows, its row
        // saying no check could be read.
        // see: A Fundamentals flag stands only for a release reporting a quarter newer than the member's newest stored filing, accepted within the last 63 sessions
        IReadOnlyList<ImprovingResultRow> ranked =
        [
            .. ImprovingRule.Ranked(
                tonight.Where(row => row.Status is not null && row.Applied > 0),
                row => (row is { BeforePassed: { } before, BeforeApplied: { } applied } && row.Status != ImprovingRule.Flagged ? ImprovingRule.Rise(row.PassedChecks, row.Applied, before, applied) : null, row.GrowthChange, row.Ticker)),
            .. tonight.Where(row => row.Status == ImprovingRule.Held && row.Applied == 0).OrderBy(row => row.Ticker, StringComparer.Ordinal),
        ];

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
            read?.Settings ?? ImprovingRule.Starting.Words,
            setting,
            liveSince,
            fixedView);
    }

    // The setting a night's row states, read off the six dials its own JSON carries; the starting setting where it states
    // none, and the starting setting's checks where a row written before the grid's dials were stored states only the
    // three the family started with.
    public static ImprovingSweepSetting SettingOf(string? settings)
    {
        if (settings is null)
        {
            return ImprovingGrid.Starting;
        }

        try
        {
            using var document = System.Text.Json.JsonDocument.Parse(settings);
            var root = document.RootElement;

            static bool Flag(System.Text.Json.JsonElement root, string name, bool otherwise) =>
                root.TryGetProperty(name, out var value) && value.ValueKind is System.Text.Json.JsonValueKind.True or System.Text.Json.JsonValueKind.False ? value.GetBoolean() : otherwise;

            return root.TryGetProperty("buy", out var buy) && root.TryGetProperty("hold", out var hold) && root.TryGetProperty("sessions", out var sessions)
                ? new ImprovingSweepSetting(buy.GetInt32(), Flag(root, "flag", false), Flag(root, "rise", true), Flag(root, "faster", true), hold.GetInt32(), sessions.GetInt32())
                : ImprovingGrid.Starting;
        }
        catch (System.Text.Json.JsonException)
        {
            return ImprovingGrid.Starting;
        }
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

        // A note of the Fundamentals family's own, which says so and that only that family's buy waits on the filing.
        // see: How the rules read it opens on the checks of the family that picked the stock, and every input that failed is named in plain words
        return row is { Status: ImprovingRule.Flagged }
            ? (row.Released is { } released
                ? FormattableString.Invariant($"Fundamentals flagged it: its results release of {released:yyyy-MM-dd} reads results better than expected. ")
                : "Fundamentals flagged it: its results release reads results better than expected. ")
              + FormattableString.Invariant($"The Fundamentals family buys it only if its filing lifts it to {settings.BuyInNine} of 9, and that buy waits for the filing; no other family's buy waits on it.")
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

    // What changed on its new report, from the figures the night stored: its sales growth's pace where the step read it, then
    // each check the newly read filing passes that the one before did not, with that check's own figures, and the checks it
    // no longer passes; or for a stock flagged from its release, what the release's answers read.
    public static string ImprovingChanged(ImprovingResultRow row, string? checks)
    {
        if (row.Status == ImprovingRule.Flagged)
        {
            var outlook = row.Outlook switch
            {
                "raised" => "a raised outlook",
                "kept" => "an unchanged outlook",
                { } other => "an outlook " + other,
                null => "an outlook it gave",
            };

            return row.Released is { } released
                ? FormattableString.Invariant($"Its results release of {released:d MMM} reads results above expectations and {outlook}.")
                : $"Its results release reads results above expectations and {outlook}.";
        }

        var said = new List<string>();

        if (row is { Growth: { } growth, GrowthChange: { } change } && change != 0)
        {
            said.Add($"Sales growth {(change > 0 ? "sped up" : "slowed")} from {Figures.Share(growth - change)} to {Figures.Share(growth)} on a year.");
        }

        if (row.BeforeChecks is { } stored && checks is not null)
        {
            var before = ScorecardText.ChecksOf(stored).ToDictionary(check => check.Number, check => check.Answer);
            var now = ScorecardText.ChecksOf(checks);
            string[] lost = [.. now.Where(check => check.Answer == CheckAnswer.Failed && before.GetValueOrDefault(check.Number) == CheckAnswer.Passed).Select(check => CheckChanges[check.Number])];

            said.AddRange(now.Where(check => check.Answer == CheckAnswer.Passed && before.GetValueOrDefault(check.Number) != CheckAnswer.Passed).Select(Gained));

            if (lost.Length > 0)
            {
                said.Add($"It no longer shows {ImprovingListed(lost)}.");
            }
        }

        if (said.Count > 0)
        {
            return string.Join(" ", said);
        }

        return row is { BeforePassed: { } beforePassed, BeforeApplied: { } beforeApplied }
            ? FormattableString.Invariant($"Its new filing scores {row.PassedChecks} of {row.Applied}, where the one before scored {beforePassed} of {beforeApplied}.")
            : FormattableString.Invariant($"Its new filing scores {row.PassedChecks} of {row.Applied}.");
    }

    // A check the newly read filing passes that the one before did not, in a sentence carrying the check's own figures, its
    // year against the year before, as the scorecard stored them.
    static string Gained(ScorecardCheck check)
    {
        static string Ratio(double value) => value.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture);

        return check.Number switch
        {
            1 => "Its profit over the last four quarters turned positive.",
            2 => "Its cash from operations over the last four quarters turned positive.",
            3 => check is { YearBefore: { } before, Now: { } now }
                ? $"Return on assets rose from {Figures.Share(before)} to {Figures.Share(now)}."
                : "Return on assets rose.",
            4 => "It now brings in more cash than it reports as profit.",
            5 => check is { YearBefore: { } before, Now: { } now }
                ? $"Its long-term debt fell from {Figures.Share(before)} to {Figures.Share(now)} of its assets."
                : "Its long-term debt fell against its assets.",
            6 => check is { YearBefore: { } before, Now: { } now }
                ? $"Current assets now cover current liabilities {Ratio(now)} times, up from {Ratio(before)}."
                : "Current assets cover more of current liabilities.",
            7 => check.Now is { } moved && moved < 0
                ? $"Shares outstanding fell {Figures.Share(-moved)}."
                : "It issued no new shares beyond the scorecard's allowance.",
            8 => check is { YearBefore: { } before, Now: { } now }
                ? $"Gross margin widened from {Figures.Share(before)} to {Figures.Share(now)}."
                : "Gross margin widened.",
            _ => check is { YearBefore: { } before, Now: { } now }
                ? $"Sales rose to {Ratio(now)} times its assets, up from {Ratio(before)}."
                : "Sales rose against its assets.",
        };
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
            ImprovingRule.UnderTheBar => FormattableString.Invariant($"Needs {settings.BuyInNine} of 9; {ChecksShort(row.PassedChecks, row.Applied, settings)}")
                + (checks is null ? "." : $", missing {ImprovingListed([.. ScorecardText.ChecksOf(checks).Where(check => check.Answer == CheckAnswer.Failed).Select(check => CheckChanges[check.Number])])}.")
                + (row.GrowthChange is { } change && change <= 0 ? " Its sales are still slowing too." : string.Empty),
            ImprovingRule.NotFaster => FormattableString.Invariant($"Has {row.PassedChecks} of {row.Applied}, but its sales are still slowing."),
            ImprovingRule.GrowthNotRead => FormattableString.Invariant($"Has {row.PassedChecks} of {row.Applied}, but its sales growth could not be read from its filings."),
            ImprovingRule.NoWord => "Too few of its checks could be read for a score.",
            _ => "It falls short of the bar.",
        };
    }

    // How many more checks a stock needs to clear the bar, in words, read through the scorecard's own test of the bar.
    static string ChecksShort(int passed, int applied, ImprovingSettings settings)
    {
        string[] words = ["one", "two", "three", "four", "five", "six", "seven", "eight", "nine"];
        var more = Enumerable.Range(1, Math.Max(applied - passed, 0)).FirstOrDefault(extra => FundamentalScorecard.Clears(passed + extra, applied, settings.BuyInNine));

        return more switch
        {
            0 => "short of the bar",
            1 => "one check short",
            _ => words[more - 1] + " checks short",
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
