using System.Globalization;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace EquityBrief.Core.Providers;

// DeepSeek's search tool over the network, its search endpoint in its own wire format, measured beside Tavily's at
// 21.19 for the theme search.
//
// The tool takes no field for a site or a window, so the query carries them in its own syntax: each site as `site:`,
// several joined by OR, and the window as `after:` and `before:`. The page's text comes only where `need_content` asks
// for it. A result's date is RFC 3339 or null, and the tool rates each result with an authority tier and a rerank
// score, both read onto the result. A search returning nothing is not billed, and neither is a refusal.
// see: Theme material comes from a search tool, and per-name material never does
public sealed class DeepSeekSearchFeed(HttpClient client, ProviderRequest? request = null, Func<TimeSpan, CancellationToken, Task>? pause = null) : ISearchFeed
{
    public const string DefaultBaseAddress = "https://api.deepseek.com/";

    public const string Path = "v1/tools/search";

    // The key is the DeepSeek account's, which the secrets file holds under the section the research model's
    // deepseek profile names, so the search reads it there rather than from a second copy of it.
    public const string KeyProfile = "deepseek";

    // The most characters the tool takes in a query, from its reference as read on 2026-11-01.
    public const int MostQueryCharacters = 128;

    // What a search the tool answers with a result costs, in dollars, at the price the operator approved on 2026-11-01.
    public const decimal PriceASearch = 0.00075m;

    // The gap kept before every request but the first, so the feed sends at most four a second, under the five the
    // operator set. The gap is kept before each attempt, a retry's among them.
    public static readonly TimeSpan Spacing = TimeSpan.FromMilliseconds(250);

    readonly ProviderRequest request = request ?? new ProviderRequest(RetryPolicy.Standard);

    readonly Func<TimeSpan, CancellationToken, Task> pause = pause ?? Task.Delay;

    readonly SemaphoreSlim turn = new(1, 1);

    int sent;

    public int Requests { get; private set; }

    // The searches the tool answered with at least one result, which are the ones it bills.
    public int Billed { get; private set; }

    public int Attempts => request.Attempts;

    // The key the secrets file holds for the deepseek profile, or none where the profile names no section for it.
    public static string? KeyFrom(Func<string, string?> value) =>
        value(ModelProfiles.Field(KeyProfile, ModelProfiles.KeyField))?.Trim() is { Length: > 0 } section
            ? value(ModelProfiles.KeyPath(section))
            : null;

    // The key travels in the authorisation header and never in an address or a body, so no request line, log line or
    // stored row can carry it. A blank one is refused by name here rather than sent as an anonymous request.
    public static DeepSeekSearchFeed Live(string? apiKey, string? baseAddress = null, HttpMessageHandler? handler = null)
    {
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            throw new InvalidOperationException(
                $"No DeepSeek key. The search reads the key the '{KeyProfile}' profile names under " +
                $"'{ModelProfiles.Field(KeyProfile, ModelProfiles.KeyField)}', set as that section's ApiKey in appsettings.Secrets.json.");
        }

        var client = handler is null ? new HttpClient() : new HttpClient(handler);

