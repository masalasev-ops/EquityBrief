using EquityBrief.Core.Candidates;
using EquityBrief.Core.Ledger;
using EquityBrief.Core.Loop;
using EquityBrief.Worker.Sweep;

namespace EquityBrief.Worker.Loop;

// On the operator's answer of 2026-11-14 to item 15 each live book's stops and take-profits measured per dollar risked over the
// history the run reads, the long one with the long facts beside the standing ones, its points a month beside, printed and
// written nowhere.
// see: Every pick takes profit at its own sale's price, a price it turned down at before or a multiple of its risk
public sealed partial class WalkForwardTester
{
    async Task<int> BooksAsync(string index, LoopRead read, SweepHistory history, History.LongHistoryReader? longHistory, DateOnly through, IReadOnlyList<RegisterRow> standing, CancellationToken cancellation)
    {
        Func<IEnumerable<string>, Task<IReadOnlyDictionary<string, IReadOnlyList<FiledFactRow>>>>? factsOf = longHistory is null
            ? null
            : ciks => longHistory.FiledFactsAsync(ciks, through, cancellation);
        var heavyweights = await HeavyweightLay.ReadAsync(read, history, through, output.WriteLine, cancellation);
        var quality = ReadsQuality(standing, index) ? await QualityLay.ReadAsync(read, history, through, output.WriteLine, cancellation, factsOf) : null;
        var releases = ReadsImproving(standing, index) ? await ReleasesAsync(databaseFile, clock, cancellation) : null;
        var improving = releases is not null ? await ImprovingLay.ReadAsync(read, history, releases.ByFiler, through, output.WriteLine, cancellation, factsOf) : null;

        // What the quality lay read at each year's first reshuffle, so a year its facts do not reach is seen as one.
        foreach (var year in quality?.Coverage().GroupBy(one => one.Session.Year).Select(one => one.First()) ?? [])
        {
            output.WriteLine(FormattableString.Invariant($"book risk: quality's reading on {year.Session:yyyy-MM-dd}: {year.Answered} member(s) answered, {year.Scored} scored, {year.FactsReach} with facts reaching every quarter, {year.Valued} valued, {year.Qualifying} qualifying at the starting setting"));
        }

        bool InTestYears(int entry) => read.Folds.Any(fold => read.Calendar[entry] >= fold.TestFrom && read.Calendar[entry] <= fold.TestThrough);

        foreach (var (family, rule, tape, book) in BookRisk.LiveBooks(standing, index, heavyweights, quality, improving))
        {
            if (tape is null || book is null)
            {
                output.WriteLine($"book risk: {family}, {rule}: off its design's six settings, walked at its own setting with no stop and take-profit");

                continue;
            }

            // On the operator's ruling of 2026-11-14 on the books the nine full exits and the nine half exits, chosen in points a month,
            // the book's own exit beside them as the reference and not a choice, each read per dollar risked beside.
            // On the operator's answer of 2026-11-14 on the tables three full take-profits beside the half: today's, the multiple of
            // the risk alone, and the first price the stock turned down at that far above the buy or more.
            (bool Half, Core.Loop.TakeProfitPlacing Placing, Func<(double Moves, double Multiple), string> Words)[] shapes =
            [
                (false, Core.Loop.TakeProfitPlacing.Turned, BookStops.Words),
                (false, Core.Loop.TakeProfitPlacing.Risk, BookStops.AtRiskWords),
                (false, Core.Loop.TakeProfitPlacing.Beyond, BookStops.BeyondWords),
                (true, Core.Loop.TakeProfitPlacing.Turned, BookStops.HalfWords),
            ];
            var walked = shapes
                .SelectMany(shape => BookStops.Grid.Select(stops => (Stops: stops, shape.Half, shape.Placing, Words: shape.Words(stops))))
                .Select(one => (one.Stops, one.Half, one.Placing, one.Words, Book: book(one.Stops, one.Half, one.Placing)))
                .ToArray();
            var own = new RuleUnderTest(family, rule, RuleTests.Live, rule, LoopProcedures.Points, BookMonths.LongestMonth, Months: book(null, false, Core.Loop.TakeProfitPlacing.Turned).Months)
            {
                BookBeside = [.. walked.Select(one => (one.Words, one.Book.Months))],
            };

            output.WriteLine(RuleTests.BookChoiceLine(own, index, read.Calendar, InTestYears));

            // On the operator's answer of 2026-11-14 on the tables a heavyweights book holding no month before its judged years takes
            // the middle exit, its box counting from its first month.
            var chosen = RuleTests.BookChosen(own, index, read.Calendar, InTestYears);
            var middle = chosen is null && family == Core.Families.HeavyweightRule.Name;

            if (middle)
            {
                chosen = BookStops.AtRiskWords(BookStops.Middle);
                output.WriteLine($"book choice: {family}, {rule}: holding no month before {RuleTests.HeavyweightsJudgedFrom}, it takes the middle exit, {chosen}");
            }

            // What each exit's trades came to, beside the book's own: how many ended, why, their mean result after the round trip,
            // and how many sold half first, so a choice's figure can be read back to the trades behind it.
            foreach (var (words, costed) in walked.Select(one => (one.Words, one.Book.Costed)).Prepend(("its own exit", book(null, false, Core.Loop.TakeProfitPlacing.Turned).Costed)))
            {
                var ended = costed.Where(trade => trade.End is not null && trade.Result is not null).ToArray();
                var reasons = string.Join(", ", ended.GroupBy(trade => trade.Reason ?? "none").OrderByDescending(group => group.Count()).Select(group => FormattableString.Invariant($"{group.Key} {group.Count()}")));

                output.WriteLine(FormattableString.Invariant($"book trades: {family}, {words}: {ended.Length} ended, {ended.Count(trade => trade.HalfAt is not null)} selling half first, a mean result of {(ended.Length > 0 ? ended.Average(trade => trade.Result!.Value) * 100 : 0):+0.00;-0.00;0.00}% and a median of {(ended.Length > 0 ? ended.Select(trade => trade.Result!.Value).Order().ElementAt(ended.Length / 2) * 100 : 0):+0.00;-0.00;0.00}%; {reasons}"));
            }

            output.WriteLine(BookRisk.Line(family, rule, index, read.Calendar, [.. walked.Select(one => (one.Words, BookRisk.Edges(tape, read.Series, one.Book.Costed, one.Stops, one.Half, one.Placing)))], chosen, boxFromFirst: middle));
        }

        return 0;
    }
}
