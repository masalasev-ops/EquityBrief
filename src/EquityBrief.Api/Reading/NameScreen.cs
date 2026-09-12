using System.Globalization;
using System.Text.Json;
using EquityBrief.Core.Indicators;
using EquityBrief.Core.Ladders;
using EquityBrief.Web.App;
using EquityBrief.Web.Marks;

namespace EquityBrief.Api.Reading;

// The projection from stored rows to what a mark takes.
//
// It sits between the read surface and the app because both of those have a
// rule about them: the read surface hands back stored values unchanged, and the
// app renders and computes nothing. Turning a row into a mark's input is
// neither, so it is named here rather than smuggled into one of them.
//
// Nothing in it derives a figure. Every value is the stored column, and the one
// thing that is worked out is which of the four momentum readings exist, which
// is a lookup in a list the indicator arithmetic already carries.
// see: A screen reads and renders, and computes nothing
public static class NameScreen
{
    // The three averages drawn on a price axis. The momentum readings are not
    // among them for the reason the level builder gives: an RSI is not a price.
    static readonly string[] Averages = [IndicatorSeries.Sma20, IndicatorSeries.Sma50, IndicatorSeries.Sma200];

    // The plan column's rows, from the ladder's stored plan. Nothing here
    // derives a figure: each row is a price the ladder wrote and a sentence
    // saying what it is, and the sentences are assembled from stored values
    // rather than computed from them.
    public static IReadOnlyList<PlanRow> PlanRows(LadderRow? ladder)
    {
        if (ladder is null)
        {
            return [];
        }

        using var plan = JsonDocument.Parse(ladder.Plan);
        var root = plan.RootElement;
        var rows = new List<PlanRow>();

        foreach (var tranche in root.GetProperty("tranches").EnumerateArray())
        {
            var low = Price(tranche, "lowEdge");
            var high = Price(tranche, "highEdge");
            var stop = tranche.GetProperty("stop").GetString();
            var condition = tranche.GetProperty("condition").GetString();

            rows.Add(new PlanRow(
                low,
                high,
                PlanKind.Tranche,
                stop is null
                    ? $"buy on {Words(condition)}, no stop beneath"
                    : $"buy on {Words(condition)}, stop on a daily close below {stop}",
                Traded: true));

            // The stop is its own row, because it is a price a close is measured
            // against rather than a zone anything is bought in.
            if (stop is not null)
            {
                var price = decimal.Parse(stop, CultureInfo.InvariantCulture);

                rows.Add(new PlanRow(price, price, PlanKind.Stop, $"stop for the {low} zone", Traded: true));
            }
        }

        foreach (var exit in root.GetProperty("exits").EnumerateArray())
        {
            var traded = exit.GetProperty("traded").GetBoolean();
            var trailing = exit.GetProperty("trailing").GetBoolean();
            var fraction = exit.GetProperty("fraction").GetString();

            rows.Add(new PlanRow(
                Price(exit, "lowEdge"),
                Price(exit, "highEdge"),
                PlanKind.Exit,
                traded
                    ? trailing
                        ? $"sell {fraction} and trail the rest"
                        : $"sell {fraction}"
                    : exit.GetProperty("reason").GetString() ?? "listed and not traded",
                traded));
        }

        // The invalidation is the lowest stop rather than a rule beside it, which
        // is what section 15.5 says the figure shows: stops are horizontal rules
        // and the invalidation is the lowest one. So the stop at that price is
        // relabelled rather than a second rule being drawn on top of it, and a
        // row is added only where no stop sits there, which is a structure whose
        // lowest tranche has no band beneath it.
        if (root.GetProperty("invalidation").GetString() is { } invalidation)
        {
            var price = decimal.Parse(invalidation, CultureInfo.InvariantCulture);
            var at = rows.FindIndex(row => row.Kind == PlanKind.Stop && row.LowEdge == price);

            var detail = "the whole position is wrong below this";

            if (at >= 0)
            {
                rows[at] = rows[at] with
                {
                    Kind = PlanKind.Invalidation,
                    Detail = $"{rows[at].Detail}, and {detail}",
                };
            }
            else
            {
                rows.Add(new PlanRow(price, price, PlanKind.Invalidation, detail, Traded: true));
            }
        }

        return rows;
    }

