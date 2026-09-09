namespace EquityBrief.Core.Providers;

// What every live feed shares that is not a client.
//
// The client itself stays in each feed's own file. `nightly-cost`'s exemption is
// one file per feed and a file earns it by implementing one, so a shared
// transport holding a client would need the carve-out to widen to cover
// something that is not a feed, which is how a named exemption turns back into
// a deleted pattern
// (see: The outward-request scan names the files that may hold a client rather than dropping the patterns).
//
// What is left over is the query, the refusal and the scrub, and none of the
// three needs an HTTP type. Four feeds repeating fifteen lines of transport is a
// smaller cost than a hole in the rule that keeps clients where they belong.
public static class EodhdQuery
{
    // The path with the key on the end of it.
    //
    // Built at the point of the call and never held. This provider takes its key
    // as a query parameter, so the returned string is the one thing in the
    // process that must not be written down.
    public static string WithKey(string path, ProviderCredentials credentials) =>
        path + (path.Contains('?', StringComparison.Ordinal) ? "&" : "?") + "api_token=" + credentials.ApiKey;

    // A provider that answered, and said no.
    //
    // The status is named because "the feed did not answer" and "the feed
    // refused the key" are different mornings for the operator, and whether it
    // is worth asking again is what decides how many attempts one request costs.
    public static ProviderRefusal Refused(int status, string what) =>
        new($"The {what} feed answered {status}. A night cannot be computed from a refusal, and " +
            "storing nothing would read as a day on which nothing happened.",
            RetryPolicy.Transient(status));

    // A transport that did not answer.
    //
    // Transient by construction: a socket that refused, a name that would not
    // resolve or a connection that dropped are all worth asking again, and a
    // provider that means no says so with a status.
    //
    // The inner exception is deliberately not carried. Its message is included
    // after redaction and its type is named, but the object is dropped: a logger
    // expanding ToString would print a message this code never scrubbed, and the
    // rule that no request URL reaches a log is absolute rather than best
    // effort.
    public static ProviderRefusal Unreachable(string what, Exception failure, ProviderCredentials credentials) =>
        new($"The {what} feed could not be reached. " +
            $"{failure.GetType().Name}: {credentials.Redact(failure.Message)}",
            transient: true);
}

// What each endpoint costs against the provider's daily allowance.
//
// `RUNBOOK.md` states the allowance and the weight of each endpoint and no code
// read either, so nothing stopped a night spending a month's allowance and
// nothing would have said it had. The figures are read back against that
// document by a test, because a number stated in a document and again in code is
// two places holding one fact.
// see: The night's cost is counted in weighted calls against the stated daily allowance
public static class ProviderWeights
{
    public const int DailyAllowance = 100_000;

    public const int BulkEndOfDay = 100;

    public const int HistoricalPerTicker = 1;

    public const int Fundamentals = 10;

    public const int News = 5;

    // Measured at 4.3 rather than read from documentation, which is what the
    // done condition asks for and what the other four figures came from. One
    // calendar request over a ninety-day window returned 22,526 rows worldwide
    // and moved the account's own request counter by one.
    public const int EarningsCalendar = 1;
}
