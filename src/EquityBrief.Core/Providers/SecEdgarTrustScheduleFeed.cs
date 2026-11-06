using System.Net;

namespace EquityBrief.Core.Providers;

// The trust's filings of its funds' schedules before their first public N-PORT, each as the archive sends it: the archive's
// file of funds' tickers with their series, a series' list of its filings of one form a page at a time, the trust's filing
// index and its older pages, each filing's header naming the series it carries, and a filing's document.
public interface ITrustScheduleFeed
{
    int Requests { get; }

    Task<string> FundTickersAsync(CancellationToken cancellation = default);

    Task<string> SeriesFilingsAsync(string series, string form, int start, CancellationToken cancellation = default);

    Task<string> IndexAsync(string cik, CancellationToken cancellation = default);

    Task<string> PageAsync(string page, CancellationToken cancellation = default);

    Task<string> HeaderAsync(string cik, string accession, CancellationToken cancellation = default);

    Task<string> FilingIndexAsync(string cik, string accession, CancellationToken cancellation = default);

    Task<string> DocumentAsync(string cik, string accession, string document, CancellationToken cancellation = default);

    // A filing's document as the archive sent its bytes, an older page's own encoding kept.
    Task<byte[]> DocumentBytesAsync(string cik, string accession, string document, CancellationToken cancellation = default);
}

// The archive's lists, headers and documents of the trust's older schedules, read once by hand for the long history. Each
// request goes under the archive's fair access with the contact the tool declares, a pause between two keeping a read well
// under its ten a second.
// see: The funds' older schedules are read from the trust's N-Q and N-CSR filings at each quarter end from 2004-12-31 to 2018-09-30
public sealed class SecEdgarTrustScheduleFeed(HttpClient client, TimeSpan pause) : ITrustScheduleFeed
{
    public static readonly TimeSpan Pause = TimeSpan.FromMilliseconds(250);

    public const string FundTickersPath = "files/company_tickers_mf.json";

    // How many filings the archive's company browser lists a page.
    public const int ListPage = 100;

    public static SecEdgarTrustScheduleFeed Live(ArchiveAgent agent)
    {
        var client = new HttpClient(new HttpClientHandler { AutomaticDecompression = DecompressionMethods.All }) { Timeout = TimeSpan.FromSeconds(120) };

        client.DefaultRequestHeaders.Add("User-Agent", agent.Declared);

        return new(client, Pause);
    }

    public int Requests { get; private set; }

    static string Padded(string cik) => cik.TrimStart('0').PadLeft(10, '0');

    static string Folder(string cik, string accession) =>
        $"Archives/edgar/data/{cik.TrimStart('0')}/{accession.Replace("-", string.Empty, StringComparison.Ordinal)}";

    public Task<string> FundTickersAsync(CancellationToken cancellation = default) =>
        GetAsync($"https://{SecEdgarArchive.DocumentHost}/{FundTickersPath}", cancellation);

    public Task<string> SeriesFilingsAsync(string series, string form, int start, CancellationToken cancellation = default) =>
        GetAsync($"https://{SecEdgarArchive.DocumentHost}/cgi-bin/browse-edgar?action=getcompany&CIK={Uri.EscapeDataString(series)}&type={Uri.EscapeDataString(form)}&dateb=&owner=include&start={start}&count={ListPage}&output=atom", cancellation);

    public Task<string> IndexAsync(string cik, CancellationToken cancellation = default) =>
        GetAsync($"https://{SecEdgarArchive.DataHost}/submissions/CIK{Padded(cik)}.json", cancellation);

    public Task<string> PageAsync(string page, CancellationToken cancellation = default) =>
        GetAsync($"https://{SecEdgarArchive.DataHost}/submissions/{page}", cancellation);

    // A filing's header: its index's headers page, or where the archive keeps none its header file; none where it keeps neither.
    public async Task<string> HeaderAsync(string cik, string accession, CancellationToken cancellation = default)
    {
        foreach (var file in new[] { accession + "-index-headers.html", accession + ".hdr.sgml" })
        {
            try
            {
                return await GetAsync($"https://{SecEdgarArchive.DocumentHost}/{Folder(cik, accession)}/{file}", cancellation).ConfigureAwait(false);
            }
            catch (HttpRequestException missing) when (missing.StatusCode == HttpStatusCode.NotFound)
            {
            }
        }

        return string.Empty;
    }

    // A filing's folder listing, each file it holds with its size.
    public Task<string> FilingIndexAsync(string cik, string accession, CancellationToken cancellation = default) =>
        GetAsync($"https://{SecEdgarArchive.DocumentHost}/{Folder(cik, accession)}/index.json", cancellation);

    public Task<string> DocumentAsync(string cik, string accession, string document, CancellationToken cancellation = default) =>
        GetAsync($"https://{SecEdgarArchive.DocumentHost}/{Folder(cik, accession)}/{document}", cancellation);

    public Task<byte[]> DocumentBytesAsync(string cik, string accession, string document, CancellationToken cancellation = default) =>
        SendAsync($"https://{SecEdgarArchive.DocumentHost}/{Folder(cik, accession)}/{document}", content => content.ReadAsByteArrayAsync(cancellation), cancellation);

    Task<string> GetAsync(string address, CancellationToken cancellation) =>
        SendAsync(address, content => content.ReadAsStringAsync(cancellation), cancellation);

    // How many times a request the archive answers as busy is asked, the wait doubling from the first.
    public const int Tries = 5;

    public static readonly TimeSpan Busy = TimeSpan.FromSeconds(2);

    async Task<T> SendAsync<T>(string address, Func<HttpContent, Task<T>> read, CancellationToken cancellation)
    {
        Requests++;

        for (var tried = 1; ; tried++)
        {
            await Task.Delay(pause, cancellation).ConfigureAwait(false);

            using var response = await client.GetAsync(new Uri(address), cancellation).ConfigureAwait(false);

            if (tried < Tries && response.StatusCode is HttpStatusCode.ServiceUnavailable or HttpStatusCode.TooManyRequests)
            {
                await Task.Delay(Busy * (1 << (tried - 1)), cancellation).ConfigureAwait(false);

                continue;
            }

            response.EnsureSuccessStatusCode();

            return await read(response.Content).ConfigureAwait(false);
        }
    }
}
