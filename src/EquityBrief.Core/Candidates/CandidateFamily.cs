namespace EquityBrief.Core.Candidates;

// One row of the register, as anything reading it back sees it.
public sealed record RegisterRow(
    long Id,
    string Candidate,
    string Rule,
    string Test,
    string Evaluator,
    string Parameters,
    string EvaluatorVersion,
    string Event,
    string? Retires,
    DateTimeOffset RegisteredAt,
    string? Evidence);

// The family a candidate is tested in, and the divisor its threshold is
// corrected by.
//
// The maximum is a convention and no count of resolved setups measures it: with
// five worthless candidates the chance one clears an ordinary test is about 23
// per cent and with twenty it is about 64, and eight is the size at which the
// correction stays a correction rather than a bar no candidate could clear.
// see: The candidate family is at most eight and the threshold is divided by it
public static class CandidateFamily
{
    public const int Maximum = 8;

    public const string Registered = "registered";

    public const string Retired = "retired";

    // The divisor: the candidates registered before the window opened and not
    // retired before it opened either.
    //
    // Before, on both halves, and that is the whole point of the figure rather
    // than a detail of it. A candidate registered after the window opened was not
    // among the things being tried over the evidence the window holds, so
    // counting it makes the test harder than it was; a candidate retired after
    // the window opened was among them for the whole of it, so not counting it
    // makes the test easier than it was, which is the direction that turns noise
    // into a discovery. A register that could be edited would let either be
    // arranged afterwards, which is why the table refuses an edit rather than the
    // components that reach it.
    // see: Candidate conditions are registered before they are scored, and scored in shadow before they are shown
    // see: Adding a candidate later restarts the clock
    public static int Divisor(IEnumerable<RegisterRow> rows, DateTimeOffset windowOpenedAt)
    {
        var before = rows.Where(row => row.RegisteredAt < windowOpenedAt).ToArray();

        var registered = before
            .Where(row => row.Event == Registered)
            .Select(row => row.Candidate)
            .ToHashSet(StringComparer.Ordinal);

        var retired = before
            .Where(row => row.Event == Retired && row.Retires is not null)
            .Select(row => row.Retires!)
            .ToHashSet(StringComparer.Ordinal);

        registered.ExceptWith(retired);

        return registered.Count;
    }

    // Whether a candidate stands registered as of an instant, which is what the
    // shadow column asks of each row and what a second registration of the same
    // name is refused against.
    public static bool StandsAt(IEnumerable<RegisterRow> rows, string candidate, DateTimeOffset at)
    {
        var mine = rows
            .Where(row => row.RegisteredAt <= at)
            .Where(row => row.Event == Registered
                ? string.Equals(row.Candidate, candidate, StringComparison.Ordinal)
                : string.Equals(row.Retires, candidate, StringComparison.Ordinal))
            .OrderBy(row => row.RegisteredAt)
            .ThenBy(row => row.Id)
            .ToArray();

        // The last word rather than the first, because a candidate retired and
        // registered again is registered: the corpus's own convention for a
        // superseded thing is a new dated row, and reading the first row would
        // make a retirement permanent in a table that can only be appended to.
        return mine.Length > 0 && mine[^1].Event == Registered;
    }
}
