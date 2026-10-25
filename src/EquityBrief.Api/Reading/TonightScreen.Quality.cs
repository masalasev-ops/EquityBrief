using EquityBrief.Core.Bars;
using EquityBrief.Core.Families;
using EquityBrief.Web.Marks;

namespace EquityBrief.Api.Reading;

// The projection from Quality at a fair price's stored rows to its card on Tonight. It reads what the step stored, each
// change since a night read off the growth the step carried on each member's answer, so the card and the step say one
// thing, and the rule's own functions for whether a night reshuffles and when the next one falls.
// see: A screen reads and renders, and each figure it works out at a price or from stored figures has one function in the core
// see: A quality holding's edge is its return less an equal-weighted hold of its index's members over the same sessions, after its round trip
public static partial class TonightScreen
{
    // The card on a night: the holdings open at its close, each with its score and its place tonight, its change since it
    // was bought and tonight's line; what the last reshuffle chose and each holding sold since it; the holdings' change
    // since that reshuffle beside an equal-weighted hold of the members answered on it; the next names in line; and the
    // next reshuffle on the exchange's calendar.
    public static QualityCardView Quality(
        string index,
        DateOnly night,
        QualityNightRow? read,
        DateOnly? lastReshuffle,
        IReadOnlyList<QualityHoldingRow> holdings,
        IReadOnlyList<QualityResultRow> tonight,
        IReadOnlyDictionary<DateOnly, IReadOnlyList<QualityResultRow>> answersOn,
        IReadOnlyDictionary<string, UniverseCell> cellByTicker,
        DateOnly? liveSince = null,
        QualityFixedView? fixedView = null)
    {
        var (holdingsKept, words) = read is not null && SettingsRead(read.Settings) is { } stored ? stored : (QualityRule.Starting.Holdings, QualityRule.Starting.Words);
        var byTicker = tonight.ToDictionary(row => row.Ticker, StringComparer.Ordinal);
        var open = holdings.Where(holding => holding.EndedOn is null).OrderBy(holding => holding.ChosenOn).ThenBy(holding => holding.Ticker, StringComparer.Ordinal).ToArray();

        QualityResultRow? AnswerOn(DateOnly on, string ticker) =>
            answersOn.TryGetValue(on, out var rows) ? rows.FirstOrDefault(row => row.Ticker == ticker) : null;

        QualityHoldingCell[] cells =
        [
            .. open.Select(holding =>
            {
                var now = byTicker.GetValueOrDefault(holding.Ticker);
                double? since = holding is { BoughtOn: not null, Gap: { } gap } && now is not null && AnswerOn(holding.ChosenOn, holding.Ticker) is { Carried: > 0 } start
                    ? (now.Carried / start.Carried) - 1.0 - gap
                    : null;

                return new QualityHoldingCell(
                    holding.Ticker,
                    cellByTicker.TryGetValue(holding.Ticker, out var cell) ? cell.Name : null,
                    holding.ChosenOn,
                    now?.PassedChecks,
                    now?.Applied,
                    now?.ValuePlace,
                    since,
                    TonightLine(holding, now, night, AnswerOn(lastReshuffle ?? holding.ChosenOn, holding.Ticker)));
            }),
        ];

        // The holdings' change since the last reshuffle beside the index's members over the same nights: each the mean of
        // its growth tonight over its growth on that night, less one.
        double? holdingsSince = null;
        double? membersSince = null;

        if (lastReshuffle is { } reshuffle && answersOn.TryGetValue(reshuffle, out var then))
        {
            var at = then.Where(row => row.Carried > 0).ToDictionary(row => row.Ticker, row => row.Carried, StringComparer.Ordinal);
            double[] held = [.. open.Where(holding => at.ContainsKey(holding.Ticker) && byTicker.ContainsKey(holding.Ticker)).Select(holding => (byTicker[holding.Ticker].Carried / at[holding.Ticker]) - 1.0)];
            double[] members = [.. tonight.Where(row => at.ContainsKey(row.Ticker)).Select(row => (row.Carried / at[row.Ticker]) - 1.0)];

            holdingsSince = held.Length > 0 ? held.Average() : null;
            membersSince = members.Length > 0 ? members.Average() : null;
        }

        var next = QualityRule.NextReshuffle(night, lastReshuffle);

        return new QualityCardView(
            index,
            night,
            read is not null,
            read?.Fault,
            holdingsKept,
            lastReshuffle,
            next,
            next is { } coming ? ExchangeClosures.SessionsUntil(night, coming) : null,
            holdingsSince,
            membersSince,
            cells,
            lastReshuffle is { } chosenOn ? [.. holdings.Where(holding => holding.ChosenOn == chosenOn).Select(holding => holding.Ticker)] : [],
            [.. holdings.Where(holding => holding.EndedOn is { } end && end <= night).OrderBy(holding => holding.EndedOn).ThenBy(holding => holding.Ticker, StringComparer.Ordinal).Select(holding => new QualitySoldCell(holding.Ticker, holding.EndedOn!.Value, holding.EndReason))],
            [.. tonight.Where(row => row.Passed && open.All(holding => holding.Ticker != row.Ticker)).Take(MarkRenderer.QualityNextShown)
                .Select(row => new QualityNextCell(row.Ticker, cellByTicker.TryGetValue(row.Ticker, out var cell) ? cell.Name : null, row.PassedChecks, row.Applied, row.ValuePlace))],
            words,
            liveSince,
            fixedView);
    }

