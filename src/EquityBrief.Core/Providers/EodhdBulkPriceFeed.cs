using System.Net.Http;

namespace EquityBrief.Core.Providers;

// The bulk price feed, over the network.
//
// One request for the whole exchange, whatever the universe is, which is the
// shape the entire nightly path is built around
// (see: The nightly run is arithmetic only). This is the first feed in the tree
// that reaches a provider, and it is this one first because it is the nightly
// path's own and the one the zero-per-name rule was written for.
//
// It holds an HttpClient, which every other shipped file is still forbidden to,
// and it is named in nightly-cost's exemption list for that reason
// (see: The outward-request scan names the files that may hold a client rather than dropping the patterns).
//
// The parser is not here. `RecordedBulkPriceFeed.Parse` reads the payload, and
// it was written against a response captured at 1.4 rather than the other way
// round. Sharing it is what makes the double and the live feed the same reader:
// a second parser would be a second opinion about what the provider sends, and
// the fixture would only ever exercise one of them.
//
// The retry is not here either. This file converts what the transport did into
// a refusal that says whether asking again would help, and `ProviderRequest`
// decides how many times. One request is what the limits row counts, however
// many attempts it took.
public sealed class EodhdBulkPriceFeed(
    HttpClient client,
    ProviderCredentials credentials,
    ProviderRequest? request = null) : IBulkPriceFeed
{
    // The endpoint, relative to the client's base address. The captured
    // response's manifest names the same one, which is how the double and this
    // are known to be reading the same thing rather than assumed to be.
    public const string Endpoint = "eod-bulk-last-day";

    public const string BaseAddressKey = "EquityBrief:Providers:Eodhd:BaseAddress";
    public const string DefaultBaseAddress = "https://eodhd.com/api/";

    readonly ProviderRequest request = request ?? new ProviderRequest(RetryPolicy.Standard);

    public int Requests { get; private set; }

    readonly List<string> notSessions = [];

    // Accumulated across calls, like the request count, because the figure
    // the operator reads is what the night did rather than what its last
    // request did.
    public IReadOnlyList<string> NotSessions => notSessions;

    // What the retry actually did. One request may have cost three attempts and
    // two waits, and the difference is a night worth reading about.
    public int Attempts => request.Attempts;

    // The live feed, with its own client.
    //
    // The client is built here rather than handed in, so this stays the only
    // shipped file that names the type. The exemption nightly-cost carries is
    // one file per feed and a file earns it by being a feed, so a caller that
    // constructed the client would need an exemption without being one, and the
    // carve-out would have to widen to cover composition code. That is the way a
    // named exemption turns back into a deleted pattern.
    //
    // The client's own timeout is set past the policy's on purpose. The bound
    // that matters is the linked token `ProviderRequest` cancels, because that
    // one composes with the night's deadline; a client timeout firing first
    // would produce the same exception type from a source nothing can reason
    // about, and the retry could not tell it from the deadline.
    public static EodhdBulkPriceFeed Live(
        string baseAddress,
        ProviderCredentials credentials,
        ProviderRequest? request = null)
    {
        var policy = request?.Policy ?? RetryPolicy.Standard;

        return new(
            new HttpClient
            {
                BaseAddress = new Uri(WithTrailingSlash(baseAddress)),
                Timeout = policy.Timeout + policy.Timeout,
            },
            credentials,
            request);
    }

    // A base address whose last segment survives.
    //
    // Uri resolution against a base that does not end in a slash drops the last
    // segment, so "https://eodhd.com/api" would send the request to
    // "https://eodhd.com/eod-bulk-last-day/US" and the provider would answer 404
    // rather than anything that named the cause.
    static string WithTrailingSlash(string baseAddress) =>
        baseAddress.EndsWith('/') ? baseAddress : baseAddress + "/";

    public async Task<IReadOnlyList<BulkBar>> RowsAsync(string exchange, CancellationToken cancellation = default)
    {
        // Counted once per call and not once per attempt. The limit this figure
        // is read against is "one request for the night whatever the universe
        // is", and three attempts at one request is still one request: what
        // grows with retries is time, and the deadline is what bounds that.
        Requests++;

        var body = await request.SendAsync(token => FetchAsync(exchange, token), cancellation)
            .ConfigureAwait(false);

        return RecordedBulkPriceFeed.Parse(body, exchange, notSessions);
    }

    async Task<string> FetchAsync(string exchange, CancellationToken cancellation)
    {
        try
        {
            // The key travels in the query string on this provider, so this
            // string is the one thing in the process that must not be written
            // down. It is built here, used once, and never held.
            using var response = await client
                .GetAsync($"{Endpoint}/{exchange}?api_token={credentials.ApiKey}&fmt=json", cancellation)
                .ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                var status = (int)response.StatusCode;

                throw new ProviderRefusal(
                    $"The bulk price feed answered {status} for {Endpoint}/{exchange}. " +
                    "A night cannot be computed from a refusal, and storing nothing would read as an " +
                    "exchange that did not trade.",
                    RetryPolicy.Transient(status));
            }

            return await response.Content.ReadAsStringAsync(cancellation).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // A per-request timeout or the night's deadline. Which of the two is
            // a question about the tokens, and `ProviderRequest` holds both, so
            // it is rethrown as it is rather than guessed at here.
            throw;
        }
        catch (Exception failure) when (failure is not ProviderRefusal)
        {
            // The transport did not answer. Transient by construction: a socket
            // that refused, a name that would not resolve or a connection that
            // dropped are all worth asking again, and a provider that means no
            // says so with a status.
            throw new ProviderRefusal(Failed(exchange, failure), transient: true);
        }
    }

    // What a transport failure says, with the key taken out of it.
    //
    // The inner exception is deliberately not attached. Its message is included
    // here after redaction, and its type is named, but the object itself is
    // dropped: a logger that expanded ToString would print an inner message this
    // code never scrubbed, and the rule that no request URL reaches a log is
    // absolute rather than best effort. The stack trace of the transport is what
    // that costs, and it is a smaller loss than a key in the run log.
    string Failed(string exchange, Exception failure) =>
        $"The bulk price feed could not be reached for {Endpoint}/{exchange}. " +
        $"{failure.GetType().Name}: {credentials.Redact(failure.Message)}";
}
