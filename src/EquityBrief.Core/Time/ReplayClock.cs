using System.Diagnostics;

namespace EquityBrief.Core.Time;

// The clock a replayed night runs on: a session that is named, and elapsed time
// that is real.
//
// A night run for a session the operator names has to derive every date from
// that session rather than from today, which is what `FixedClock` is for. But a
// frozen clock freezes the run log with it, and the run log is not a derivation:
// it is the measurement of how long each stage took, and it is what section
// 15.10's operational header draws. Run under a frozen clock every stage starts
// and ends at the same instant, so a replayed night reports as having taken no
// time at all, and the page shows a row of zeroes that reads as a value.
//
// So the base decides the session and the stopwatch decides the duration. The
// two cannot disagree about which night it is, because the offset from the base
// is minutes and a session boundary is a day away.
// see: Queued work runs off-peak, and every schedule is written in UTC
public sealed class ReplayClock : IClock
{
    readonly DateTimeOffset session;
    readonly Stopwatch since = Stopwatch.StartNew();

    public ReplayClock(DateTimeOffset session, TimeZoneInfo sessionZone)
    {
        this.session = session.ToUniversalTime();
        SessionZone = sessionZone;
    }

    public static ReplayClock At(DateTimeOffset instant, string sessionZoneIdentifier) =>
        new(instant, SessionZones.ResolveSessionZone(sessionZoneIdentifier));

    // The named session's instant plus however long this run has been going.
    public DateTimeOffset UtcNow => session + since.Elapsed;

    public TimeZoneInfo SessionZone { get; }
}
