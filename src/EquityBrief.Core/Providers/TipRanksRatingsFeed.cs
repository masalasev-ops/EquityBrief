using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace EquityBrief.Core.Providers;

// TipRanks' server over the network on the operator's key: a session opened as the server's protocol asks, its count of
// the month's calls, which costs none, and a stock's recent ratings, which cost one. The key goes in each request's
// header and in no address, and every word a failure carries is redacted of it before it leaves this file. The
// session's opening and the count are tried once more on a transient failure; the ratings are asked once and never
// again, since a request the server counted and whose answer was lost would spend a second call. The parsers are
// `TipRanksAnswers`'.
// see: TipRanks' analyst ratings are asked only on a press, once a stock a day and never past the month's limit
public sealed class TipRanksRatingsFeed(HttpClient client, TipRanksCredentials credentials, ProviderRequest? request = null, ProviderRequest? once = null) : IAnalystRatingsFeed
{
    public const string Address = "https://mcp.tipranks.com/mcp/";

    // The tools the job calls, the count and the ratings.
    public const string UsageTool = "get_my_usage";

    public const string RatingsTool = "get_recent_analyst_ratings";

    // The protocol version the session is opened under.
    public const string Protocol = "2025-06-18";

    public const string SessionHeader = "Mcp-Session-Id";

    public static RetryPolicy Policy { get; } = RetryPolicy.Standard with { Attempts = 2, Timeout = TimeSpan.FromSeconds(30) };

    readonly ProviderRequest request = request ?? new ProviderRequest(Policy);

    readonly ProviderRequest once = once ?? new ProviderRequest(Policy with { Attempts = 1 });

    string? session;

    bool opened;

    int next;

    public int Requests { get; private set; }

    public static TipRanksRatingsFeed Live(TipRanksCredentials credentials) =>
        new(new HttpClient { BaseAddress = new Uri(Address), Timeout = Policy.Timeout + Policy.Timeout }, credentials);

    public async Task<TipRanksUsage> UsageAsync(CancellationToken cancellation = default)
    {
        await OpenAsync(cancellation).ConfigureAwait(false);

        return TipRanksAnswers.Usage(await CallAsync(UsageTool, new Dictionary<string, string>(), request, cancellation).ConfigureAwait(false));
    }

    public async Task<TipRanksAnswer?> RatingsAsync(string ticker, CancellationToken cancellation = default)
    {
        await OpenAsync(cancellation).ConfigureAwait(false);

        return TipRanksAnswers.Ratings(await CallAsync(RatingsTool, new Dictionary<string, string> { ["ticker"] = ticker }, once, cancellation).ConfigureAwait(false));
    }

    // The session the server's protocol opens before any tool is called: the opening, whose answer names the session, and
    // the notice that the client has read it.
    async Task OpenAsync(CancellationToken cancellation)
    {
        if (opened)
        {
            return;
        }

        var answer = await PostAsync(
            new Dictionary<string, object>
            {
                ["jsonrpc"] = "2.0",
                ["id"] = ++next,
                ["method"] = "initialize",
                ["params"] = new Dictionary<string, object>
                {
                    ["protocolVersion"] = Protocol,
                    ["capabilities"] = new Dictionary<string, object>(),
                    ["clientInfo"] = new Dictionary<string, string> { ["name"] = "equitybrief", ["version"] = "1" },
                },
            },
            request,
            cancellation).ConfigureAwait(false);

        if (answer.Contains("\"error\"", StringComparison.Ordinal) && !answer.Contains("\"result\"", StringComparison.Ordinal))
        {
            throw new ProviderRefusal(credentials.Redact("TipRanks refused to open a session: " + answer[..Math.Min(answer.Length, 300)]), transient: false);
        }

        await PostAsync(new Dictionary<string, object> { ["jsonrpc"] = "2.0", ["method"] = "notifications/initialized" }, request, cancellation).ConfigureAwait(false);

        opened = true;
    }

    async Task<string> CallAsync(string tool, Dictionary<string, string> arguments, ProviderRequest sender, CancellationToken cancellation) =>
        await PostAsync(
            new Dictionary<string, object>
            {
                ["jsonrpc"] = "2.0",
                ["id"] = ++next,
                ["method"] = "tools/call",
                ["params"] = new Dictionary<string, object> { ["name"] = tool, ["arguments"] = arguments },
            },
            sender,
            cancellation).ConfigureAwait(false);

    // One message to the server with the key in its header and the session once one is named, the answer's text where the
    // server took it and a refusal naming the status otherwise, every word of either redacted of the key.
    async Task<string> PostAsync(Dictionary<string, object> message, ProviderRequest sender, CancellationToken cancellation)
    {
        Requests++;

        var body = JsonSerializer.Serialize(message);

        return await sender
            .SendAsync(
                async token =>
                {
                    try
                    {
                        using var post = new HttpRequestMessage(HttpMethod.Post, string.Empty)
                        {
                            Content = new StringContent(body, Encoding.UTF8, "application/json"),
                        };

                        post.Headers.Authorization = new AuthenticationHeaderValue("Bearer", credentials.ApiKey);
                        post.Headers.Accept.ParseAdd("application/json");
                        post.Headers.Accept.ParseAdd("text/event-stream");
                        post.Headers.TryAddWithoutValidation("MCP-Protocol-Version", Protocol);

                        // Each request on a connection of its own, which the server closes once it has answered: an
                        // answer it sends with no length ends there, and none waits on a connection kept open.
                        post.Headers.ConnectionClose = true;

                        if (session is { } named)
                        {
                            post.Headers.TryAddWithoutValidation(SessionHeader, named);
                        }

                        using var response = await client.SendAsync(post, HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(false);

                        if (response.Headers.TryGetValues(SessionHeader, out var sessions) && sessions.FirstOrDefault() is { Length: > 0 } given)
                        {
                            session = given;
                        }

                        // A notice the server accepts is answered 202 with no message, and its body, sent with no length,
                        // is never read.
                        if (response.StatusCode == System.Net.HttpStatusCode.Accepted)
                        {
                            return string.Empty;
                        }

                        return response.IsSuccessStatusCode
                            ? await response.Content.ReadAsStringAsync(token).ConfigureAwait(false)
                            : throw new ProviderRefusal(
                                $"TipRanks answered {(int)response.StatusCode}.",
                                transient: RetryPolicy.Transient((int)response.StatusCode));
                    }
                    catch (OperationCanceledException)
                    {
                        throw;
                    }
                    catch (HttpRequestException failure)
                    {
                        throw new ProviderRefusal("TipRanks could not be reached: " + credentials.Redact(failure.Message), transient: true);
                    }
                },
                cancellation)
            .ConfigureAwait(false);
    }
}
