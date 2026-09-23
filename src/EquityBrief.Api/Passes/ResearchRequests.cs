namespace EquityBrief.Api.Passes;

// What a request came to, and the line the page states beside the control either way.
public sealed record RequestWritten(bool Written, string Line);

// One row of the queue, as a screen reads it.
public sealed record RequestRow(
    string Ticker,
    DateTimeOffset AskedAt,
    string AskedFrom,
    string Lane,
    string State,
    DateTimeOffset? SettledAt,
    string? RunId,
    string? Reason);

// The words a request is written in, held apart from the statements that write
// them. The read surface and the worker both spell these states, and the surface
// is the one that owns the table's insert, so the vocabulary carries no
// statement of its own: a file holding a write is read as the writer of it, and
// a second writer of this table beside the one SCHEMA declares is the thing that
// would be wrong.
// see: A press writes a request and starts the worker's drain as a process of its own, and every pass waits for the off-peak hours
public static class ResearchRequests
{
    public const string Outstanding = "outstanding";
    public const string Writing = "writing";
    public const string Written = "written";
    public const string Refused = "refused";
    public const string Withdrawn = "withdrawn";

    public const string FromList = "list";
    public const string FromName = "name";

    public const string Local = "local";

    // The lane a request carries. Every request is asked under the paid lane
    // until the local one is offered, which waits on the comparison that would
    // let it be chosen.
    public const string Paid = "paid";

    public const string Instant = "yyyy-MM-ddTHH:mm:ssZ";
}
