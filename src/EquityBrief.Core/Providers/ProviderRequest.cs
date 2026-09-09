namespace EquityBrief.Core.Providers;

// The retry, the backoff and the per-request timeout, in one place.
//
// Shared by every live feed rather than written into each, because a retry
// written four times is four policies, and three of them go stale the day the
// fourth is corrected.
//
// It knows nothing about HTTP. A feed converts what its transport did into a
// `ProviderRefusal` carrying whether asking again would help, and this decides
// only how many times and how long between. That seam is not tidiness: naming a
// transport exception here would need this file in nightly-cost's exemption
// list, and a file earns that by being a feed
// (see: The outward-request scan names the files that may hold a client rather than dropping the patterns).
//
// The wait is injected so a test can prove the schedule without spending it. A
// backoff asserted by waiting six seconds is a backoff nobody runs twice.
// see: A feed is tried three times with a doubling backoff, and the night has a deadline it cannot move
public sealed class ProviderRequest(RetryPolicy policy, Func<TimeSpan, CancellationToken, Task>? wait = null)
{
    readonly Func<TimeSpan, CancellationToken, Task> wait = wait ?? Task.Delay;
    readonly List<TimeSpan> waited = [];

    public RetryPolicy Policy => policy;

    // What this request actually did, read off it rather than asserted by the
    // caller. Two attempts and one wait is a different night from one attempt,
    // and the run log is where that difference is read.
    public int Attempts { get; private set; }

    public IReadOnlyList<TimeSpan> Waited => waited;

    public async Task<T> SendAsync<T>(Func<CancellationToken, Task<T>> send, CancellationToken cancellation)
    {
        for (var attempt = 1; ; attempt++)
        {
            Attempts++;

            // Linked rather than separate, so the per-request timeout and the
            // night's deadline compose: whichever comes first cancels the
            // attempt, and the two are told apart afterwards by asking which
            // token was the one that fired.
            using var perRequest = CancellationTokenSource.CreateLinkedTokenSource(cancellation);

            perRequest.CancelAfter(policy.Timeout);

            try
            {
                return await send(perRequest.Token).ConfigureAwait(false);
            }
            catch (Exception failure) when (Again(failure, cancellation, attempt))
            {
                var pause = policy.WaitBefore(attempt + 1);

                waited.Add(pause);

                await wait(pause, cancellation).ConfigureAwait(false);
            }
        }
    }

    bool Again(Exception failure, CancellationToken cancellation, int attempt)
    {
        // The night's deadline is not a transient failure and is never retried.
        // It is the caller's decision that there is no time left, and a retry
        // would be this class overruling it. Checked first, because a deadline
        // arrives as the same exception type a per-request timeout does and the
        // only thing that tells them apart is which token was cancelled.
        if (cancellation.IsCancellationRequested)
        {
            return false;
        }

        if (attempt >= policy.Attempts)
        {
            return false;
        }

        // A per-request timeout. The provider did not answer inside the bound,
        // which is exactly the case a second attempt is for.
        return failure is OperationCanceledException
            || (failure is ProviderRefusal refusal && refusal.Transient);
    }
}
