using System.Globalization;
using System.Net.Http;
using System.Text;
using System.Text.Json;

namespace EquityBrief.Core.Providers;

// FINRA's consolidated short interest over the network: its query interface asked with a JSON body, keyless and free,
// at FINRA's own address rather than the provider's, so it counts against no allowance. One request reads one symbol's
// settlements over a span, or one settlement's records for up to `ShortInterests.SymbolsARequest` symbols. An answer
// holding FINRA's most records an answer is refused, since it may hold fewer than the request matched. The parser is
// `ShortInterests.Parse`, shared.
// see: FINRA's short interest is read once a settlement after the labeller, and its history once by hand
public sealed class FinraShortInterestFeed(HttpClient client, ProviderRequest? request = null) : IShortInterestFeed
{
    public const string BaseAddress = "https://api.finra.org/";

    public const string Endpoint = "data/group/otcMarket/name/consolidatedShortInterest";

    // The fields a record is asked for, the ones the store keeps.
    static readonly string[] Fields = ["symbolCode", "settlementDate", "currentShortPositionQuantity", "averageDailyVolumeQuantity", "daysToCoverQuantity", "revisionFlag"];

    readonly ProviderRequest request = request ?? new ProviderRequest(RetryPolicy.Standard);

    public int Requests { get; private set; }

    public static FinraShortInterestFeed Live(ProviderRequest? request = null)
    {
        var policy = request?.Policy ?? RetryPolicy.Standard;

        return new(
            new HttpClient
            {
                BaseAddress = new Uri(BaseAddress),
                Timeout = policy.Timeout + policy.Timeout,
            },
            request);
    }

    public async Task<IReadOnlyList<DateOnly>> SettlementsAsync(string symbol, DateOnly from, DateOnly through, CancellationToken cancellation = default)
    {
        var body = new Dictionary<string, object>
        {
            ["limit"] = ShortInterests.RecordsAnAnswer,
            ["fields"] = Fields,
            ["compareFilters"] = new[] { new Dictionary<string, string> { ["compareType"] = "EQUAL", ["fieldName"] = "symbolCode", ["fieldValue"] = symbol } },
            ["dateRangeFilters"] = new[] { new Dictionary<string, string> { ["fieldName"] = "settlementDate", ["startDate"] = Day(from), ["endDate"] = Day(through) } },
        };

        return ShortInterests.SettlementsIn(await AskAsync(body, $"{symbol}'s settlements", cancellation).ConfigureAwait(false));
    }

    public async Task<IReadOnlyList<ShortInterestReport>> SettlementAsync(DateOnly settlement, IReadOnlyList<string> symbols, CancellationToken cancellation = default)
    {
        if (symbols.Count == 0)
        {
            return [];
        }

        if (symbols.Count > ShortInterests.SymbolsARequest)
        {
            throw new ArgumentException(FormattableString.Invariant($"A request asks for at most {ShortInterests.SymbolsARequest} symbols, and {symbols.Count} were given."), nameof(symbols));
        }

        var body = new Dictionary<string, object>
        {
            ["limit"] = ShortInterests.RecordsAnAnswer,
            ["fields"] = Fields,
            ["domainFilters"] = new[] { new Dictionary<string, object> { ["fieldName"] = "symbolCode", ["values"] = symbols } },
            ["compareFilters"] = new[] { new Dictionary<string, string> { ["compareType"] = "EQUAL", ["fieldName"] = "settlementDate", ["fieldValue"] = Day(settlement) } },
        };

        return ShortInterests.Parse(await AskAsync(body, $"the settlement of {Day(settlement)}", cancellation).ConfigureAwait(false));
    }

    async Task<string> AskAsync(Dictionary<string, object> body, string named, CancellationToken cancellation)
    {
        Requests++;

        var json = JsonSerializer.Serialize(body);
        var answer = await request
            .SendAsync(token => FetchAsync(json, named, token), cancellation)
            .ConfigureAwait(false);

        using (var document = JsonDocument.Parse(answer))
        {
            if (document.RootElement.ValueKind == JsonValueKind.Array && document.RootElement.GetArrayLength() >= ShortInterests.RecordsAnAnswer)
            {
                throw new ProviderRefusal(
                    FormattableString.Invariant($"FINRA's answer for {named} held {document.RootElement.GetArrayLength()} records, its most an answer, so it may hold fewer than were asked for and is not read."),
                    transient: false);
            }
        }

        return answer;
    }

    async Task<string> FetchAsync(string json, string named, CancellationToken cancellation)
    {
        try
        {
            using var content = new StringContent(json, Encoding.UTF8, "application/json");
            using var message = new HttpRequestMessage(HttpMethod.Post, Endpoint) { Content = content };

            message.Headers.TryAddWithoutValidation("Accept", "application/json");

            using var response = await client.SendAsync(message, cancellation).ConfigureAwait(false);

            // FINRA answers a query matching no record with no content at all.
            if (response.StatusCode == System.Net.HttpStatusCode.NoContent)
            {
                return "[]";
            }

            return response.IsSuccessStatusCode
                ? await response.Content.ReadAsStringAsync(cancellation).ConfigureAwait(false)
                : throw new ProviderRefusal(
                    FormattableString.Invariant($"FINRA's short interest answered {(int)response.StatusCode} for {named}."),
                    transient: RetryPolicy.Transient((int)response.StatusCode));
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (HttpRequestException failure)
        {
            throw new ProviderRefusal($"FINRA's short interest could not be reached for {named}: " + failure.Message, transient: true);
        }
    }

    static string Day(DateOnly day) => day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
}
