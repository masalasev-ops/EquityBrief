using EquityBrief.Core.Providers;
using EquityBrief.Tests.Checks;
using EquityBrief.Tests.Harness;

namespace EquityBrief.Tests.Providers;

// The retry, the backoff and the two bounds, asserted without spending them.
//
// The wait is injected, so the schedule is read off what the request asked to
// wait for rather than off a clock. A backoff proved by waiting six seconds is
// a backoff nobody runs twice, and a test nobody runs twice is one that gets a
// Skip attribute the first time it is inconvenient.
// see: A feed is tried three times with a doubling backoff, and the night has a deadline it cannot move
public class ProviderRequestTests
{
    static readonly RetryPolicy Fast = RetryPolicy.Standard with { Timeout = TimeSpan.FromMilliseconds(50) };

    static ProviderRequest Request(RetryPolicy? policy = null) =>
        new(policy ?? RetryPolicy.Standard, (_, _) => Task.CompletedTask);

    [Fact]
    public async Task ATransientRefusalIsTriedToTheStatedPolicyAndNoFurther()
    {
        var request = Request();
        var tried = 0;

        var failure = await Assert.ThrowsAsync<ProviderRefusal>(() => request.SendAsync<string>(
            _ =>
            {
                tried++;

                throw new ProviderRefusal("the provider refused the rate", transient: true);
            },
            CancellationToken.None));

        // Three, which is what the policy says and what section 17's row states.
        // Read off the policy rather than written here, so the two cannot drift.
        Assert.Equal(RetryPolicy.Standard.Attempts, tried);
        Assert.Equal(RetryPolicy.Standard.Attempts, request.Attempts);
        Assert.Contains("refused the rate", failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task APersistentRefusalIsNotTriedAgain()
    {
        // The counter-test, and the one that matters. A rejected key is wrong
        // three times, and retrying turns one clear refusal into three and a
        // delay before the operator is told anything.
        var request = Request();
        var tried = 0;

        await Assert.ThrowsAsync<ProviderRefusal>(() => request.SendAsync<string>(
            _ =>
            {
                tried++;

                throw new ProviderRefusal("the provider rejected the key", transient: false);
            },
            CancellationToken.None));

        Assert.Equal(1, tried);
        Assert.Empty(request.Waited);
    }

    [Fact]
    public async Task ASecondAttemptThatSucceedsEndsTheRequest()
    {
        var request = Request();
        var tried = 0;

        var answer = await request.SendAsync(
            _ =>
            {
                tried++;

                return tried == 1
                    ? throw new ProviderRefusal("the socket refused", transient: true)
                    : Task.FromResult("the payload");
            },
            CancellationToken.None);

        Assert.Equal("the payload", answer);
        Assert.Equal(2, tried);
    }

    [Fact]
    public async Task TheWaitDoublesAndIsAskedForRatherThanTaken()
    {
        var asked = new List<TimeSpan>();
        var request = new ProviderRequest(RetryPolicy.Standard, (pause, _) =>
        {
            asked.Add(pause);

            return Task.CompletedTask;
        });

        await Assert.ThrowsAsync<ProviderRefusal>(() => request.SendAsync<string>(
            _ => throw new ProviderRefusal("no", transient: true),
            CancellationToken.None));

        // Two waits for three attempts, doubling, and nothing before the first
        // attempt because nothing has failed yet.
        Assert.Equal([TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(4)], asked);
        Assert.Equal(asked, request.Waited);
        Assert.Equal(TimeSpan.Zero, RetryPolicy.Standard.WaitBefore(1));
    }

    [Fact]
    public async Task AnAttemptThatPassesItsTimeoutIsTriedAgain()
    {
        // The per-request bound, exercised rather than described. Each attempt
        // is given a token that cancels after the policy's timeout, and an
        // attempt that never returns is the case the bound exists for.
        var request = new ProviderRequest(Fast, (_, _) => Task.CompletedTask);
        var tried = 0;

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => request.SendAsync<string>(
            async token =>
            {
                tried++;

                await Task.Delay(TimeSpan.FromSeconds(30), token);

                return "never";
            },
            CancellationToken.None));

        Assert.Equal(Fast.Attempts, tried);
    }

    [Fact]
    public async Task TheNightsDeadlineIsNotATransientFailureAndIsNeverRetried()
    {
        // The two arrive as the same exception type and only the token tells
        // them apart. A deadline retried is this class overruling the caller's
        // decision that there is no time left, which is how a night with a
        // fifteen-minute bound runs for forty-five.
        using var night = new CancellationTokenSource();
        var request = Request();
        var tried = 0;

        await night.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => request.SendAsync<string>(
            token =>
            {
                tried++;
                token.ThrowIfCancellationRequested();

                return Task.FromResult("never");
            },
            night.Token));

        Assert.Equal(1, tried);
        Assert.Empty(request.Waited);
    }

    [Fact]
    public void TheLimitsRowStatesTheFiguresTheCodeUses()
    {
        // A limit stated in a document and again in code is two places holding
        // one fact. The row is read rather than repeated, so a figure changed in
        // either place fails here.
        var row = Corpus.Read("docs/ARCHITECTURE.html");
        var at = row.IndexOf("Per-request timeout and the night's deadline", StringComparison.Ordinal);

        Assert.True(at >= 0, "Section 17 no longer carries the timeout and deadline row.");

        var cell = row[at..row.IndexOf("</tr>", at, StringComparison.Ordinal)];
        var policy = RetryPolicy.Standard;

        Assert.Contains($"at most {policy.Attempts} attempts", cell, StringComparison.Ordinal);
        Assert.Contains($"{policy.FirstWait.TotalSeconds:0} seconds and then {policy.FirstWait.TotalSeconds * 2:0}", cell, StringComparison.Ordinal);
        Assert.Contains($"bounded by {policy.Timeout.TotalSeconds:0} seconds", cell, StringComparison.Ordinal);
        Assert.Contains($"bounded by {policy.Deadline.TotalMinutes:0} minutes", cell, StringComparison.Ordinal);
    }

    [Fact]
    public void OnlyTheAnswersWorthAskingAgainAreTransient()
    {
        foreach (var status in new[] { 408, 429, 500, 502, 503, 504 })
        {
            Assert.True(RetryPolicy.Transient(status), $"{status} should be worth asking again.");
        }

        // The counter-test. A rejected key, a forbidden route and a route that
        // does not exist are the same answer however many times they are asked.
        foreach (var status in new[] { 400, 401, 403, 404, 422 })
        {
            Assert.False(RetryPolicy.Transient(status), $"{status} should not be retried.");
        }
    }
}
