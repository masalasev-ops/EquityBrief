namespace EquityBrief.Core.Providers;

// A provider said no, and whether saying it again would help.
//
// The distinction is the whole of the retry policy. A refused connection and a
// rejected rate are worth asking again; a rejected key is wrong three times and
// the retry only delays the message that says so.
// see: A feed is tried three times with a doubling backoff, and the night has a deadline it cannot move
public sealed class ProviderRefusal(string message, bool transient) : Exception(message)
{
    public bool Transient { get; } = transient;
}

// How many times, how long between, and how long any one of them may take.
//
// The figures are settled at 2.0 and stated in section 17's limits table, and
// the test that reads that row against this record is what keeps the two from
// drifting. Written as a record rather than as constants so a test can hand in
// a policy of its own without the production one moving.
// see: A feed is tried three times with a doubling backoff, and the night has a deadline it cannot move
public sealed record RetryPolicy(int Attempts, TimeSpan FirstWait, TimeSpan Timeout, TimeSpan Deadline)
{
    // The wall clock section 17 states for a night at index size, and the
    // multiple the deadline follows it by.
    //
    // Proposed, and it stays proposed until five scheduled nights over the whole
    // index have run and the run page's operational header is read for them. One
    // night run by hand is an observation and a limit needs a distribution.
    // owes: The nightly wall clock at index size, measured from nights that ran on the schedule
    //
    // The deadline is derived from it rather than stated beside it, which is
    // what "the deadline follows at three times the limit, in the document and
    // in the retry policy together" has to mean if it is to survive the limit
    // moving. Written as two numbers the relationship was a coincidence held by
    // a comment, and the comment was the only thing that would have noticed.
    public static TimeSpan WallClock { get; } = TimeSpan.FromMinutes(5);

    public const int DeadlineMultiple = 3;

    // Three attempts, waiting two seconds and then four, each bounded by thirty
    // seconds, inside a night bounded by three times the wall clock.
    //
    // Thirty because the bulk file for a whole exchange is the largest thing the
    // night fetches and arrives in about four seconds at six and a half
    // megabytes: far outside a healthy fetch and far inside the night. Three
    // times the wall clock because the deadline stops a night that has hung
    // rather than one that is merely slow.
    public static RetryPolicy Standard { get; } = new(
        Attempts: 3,
        FirstWait: TimeSpan.FromSeconds(2),
        Timeout: TimeSpan.FromSeconds(30),
        Deadline: WallClock * DeadlineMultiple);

    // The wait before attempt n, doubling. Attempt 1 waits for nothing because
    // nothing has failed yet.
    public TimeSpan WaitBefore(int attempt) =>
        attempt <= 1 ? TimeSpan.Zero : FirstWait * Math.Pow(2, attempt - 2);

    // Which answers are worth asking again.
    //
    // A rejected rate and the provider's own fault statuses are transient. A
    // request timeout at the gateway is too. A rejected key, a forbidden route
    // and a route that does not exist are not: they are the same answer however
    // many times they are asked, and retrying turns one clear refusal into three
    // and a delay.
    public static bool Transient(int status) =>
        status is 408 or 429 || status >= 500;
}