    // The holdings a rule keeps and its words, as the night stored its settings on its row; none where the row's settings
    // cannot be read.
    public static (int Holdings, string Words)? SettingsRead(string settings)
    {
        try
        {
            using var document = System.Text.Json.JsonDocument.Parse(settings);
            var root = document.RootElement;

            return root.TryGetProperty("holdings", out var holdings) && root.TryGetProperty("words", out var words) && words.GetString() is { } said
                ? (holdings.GetInt32(), said)
                : null;
        }
        catch (System.Text.Json.JsonException)
        {
            return null;
        }
    }

    // From 22.11 a fixed card's line read off the answer its history test recorded before its freeze, none where none was
    // recorded or it states no figures.
    // see: A frozen card on the S&P 400 or 600 states its sweep's figures and that no setting beat luck or the tester's bar
    public static QualityFixedView? QualityFixed(DateOnly fixedOn, EquityBrief.Core.Sweep.RecordedAnswer? answer) =>
        answer?.Figures is { } figures
            ? new QualityFixedView(
                fixedOn,
                figures.YearEdges.Count > 0 ? figures.YearEdges.Keys.Min() : fixedOn.Year,
                figures.Trades,
                figures.Edge,
                figures.YearEdges.Count(year => year.Value is not null),
                figures.YearEdges.Count(year => year.Value > 0),
                figures.BeatLuck,
                figures.TesterPassed)
            : null;

    // A holding's line tonight in the reader's words: chosen tonight, waiting for its buy, still qualifying, or what it fell
    // short of and what happens at the next reshuffle, its score's fall said where its score stood higher at the
    // reshuffle before.
    public static string TonightLine(QualityHoldingRow holding, QualityResultRow? now, DateOnly night, QualityResultRow? before)
    {
        if (holding.ChosenOn == night)
        {
            return "Chosen tonight; bought at the next open.";
        }

        if (holding.BoughtOn is null)
        {
            return "Bought at the next open the store holds.";
        }

        if (now is null)
        {
            return "Not answered tonight: no close is stored for it.";
        }

        if (now.Passed)
        {
            return "Still qualifies.";
        }

        return now.Reason switch
        {
            QualityRule.UnderTheFloor => before is { } earlier && QualityRule.Share(earlier.PassedChecks, earlier.Applied) > QualityRule.Share(now.PassedChecks, now.Applied)
                ? FormattableString.Invariant($"Score fell to {now.PassedChecks} of {now.Applied}; sold at the next reshuffle unless it recovers.")
                : FormattableString.Invariant($"Score of {now.PassedChecks} of {now.Applied}, under the floor; sold at the next reshuffle unless it recovers."),
            QualityRule.TooDear => "Now dearer than its own usual price; sold at the next reshuffle unless it cheapens.",
            _ => "Its score or its price over earnings was not read tonight; sold at the next reshuffle unless it is.",
        };
    }
}
