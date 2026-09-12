using EquityBrief.Core.Bars;
using System.Globalization;

namespace EquityBrief.Worker.Bars;

// The stop a computed stage makes over a name whose stored series has an
// interior hole, and the tally it reports afterwards.
//
// A decision has said since 1.5 that every computation over a name whose stored
// series has a gap stops and reports the gap's date, and until 6.0 no stage did:
// only the backfill consulted the calendar at all. Two of 503 members are absent
// from an ordinary day's bulk file, so their series carry interior holes that
// the indicators, the swings, the profile, the levels and the moves all counted
// across as though the sessions either side were adjacent.
// see: A gap is a session the exchange traded and the store does not hold
// see: Bars are never interpolated
//
// One reader rather than five copies of the rule, for the reason
// ProviderBarReader is one: five readings of one rule drift, and the reading is
// the whole of what is being asserted. What is not shared is the write. Each
// stage skips its own name and appends its own tally to its own run log row,
// because a write is attributed to the file it appears in.
//
// The whole stored series is what is checked, not the window a stage happens to
// compute over. The decision is written about the name's series, and a hole
// older than one stage's window is still a hole the next stage's window reaches,
// so a per-window reading would stop a name in one table and not in another
// while both drew from the same broken series.
public sealed class SeriesGapStop
{
    readonly List<Gap> stopped = [];

    int notChecked;

    // Whether this name is stopped, which is the only question the caller asks.
    // The tally is kept here so the caller does not have to.
    public bool Stops(string ticker, IReadOnlyCollection<DateOnly> sessions)
    {
        var gaps = TradingCalendar.Check(sessions);

        if (!gaps.Checked)
        {
            notChecked++;

            return false;
        }

        if (!gaps.HasGap)
        {
            return false;
        }

        stopped.Add(new Gap(ticker, gaps.Earliest!.Value));

        return true;
    }

    public int Stopped => stopped.Count;

    // Counted and reported rather than folded into the clean count. A series the
    // closure table cannot place was not compared, and reporting it as clean is
    // the shape of a check that passes by having no population.
    public int NotChecked => notChecked;

    public IReadOnlyList<Gap> Gaps => stopped;

    // What the stage appends to its run log detail. Empty where nothing was
    // stopped and nothing went unchecked, so an ordinary night's row is
    // unchanged and a night with a hole in it says which name and which session.
    //
    // Every stopped name is named with its earliest gap rather than a count
    // alone, because the operator's next action is to look at that name on that
    // date, and a count sends them to a query instead.
    public string Report()
    {
        if (stopped.Count == 0 && notChecked == 0)
        {
            return string.Empty;
        }

        var parts = new List<string>();

        if (stopped.Count > 0)
        {
            parts.Add(
                stopped.Count.ToString(CultureInfo.InvariantCulture)
                + " stopped at a gap ("
                + string.Join(
                    ", ",
                    stopped
                        .OrderBy(gap => gap.Ticker, StringComparer.Ordinal)
                        .Select(gap =>
                            gap.Ticker + " " + gap.SessionDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)))
                + ")");
        }

        if (notChecked > 0)
        {
            parts.Add(notChecked.ToString(CultureInfo.InvariantCulture) + " not checked for gaps");
        }

        return ", " + string.Join(", ", parts);
    }
}
