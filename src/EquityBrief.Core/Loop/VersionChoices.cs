namespace EquityBrief.Core.Loop;

// From 21.10 choosing a family's version by its recent record, a procedure the per-rule test walks: at each month's first
// session, among the family's rules standing on the index, the one whose mean edge after costs over the sessions before
// stands highest over at least the floor of ended trades, or a book's months, the live rule where none qualifies, its
// trades walked for the month. Three procedures a family, one a window. Each is judged as a variant against the live rule
// in the family's own correction, and its row in the per-rule test's table carries its name, which pages tell it apart
// from a registered rule by.
// see: A family's version is chosen each month by its recent record as a procedure judged against staying with the live rule, and an approval of it waits on the operator's word
public static class VersionChoices
{
    // The windows before each month the record is read over, in sessions.
    public static IReadOnlyList<int> Windows { get; } = [126, 252, 504];

    // The ended trades a swing rule's record must hold, and the months a book's, to be chosen.
    public const int TradeFloor = 50;

    public const int MonthFloor = 12;

    public const string Prefix = "choosing a version by its record over ";

    public static string NameOf(int window) => FormattableString.Invariant($"{Prefix}the {window} sessions before");

    // Whether a per-rule test's row is one of these procedures rather than a registered rule.
    public static bool Is(string rule) => rule.StartsWith(Prefix, StringComparison.Ordinal);

    // A rule's record at a month's first session: its mean edge over its units entered within the window before and
    // ended before the month, a unit counted ended once its cap's sessions have passed; none under the floor.
    public static double? Record(IReadOnlyList<(int Entry, double? Edge)> units, int cap, int start, int window, bool book)
    {
        var floor = book ? MonthFloor : TradeFloor;
        var ended = units
            .Where(unit => unit.Edge is not null && unit.Entry >= start - window && unit.Entry + cap < start)
            .Select(unit => unit.Edge!.Value)
            .ToArray();

        return ended.Length >= floor ? ended.Average() : null;
    }

    // The rule chosen at a month's first session among the rules given, the live rule first: the one whose record stands
    // highest, an earlier one on a tie, and the live rule where none qualifies.
    public static int Choose(IReadOnlyList<(IReadOnlyList<(int Entry, double? Edge)> Units, int Cap)> rules, int start, int window, bool book)
    {
        var chosen = 0;
        double? best = null;

        for (var at = 0; at < rules.Count; at++)
        {
            if (Record(rules[at].Units, rules[at].Cap, start, window, book) is { } record && (best is not { } standing || record > standing))
            {
                (chosen, best) = (at, record);
            }
        }

        return chosen;
    }

    // The procedure's walk: at each month's first session the rule chosen, its units entered before the next month's
    // first session kept; with the rule chosen each month, and the one chosen after the last session given.
    public static (IReadOnlyList<(int Entry, double? Edge)> Units, IReadOnlyList<int> Chosen, int Now) Walk(
        IReadOnlyList<(IReadOnlyList<(int Entry, double? Edge)> Units, int Cap)> rules,
        IReadOnlyList<int> monthStarts,
        int sessions,
        int window,
        bool book)
    {
        var units = new List<(int Entry, double? Edge)>();
        var chosen = new List<int>();

        for (var month = 0; month < monthStarts.Count; month++)
        {
            var start = monthStarts[month];
            var end = month + 1 < monthStarts.Count ? monthStarts[month + 1] : sessions;
            var at = Choose(rules, start, window, book);

            chosen.Add(at);
            units.AddRange(rules[at].Units.Where(unit => unit.Entry >= start && unit.Entry < end));
        }

        return (units, chosen, Choose(rules, sessions, window, book));
    }

    // The first session of each month over a calendar, by its place.
    public static IReadOnlyList<int> MonthStarts(IReadOnlyList<DateOnly> calendar) =>
    [
        .. Enumerable.Range(0, calendar.Count).Where(at => at == 0 || calendar[at].Month != calendar[at - 1].Month || calendar[at].Year != calendar[at - 1].Year),
    ];
}
