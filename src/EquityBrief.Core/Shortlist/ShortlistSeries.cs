using System.Globalization;

namespace EquityBrief.Core.Shortlist;

// One band edge, as the reasons read it.
public readonly record struct EdgeAt(decimal Price, string Role);

// One tranche zone, as the reasons read it.
public readonly record struct Zone(decimal LowEdge, decimal HighEdge);

// One of tonight's bands, both edges, as the breakout reason reads it. Its role is
// not carried, because the role the level builder stores is read against tonight's
// close and the breakout asks which side of the band last night's close was on.
public readonly record struct Band(decimal LowEdge, decimal HighEdge);

// Everything one name's reasons are evaluated against, already read.
//
// Nullable throughout, because the population is the index and a name the night
// computed nothing for still gets a row. A reason that cannot be evaluated does
// not fire and says why, rather than firing on an absence read as a zero.
//
// `NextEvent` is the date the calendar holds and `SessionsToNextEvent` the count
// the exchange calendar gives to it, null past the closure table's end. A date
// with no count is an event nobody can count the sessions to, which is a third
// state and not the absence of a date.
//
// `NotCountedBecause` is why a member the night evaluates over nothing had no
// count made, so its row states the date it holds rather than an absence.
// see: A member the night evaluates over nothing keeps the dated event the calendar holds and says no count was made
public sealed record ReasonInputs(
    decimal? Close,
    decimal? PreviousClose,
    long? Volume,
    double? VolumeAverage50,
    IReadOnlyList<EdgeAt> Edges,
    IReadOnlyList<Band> Bands,
    IReadOnlyList<Zone> Zones,
    string? TrendState,
    string? PreviousTrendState,
    DateOnly? NextEvent,
    int? SessionsToNextEvent,
    string? NotCountedBecause = null);

// One reason and whether it fired, with the values that made it so.
//
// `Values` is what the row on the page shows on hover and what a later session
// scores the reason against. A reason that fired and states no values is a
// reason nobody can check.
public sealed record ReasonOutcome(string Name, bool Fired, IReadOnlyDictionary<string, string> Values);

// A threshold a reason is evaluated under, as its rows store it.
public sealed record ReasonThreshold(string Reason, string Value, string Constant, string Carried);

// The six reasons of section 11.
//
// A name is on tonight's list if any is true. There is no score and no fixed
// length, because the length of the list is itself the reading.
// see: Tonight's list is built from stated conditions, not a score
//
// Every threshold here is a proposal and the page says so. Two of them are known
// to produce too many names as written, and the calibration is not a backfill:
// the run page records how many names fired each night and which reason
// contributed, and the thresholds are set against the operator's own
// distribution after enough nights.
// see: Condition thresholds are calibrated from your own nights, not from a backfill
public static class ShortlistSeries
{
    public const string AtEntryZone = "at entry zone";
    public const string CrossedALevel = "crossed a level";
    public const string BreakoutOnVolume = "breakout on volume";
    public const string TrendStateChanged = "trend state changed";
    public const string UnusualVolume = "unusual volume";
    public const string EarningsSoon = "earnings soon";

    // Section 11's six, in the order the table states them, so the row on the
    // page and the table read the same way down.
    public static readonly string[] Reasons =
        [AtEntryZone, CrossedALevel, BreakoutOnVolume, TrendStateChanged, UnusualVolume, EarningsSoon];

    // Section 17's earnings horizon. About a month of trading, which is far
    // enough ahead that a position can still be staged or trimmed before the
    // print.
    public const int EarningsHorizonSessions = 20;

    // The multiple of the fifty-day average volume that counts as unusual.
    // A proposal, like the two the section 11 callout names.
    public const double UnusualVolumeMultiple = 2;

    public const string MultipleValue = "multiple";

    public const string HorizonValue = "horizon";

    // Each threshold a reason is evaluated under: the value its rows store it as, the
    // constant that holds it, and that constant as a row writes it.
    // see: A reason's record reads only the rows written under the threshold the code carries, and the rows written under another are kept
    public static IReadOnlyList<ReasonThreshold> Thresholds { get; } =
    [
        new(UnusualVolume, MultipleValue, nameof(UnusualVolumeMultiple), UnusualVolumeMultiple.ToString(CultureInfo.InvariantCulture)),
        new(EarningsSoon, HorizonValue, nameof(EarningsHorizonSessions), EarningsHorizonSessions.ToString(CultureInfo.InvariantCulture)),
    ];

