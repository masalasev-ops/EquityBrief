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

    public static string Region(
        SinglePageApp page,
        MarkRenderer marks,
        string ticker,
        IReadOnlyList<BarRow> bars,
        IReadOnlyList<IndicatorRow> indicators,
        IReadOnlyList<LevelRow> levels,
        IReadOnlyList<ProfileRow> profile,
        LadderRow? ladder)
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
            ladder?.AsOf);
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
