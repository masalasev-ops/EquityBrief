namespace EquityBrief.Core.Releases;

// The two slots the local run reads at each day, in UTC: the early one after the second off-peak run, and the late
// one ending before the night's window, so a release filed after the close is read before the night that reads it.
// Each slot asks nothing past its own last instant, and the late one writes its row before the night starts.
// see: The local run is a process of its own at two slots outside the night and refuses any model file, runtime or prompt but its pin
public static class LocalSlots
{
    // The stage every local run's row is written under, which the Run page reads.
    public const string Stage = "local-run";

    public const string Early = "early";
    public const string Late = "late";

    public static IReadOnlyList<string> All { get; } = [Early, Late];

    public static TimeOnly EarlyStart { get; } = new(12, 5);

    public static TimeOnly EarlyAsksNothingAfter { get; } = new(21, 30);

    public static TimeOnly LateStart { get; } = new(21, 40);

    public static TimeOnly LateAsksNothingAfter { get; } = new(22, 50);

    public static TimeOnly LateRowBy { get; } = new(22, 55);

    // The instant a slot asks nothing after on the day it runs, in UTC.
    public static DateTimeOffset AsksNothingAfter(string slot, DateOnly day) =>
        new(day.ToDateTime(slot == Late ? LateAsksNothingAfter : EarlyAsksNothingAfter), TimeSpan.Zero);
}

// When a night may read a release's reading: from the first night whose late slot starts after the SEC accepted the
// release, one rule the history and the live run follow alike. The night of a session reads a release accepted before
// the late slot's start on that session's date, and a release accepted after it from the next session's night. The
// late slot's start is a constant here rather than the scheduler's, so a moved schedule moves no history.
// see: A release reading is readable from the first night whose late slot starts after the release was accepted
public static class ReleaseAvailability
{
    // The late slot's start on a session's date, in UTC.
    public static DateTimeOffset LateSlotOn(DateOnly session) =>
        new(session.ToDateTime(LocalSlots.LateStart), TimeSpan.Zero);

    // Whether the night of a session may read a release the SEC accepted at the instant given.
    public static bool ReadableOn(DateTimeOffset accepted, DateOnly session) =>
        accepted.ToUniversalTime() < LateSlotOn(session);

    // The first of the sessions given, in their order, whose night may read the release, or none where none may.
    public static DateOnly? FirstNight(DateTimeOffset accepted, IEnumerable<DateOnly> sessions) =>
        sessions.Order().Where(session => ReadableOn(accepted, session)).Select(session => (DateOnly?)session).FirstOrDefault();
}
