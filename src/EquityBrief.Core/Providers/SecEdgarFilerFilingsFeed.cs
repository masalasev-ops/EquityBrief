using System.Net;

namespace EquityBrief.Core.Providers;

// A filer's filing index, its older pages and a filing's document, each as the archive sends it.
public interface IFilerFilingsFeed
{
    int Requests { get; }

    Task<string> IndexAsync(string cik, CancellationToken cancellation = default);

    Task<string> PageAsync(string page, CancellationToken cancellation = default);

    Task<string> DocumentAsync(string cik, string accession, string document, CancellationToken cancellation = default);
}

// The archive's filing index of one filer and its documents, read for where a company's history ends. The index answers on the
// structured host and the documents on the other, each request under the archive's fair access with the contact the tool
// declares, and a pause between two keeps a read well under its ten a second.
// see: A stock's history ends by rule on its company's last regular session read from its Form 25 or the 8-K completing its merger
public sealed class SecEdgarFilerFilingsFeed(HttpClient client, TimeSpan pause) : IFilerFilingsFeed
{
    public static readonly TimeSpan Pause = TimeSpan.FromMilliseconds(150);

    public static SecEdgarFilerFilingsFeed Live(ArchiveAgent agent)
    {
        var client = new HttpClient(new HttpClientHandler { AutomaticDecompression = DecompressionMethods.All }) { Timeout = TimeSpan.FromSeconds(60) };

        client.DefaultRequestHeaders.Add("User-Agent", agent.Declared);

        return new(client, Pause);
    }

    public int Requests { get; private set; }

    static string Padded(string cik) => cik.TrimStart('0').PadLeft(10, '0');

    public Task<string> IndexAsync(string cik, CancellationToken cancellation = default) =>
        GetAsync($"https://{SecEdgarArchive.DataHost}/submissions/CIK{Padded(cik)}.json", cancellation);

    public Task<string> PageAsync(string page, CancellationToken cancellation = default) =>
        GetAsync($"https://{SecEdgarArchive.DataHost}/submissions/{page}", cancellation);

    public Task<string> DocumentAsync(string cik, string accession, string document, CancellationToken cancellation = default) =>
        GetAsync($"https://{SecEdgarArchive.DocumentHost}/Archives/edgar/data/{Padded(cik).TrimStart('0')}/{accession.Replace("-", string.Empty, StringComparison.Ordinal)}/{document}", cancellation);

    async Task<string> GetAsync(string address, CancellationToken cancellation)
    {
        Requests++;
        await Task.Delay(pause, cancellation).ConfigureAwait(false);

        using var response = await client.GetAsync(new Uri(address), cancellation).ConfigureAwait(false);

        response.EnsureSuccessStatusCode();

        return await response.Content.ReadAsStringAsync(cancellation).ConfigureAwait(false);
    }
}
