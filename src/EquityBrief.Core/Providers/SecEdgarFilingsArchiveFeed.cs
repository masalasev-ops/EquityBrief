using System.Net;
using System.Net.Http;

namespace EquityBrief.Core.Providers;

// One name's filings, over the network.
//
// The first provider here that is free and needs no key, and the first that
// refuses a request on a transport rule instead: the archive answers a request
// naming no user agent with a refusal, and its fair-access policy asks that the
// agent carry contact details. So the agent is required to construct this feed and
// a blank one refuses before a request is composed, which is the shape the
// credential path has had since 2.1.
// see: The archive declares a contact in its user agent, and a blank one refuses at startup
//
// Per name and on demand, never on the nightly path, for the reason the company
// financials feed is: the night's per-name request count is zero.
// see: The nightly run is arithmetic only
//
// Two hosts, so the address comes off each request rather than off a client. One
// client with no base address and absolute URIs, because a client per host would
// make one read's request count two numbers to add up and the fair-access policy
// is stated per caller rather than per host.
//
// The reader is `SecEdgarArchive`, shared with the recorded feed, and it was
// written against thirteen captured responses at 6.2 before this existed.
public sealed class SecEdgarFilingsArchiveFeed(
    HttpClient client,
    ArchiveAgent agent,
    ProviderRequest? request = null) : IFilingsArchiveFeed
{
    readonly ProviderRequest request = request ?? new ProviderRequest(RetryPolicy.Standard);

    readonly ArchiveAgent agent = agent
        ?? throw new ArgumentNullException(
            nameof(agent),
            "The archive refuses a request that names no user agent, so this feed cannot be built "
            + "without one. A feed that composed requests and left the header off would fail at the "
            + "archive and look like the archive being down.");

    // One read of one name, not one request. Six requests where every part is
    // served, and the figure the run log records is this one, because what an open
    // cost the operator is the read and not its parts.
    public int Requests { get; private set; }

    public int Attempts => this.request.Attempts;

    // How many documents this feed has asked the archive for. Counted apart from
    // the reads, because the archive's fair-access policy is stated in requests a
    // second and a read that made eight is a different thing from eight reads.
    public int Documents { get; private set; }

    public static SecEdgarFilingsArchiveFeed Live(ArchiveAgent agent, ProviderRequest? request = null)
    {
        var policy = request?.Policy ?? RetryPolicy.Standard;

        var client = new HttpClient(new HttpClientHandler { AutomaticDecompression = DecompressionMethods.All })
        {
            Timeout = policy.Timeout + policy.Timeout,
        };

        // Composed once, here, rather than added at each call. The archive reads
        // this header and nothing else about the caller.
        client.DefaultRequestHeaders.Add("User-Agent", agent.Declared);

        return new(client, agent, request);
    }

    public async Task<ArchiveFilings> FilingsAsync(
        string ticker,
        string cik,
        CancellationToken cancellation = default)
    {
        Requests++;

        return await SecEdgarArchive
            .ReadAsync(FetchAsync, ticker, cik, cancellation)
            .ConfigureAwait(false);
    }

    async Task<string?> FetchAsync(ArchiveRequest wanted, CancellationToken cancellation)
    {
        Documents++;

        return await request
            .SendAsync(token => SendAsync(wanted, token), cancellation)
            .ConfigureAwait(false);
    }

    async Task<string?> SendAsync(ArchiveRequest wanted, CancellationToken cancellation)
    {
        var address = new Uri($"https://{wanted.Host}/{wanted.Path}");

        try
        {
            // Asserted rather than assumed, because the whole of this provider's
            // transport rule is that the header is present. A client built by hand
            // rather than by `Live` is the way it could be absent, and a request
            // sent without it comes back refused for a reason nothing in the run
            // would name.
            if (!client.DefaultRequestHeaders.Contains("User-Agent"))
            {
                throw new ProviderRefusal(
                    "The archive refuses a request that names no user agent, and this client carries "
                    + "none. Refused here rather than sent, so the failure names the rule instead of "
                    + "arriving as the archive being unavailable.",
                    transient: false);
            }

            using var response = await client.GetAsync(address, cancellation).ConfigureAwait(false);

            // The archive saying a document is not there, which is a part not
            // carried and not a failure. A filing without rendered reports and a
            // company without filed facts are both ordinary.
            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                return null;
            }

            return response.IsSuccessStatusCode
                ? await response.Content.ReadAsStringAsync(cancellation).ConfigureAwait(false)
                : throw new ProviderRefusal(
                    agent.Redact($"The archive refused {wanted.Document} with status {(int)response.StatusCode}."),
                    transient: (int)response.StatusCode is 429 or >= 500);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception failure) when (failure is not ProviderRefusal)
        {
            // The contact taken out of anything about to be written down. It
            // travels in a header rather than in a query string, so it does not
            // reach a URL the way a provider key does, and a transport failure can
            // still quote the request it was making.
            throw new ProviderRefusal(
                agent.Redact($"The archive could not be reached for {wanted.Document}: {failure.Message}"),
                transient: true);
        }
    }
}
