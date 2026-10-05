namespace EquityBrief.Core.Providers;

// A provider said no, and whether saying it again would help.
//
// The distinction is the whole of the retry policy. A refused connection and a
// rejected rate are worth asking again; a rejected key is wrong three times and
// the retry only delays the message that says so.
// see: A feed is tried three times with a doubling backoff, and the night has a two-hour deadline it cannot move
//
// `Unusable` marks a model's answer that arrived and could not be stored, empty or cut short,
// which a caller may ask for once more where it would not ask again after a refusal.
public sealed class ProviderRefusal(string message, bool transient, bool unusable = false) : Exception(message)
{
    public bool Transient { get; } = transient;

    public bool Unusable { get; } = unusable;
}

// How many times, how long between, and how long any one of them may take.
//
// The figures are settled at 2.0 and stated in section 17's limits table, and
// the test that reads that row against this record is what keeps the two from
// drifting. Written as a record rather than as constants so a test can hand in
// a policy of its own without the production one moving.
// see: A feed is tried three times with a doubling backoff, and the night has a two-hour deadline it cannot move
public sealed record RetryPolicy(int Attempts, TimeSpan FirstWait, TimeSpan Timeout, TimeSpan Deadline)
{
    // The wall clock section 17 states for a night at universe size, and the
    // multiple the deadline follows it by.
    //
    // Ruled by the operator on 2026-10-04 at forty minutes for a night over the
    // S&P 500's, 400's and 600's members, from the last four scheduled nights
    // over the 500, which reached the close in 580 to 701 seconds and scale to
    // about 29 to 35 minutes over three times the names, so the deadline is two
    // hours. A night runs on the operator's own machine, where a slow night costs
    // nobody anything, and the deadline stops a night that has hung rather than
    // one the store's disk has slowed.
    //
    // The deadline is derived from it rather than stated beside it, which is
    // what "the deadline follows at three times the limit, in the document and
    // in the retry policy together" has to mean if it is to survive the limit
    // moving. Written as two numbers the relationship was a coincidence held by
    // a comment, and the comment was the only thing that would have noticed.
    public static TimeSpan WallClock { get; } = TimeSpan.FromMinutes(40);

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
