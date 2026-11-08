using System.Net;

namespace EquityBrief.Core.Providers;

// A filer's facts as the archive sends them, the whole answer.
public interface IFilerFactsFeed
{
    int Requests { get; }

    Task<string> FactsAsync(string cik, CancellationToken cancellation = default);
}

// The archive's facts of one filer, asked on the structured host under its fair access with the contact the tool declares,
// a pause between two keeping a read well under its ten a second.
// see: The long history's facts are the SEC's figures before the standing table's first, kept as first filed apart from it
public sealed class SecEdgarFilerFactsFeed(HttpClient client, TimeSpan pause) : IFilerFactsFeed
{
    public static readonly TimeSpan Pause = SecEdgarFilerFilingsFeed.Pause;

    public static SecEdgarFilerFactsFeed Live(ArchiveAgent agent)
    {
        var client = new HttpClient(new HttpClientHandler { AutomaticDecompression = DecompressionMethods.All }) { Timeout = TimeSpan.FromSeconds(120) };

        client.DefaultRequestHeaders.Add("User-Agent", agent.Declared);

        return new(client, pause: Pause);
    }

    public int Requests { get; private set; }

    public async Task<string> FactsAsync(string cik, CancellationToken cancellation = default)
    {
        Requests++;
        await Task.Delay(pause, cancellation).ConfigureAwait(false);

        var padded = cik.TrimStart('0').PadLeft(10, '0');

        using var response = await client.GetAsync(new Uri($"https://{SecEdgarArchive.DataHost}/api/xbrl/companyfacts/CIK{padded}.json"), cancellation).ConfigureAwait(false);

        response.EnsureSuccessStatusCode();

        return await response.Content.ReadAsStringAsync(cancellation).ConfigureAwait(false);
    }
}