    // Whether a stored reason was evaluated under a threshold other than the one the code
    // carries. A row that states none cannot say which window it belongs to.
    public static bool MeasuredUnderAnotherThreshold(string reason, Func<string, string?> valueOf) =>
        Thresholds.FirstOrDefault(threshold => threshold.Reason == reason) is { } carried
            && valueOf(carried.Value) != carried.Carried;

    public static IReadOnlyList<ReasonOutcome> For(ReasonInputs inputs)
    {
        var outcomes = new List<ReasonOutcome>();

        // At entry zone: tonight's close is inside a tranche zone, so the plan's
        // first step is available at tonight's price.
        var inZone = inputs.Close is { } close
            && inputs.Zones.FirstOrDefault(zone => close >= zone.LowEdge && close <= zone.HighEdge) is { LowEdge: > 0 } zoneAt
                ? zoneAt
                : (Zone?)null;

        outcomes.Add(new ReasonOutcome(
            AtEntryZone,
            inZone is not null,
            inZone is { } found
                ? Values(("close", Price(inputs.Close)), ("zone low", Price(found.LowEdge)), ("zone high", Price(found.HighEdge)))
                : Values(("close", Price(inputs.Close)), ("zones", inputs.Zones.Count.ToString(CultureInfo.InvariantCulture)))));

        // Crossed a level: the close moved through a band edge it was on the
        // other side of yesterday. The level either held or failed today, and
        // both are decisions.
        var crossed = inputs.Close is { } today && inputs.PreviousClose is { } yesterday
            ? inputs.Edges.Where(edge => (yesterday < edge.Price && today >= edge.Price)
                || (yesterday > edge.Price && today <= edge.Price)).ToArray()
            : [];

        // The one nearest the close it crossed to, which on a fall is the lowest
        // of them and on a rise the highest. Taking the first edge instead names
        // the furthest one on every rise that crossed more than one, and the
        // order the edges arrive in is no order at all.
        decimal? nearest = inputs.Close is { } crossedTo && crossed.Length > 0
            ? crossed.OrderBy(edge => Math.Abs(edge.Price - crossedTo)).First().Price
            : null;

        outcomes.Add(new ReasonOutcome(
            CrossedALevel,
            crossed.Length > 0,
            crossed.Length > 0
                ? Values(
                    ("close", Price(inputs.Close)),
                    ("previous close", Price(inputs.PreviousClose)),
                    ("edges crossed", crossed.Length.ToString(CultureInfo.InvariantCulture)),
                    ("nearest edge", Price(nearest)))
                : Values(("close", Price(inputs.Close)), ("previous close", Price(inputs.PreviousClose)))));

        // Breakout on volume: the close is above a band that sat at or above last
        // night's close, on volume above the fifty-day average. The one condition
        // that argues for buying strength rather than weakness.
        //
        // The side is read at last night's close and never off the role the level
        // builder stores, which is set against tonight's close, so a close is never
        // above a band it calls resistance.
        // see: Breakout on volume reads resistance at the previous session's close
        var cleared = inputs.Close is { } price && inputs.PreviousClose is { } before
            ? inputs.Bands.Where(band => band.LowEdge >= before && price > band.HighEdge).ToArray()
            : [];

        var heavy = inputs.Volume is { } volume && inputs.VolumeAverage50 is > 0 && volume > inputs.VolumeAverage50;

        outcomes.Add(new ReasonOutcome(
            BreakoutOnVolume,
            cleared.Length > 0 && heavy,
            Values(
                ("close", Price(inputs.Close)),
                (PreviousCloseValue, Price(inputs.PreviousClose)),
                ("bands above the previous close cleared", cleared.Length.ToString(CultureInfo.InvariantCulture)),
                ("volume", Count(inputs.Volume)),
                ("fifty-day average volume", Average(inputs.VolumeAverage50)))));

        // Trend state changed: tonight's label differs from last night's, so the
        // ladder changes shape and the whole plan is different from yesterday's.
        //
        // A name with no label last night has not changed. That is what the
        // ladder row written for every member every night is for: a name with no
        // row has no yesterday, and this reason would fire on its first night
        // for every name in the index.
        var changed = inputs.TrendState is { } tonight
            && inputs.PreviousTrendState is { } lastNight
            && !string.Equals(tonight, lastNight, StringComparison.Ordinal);

        outcomes.Add(new ReasonOutcome(
            TrendStateChanged,
            changed,
            Values(
                ("trend state", inputs.TrendState ?? "not classified"),
                ("previous trend state", inputs.PreviousTrendState ?? "no row last night"))));

        // Unusual volume: volume above twice the fifty-day average. Something
        // happened that the price may not have shown yet.
        var unusual = inputs.Volume is { } tonightVolume
            && inputs.VolumeAverage50 is > 0
            && tonightVolume > inputs.VolumeAverage50 * UnusualVolumeMultiple;

        outcomes.Add(new ReasonOutcome(
            UnusualVolume,
            unusual,
            Values(
                ("volume", Count(inputs.Volume)),
                ("fifty-day average volume", Average(inputs.VolumeAverage50)),
                (MultipleValue, UnusualVolumeMultiple.ToString(CultureInfo.InvariantCulture)))));

        // Earnings soon: the next earnings date is within twenty sessions. A
        // calendar fact rather than a setup, listed so a print is never a
        // surprise.
        //
        // A name with no date on file does not fire and says so, because a
        // guessed date is a wrong date and the failure table promises an
        // explicit blank. A date past the closure table's end has no count and
        // does not fire either, and says that instead, because it is a date
        // nobody can count the sessions to rather than a date nobody has.
        //
        // The count is the exchange calendar's, from the night to the date, and
        // never the stored bars after the night, which a live store never holds.
        // see: Sessions to a dated event are counted on the exchange calendar and never on stored bars
        outcomes.Add(new ReasonOutcome(
            EarningsSoon,
            inputs.SessionsToNextEvent is { } sessions && sessions >= 0 && sessions <= EarningsHorizonSessions,
            Values(
                (NextDatedEventValue, inputs.NextEvent is { } dated ? dated.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) : NotOnFile),
                ("sessions to the next dated event", inputs.NotCountedBecause is { } why
                    ? $"{NotCounted}: {why}"
                    : inputs.SessionsToNextEvent is { } counted
                        ? counted.ToString(CultureInfo.InvariantCulture)
                        : inputs.NextEvent is null ? NotOnFile : BeyondTheExchangeCalendar),
                (HorizonValue, EarningsHorizonSessions.ToString(CultureInfo.InvariantCulture)))));

