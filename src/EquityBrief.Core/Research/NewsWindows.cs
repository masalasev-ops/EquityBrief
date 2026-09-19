using EquityBrief.Core.Facts;

namespace EquityBrief.Core.Research;

// The windows of one name's news a research pass reads: the span of each stored move, which is
// where the cause of that move may rest, and the days from the company's own filing to the night,
// which is what the sections built across the evidence are handed beside it. Spans that overlap
// or touch are read as one, and nothing older than three months before the night is read.
//
// Never the stored year. The rule hands a section nothing outside these windows, and one name's
// year can pass the most pages a query reads: 6.11's production run asked for MSFT's and the
// provider still had more after twenty pages of a thousand articles.
// see: A research pass reads a name's news from the last three months alone, inside each stored move and since the company's own filing, and hands each section the documents code picks from it
public static class NewsWindows
{
    // Where the company has no filing a pass can read, the sections built across the evidence are
    // handed the newest documents of the quarter back from the night, which is the stretch a
    // results release would have covered.
    public const int WithoutAFilingMonths = 3;

    // How far back from the night a pass reads news at all. A move older than this is read
    // with no document, and its cause is not written.
    public const int NewsMonths = 3;

    public static IReadOnlyList<(DateOnly From, DateOnly To)> For(IReadOnlyList<MoveWindow> moves, DateOnly? filedOn, DateOnly asOf)
    {
        var earliest = asOf.AddMonths(-NewsMonths);
        var since = filedOn is { } filed && filed <= asOf ? filed : asOf.AddMonths(-WithoutAFilingMonths);

        var spans = moves
            .Where(move => move.From is { } from && from <= move.To)
            .Select(move => (From: move.From!.Value, move.To))
            .Append((From: since, To: asOf))
            .Where(span => span.To >= earliest)
            .Select(span => (From: span.From < earliest ? earliest : span.From, span.To))
            .OrderBy(span => span.From)
            .ThenBy(span => span.To)
            .ToArray();

        var windows = new List<(DateOnly From, DateOnly To)>();

        foreach (var span in spans)
        {
            if (windows.Count > 0 && span.From <= windows[^1].To.AddDays(1))
            {
                windows[^1] = (windows[^1].From, span.To > windows[^1].To ? span.To : windows[^1].To);
            }
            else
            {
                windows.Add(span);
            }
        }

        return windows;
    }
}
