using System.Net.Http;

namespace EquityBrief.Core.Providers;

// The earnings calendar, over the network.
//
// One request for the window, whatever the universe size. The endpoint filters
// by symbol and this does not use that: a request carrying five hundred symbols
// keeps the request count at one and makes the request itself grow with the
// index, which is the same defect a page count that grows with the index is.
// see: A feed that pages counts every page, and a page count that grows with the index is a per-name call
//
// The parser is `RecordedEarningsCalendarFeed.Parse`, shared, and it was written
// against a captured response at 4.3 before this existed.
public sealed class EodhdEarningsCalendarFeed(
    HttpClient client,
    ProviderCredentials credentials,
    ProviderRequest? request = null) : IEarningsCalendarFeed
{
    public const string Endpoint = "calendar/earnings";

    readonly ProviderRequest request = request ?? new ProviderRequest(RetryPolicy.Standard);

    public int Requests { get; private set; }

    public int Attempts => request.Attempts;

    public static EodhdEarningsCalendarFeed Live(
        string baseAddress,
        ProviderCredentials credentials,
        ProviderRequest? request = null)
    {
        var policy = request?.Policy ?? RetryPolicy.Standard;

        return new(
            new HttpClient
            {
                BaseAddress = new Uri(baseAddress.EndsWith('/') ? baseAddress : baseAddress + "/"),
                Timeout = policy.Timeout + policy.Timeout,
            },
            credentials,
            request);
    }

    public async Task<IReadOnlyList<CalendarEvent>> EventsAsync(
        DateOnly from,
        DateOnly to,
        CancellationToken cancellation = default)
    {
        Requests++;

        var body = await request
            .SendAsync(token => FetchAsync(from, to, token), cancellation)
            .ConfigureAwait(false);

        return RecordedEarningsCalendarFeed.Parse(body, from, to);
    }

    async Task<string> FetchAsync(DateOnly from, DateOnly to, CancellationToken cancellation)
    {
        try
        {
            using var response = await client
                .GetAsync(
                    EodhdQuery.WithKey(
                        $"{Endpoint}?from={from:yyyy-MM-dd}&to={to:yyyy-MM-dd}&fmt=json",
                        credentials),
                    cancellation)
                .ConfigureAwait(false);

            return response.IsSuccessStatusCode
                ? await response.Content.ReadAsStringAsync(cancellation).ConfigureAwait(false)
                : throw EodhdQuery.Refused((int)response.StatusCode, "earnings calendar feed");
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception failure) when (failure is not ProviderRefusal)
        {
            throw EodhdQuery.Unreachable("earnings calendar feed", failure, credentials);
        }
    }
}
