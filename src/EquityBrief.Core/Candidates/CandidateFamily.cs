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
// see: A candidate stands by the last row naming it, and a name retired and registered again stands once
public static class CandidateFamily
{
    public const int Maximum = 8;

    public const string Registered = "registered";

    public const string Retired = "retired";

    // The divisor: the candidates standing registered before the window opened.
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
    public static int Divisor(IEnumerable<RegisterRow> rows, DateTimeOffset windowOpenedAt) =>
        StandingBefore(rows, windowOpenedAt).Count;

    // The registration each candidate stands by before an instant, the instant taken to the second the register holds.
    // see: A register row in the same second as the instant it is compared with is read as after it
    public static IReadOnlyList<RegisterRow> StandingBefore(IEnumerable<RegisterRow> rows, DateTimeOffset at)
    {
        var second = new DateTimeOffset(at.UtcTicks - (at.UtcTicks % TimeSpan.TicksPerSecond), TimeSpan.Zero);

        return LastWords(rows.Where(row => row.RegisteredAt < second));
    }

    // The registration each candidate stands by as of an instant, the rows written at it included,
    // which is what the registrar asks of the register it has just read.
    public static IReadOnlyList<RegisterRow> Standing(IEnumerable<RegisterRow> rows, DateTimeOffset at) =>
        LastWords(rows.Where(row => row.RegisteredAt <= at));

    public static bool StandsAt(IEnumerable<RegisterRow> rows, string candidate, DateTimeOffset at) =>
        Standing(rows, at).Any(row => string.Equals(row.Candidate, candidate, StringComparison.Ordinal));

    // The last row naming each candidate, kept where it is a registration: the first would make a
    // retirement permanent in a table that can only be appended to.
    static IReadOnlyList<RegisterRow> LastWords(IEnumerable<RegisterRow> rows) =>
    [
        .. rows
            .Select(row => (Row: row, Names: row.Event == Registered ? row.Candidate : row.Retires))
            .Where(named => named.Names is not null)
            .GroupBy(named => named.Names!, StringComparer.Ordinal)
            .Select(group => group.OrderBy(named => named.Row.RegisteredAt).ThenBy(named => named.Row.Id).Last().Row)
            .Where(row => row.Event == Registered)
            .OrderBy(row => row.Candidate, StringComparer.Ordinal),
    ];
}
