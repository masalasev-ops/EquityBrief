namespace EquityBrief.Core.Shortlist;

// One night's firing as its listings recorded it: for each reason, the rows it was evaluated on
// under its current rule and how many of those it fired on, and the rows on which every reason was
// evaluated under its current rule, with how many of those fired any.
public sealed record NightFiring(
    DateOnly Session,
    IReadOnlyDictionary<string, ReasonCount> Reasons,
    int Whole,
    int FiredAny);

public readonly record struct ReasonCount(int Counted, int Fired);

// A reason's share of the index against its target: the night's own, where that night evaluated
// it under its current rule, and the median over the ordinary nights with how many there are.
public sealed record ShareAgainstTarget(
    string Reason,
    ReasonCount? Night,
    double? Share,
    double? Median,
    int OrdinaryNights,
    double Target);

// A night a usually quiet reason flooded, with each reason that made it one, its share that night
// and its median over every night it was evaluated under its current rule.
public sealed record EventSession(DateOnly Session, IReadOnlyList<Flooded> FloodedBy);

public readonly record struct Flooded(string Reason, double Share, double Median);

public sealed record SharesAgainstTargets(
    DateOnly Night,
    IReadOnlyList<ShareAgainstTarget> Reasons,
    ShareAgainstTarget AnyReason,
    IReadOnlyList<EventSession> Events,
    int NightsWanted)
{
    public bool IsAnEventSession => Events.Any(session => session.Session == Night);
}

// Each reason's nightly share of the index against the target its threshold is calibrated to,
// read over the listings the store holds, with the nights one reason flooded marked and left out.
//
// A reason's share on a night is over the rows that evaluated it under its current rule, so a row
// written before the 5.4 corrections counts for neither reason they changed; the share firing any
// reason is over the rows that evaluated all six under their current rules. A night is an event
// session where a reason fires for more than a quarter of the index while its own median share,
// over every night it was evaluated under its current rule, is below a quarter: a reason above a
// quarter on its ordinary nights is flooding by its threshold, which is what the calibration moves,
// and marks nothing. An event session is counted and marked, and no median here reads it.
// see: A reason's threshold is calibrated to a target share of the index over ordinary nights and a night a usually quiet reason floods is left out
// owes: The six reason thresholds calibrated from the nights they fired on
public static class TargetShares
{
    // Proposed, and each moved only by the calibration the obligation above owes.
    public const double ReasonTarget = 0.02;

    public const double AnyReasonTarget = 0.06;

    public const double EventShare = 0.25;

    // The ordinary nights the calibration waits on.
    public const int CalibrationNights = 60;

    public const string AnyReason = "any reason";

    public static SharesAgainstTargets For(IReadOnlyList<NightFiring> nights, DateOnly night)
    {
        var evaluated = ShortlistSeries.Reasons.ToDictionary(
            reason => reason,
            reason => nights
                .Where(held => held.Reasons.TryGetValue(reason, out var count) && count.Counted > 0)
                .Select(held => (held.Session, Share: Share(held.Reasons[reason])))
                .ToArray(),
            StringComparer.Ordinal);

        var medians = evaluated.ToDictionary(pair => pair.Key, pair => Median([.. pair.Value.Select(held => held.Share)]), StringComparer.Ordinal);

        EventSession[] events =
        [
            .. evaluated
                .SelectMany(pair => pair.Value
                    .Where(held => held.Share > EventShare && medians[pair.Key] is < EventShare)
                    .Select(held => (held.Session, Flooded: new Flooded(pair.Key, held.Share, medians[pair.Key]!.Value))))
                .GroupBy(flood => flood.Session)
                .OrderBy(group => group.Key)
                .Select(group => new EventSession(
                    group.Key,
                    [.. group.Select(flood => flood.Flooded).OrderBy(flood => ShortlistSeries.Reasons.ToList().IndexOf(flood.Reason))])),
        ];

        var left = events.Select(session => session.Session).ToHashSet();
        var tonight = nights.FirstOrDefault(held => held.Session == night);

        ShareAgainstTarget Against(string reason)
        {
            var ordinary = evaluated[reason].Where(held => !left.Contains(held.Session)).Select(held => held.Share).ToArray();
            ReasonCount? counted = tonight is { } shown && shown.Reasons.TryGetValue(reason, out var count) && count.Counted > 0 ? count : null;

            return new ShareAgainstTarget(
                reason,
                counted,
                counted is { } held ? Share(held) : null,
                Median(ordinary),
                ordinary.Length,
                ReasonTarget);
        }

        var whole = nights.Where(held => held.Whole > 0).ToArray();
        var anyOrdinary = whole.Where(held => !left.Contains(held.Session)).Select(held => Share(new ReasonCount(held.Whole, held.FiredAny))).ToArray();
        ReasonCount? anyTonight = tonight is { Whole: > 0 } shownAny ? new ReasonCount(shownAny.Whole, shownAny.FiredAny) : null;

        return new SharesAgainstTargets(
            night,
            [.. ShortlistSeries.Reasons.Select(Against)],
            new ShareAgainstTarget(
                AnyReason,
                anyTonight,
                anyTonight is { } any ? Share(any) : null,
                Median(anyOrdinary),
                anyOrdinary.Length,
                AnyReasonTarget),
            events,
            CalibrationNights);
    }

    static double Share(ReasonCount count) => (double)count.Fired / count.Counted;

    // The median's own rule: the middle share, or the mean of the two middle ones over an even
    // count, and none over no nights at all.
    static double? Median(IReadOnlyList<double> shares)
    {
        if (shares.Count == 0)
        {
            return null;
        }

        var ordered = shares.Order().ToArray();
        var middle = ordered.Length / 2;

        return ordered.Length % 2 == 1 ? ordered[middle] : (ordered[middle - 1] + ordered[middle]) / 2;
    }
}