        return outcomes;
    }

    public const string NotOnFile = "not on file";

    public const string BeyondTheExchangeCalendar = "beyond the exchange calendar";

    public const string NotCounted = "not counted";

    // The two values a row written since the 5.4 correction carries and a row
    // written before it does not, one per reason the correction changed. They are
    // a version marker keyed on the row's shape rather than a stamp, because the
    // listing has no version column and gains none: `next dated event` among
    // earnings soon's values, `previous close` among breakout on volume's.
    // see: Sessions to a dated event are counted on the exchange calendar and never on stored bars
    public const string NextDatedEventValue = "next dated event";

    public const string PreviousCloseValue = "previous close";

    // Whether a stored reason was evaluated by the rule the 5.4 correction
    // replaced, read off whether its values carry that reason's marker. Only the
    // two reasons the correction changed can be; the other four read the same
    // before and after.
    public static bool WrittenBeforeTheCorrection(string reason, Func<string, bool> carriesValue) => reason switch
    {
        EarningsSoon => !carriesValue(NextDatedEventValue),
        BreakoutOnVolume => !carriesValue(PreviousCloseValue),
        _ => false,
    };

    static IReadOnlyDictionary<string, string> Values(params (string Name, string Value)[] values) =>
        values.ToDictionary(value => value.Name, value => value.Value, StringComparer.Ordinal);

    // A price the store did not hold says so rather than reading as zero, which
    // is the falsy-value-for-an-absent-one class this corpus refuses.
    static string Price(decimal? value) =>
        value is { } price ? price.ToString(CultureInfo.InvariantCulture) : "not computed";

    static string Count(long? value) =>
        value is { } count ? count.ToString(CultureInfo.InvariantCulture) : "not computed";

    static string Average(double? value) =>
        value is { } average ? average.ToString("0.##", CultureInfo.InvariantCulture) : "not computed";
}