    // The second book, as the page shows it. Every setup says it is a proposal
    // and names what will calibrate it, because a figure on a screen gets acted
    // on and one that looks measured and is not is the failure this states its
    // way out of.
    // see: The event setups' triggers are proposals until resolved setups can score them
    public static string EventBook(LadderRow? ladder)
    {
        if (ladder is null)
        {
            return string.Empty;
        }

        using var plan = JsonDocument.Parse(ladder.Plan);
        var setups = plan.RootElement.GetProperty("events").EnumerateArray().ToArray();

        var html = new System.Text.StringBuilder();

        html.Append(CultureInfo.InvariantCulture, $"<section class=\"event-book\" data-ticker=\"{ladder.Ticker}\" data-setups=\"{setups.Length}\">");

        if (setups.Length == 0)
        {
            html.Append("<p class=\"degraded\">no dated event is on file, so there are no setups</p></section>");

            return html.ToString();
        }

        html.Append(
            "<p class=\"proposal-note\" data-proposal=\"true\">Every figure in these setups is a " +
            "proposal and none has been tested. They are calibrated against resolved setups on the " +
            "run page, not tuned in advance.</p>");

        html.Append("<table class=\"event-table\"><tr><th>Setup</th><th>Trigger</th><th>Entry, stop and target</th></tr>");

        foreach (var setup in setups)
        {
            html.Append(CultureInfo.InvariantCulture, $"<tr data-setup=\"{setup.GetProperty("name").GetString()}\" data-proposal=\"true\">");
            html.Append(CultureInfo.InvariantCulture, $"<td>{setup.GetProperty("name").GetString()} on {setup.GetProperty("eventDate").GetString()}</td>");
            html.Append(CultureInfo.InvariantCulture, $"<td>{setup.GetProperty("trigger").GetString()}</td>");
            html.Append(CultureInfo.InvariantCulture, $"<td>enter {setup.GetProperty("entry").GetString()}, stop {setup.GetProperty("stop").GetString()}, target {setup.GetProperty("target").GetString()}</td></tr>");
        }

        html.Append("</table></section>");

        return html.ToString();
    }

