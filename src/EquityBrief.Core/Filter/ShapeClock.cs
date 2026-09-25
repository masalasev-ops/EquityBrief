using EquityBrief.Core.Shortlist;

namespace EquityBrief.Core.Filter;

// One night's shape as the stored gate results give it: the version it ran under, the members, how
// many passed each of the four gates after the market and every one of them before it, and how many
// of those no exclusion removed, all counted with the market gate held open, since the market opens or
// closes the whole list and is not calibrated by how many it passes; and the index's median volume
// against its fifty-day average.
public sealed record NightShape(
    DateOnly Session,
    string Version,
    int Members,
    IReadOnlyList<int> Through,
    int Listed,
    double? MedianVolumeRatio);

// What flooded on an event night: a gate or a reason, its share of the index that night and its
// median share over every night in the window.
public readonly record struct Flood(string Measure, double Share, double Median);

// An event night: the gates and reasons that flooded it, and the volume ratio where it was the
// index's own volume that made it one.
public sealed record EventNight(DateOnly Session, IReadOnlyList<Flood> Floods, double? VolumeRatio);

// A median over the ordinary nights against the band it is calibrated to, none where no ordinary
// night is stored.
public sealed record Banded(string Measure, double? Median, int Low, int High);

// A reason's share of the index on the night and its median over the ordinary nights, as context.
public sealed record ReasonShare(string Reason, double? Tonight, double? Median);

// The shape half of the calibration for one night: the window, being the nights stored under the
// open filter version or, with none open, under section 17's proposed values; its ordinary nights
// against the sixty the calibration waits on; its event nights with what made each one; each gate's
// median count over the ordinary nights and the list's, against their bands; and each reason's share.
public sealed record ShapeState(
    DateOnly Night,
    string Version,
    int WindowNights,
    int Ordinary,
    int Wanted,
    bool Crossed,
    IReadOnlyList<EventNight> Events,
    IReadOnlyList<Banded> Gates,
    Banded List,
    IReadOnlyList<ReasonShare> Reasons)
{
    public bool VersionOpen => Version != ShapeClock.NoVersion;

    public EventNight? Tonight => Events.FirstOrDefault(session => session.Session == Night);
}

// The shape clock. It tunes how many names each gate passes and how many reach the list, and never
// judges whether the list makes money. A night on which a gate or a reason whose own median share
// over every night in the window is below a quarter passes or fires for more than a quarter of the
// index and more than twice that median, or on which the index's median volume is at 1.8 times its
// fifty-day average or more, is an event: its setups share one cause, so it is counted, shown, and
// read by no median. The market gate
// is never read for it. The medians are over every night in the window, event or not, so a night's
// classification never depends on the classification it decides.
// see: The swing filter's shape is calibrated over its ordinary nights, a night one cause pushes past a quarter and twice its usual share is left out, and each band spans a third to three times what the ruled filter passes
// owes: The swing filter's shape calibrated from its ordinary nights
public static class ShapeClock
{
    // The ordinary nights under one version the calibration waits on.
    public const int CalibrationNights = 60;

    // The share of the index a usually quiet gate or reason passes or fires for that makes a night an event.
    public const double EventShare = 0.25;

    // How many times its own median share a gate or reason must also reach, so a broad gate's ordinary
    // movement is not read as one cause flooding it.
    public const double EventMedianMultiple = 2;

    // The index's median volume against its fifty-day average that makes a night an event.
    public const double EventVolumeRatio = 1.8;

    public const string NoVersion = "none";

    // Each gate's band, the count through it and every gate before it after the market: a third to three
    // times the median the ruled filter passed over the ordinary sessions it was measured on, the low end
    // rounded down and the high end up.
    public static IReadOnlyList<(string Gate, int Low, int High)> GateBands { get; } =
    [
        (SwingGates.Trend, 38, 347),
        (SwingGates.Setup, 14, 126),
        (SwingGates.Trigger, 6, 56),
        (SwingGates.Trade, 1, 12),
    ];

    // The list's band, the names passing every gate and no exclusion on an ordinary night, fitted the same way.
    public const int ListLow = 1;

    public const int ListHigh = 9;

    public const string ListMeasure = "the list";

