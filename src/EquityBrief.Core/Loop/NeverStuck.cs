namespace EquityBrief.Core.Loop;

// An idea the one tester read as a near miss: its notebook row, its setup and index, its words and line, its adjusted p-value
// in its run and the bar it was held to, and when it was written.
public sealed record NearMissRow(long Id, string Index, string Family, string Words, string? Line, double? Adjusted, double Bar, DateTimeOffset Entered);

// An idea the researcher placed: its setup and index, its line and sentence, and when it was written.
public sealed record PlacedIdea(string Index, string Family, string Line, string Reason, DateTimeOffset Written);

// The loop's state since the monthly run before the newest: the day it ran, how many ideas passed since, the nearest misses with
// what the researcher tried next on each one's setup, how many of the researcher's weeks failed and ran, and the next week's
// day.
public sealed record StuckView(DateOnly? Since, int Passed, IReadOnlyList<(NearMissRow Miss, PlacedIdea? Next)> Nearest, int FailedWeeks, int Weeks, DateOnly NextWeek);

// Never stuck: where no idea has passed since the monthly run before the newest, the page says so and names the nearest misses,
// those closest to the bar, each with the first idea the researcher placed on its setup and index after it was tested; and it
// counts the researcher's weeks that failed, each asked once more before it was counted, beside the day the next runs
// regardless.
// see: A month with no idea passed names its nearest misses and what the researcher tried next, and a failed week is counted and never stops the next
public static class NeverStuck
{
    // The nearest misses the page names.
    public const int NearestShown = 3;

    // The researcher's day and hour, in UTC.
    public const DayOfWeek ResearchDay = DayOfWeek.Sunday;

    public const int ResearchHourUtc = 13;

    // The nearest misses: those holding an adjusted p-value, the lowest first, the newer first on a tie.
    public static IReadOnlyList<NearMissRow> Nearest(IEnumerable<NearMissRow> misses) =>
        [.. misses.Where(miss => miss.Adjusted is not null).OrderBy(miss => miss.Adjusted).ThenByDescending(miss => miss.Entered).ThenByDescending(miss => miss.Id).Take(NearestShown)];

    // What the researcher tried next from a near miss: the first idea it placed on the same setup and index after the miss was
    // written; none where it has placed none since.
    public static PlacedIdea? NextFrom(NearMissRow miss, IEnumerable<PlacedIdea> placed) =>
        placed
            .Where(idea => idea.Index == miss.Index && idea.Family == miss.Family && idea.Written > miss.Entered && idea.Line != miss.Line)
            .OrderBy(idea => idea.Written)
            .FirstOrDefault();

    // The day the researcher's next week runs: the next Sunday whose hour has not passed.
    public static DateOnly NextWeek(DateTimeOffset now)
    {
        var at = now.ToUniversalTime();
        var day = DateOnly.FromDateTime(at.UtcDateTime);
        var ahead = ((int)ResearchDay - (int)day.DayOfWeek + 7) % 7;

        return ahead == 0 && at.Hour >= ResearchHourUtc ? day.AddDays(7) : day.AddDays(ahead);
    }

    // The view over what the store holds since the monthly run before the newest.
    public static StuckView Of(DateOnly? since, int passed, IReadOnlyList<NearMissRow> misses, IReadOnlyList<PlacedIdea> placed, int failedWeeks, int weeks, DateTimeOffset now) =>
        new(since, passed, [.. Nearest(misses).Select(miss => (miss, NextFrom(miss, placed)))], failedWeeks, weeks, NextWeek(now));
}
