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

            var walked = BookStops.Grid.Select(stops => (Stops: stops, Book: book(stops))).ToArray();

            output.WriteLine(BookRisk.Line(family, rule, index, read.Calendar, [.. walked.Select(one => (BookStops.Words(one.Stops), BookRisk.Edges(tape, read.Series, one.Book.Costed, one.Stops)))]));

            var own = new RuleUnderTest(family, rule, RuleTests.Live, rule, LoopProcedures.Points, BookMonths.LongestMonth, Months: book(null).Months)
            {
                BookBeside = [.. walked.Select(one => (BookStops.Words(one.Stops), one.Book.Months))],
            };

            output.WriteLine(RuleTests.BookChoiceLine(own, index, read.Calendar, InTestYears));
        }

        return 0;
    }
}
