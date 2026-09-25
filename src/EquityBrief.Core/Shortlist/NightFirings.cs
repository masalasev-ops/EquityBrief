using System.Text.Json;

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

// Each night's firing read off its listings by the rule the records count by, held once so the run
// page and the shape counts read one statement of it. A reason counts on a row only where the row
// evaluated it under its current rule, so a row written before the 5.4 corrections counts for neither
// reason they changed, and earnings soon on a session read over a calendar holding other listings'
// dates counts toward nothing, which leaves that session's rows out of the share firing any reason.
// see: The calendar holds each member's own listing's prints, and each night's answer replaces what its window held
public static class NightFirings
{
    // The reasons a stored row counts and the ones among them that fired.
    public static (IReadOnlyList<string> Counted, IReadOnlyList<string> Fired) Counting(string reasons)
    {
        using var document = JsonDocument.Parse(reasons);

        var counted = document.RootElement.EnumerateArray()
            .Where(reason => !ShortlistSeries.WrittenBeforeTheCorrection(
                reason.GetProperty("name").GetString()!,
                value => reason.GetProperty("values").TryGetProperty(value, out _)))
            .Where(reason => !ShortlistSeries.MeasuredUnderAnotherThreshold(
                reason.GetProperty("name").GetString()!,
                value => reason.GetProperty("values").TryGetProperty(value, out var stored) ? stored.GetString() : null))
            .ToArray();

        return (
            [.. counted.Select(reason => reason.GetProperty("name").GetString()!)],
            [.. counted.Where(reason => reason.GetProperty("fired").GetBoolean()).Select(reason => reason.GetProperty("name").GetString()!)]);
    }

    // Every night's firing over the listing rows given, each a session and its stored reasons.
    public static IReadOnlyList<NightFiring> Of(IEnumerable<(DateOnly Session, string Reasons)> rows) =>
    [
        .. rows
            .GroupBy(row => row.Session)
            .OrderBy(group => group.Key)
            .Select(group =>
            {
                var reasons = ShortlistSeries.Reasons.ToDictionary(reason => reason, _ => new ReasonCount(0, 0), StringComparer.Ordinal);
                var whole = 0;
                var firedAny = 0;

                foreach (var row in group)
                {
                    var (counting, firing) = Counting(row.Reasons);
                    var counted = counting.Where(reason => !ShortlistSeries.ReadOverAnotherListing(reason, row.Session)).ToArray();
                    var fired = firing.Where(counted.Contains).ToArray();

                    foreach (var reason in counted.Where(reasons.ContainsKey))
                    {
                        var held = reasons[reason];

                        reasons[reason] = new ReasonCount(held.Counted + 1, held.Fired + (fired.Contains(reason) ? 1 : 0));
                    }

                    if (ShortlistSeries.Reasons.All(counted.Contains))
                    {
                        whole++;
                        firedAny += fired.Length > 0 ? 1 : 0;
                    }
                }

                return new NightFiring(group.Key, reasons, whole, firedAny);
            }),
    ];
}