    // The sizing arithmetic and the earnings rule, as the plan section states
    // them. Every figure is read off the ladder row, which derived them from its
    // own prices, so nothing here computes and nothing can drift.
    // see: A screen reads and renders, and computes nothing
    public static string Arithmetic(LadderRow? ladder)
    {
        if (ladder is null)
        {
            return string.Empty;
        }

        using var plan = JsonDocument.Parse(ladder.Plan);
        var figures = plan.RootElement.GetProperty("arithmetic");
        var prints = plan.RootElement.GetProperty("earningsRule").EnumerateArray().ToArray();

        var html = new System.Text.StringBuilder();

        html.Append(CultureInfo.InvariantCulture, $"<section class=\"plan-arithmetic\" data-ticker=\"{ladder.Ticker}\">");

        if (figures.GetProperty("absent").GetString() is { } absent)
        {
            html.Append(CultureInfo.InvariantCulture, $"<p class=\"degraded\" data-absent=\"true\">{absent}</p>");
        }
        else
        {
            html.Append("<table class=\"arithmetic-table\">");
            html.Append("<tr><th>From</th><th>Entry</th><th>Risk</th><th>Reward</th><th>Reward to risk</th></tr>");

            html.Append(CultureInfo.InvariantCulture,
                $"<tr data-from=\"first\"><td>the first tranche</td><td>{figures.GetProperty("firstEntry").GetString()}</td>" +
                $"<td>{figures.GetProperty("firstRisk").GetString()}</td><td>{figures.GetProperty("firstReward").GetString()}</td>" +
                $"<td>{figures.GetProperty("firstRewardToRisk").GetString()}</td></tr>");

            if (figures.GetProperty("blendedEntry").GetString() is { } blended)
            {
                html.Append(CultureInfo.InvariantCulture,
                    $"<tr data-from=\"blended\"><td>the blended first two</td><td>{blended}</td>" +
                    $"<td>{figures.GetProperty("blendedRisk").GetString()}</td><td>{figures.GetProperty("blendedReward").GetString()}</td>" +
                    $"<td>{figures.GetProperty("blendedRewardToRisk").GetString()}</td></tr>");
            }

            html.Append("</table>");

            // The break-even, which is what the plan demands of itself rather
            // than a benchmark borrowed from elsewhere.
            html.Append(CultureInfo.InvariantCulture,
                $"<p class=\"break-even\" data-break-even=\"{figures.GetProperty("breakEven").GetString()}\">" +
                $"This plan is worth taking if its first tranche reaches the target before the stop " +
                $"more than {figures.GetProperty("breakEven").GetString()} of the time.</p>");

            // The worked sizing example, from a risk budget the reader chooses.
            // The plan places a position and never sizes one, so the budget is a
            // sentence rather than a figure this system holds.
            // see: The plan places a position and never sizes one
            html.Append(CultureInfo.InvariantCulture,
                $"<p class=\"sizing\" data-risk=\"{figures.GetProperty("firstRisk").GetString()}\">" +
                $"Sizing is yours: a risk budget divided by {figures.GetProperty("firstRisk").GetString()} " +
                $"is the number of shares the first tranche takes. This tool places the position and " +
                $"never sizes it.</p>");
        }

        if (prints.Length > 0)
        {
            html.Append("<table class=\"earnings-rule\"><tr><th>Print</th><th>Session</th><th>One-day move</th><th>Against the stop</th></tr>");

            foreach (var print in prints)
            {
                html.Append(CultureInfo.InvariantCulture,
                    $"<tr data-print=\"{print.GetProperty("eventDate").GetString()}\">" +
                    $"<td>{print.GetProperty("eventDate").GetString()}</td>" +
                    $"<td>{print.GetProperty("session").GetString()}</td>" +
                    $"<td>{print.GetProperty("move").GetString()}</td>" +
                    $"<td>{print.GetProperty("shareOfStop").GetString() ?? "no stop to measure against"}</td></tr>");
            }

            html.Append("</table>");
        }

        html.Append("</section>");

        return html.ToString();
    }

    static decimal Price(JsonElement row, string name) =>
        decimal.Parse(row.GetProperty(name).GetString()!, CultureInfo.InvariantCulture);

    // The condition in words. The enum's names are what the store holds and are
    // not what a reader reads, and this is the one place the two are paired.
    //
    // It ended in a catch-all arm until 5.4, so a sixth condition, a typo or an
    // unset value rendered as the sentence for reaching the zone with nothing
    // failing, and no test in the suite asserted any plan sentence at all. The
    // phase 4 sign-off found it and 5.4 owes it.
    //
    // Every condition the enum carries has its own arm and anything else throws.
    // A sentence a reader acts on that was produced by a value nobody wrote is
    // exactly the failure a catch-all makes invisible: it looks like a plan and
    // it is a default.
    // see: Every figure carries a plain-language key
    static string Words(string? condition) => condition switch
    {
        nameof(TrancheCondition.AvailableNow) => "this price now",
        nameof(TrancheCondition.FailedBreakdown) => "a failed breakdown back into the zone",
        nameof(TrancheCondition.FirstCloseBackAbove) => "the first close back above the zone after a dip",
        nameof(TrancheCondition.SecondDayAfterAShock) => "the second day after a shock, once the first day's low has held",
        nameof(TrancheCondition.ReachesTheZone) => "the price reaching the zone",
        _ => throw new InvalidOperationException(
            $"The stored plan carries the condition '{condition ?? "null"}', which this mapping has no words for. " +
            "A sentence a reader acts on that was produced by a value nobody wrote is what a catch-all arm " +
            "makes invisible, so the page fails rather than rendering a default."),
    };

