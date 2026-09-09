using System.Globalization;
using System.Text.Json;
using EquityBrief.Core.Indicators;
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

    static decimal Price(JsonElement row, string name) =>
        decimal.Parse(row.GetProperty(name).GetString()!, CultureInfo.InvariantCulture);

    // The condition in words. The enum's names are what the store holds and are
    // not what a reader reads, and this is the one place the two are paired.
    static string Words(string? condition) => condition switch
    {
        "AvailableNow" => "this price now",
        "FailedBreakdown" => "a failed breakdown back into the zone",
        "FirstCloseBackAbove" => "the first close back above the zone after a dip",
        "SecondDayAfterAShock" => "the second day after a shock, once the first day's low has held",
        _ => "the price reaching the zone",
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
        CalendarRow? nextEvent)
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
            bars.Count > 0 ? bars[^1].Close : 0m);
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
