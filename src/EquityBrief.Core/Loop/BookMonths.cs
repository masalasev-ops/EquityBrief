namespace EquityBrief.Core.Loop;

// A holding a book kept, as its months read it: the session it was bought on, the session it was sold on or none while
// it is held, its round trip as a fraction of its buy, and its own return and its size cut's between two sessions'
// closes, none where a close is missing.
public sealed record BookHolding(int Entry, int? End, double Cost, Func<int, int, double?> Return, Func<int, int, double?> CutReturn);

// The sector heavyweights' unit: a book's month, from the close of the session one month opens on to the close of the
// session the next opens on. Its edge is the mean, over the holdings held across any part of it, of each holding's return
// over the sessions it held in the month less its size cut's over the same sessions, a holding paying its round trip in
// the month it was bought; a month the book held nothing in has no edge, and the newest month, which no next month
// closes, is not read. A book's holdings run for months and overlap, so a month is the unit the tester pairs, and a
// month's edge is known at its close.
// see: A sector heavyweights book is judged a month at a time, each month's edge the mean over the holdings it held
public static class BookMonths
{
    // The most sessions a month holds, which a block of months waits after its last session before it is read.
    public const int LongestMonth = 23;

    public static IReadOnlyList<(int Opens, double? Edge)> Edges(IReadOnlyList<int> months, IReadOnlyList<BookHolding> holdings)
    {
        var edges = new List<(int Opens, double? Edge)>();

        for (var month = 0; month + 1 < months.Count; month++)
        {
            var (opens, closes) = (months[month], months[month + 1]);
            var sum = 0.0;
            var held = 0;

            foreach (var holding in holdings)
            {
                var from = Math.Max(holding.Entry, opens);
                var to = Math.Min(holding.End ?? int.MaxValue, closes);

                if (from >= to || holding.Return(from, to) is not { } own || holding.CutReturn(from, to) is not { } cut)
                {
                    continue;
                }

                sum += own - cut - (holding.Entry >= opens && holding.Entry < closes ? holding.Cost : 0);
                held++;
            }

            edges.Add((opens, held > 0 ? sum / held : null));
        }

        return edges;
    }

    // Two books' months paired, a month either book held something in, a book holding nothing in it counting nothing.
    public static IReadOnlyList<PairedUnit> Paired(IReadOnlyList<(int Opens, double? Edge)> proposed, IReadOnlyList<(int Opens, double? Edge)> current)
    {
        var mine = proposed.ToDictionary(month => month.Opens, month => month.Edge);
        var theirs = current.ToDictionary(month => month.Opens, month => month.Edge);

        return
        [
            .. mine.Keys.Union(theirs.Keys)
                .Order()
                .Select(opens => new PairedUnit(opens, mine.GetValueOrDefault(opens), theirs.GetValueOrDefault(opens)))
                .Where(unit => unit.Proposed is not null || unit.Current is not null),
        ];
    }
}
