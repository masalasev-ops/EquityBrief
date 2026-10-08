using System.Net.Http;
using System.Text;

namespace EquityBrief.Core.Providers;

// OpenFIGI's mapping of identifiers to tickers, over the network: one request of up to ten identifiers, free and
// keyless at twenty-five requests a minute, read by `OpenFigiMappings`. Asked by the history pull alone for the
// holdings the provider's symbol lists carry under no code, never on a night, and it names no key. Its agent names
// the tool and its version and no contact, since the contact is the archive's to ask for and OpenFIGI asks for none.
// see: A holding the symbol lists carry under no code is mapped to the tickers it traded under through OpenFIGI, each held to the checks a name's code is
public sealed class OpenFigiMappingFeed(
    HttpClient client,
    ProviderRequest? request = null) : IOpenFigiMappingFeed
{
    readonly ProviderRequest request = request ?? new ProviderRequest(RetryPolicy.Standard);

    public const string Address = "https://api.openfigi.com/v3/mapping";

    public int Requests { get; private set; }

    public static OpenFigiMappingFeed Live(ProviderRequest? request = null)
    {
        var policy = request?.Policy ?? RetryPolicy.Standard;
        var client = new HttpClient { Timeout = policy.Timeout + policy.Timeout };

        client.DefaultRequestHeaders.Add("User-Agent", $"{ArchiveAgent.Tool}/{ArchiveAgent.Version}");

        return new(client, request);
    }

    public async Task<string> MapAsync(IReadOnlyList<string> isins, CancellationToken cancellation = default)
    {
        Requests++;

        var body = OpenFigiMappings.Request(isins);

        return await request
            .SendAsync(token => SendAsync(body, token), cancellation)
            .ConfigureAwait(false);
    }

    async Task<string> SendAsync(string body, CancellationToken cancellation)
    {
        try
        {
            using var content = new StringContent(body, Encoding.UTF8, "application/json");
            using var response = await client.PostAsync(new Uri(Address), content, cancellation).ConfigureAwait(false);

            return response.IsSuccessStatusCode
                ? await response.Content.ReadAsStringAsync(cancellation).ConfigureAwait(false)
                : throw new ProviderRefusal($"OpenFIGI answered {(int)response.StatusCode} to a mapping request.", RetryPolicy.Transient((int)response.StatusCode));
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception failure) when (failure is not ProviderRefusal)
        {
            throw new ProviderRefusal($"OpenFIGI could not be reached. {failure.GetType().Name}: {failure.Message}", transient: true);
        }
    }
}