    // What the two clocks can do, in the one sentence the run page and section 13 both state.
    public const string Timeline =
        "The list's thresholds move only through shape calibration and the operator's rulings, and the edge clock gathers evidence and changes nothing until it can retire a variant, after about two years, or promote one, after about three.";

    public static ShapeState For(IReadOnlyList<NightShape> nights, IReadOnlyList<NightFiring> firings, string version, DateOnly night)
    {
        var window = nights.Where(one => one.Version == version).OrderBy(one => one.Session).ToArray();
        var sessions = window.Select(one => one.Session).ToHashSet();
        var inWindow = firings.Where(firing => sessions.Contains(firing.Session)).ToArray();

        var events = Events(window, inWindow);
        var left = events.Select(one => one.Session).ToHashSet();
        var ordinary = window.Where(one => !left.Contains(one.Session)).ToArray();
        var ordinaryFirings = inWindow.Where(firing => !left.Contains(firing.Session)).ToArray();
        var tonight = firings.FirstOrDefault(firing => firing.Session == night);

        return new ShapeState(
            night,
            version,
            window.Length,
            ordinary.Length,
            CalibrationNights,
            version != NoVersion && ordinary.Length >= CalibrationNights,
            events,
            [
                .. GateBands.Select((band, at) => new Banded(
                    band.Gate,
                    SwingReadings.Median([.. ordinary.Select(one => one.Through[at] * 1.0)]),
                    band.Low,
                    band.High)),
            ],
            new Banded(ListMeasure, SwingReadings.Median([.. ordinary.Select(one => one.Listed * 1.0)]), ListLow, ListHigh),
            [
                .. ShortlistSeries.Reasons.Select(reason => new ReasonShare(
                    reason,
                    tonight is { } shown && shown.Reasons.TryGetValue(reason, out var count) && count.Counted > 0 ? Share(count) : null,
                    SwingReadings.Median([.. ordinaryFirings.Where(firing => firing.Reasons.TryGetValue(reason, out var held) && held.Counted > 0).Select(firing => Share(firing.Reasons[reason]))]))),
            ]);
    }

    // The event nights among the nights given: a gate or reason whose median share over every one of
    // them is below a quarter passing or firing for more than a quarter of the index and more than twice
    // that median, or the index's median volume at the event ratio or more.
    public static IReadOnlyList<EventNight> Events(IReadOnlyList<NightShape> nights, IReadOnlyList<NightFiring> firings)
    {
        var measures = new List<(string Measure, IReadOnlyList<(DateOnly Session, double Share)> Shares)>();

        for (var at = 0; at < GateBands.Count; at++)
        {
            var gate = at;

            measures.Add((GateBands[gate].Gate, [.. nights.Where(one => one.Members > 0).Select(one => (one.Session, one.Through[gate] * 1.0 / one.Members))]));
        }

        foreach (var reason in ShortlistSeries.Reasons)
        {
            measures.Add((reason, [.. firings.Where(firing => firing.Reasons.TryGetValue(reason, out var count) && count.Counted > 0).Select(firing => (firing.Session, Share(firing.Reasons[reason])))]));
        }

        var floods = measures
            .Select(measure => (measure.Measure, measure.Shares, Median: SwingReadings.Median([.. measure.Shares.Select(one => one.Share)])))
            .Where(measure => measure.Median is < EventShare)
            .SelectMany(measure => measure.Shares
                .Where(one => one.Share > EventShare && one.Share > EventMedianMultiple * measure.Median!.Value)
                .Select(one => (one.Session, Flood: new Flood(measure.Measure, one.Share, measure.Median!.Value))))
            .ToArray();

        var loud = nights
            .Where(one => one.MedianVolumeRatio is >= EventVolumeRatio)
            .ToDictionary(one => one.Session, one => one.MedianVolumeRatio!.Value);

        return
        [
            .. floods.Select(one => one.Session)
                .Concat(loud.Keys)
                .Distinct()
                .Order()
                .Select(session => new EventNight(
                    session,
                    [.. floods.Where(one => one.Session == session).Select(one => one.Flood)],
                    loud.TryGetValue(session, out var ratio) ? ratio : null)),
        ];
    }

    static double Share(ReasonCount count) => count.Fired * 1.0 / count.Counted;
}
