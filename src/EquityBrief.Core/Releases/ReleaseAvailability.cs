namespace EquityBrief.Core.Releases;

// The two slots the local run reads at each day, in UTC: the early one after the second off-peak run, and the late
// one ending before the night's window, so a release filed after the close is read before the night that reads it.
// Each slot asks nothing past its own last instant, and the late one writes its row before the night starts.
// see: The local run is a process of its own at two slots outside the night and refuses any model file or prompt but its pin, whatever runtime answers
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

    // From 20.3 the instant a print's release may be accepted from: the start in New York of the exchange's session
    // before the print's report date, so a release of an earlier quarter never answers for the print, and none where
    // the calendar places no session before it.
    // see: The drift's tone variant lists only where its print's release reads better than expected and its outlook was not lowered or withdrawn
    public static DateTimeOffset? WindowFrom(DateOnly reportDate, TimeZoneInfo zone, ReleaseCalendar calendar) =>
        calendar.SessionBefore(reportDate) is { } day
            ? new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(day.ToDateTime(TimeOnly.MinValue), zone), TimeSpan.Zero)
            : null;

    // The release that answers for a print on a night: of the filer's releases, the newest the SEC accepted from the
    // print's window that the night may read, or none.
    public static T? ReleaseFor<T>(IEnumerable<T> releases, Func<T, DateTimeOffset> accepted, DateOnly reportDate, DateOnly night, TimeZoneInfo zone, ReleaseCalendar calendar)
        where T : class
    {
        if (WindowFrom(reportDate, zone, calendar) is not { } from)
        {
            return null;
        }

        return releases
            .Where(release => accepted(release) >= from && ReadableOn(accepted(release), night))
            .OrderByDescending(accepted)
            .FirstOrDefault();
    }
}

// The sessions the release rules read by: the exchange's closure table over the years it covers, and over the years
// before it the sessions handed in, being the ones the ledger read, as the pulled history's sessions are read off the
// names that traded them. A day before the first session handed in is placed by neither, and a day past the table's
// years is refused by the table.
public sealed class ReleaseCalendar
{
    readonly DateOnly[] before;

    public ReleaseCalendar(IEnumerable<DateOnly> sessions) =>
        before = [.. sessions.Where(day => day < Bars.ExchangeClosures.CoveredFrom).Distinct().Order()];

    // The closure table's alone, as the night reads it.
    public static ReleaseCalendar Exchange { get; } = new([]);

    // Whether the exchange traded on a day, none where the calendar cannot place it.
    public bool? IsSession(DateOnly day) =>
        day >= Bars.ExchangeClosures.CoveredFrom ? Bars.ExchangeClosures.IsSession(day)
        : before.Length > 0 && day >= before[0] ? Array.BinarySearch(before, day) >= 0
        : null;

    // The session before a day, none where the calendar cannot place a day before it.
    public DateOnly? SessionBefore(DateOnly day) => Walk(day, -1);

    // The session after a day, none where the calendar cannot place a day after it.
    public DateOnly? SessionAfter(DateOnly day) => Walk(day, 1);

    DateOnly? Walk(DateOnly day, int step)
    {
        for (var at = day.AddDays(step); ; at = at.AddDays(step))
        {
            switch (IsSession(at))
            {
                case true:
                    return at;
                case null:
                    return null;
            }
        }
    }
}
