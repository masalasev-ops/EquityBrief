using System.Globalization;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace EquityBrief.Core.Providers;

// The search tool over the network: Tavily's search endpoint, in its own wire format.
//
// Written against responses captured from it before a line of this existed, which is where
// three things this reader handles were first seen rather than supposed. A result can come
// back with no page text although the request asked for it. A result can come from a site
// the request's list does not name: a company search restricted to thirteen sites returned
// seven of its ten results from sites outside them. And the publish date is written as an
// HTTP date rather than as a calendar date.
// see: Theme material comes from a search tool, and per-name material never does
public sealed class TavilySearchFeed(HttpClient client, ProviderRequest? request = null) : ISearchFeed
{
    public const string DefaultBaseAddress = "https://api.tavily.com/";

    public const string Path = "search";

    public const string ApiKeyName = "EquityBrief:Providers:Tavily:ApiKey";

    // The most sites a request's list may name, as the tool's own reference gave it when it
    // was read at 6.9, and not captured: a request naming more is refused by the tool rather
    // than answered. The two source lists name thirteen and twelve.
    public const int MostDomains = 300;

    // The request's fixed half: a general search, the basic depth, the page's text as text,
    // the publish date asked for, and nothing the tool writes itself.
    public const string Topic = "general";
    public const string Depth = "basic";
    public const string RawContent = "text";

    readonly ProviderRequest request = request ?? new ProviderRequest(RetryPolicy.Standard);

    public int Requests { get; private set; }

    public int Attempts => request.Attempts;

    // The key travels in the authorisation header and never in an address, so no request
    // line, log line or stored row can carry it. A blank one is refused by name here, when
    // the feeds are resolved, rather than sent as an anonymous request.
    public static TavilySearchFeed Live(string? apiKey, string? baseAddress = null)
    {
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            throw new InvalidOperationException(
                $"No search tool key. Set '{ApiKeyName}' in appsettings.Secrets.json beside appsettings.json, or in the " +
                "environment. A blank key reaches the tool as an anonymous request and comes back as a refusal.");
        }

        var client = new HttpClient
        {
            BaseAddress = new Uri(string.IsNullOrWhiteSpace(baseAddress) ? DefaultBaseAddress : baseAddress),
            Timeout = RetryPolicy.Standard.Timeout + RetryPolicy.Standard.Timeout,
        };

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);

        return new TavilySearchFeed(client);
    }

    public async Task<SearchAnswer> SearchAsync(SearchQuery query, CancellationToken cancellation = default)
    {
        Requests++;

        // Whether the last attempt got no answer at all, which is the one failure the caller
        // is told apart: a refusal is an answer, and so is a search that found nothing.
        string? unanswered = null;

        try
        {
            var body = await request
                .SendAsync(
                    async token =>
                    {
                        unanswered = null;

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
                            unanswered = $"The search tool could not be reached: {unreachable.GetType().Name}: {unreachable.Message}";

                            throw new ProviderRefusal(unanswered, transient: true);
                        }
                    },
                    cancellation)
                .ConfigureAwait(false);

            return Parse(body);
        }
        catch (ProviderRefusal) when (unanswered is not null)
        {
            throw new SearchToolUnavailable(unanswered);
        }
        catch (OperationCanceledException) when (!cancellation.IsCancellationRequested)
        {
            throw new SearchToolUnavailable(
                $"The search tool did not answer within {request.Policy.Timeout.TotalSeconds.ToString(CultureInfo.InvariantCulture)} seconds, " +
                $"asked {request.Policy.Attempts.ToString(CultureInfo.InvariantCulture)} times.");
        }
    }

    // The request as it is sent. The four parameters a theme search is scoped by are the
    // words, the two dates, the list and the text, and a test reads each here rather than
    // in the prose that describes them.
    // see: A theme search is scoped by parameter, not by hope
    public static string Body(SearchQuery query) =>
        new JsonObject
        {
            ["query"] = query.Query,
            ["topic"] = Topic,
            ["search_depth"] = Depth,
            ["start_date"] = query.From.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            ["end_date"] = query.To.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            ["include_domains"] = new JsonArray([.. query.Domains.Select(domain => JsonValue.Create(domain))]),
            ["max_results"] = query.MostResults,
            ["include_raw_content"] = RawContent,
            ["include_published_date"] = true,
            ["include_answer"] = false,
            ["include_images"] = false,
        }.ToJsonString();

    // The answer as the tool sends it. A result with no text, or with an empty one, carries
    // a null, which is the snippet case a pass refuses to store; a result with no publish
    // date carries a null, which admissibility refuses.
    public static SearchAnswer Parse(string body)
    {
        using var document = JsonDocument.Parse(body);

        if (!document.RootElement.TryGetProperty("results", out var results) || results.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidOperationException(
                "The search tool answered with no list of results. An answer that is not a search is refused rather than " +
                "read as one that found nothing.");
        }

        var parsed = new List<SearchResult>();

        foreach (var result in results.EnumerateArray())
        {
            var url = Text(result, "url");

            if (string.IsNullOrWhiteSpace(url))
            {
                throw new InvalidOperationException("The search tool returned a result with no address, which nothing could store or cite.");
            }

            var text = Text(result, "raw_content");

            parsed.Add(new SearchResult(
                url,
                Text(result, "title") ?? string.Empty,
                Text(result, "content") ?? string.Empty,
                string.IsNullOrWhiteSpace(text) ? null : text,
                Text(result, "published_date") is { } published ? Published(published) : null));
        }

        return new SearchAnswer(parsed);
    }

    // The publish date in the form every captured result carried it, an HTTP date. A value
    // in any other form is a shape nobody captured, and it is refused by name rather than
    // guessed at.
    static DateTimeOffset Published(string value) =>
        DateTimeOffset.TryParseExact(value, "r", CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var published)
            ? published
            : throw new InvalidOperationException(
                $"The search tool gave a publish date of '{value}', which is not the HTTP date every captured result carried. " +
                "A date read in a form nobody captured is a date that may be read wrong, so it is refused rather than guessed.");

    static string? Text(JsonElement result, string name) =>
        result.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    // A refusal in the tool's own words, which it writes under `detail`.
    static string Refused(int status, string body)
    {
        string? words = null;

        try
        {
            using var document = JsonDocument.Parse(body);

            if (document.RootElement.TryGetProperty("detail", out var detail))
            {
                words = detail.ValueKind switch
                {
                    JsonValueKind.Object when detail.TryGetProperty("error", out var error) && error.ValueKind == JsonValueKind.String => error.GetString(),
                    JsonValueKind.String => detail.GetString(),
                    _ => null,
                };
            }
        }
        catch (JsonException)
        {
        }

        return $"The search tool refused the search with status {status.ToString(CultureInfo.InvariantCulture)}" +
            (string.IsNullOrWhiteSpace(words) ? "." : $": {words}");
    }
}
