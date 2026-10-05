using System.Net;
using System.Net.Http;

namespace EquityBrief.Core.Providers;

// The funds' quarter-end holdings, over the network: a fund's list of its filings off the archive's company browser,
// asked under the fund's series, and each filing's holdings document under the trust both funds file as. Free and
// keyless, refused on the transport rule the archive keeps, so it carries the archive's agent as the filings feed does,
// and asked by the history pull alone, never on a night.
// see: The archive declares a contact in its user agent, and a blank one refuses at startup
// see: Membership as it stood is rebuilt from the funds' quarterly holdings filed with the SEC, matched by ISIN and then by name
public sealed class SecEdgarFundSnapshotFeed(
    HttpClient client,
    ArchiveAgent agent,
    ProviderRequest? request = null) : IFundSnapshotFeed
{
    readonly ProviderRequest request = request ?? new ProviderRequest(RetryPolicy.Standard);

    readonly ArchiveAgent agent = agent
        ?? throw new ArgumentNullException(nameof(agent), "The archive refuses a request that names no user agent, so this feed cannot be built without one.");

    public int Requests { get; private set; }

    public static SecEdgarFundSnapshotFeed Live(ArchiveAgent agent, ProviderRequest? request = null)
    {
        var policy = request?.Policy ?? RetryPolicy.Standard;

        var client = new HttpClient(new HttpClientHandler { AutomaticDecompression = DecompressionMethods.All })
        {
            Timeout = policy.Timeout + policy.Timeout,
        };

        client.DefaultRequestHeaders.Add("User-Agent", agent.Declared);

        return new(client, agent, request);
    }

    public async Task<IReadOnlyList<FundFiling>> FilingsAsync(string series, CancellationToken cancellation = default)
    {
        var address = new Uri($"https://www.sec.gov/cgi-bin/browse-edgar?action=getcompany&CIK={Uri.EscapeDataString(series)}&type={Uri.EscapeDataString(FundSnapshots.Form)}&dateb=&owner=include&count=100&output=atom");
        var body = await FetchAsync(address, $"{series}'s filings", cancellation).ConfigureAwait(false);

        return FundSnapshots.ParseFilings(body ?? throw new ProviderRefusal($"The archive holds no list of {series}'s filings.", transient: false), series);
    }

    public async Task<FundSnapshot> SnapshotAsync(string accession, CancellationToken cancellation = default)
    {
        var address = new Uri($"https://www.sec.gov/Archives/edgar/data/{FundSnapshots.Trust}/{accession.Replace("-", string.Empty, StringComparison.Ordinal)}/primary_doc.xml");
        var body = await FetchAsync(address, $"the filing {accession}", cancellation).ConfigureAwait(false);

        return FundSnapshots.ParseSnapshot(body ?? throw new ProviderRefusal($"The archive holds no holdings document for the filing {accession}.", transient: false), accession);
    }

    async Task<string?> FetchAsync(Uri address, string wanted, CancellationToken cancellation)
    {
        Requests++;

        return await request
            .SendAsync(token => SendAsync(address, wanted, token), cancellation)
            .ConfigureAwait(false);
    }

    async Task<string?> SendAsync(Uri address, string wanted, CancellationToken cancellation)
    {
        try
        {
            if (!client.DefaultRequestHeaders.Contains("User-Agent"))
            {
                throw new ProviderRefusal("The archive refuses a request that names no user agent, and this client carries none.", transient: false);
            }

            using var response = await client.GetAsync(address, cancellation).ConfigureAwait(false);

            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                return null;
            }

            return response.IsSuccessStatusCode
                ? await response.Content.ReadAsStringAsync(cancellation).ConfigureAwait(false)
                : throw new ProviderRefusal(
                    agent.Redact($"The archive refused {wanted} with status {(int)response.StatusCode}."),
                    transient: (int)response.StatusCode is 429 or >= 500);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception failure) when (failure is not ProviderRefusal)
        {
            throw new ProviderRefusal(agent.Redact($"The archive could not be reached for {wanted}: {failure.Message}"), transient: true);
        }
    }
}
