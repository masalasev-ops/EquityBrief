namespace EquityBrief.Core.Loop;

// One fold of the walk-forward tester: the year it tests, the first and the last session the history holds in it, and
// whether the year is complete, the newest session falling in a later year.
public sealed record LoopFold(int Year, DateOnly TestFrom, DateOnly TestThrough, bool Complete);

// The tester's windows: a fold a year from the first test year to the year of the newest session, the newest year a
// partial one of the months since the latest complete. A fold learns on what ended before its year's first session and
// is tested on what was entered in its year, so a setup or a trade still open at that session is learned on by none of
// the folds it reaches into, which is the purge by its actual end rather than by a fixed gap.
// see: A change is adopted only on test years the proposal never saw, and a search is judged as a procedure run year by year
public static class LoopFolds
{
    public const int FirstTestYear = 2022;

    // The folds over a calendar, each year from the first test year that holds a session at or before the newest.
    public static IReadOnlyList<LoopFold> Of(IReadOnlyList<DateOnly> calendar, DateOnly newest)
    {
        var folds = new List<LoopFold>();

        foreach (var year in calendar.Where(session => session <= newest && session.Year >= FirstTestYear).GroupBy(session => session.Year).OrderBy(group => group.Key))
        {
            folds.Add(new LoopFold(year.Key, year.Min(), year.Max(), year.Key < newest.Year));
        }

        return folds;
    }

    // Whether a fold learns on what ended on a session: it ended before the fold's first session. What has not ended is
    // learned on by no fold.
    public static bool Learns(LoopFold fold, DateOnly? ended) => ended is { } end && end < fold.TestFrom;

    // Whether a fold tests what was entered on a session.
    public static bool Tests(LoopFold fold, DateOnly entered) => entered >= fold.TestFrom && entered <= fold.TestThrough;

    // The fold a session falls in, none before the first test year.
    public static LoopFold? Holding(IReadOnlyList<LoopFold> folds, DateOnly session) =>
        folds.FirstOrDefault(fold => Tests(fold, session));
}
