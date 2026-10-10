namespace EquityBrief.Core.Bars;

// The sessions the pulled history is read on, over the years the exchange's closure table does not cover.
//
// A day is a session where at least half the names whose pulled series span it hold it, and no session where fewer
// do: the closure table covers the store's own years alone, so what places an older day is the names that traded it,
// and a bar the provider sent for one name on a day the exchange was closed is a stray rather than a session every
// other name missed. The pull reads its holes by this rule and the name page's typical earnings move reads its prints
// by the same rule, each over the names it reads.
// see: The history pulled before the store's year sits apart from its bars under the pull that wrote it, read on a night for the name page's figures alone and removed whole by that pull
public sealed class PulledCalendar
{
    readonly DateOnly[] firsts;
    readonly DateOnly[] lasts;
    readonly DateOnly[] sessions;

    // How many names hold each day, and each name's first and last day held.
    public PulledCalendar(IReadOnlyDictionary<DateOnly, int> holders, IEnumerable<(DateOnly First, DateOnly Last)> spans)
    {
        var held = spans.ToArray();

        firsts = [.. held.Select(span => span.First).Order()];
        lasts = [.. held.Select(span => span.Last).Order()];
        sessions = [.. holders.Where(day => day.Value * 2 >= Spanning(day.Key)).Select(day => day.Key).Order()];
    }

    // The days read as sessions, oldest first.
    public IReadOnlyList<DateOnly> Sessions => sessions;

    // How many names' series span a day: those whose first day held is on or before it and whose last is on or after.
    public int Spanning(DateOnly day) => AtOrBefore(firsts, day) - Before(lasts, day);

    public bool IsSession(DateOnly day) => Array.BinarySearch(sessions, day) >= 0;

    // The first session on a day or after it, none past the last session read.
    public DateOnly? OnOrAfter(DateOnly day) => At(Before(sessions, day));

    // The first session after a day, none past the last session read.
    public DateOnly? After(DateOnly day) => At(AtOrBefore(sessions, day));

    // The session before a day, none on or before the first session read.
    public DateOnly? SessionBefore(DateOnly day)
    {
        var count = Before(sessions, day);

        return count > 0 ? sessions[count - 1] : null;
    }

    DateOnly? At(int index) => index < sessions.Length ? sessions[index] : null;

    // How many of the ordered days fall before a day.
    static int Before(DateOnly[] ordered, DateOnly day)
    {
        var at = Array.BinarySearch(ordered, day);

        if (at < 0)
        {
            return ~at;
        }

        while (at > 0 && ordered[at - 1] == day)
        {
            at--;
        }

        return at;
    }

    // How many of the ordered days fall on or before a day.
    static int AtOrBefore(DateOnly[] ordered, DateOnly day)
    {
        var at = Array.BinarySearch(ordered, day);

        if (at < 0)
        {
            return ~at;
        }

        while (at + 1 < ordered.Length && ordered[at + 1] == day)
        {
            at++;
        }

        return at + 1;
    }
}
