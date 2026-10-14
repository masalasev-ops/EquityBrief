using EquityBrief.Core.Providers;

namespace EquityBrief.Core.Research;

// When the off-peak run starts: five minutes after each peak window of the research job's
// profiles ends, every day of the week, so a night's queued reports are written at the first
// off-peak stretch after the night and never a day later. A day naming no peak still runs at
// those times, which is what lets a Friday night's reports go on the Saturday rather than on
// the Monday. The machine's scheduler holds the times; this is what the queue page reads them
// from, so the page and the schedule the runbook registers are worked out the same way.
// see: The night queues its report requests and starts neither the drain nor the labeller, and the off-peak run starts both five minutes after each peak window ends
public static class OffPeakRun
{
    public static readonly TimeSpan After = TimeSpan.FromMinutes(5);

    // The times of day in UTC the run starts at, earliest first, each a peak window's end hour
    // and the five minutes after it, once however many profiles name the window.
    public static IReadOnlyList<TimeSpan> Times(IEnumerable<ResearchPricing> pricings) =>
        [.. pricings
            .SelectMany(pricing => pricing.PeakHours)
            .Select(window => TimeSpan.FromHours(window.To % 24) + After)
            .Distinct()
            .Order()];

    // The first run at or after an instant, or none where no profile names a peak window, in
    // which case nothing waits for a run and a press is the only start.
    public static DateTimeOffset? NextAt(IEnumerable<ResearchPricing> pricings, DateTimeOffset instant)
    {
        var times = Times(pricings);

        if (times.Count == 0)
        {
            return null;
        }

        var utc = instant.ToUniversalTime();
        var day = new DateTimeOffset(utc.Year, utc.Month, utc.Day, 0, 0, 0, TimeSpan.Zero);

        for (var ahead = 0; ahead < 2; ahead++)
        {
            foreach (var time in times)
            {
                var at = day.AddDays(ahead) + time;

                if (at >= utc)
                {
                    return at;
                }
            }
        }

        return day.AddDays(1) + times[0];
    }
}
