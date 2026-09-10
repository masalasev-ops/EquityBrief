using System.Globalization;

namespace EquityBrief.Core.Shortlist;

// One band edge, as the reasons read it.
public readonly record struct EdgeAt(decimal Price, string Role);

// One tranche zone, as the reasons read it.
public readonly record struct Zone(decimal LowEdge, decimal HighEdge);

// Everything one name's reasons are evaluated against, already read.
//
// Nullable throughout, because the population is the index and a name the night
// computed nothing for still gets a row. A reason that cannot be evaluated does
// not fire and says why, rather than firing on an absence read as a zero.
public sealed record ReasonInputs(
    decimal? Close,
    decimal? PreviousClose,
    long? Volume,
    double? VolumeAverage50,
    IReadOnlyList<EdgeAt> Edges,
    IReadOnlyList<Zone> Zones,
    string? TrendState,
    string? PreviousTrendState,
    int? SessionsToNextEvent);

// One reason and whether it fired, with the values that made it so.
//
// `Values` is what the row on the page shows on hover and what a later session
// scores the reason against. A reason that fired and states no values is a
// reason nobody can check.
public sealed record ReasonOutcome(string Name, bool Fired, IReadOnlyDictionary<string, string> Values);

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

        outcomes.Add(new ReasonOutcome(
            CrossedALevel,
            crossed.Length > 0,
            crossed.Length > 0
                ? Values(
                    ("close", Price(inputs.Close)),
                    ("previous close", Price(inputs.PreviousClose)),
                    ("edges crossed", crossed.Length.ToString(CultureInfo.InvariantCulture)),
                    ("nearest edge", Price(crossed[0].Price)))
                : Values(("close", Price(inputs.Close)), ("previous close", Price(inputs.PreviousClose)))));

        // Breakout on volume: the close is above a resistance band on volume
        // above the fifty-day average. The one condition that argues for buying
        // strength rather than weakness.
        var above = inputs.Close is { } price
            ? inputs.Edges.Where(edge => edge.Role == Resistance && price > edge.Price).ToArray()
            : [];

        var heavy = inputs.Volume is { } volume && inputs.VolumeAverage50 is > 0 && volume > inputs.VolumeAverage50;

        outcomes.Add(new ReasonOutcome(
            BreakoutOnVolume,
            above.Length > 0 && heavy,
            Values(
                ("close", Price(inputs.Close)),
                ("resistance bands below the close", above.Length.ToString(CultureInfo.InvariantCulture)),
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
                ("multiple", UnusualVolumeMultiple.ToString(CultureInfo.InvariantCulture)))));

        // Earnings soon: the next earnings date is within twenty sessions. A
        // calendar fact rather than a setup, listed so a print is never a
        // surprise.
        //
        // A name with no date on file does not fire and says so, because a
        // guessed date is a wrong date and the failure table promises an
        // explicit blank.
        outcomes.Add(new ReasonOutcome(
            EarningsSoon,
            inputs.SessionsToNextEvent is { } sessions && sessions >= 0 && sessions <= EarningsHorizonSessions,
            Values(
                ("sessions to the next dated event", inputs.SessionsToNextEvent?.ToString(CultureInfo.InvariantCulture) ?? "not on file"),
                ("horizon", EarningsHorizonSessions.ToString(CultureInfo.InvariantCulture)))));

        return outcomes;
    }

    public const string Resistance = "resistance";

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