    public static string Region(
        SinglePageApp page,
        MarkRenderer marks,
        string ticker,
        IReadOnlyList<BarRow> bars,
        IReadOnlyList<IndicatorRow> indicators,
        IReadOnlyList<LevelRow> levels,
        IReadOnlyList<ProfileRow> profile,
        LadderRow? ladder,
        CalendarRow? nextEvent,
        IReadOnlyList<MoveRow> moves,
        ListingRow? listing = null,
        string? previousOnTheList = null,
        string? nextOnTheList = null)
    {
        var drawn = bars
            .Select(bar => new ChartBar(bar.SessionDate, bar.Open, bar.High, bar.Low, bar.Close, bar.Volume))
            .ToArray();

        // The averages, aligned to the bars session by session rather than by
        // position. The two queries are ordered the same way and over the same
        // window, so the sequences agree today; aligning on the date is what
        // keeps them agreeing when one of them does not, and a line drawn one
        // slot out would look entirely plausible.
        var bySession = indicators
            .GroupBy(row => row.Name)
            .ToDictionary(
                group => group.Key,
                group => group.ToDictionary(row => row.SessionDate, row => row.Value));

        var lines = Averages
            .Where(bySession.ContainsKey)
            .Select(name => new ChartAverage(
                name,
                [.. drawn.Select(bar => bySession[name].GetValueOrDefault(bar.SessionDate))]))
            .ToArray();

        var readings = IndicatorSeries.Momentum
            .Select(name =>
            {
                var bounds = IndicatorSeries.BoundsOf(name);

                return new MomentumReading(
                    name,
                    [.. indicators.Where(row => row.Name == name).OrderBy(row => row.SessionDate).Select(row => row.Value)],
                    IndicatorSeries.NeutralOf(name),
                    bounds?.Floor,
                    bounds?.Ceiling);
            })
            .ToArray();

        // An average that has no value on any stored session anchors no band, and
        // section 18 says the absence is stated with the bar count rather than
        // left out of the table.
        var absent = Averages
            .Where(name => bySession.TryGetValue(name, out var values) && values.Values.All(value => value is null))
            .Select(name => new AbsentAverage(
                name,
                indicators.Where(row => row.Name == name).Select(row => row.BarCount).DefaultIfEmpty(0).Max()))
            .ToArray();

        return page.NameRegion(
            marks,
            ticker,
            drawn,
            lines,
            [.. levels.Select(level => new ChartBand(level.LowEdge, level.HighEdge, level.Role, level.Immediate, level.Strength))],
            [.. profile.Select(band => new ProfileBand(band.BandLow, band.BandHigh, band.Shares, band.ShareOfPeriod))],
            readings,
            [.. levels.Select(level => new SummaryBand(
                level.LowEdge,
                level.HighEdge,
                level.Role,
                level.Immediate,
                level.Strength,
                level.HasNonAverageAnchor,
                Members(level.Members)))],
            absent,
            ladder?.TrendState,
            ladder?.AsOf,
            nextEvent?.EventDate,
            PlanRows(ladder),
            bars.Count > 0 ? bars[^1].Close : 0m,
            EventBook(ladder),
            Arithmetic(ladder),
            [.. moves.Select(move => new MoveCell(move.SessionDate, move.Sessions, move.ChangePct, move.Rank))],
            TwelveMonths(bars),
            FiredReasons(listing),
            previousOnTheList,
            nextOnTheList);
    }