        client.BaseAddress = new Uri(string.IsNullOrWhiteSpace(baseAddress) ? DefaultBaseAddress : baseAddress);
        client.Timeout = RetryPolicy.Standard.Timeout + RetryPolicy.Standard.Timeout;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);

        return new DeepSeekSearchFeed(client);
    }

    public async Task<SearchAnswer> SearchAsync(SearchQuery query, CancellationToken cancellation = default)
    {
        Requests++;

        string? unanswered = null;

        try
        {
            var body = await request
                .SendAsync(
                    async token =>
                    {
                        unanswered = null;

                        await WaitTurnAsync(token).ConfigureAwait(false);

                        try
                        {
                            using var content = new StringContent(Body(query), Encoding.UTF8, "application/json");
                            using var response = await client.PostAsync(Path, content, token).ConfigureAwait(false);

                            var text = await response.Content.ReadAsStringAsync(token).ConfigureAwait(false);

                            return response.IsSuccessStatusCode
                                ? text
                                : throw new ProviderRefusal(Refused((int)response.StatusCode, text), RetryPolicy.Transient((int)response.StatusCode));
                        }
                        catch (HttpRequestException unreachable)
                        {
                            unanswered = $"DeepSeek's search could not be reached: {unreachable.GetType().Name}: {unreachable.Message}";

                            throw new ProviderRefusal(unanswered, transient: true);
                        }
                    },
                    cancellation)
                .ConfigureAwait(false);

            var answer = Parse(body);

            if (answer.Results.Count > 0)
            {
                Billed++;
            }

            return answer;
        }
        catch (ProviderRefusal) when (unanswered is not null)
        {
            throw new SearchToolUnavailable(unanswered);
        }
        catch (OperationCanceledException) when (!cancellation.IsCancellationRequested)
        {
            throw new SearchToolUnavailable(
                $"DeepSeek's search did not answer within {request.Policy.Timeout.TotalSeconds.ToString(CultureInfo.InvariantCulture)} seconds, " +
                $"asked {request.Policy.Attempts.ToString(CultureInfo.InvariantCulture)} times.");
        }
    }

    async Task WaitTurnAsync(CancellationToken cancellation)
    {
        await turn.WaitAsync(cancellation).ConfigureAwait(false);

        try
        {
            if (sent++ > 0)
            {
                await pause(Spacing, cancellation).ConfigureAwait(false);
            }
        }
        finally
        {
            turn.Release();
        }
    }

    // The query as the tool reads it: the words, each site the search is restricted to, and the window. The tool does
    // not say whether `before:` takes its own day, so the day after the window is named and admissibility holds the
    // window as it holds Tavily's.
    // see: A theme search is scoped by parameter, not by hope
    public static string Query(SearchQuery query) =>
        string.Join(
            ' ',
            new[]
            {
                query.Query,
                string.Join(" OR ", query.Domains.Select(site => "site:" + site)),
                "after:" + query.From.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                "before:" + query.To.AddDays(1).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            }.Where(part => part.Length > 0));

    // A list of sites searched together in as few queries as the tool takes, each query's sites joined by OR in the
    // list's order and each query under the most characters the tool reads. The whole industry list in one query is
    // past that limit, so a search of the list together is this.
    public static IReadOnlyList<SearchQuery> Packed(string words, DateOnly from, DateOnly to, IReadOnlyList<string> sites, int mostResults)
    {
        var packed = new List<SearchQuery>();
        var held = new List<string>();

        foreach (var site in sites)
        {
            if (held.Count > 0 && Query(new SearchQuery(words, from, to, [.. held, site], mostResults)).Length > MostQueryCharacters)
            {
                packed.Add(new SearchQuery(words, from, to, [.. held], mostResults));
                held.Clear();
            }

            held.Add(site);
        }

        if (held.Count > 0)
        {
            packed.Add(new SearchQuery(words, from, to, [.. held], mostResults));
        }

        return packed;
    }

    // The request as it is sent: the query, how many results, and the page's text asked for.
    public static string Body(SearchQuery query) =>
        new JsonObject
        {
            ["query"] = Query(query),
            ["count"] = query.MostResults,
            ["need_content"] = true,
        }.ToJsonString();

    // The answer as the tool sends it. A result with no text, or an empty one, carries a null, which is the snippet
    // case a pass refuses to store; a result with no date carries a null, which admissibility refuses.
    public static SearchAnswer Parse(string body)
    {
        using var document = JsonDocument.Parse(body);

        if (!document.RootElement.TryGetProperty("results", out var results) || results.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidOperationException(
                "DeepSeek's search answered with no list of results. An answer that is not a search is refused rather than " +
                "read as one that found nothing.");
        }

        var parsed = new List<SearchResult>();

        foreach (var result in results.EnumerateArray())
        {
            var url = Text(result, "url");

            if (string.IsNullOrWhiteSpace(url))
            {
                throw new InvalidOperationException("DeepSeek's search returned a result with no address, which nothing could store or cite.");
            }

            var text = Text(result, "content");

            parsed.Add(new SearchResult(
                url,
                Text(result, "title") ?? string.Empty,
                Text(result, "snippet") ?? string.Empty,
                string.IsNullOrWhiteSpace(text) ? null : text,
                Text(result, "date") is { } date ? Published(date) : null)
            {
                Authority = result.TryGetProperty("authority", out var authority) && authority.ValueKind == JsonValueKind.Number ? authority.GetInt32() : null,
                Rerank = result.TryGetProperty("rerank_score", out var rerank) && rerank.ValueKind == JsonValueKind.Number ? rerank.GetDouble() : null,
            });
        }

        return new SearchAnswer(parsed);
    }

    // The offset a date is read in. Every date the tool sent at 21.19 was 16:00 UTC, which is midnight in Beijing, and
    // the day a page gives itself is that Beijing day: TrendForce's report addressed under 2026/10/14 came dated
    // 2026-10-13T16:00:00+00:00. So the date is read in Beijing's offset, and the publish date is its day there.
    public static readonly TimeSpan DatedAt = TimeSpan.FromHours(8);

    // The date in the RFC 3339 form the tool's reference gives. A value in any other form is a shape nobody captured,
    // refused by name rather than guessed at.
    static readonly string[] DateForms = ["yyyy-MM-dd'T'HH:mm:ssK", "yyyy-MM-dd'T'HH:mm:ss.FFFFFFFK"];

    static DateTimeOffset Published(string value) =>
        DateTimeOffset.TryParseExact(value, DateForms, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var published)
            ? published.ToOffset(DatedAt)
            : throw new InvalidOperationException(
                $"DeepSeek's search gave a date of '{value}', which is not the RFC 3339 form its reference gives. A date read " +
                "in a form nobody captured is a date that may be read wrong, so it is refused rather than guessed.");

    static string? Text(JsonElement result, string name) =>
        result.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    // A refusal in the tool's own words, which it writes under `error` as an OpenAI-format error.
    static string Refused(int status, string body)
    {
        string? words = null;

        try
        {
            using var document = JsonDocument.Parse(body);

            if (document.RootElement.TryGetProperty("error", out var error))
            {
                words = error.ValueKind switch
                {
                    JsonValueKind.Object when error.TryGetProperty("message", out var message) && message.ValueKind == JsonValueKind.String => message.GetString(),
                    JsonValueKind.String => error.GetString(),
                    _ => null,
                };
            }
        }
        catch (JsonException)
        {
        }

        return $"DeepSeek's search refused the search with status {status.ToString(CultureInfo.InvariantCulture)}" +
            (string.IsNullOrWhiteSpace(words) ? "." : $": {words}");
    }
}
