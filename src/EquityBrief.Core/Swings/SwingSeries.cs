namespace EquityBrief.Core.Swings;

// One session's high and low, as prices.
//
// Decimal rather than double, and that is not the same choice IndicatorSeries
// made. An indicator is a statistic about prices and is double; a swing is a
// price, stored TEXT, quoted in a report and compared against a level. Nothing
// here averages anything, so nothing here crosses the money boundary at all.
public readonly record struct SwingBar(DateOnly SessionDate, decimal High, decimal Low);

// A local peak or trough, with the date its lookback completed.
//
// ConfirmedOn is part of the swing rather than a column the store adds, because
// a swing is not knowable on its own day and a value that reaches a reader
// without it is a value the reader will use too early.
public readonly record struct Swing(DateOnly SessionDate, string Direction, decimal Price, DateOnly ConfirmedOn);

// The swing arithmetic, as a pure function of a session-ordered series.
//
// Separate from the component that reads and writes, for the reason
// IndicatorSeries is: the days it marks can be asserted against a window read
// off the committed bars with no store in the way.
// see: Code owns every number
public static class SwingSeries
{
    // SCHEMA's direction column, both values.
    public const string High = "high";
    public const string Low = "low";

    // Section 17's swing lookback. Three bars each side, so a swing is decided
    // over seven sessions and cannot be decided at all until three have passed.
    public const int Lookback = 3;

    // A session is a swing high when its high is above every one of the six
    // around it, and a swing low when its low is below every one of them.
    //
    // Strictly above, and the strictness is the whole of the tie rule. Two
    // adjacent sessions sharing a high are a plateau rather than a peak: marking
    // both would put two swings at one price three days apart, and marking one
    // would make the answer depend on which end the scan started from. Neither is
    // a local extreme in the sense the level builder needs, so a plateau produces
    // nothing.
    //
    // The first and last three sessions of a series produce nothing either. They
    // have no window on one side, and a session judged against four bars instead
    // of six is a swing under a different rule than the one section 17 states.
    // The stored series starts where the retention window starts rather than
    // where the name started trading, so its first three sessions are an artefact
    // of the window and never a peak anyone saw.
    public static IReadOnlyList<Swing> For(IReadOnlyList<SwingBar> bars)
    {
        var swings = new List<Swing>();

        for (var index = Lookback; index < bars.Count - Lookback; index++)
        {
            var session = bars[index];

            // The seven-session window, the middle one excluded, so the
            // comparison below is against the six neighbours and never against
            // the session itself. Comparing a session with itself under a strict
            // test would mark nothing at all, which is the failure mode a window
            // built by slicing invites.
            var highest = true;
            var lowest = true;

            for (var at = index - Lookback; at <= index + Lookback; at++)
            {
                if (at == index)
                {
                    continue;
                }

                highest &= session.High > bars[at].High;
                lowest &= session.Low < bars[at].Low;
            }

            // The session that completes the lookback, which is the third one
            // after the peak and not three calendar days after it. A holiday
            // inside the window moves this date and does not change the swing.
            var confirmedOn = bars[index + Lookback].SessionDate;

            if (highest)
            {
                swings.Add(new Swing(session.SessionDate, High, session.High, confirmedOn));
            }

            // Not an else. A session whose range covers the six around it on
            // both sides is an outside day, and it is a peak and a trough at
            // once. SCHEMA's primary key carries the direction for that reason,
            // so the two rows sit side by side rather than one overwriting the
            // other.
            if (lowest)
            {
                swings.Add(new Swing(session.SessionDate, Low, session.Low, confirmedOn));
            }
        }

        return swings;
    }
}