    // The plan region alone, which is what tonight's list shows for the selected
    // name: the plan column and the two tables it is read beside. Section 15.7
    // says the common case of checking a plan needs no navigation.
    public static string PlanRegion(
        SinglePageApp page,
        MarkRenderer marks,
        string ticker,
        LadderRow? ladder,
        decimal close,
        IReadOnlyList<LevelRow> levels)
    {
        var rows = PlanRows(ladder);

        // The level summary beside the plan column, which is the other half of
        // what section 15.7 states this region holds and which stood undrawn
        // under the row's own PASS until 5.8. It is the name screen's own table,
        // the same mark over the same stored bands, because the common case is
        // checking a plan and a plan read without the levels it rests on is a
        // list of prices.
        //
        // No absent-average row here. That is the name screen's statement about
        // a name whose long average has no value over its stored year, and it
        // needs the indicator series this region does not read; the name page is
        // where it belongs and where it is drawn.
        return $"<section class=\"selected-name\" data-ticker=\"{ticker}\" data-plan-rows=\"{rows.Count}\" data-bands=\"{levels.Count}\">"
            + marks.PlanColumn(ticker, close, rows)
            + marks.PlanTables(ticker, rows)
            + marks.LevelSummary(
                ticker,
                [.. levels.Select(level => new SummaryBand(
                    level.LowEdge,
                    level.HighEdge,
                    level.Role,
                    level.Immediate,
                    level.Strength,
                    level.HasNonAverageAnchor,
                    Members(level.Members)))],
                [])
            + "</section>";
    }

    // The sessions of the last twelve months, which is what section 15.9's
    // how-it-got-here region draws its picture over.
    //
    // Measured back from the newest stored session rather than from the clock,
    // so a page opened on a Sunday draws the year to Friday and not a year to
    // today with two blank days at the end of it. The bar history kept is a year
    // (section 17), so on an ordinary name this is every stored bar; it is a
    // filter rather than a pass-through because that is a limit the store is
    // trusted to hold rather than one this region may assume.
    public static IReadOnlyList<ChartBar> TwelveMonths(IReadOnlyList<BarRow> bars)
    {
        if (bars.Count == 0)
        {
            return [];
        }

        var from = bars[^1].SessionDate.AddYears(-1);

        return
        [
            .. bars
                .Where(bar => bar.SessionDate > from)
                .Select(bar => new ChartBar(bar.SessionDate, bar.Open, bar.High, bar.Low, bar.Close, bar.Volume)),
        ];
    }

    // The reasons that fired for this name tonight, read back from the stored
    // listing. A name with no listing row, or one that fired nothing, has none,
    // and the region says the name is not on tonight's list rather than being
    // absent.
    static IReadOnlyList<FiredReason> FiredReasons(ListingRow? listing)
    {
        if (listing is null || listing.FiredCount == 0)
        {
            return [];
        }

        using var document = JsonDocument.Parse(listing.Reasons);

        return
        [
            .. document.RootElement.EnumerateArray()
                .Where(reason => reason.GetProperty("fired").GetBoolean())
                .Select(reason => new FiredReason(
                    reason.GetProperty("name").GetString()!,
                    reason.GetProperty("values").EnumerateObject()
                        .ToDictionary(value => value.Name, value => value.Value.GetString()!, StringComparer.Ordinal))),
        ];
    }

    // The members column, as the mark needs it. SCHEMA stores each member's
    // kind, price and date as JSON with the price in the storage form, so this
    // reads back what was written and derives nothing.
    static IReadOnlyList<SummaryMember> Members(string stored)
    {
        using var document = JsonDocument.Parse(stored);

        return
        [
            .. document.RootElement.EnumerateArray().Select(member => new SummaryMember(
                member.GetProperty("kind").GetString()!,
                decimal.Parse(member.GetProperty("price").GetString()!, CultureInfo.InvariantCulture),
                DateOnly.ParseExact(member.GetProperty("date").GetString()!, "yyyy-MM-dd", CultureInfo.InvariantCulture))),
        ];
    }
}
