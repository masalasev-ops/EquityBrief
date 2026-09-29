using System.Globalization;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace EquityBrief.Core.Providers;

// A paid job's model over Claude's own messages interface.
//
// It names no model and no key: which model it asks for, the options it is asked with, the
// key and the prices are the profile's settings. Its refusal reader was written against the
// provider's own refusal, captured on 2026-09-29. Its answer reader reads the shape the
// provider's documentation states, and it is held to the answers captured from the provider
// before it writes a live pass. The
// interface returns the answer as content blocks and the counts under `usage`, with the
// prompt tokens read from the provider's cache and those written to it counted apart from
// the rest, and a model's thinking counted inside the output it bills. It stamps no instant
// in the body, so the instant an answer carries is the response's own `Date` header, which
// is the provider's clock rather than this machine's.
// see: A paid model is one interface with an implementation per wire format, and a job never falls back from the profile it names
// see: A paid job names its model profile in one word, and a profile is priced at its configured rates at its call's own timestamp
public sealed class AnthropicMessagesFeed(HttpClient client, ResearchModelSettings settings, ProviderRequest? request = null) : IResearchModelFeed
{
    public const string Path = "v1/messages";

    // The interface's own list of the models a key may call, which is what a probe asks for:
    // it bills nothing and answers only where the address and the key both do.
    public const string ModelsPath = "v1/models";

    // The headers every request carries: the key, never in an address, and the interface's
    // version, without which the provider answers nothing; and the workspace, where the key is
    // not scoped to one and the provider refuses it without one.
    public const string KeyHeader = "x-api-key";
    public const string VersionHeader = "anthropic-version";
    public const string Version = "2023-06-01";
    public const string WorkspaceHeader = "anthropic-workspace-id";

    // How long a probe waits, for the reason the other format's probe waits that long.
    public static readonly TimeSpan ProbeTimeout = TimeSpan.FromSeconds(30);

    // One attempt, for the other format's reason: a call that timed out may already be
    // billed, and a retry bills it again for the same answer.
    readonly ProviderRequest request = request
        ?? new ProviderRequest(new RetryPolicy(1, TimeSpan.Zero, settings.Timeout, settings.Timeout + settings.Timeout));

    public int Requests { get; private set; }

    public int Probes { get; private set; }

    public string Identity => settings.Identity;

    public static AnthropicMessagesFeed Live(ResearchModelSettings settings)
    {
        var client = new HttpClient
        {
            BaseAddress = new Uri(settings.BaseAddress),
            Timeout = settings.Timeout + settings.Timeout,
        };

        client.DefaultRequestHeaders.Add(KeyHeader, settings.ApiKey);

        foreach (var (name, values) in Headers(settings))
        {
            client.DefaultRequestHeaders.Add(name, values);
        }

        return new AnthropicMessagesFeed(client, settings);
    }

    // The headers every request carries beside the key, which is never among them so that
    // nothing reading these can print it: the interface's version, and the workspace where the
    // key is not scoped to one.
    public static IReadOnlyDictionary<string, string[]> Headers(ResearchModelSettings settings)
    {
        var headers = new Dictionary<string, string[]>(StringComparer.Ordinal) { [VersionHeader] = [Version] };

        if (settings.Workspace is { } workspace)
        {
            headers[WorkspaceHeader] = [workspace];
        }

        return headers;
    }

    public async Task<ResearchAnswer> CompleteAsync(ModelRequest wanted, CancellationToken cancellation = default)
    {
        Requests++;

        DateTimeOffset? answeredAt = null;

        var body = await request.SendAsync(
            async token =>
            {
                try
                {
                    using var content = new StringContent(Body(wanted, settings), Encoding.UTF8, "application/json");
                    using var response = await client.PostAsync(Path, content, token).ConfigureAwait(false);

                    var text = await response.Content.ReadAsStringAsync(token).ConfigureAwait(false);

                    answeredAt = response.Headers.Date;

                    return response.IsSuccessStatusCode
                        ? text
                        : throw new ProviderRefusal(Refused(settings.Job, wanted.Section, (int)response.StatusCode, text), transient: false);
                }
                catch (HttpRequestException unreachable)
                {
                    throw new ResearchModelUnavailable($"The {settings.Job} job's model could not be reached for {wanted.Section}: {unreachable.Message}");
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested && !cancellation.IsCancellationRequested)
                {
                    throw new ProviderRefusal(
                        $"The {settings.Job} job's model did not answer {wanted.Section} within {settings.Timeout.TotalSeconds.ToString(CultureInfo.InvariantCulture)} seconds.",
                        transient: false);
                }
            },
            cancellation).ConfigureAwait(false);

        return Parse(
            body,
            wanted.Section,
            answeredAt ?? throw new ProviderRefusal(
                $"The response for {wanted.Section} carries no Date header, and the instant a call is priced at is the provider's.",
                transient: false));
    }

    public async Task<string?> UnreachableAsync(CancellationToken cancellation = default)
    {
        Probes++;

        using var bounded = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
        bounded.CancelAfter(ProbeTimeout);

        try
        {
            using var response = await client.GetAsync(ModelsPath, bounded.Token).ConfigureAwait(false);

            return response.IsSuccessStatusCode
                ? null
                : Refused(settings.Job, "the model list", (int)response.StatusCode, await response.Content.ReadAsStringAsync(bounded.Token).ConfigureAwait(false));
        }
        catch (HttpRequestException unreachable)
        {
            return $"The {settings.Job} job's model could not be reached: {unreachable.Message}";
        }
        catch (OperationCanceledException) when (!cancellation.IsCancellationRequested)
        {
            return $"The {settings.Job} job's model did not answer a request for its model list within {ProbeTimeout.TotalSeconds.ToString(CultureInfo.InvariantCulture)} seconds.";
        }
    }

    // The request as it is sent: the profile's model, the answer's budget, the instructions as
    // the system prompt, the one user message and no stream, with the profile's own options
    // merged in beside them. The options cannot replace those five, which the settings refuse
    // at startup.
    public static string Body(ModelRequest wanted, ResearchModelSettings settings)
    {
        var body = new JsonObject
        {
            ["model"] = settings.Model,
            ["max_tokens"] = settings.AnswerTokens,
            ["system"] = wanted.System,
            ["messages"] = new JsonArray(new JsonObject { ["role"] = "user", ["content"] = wanted.Prompt }),
            ["stream"] = false,
        };

        if (settings.Options is { } options)
        {
            foreach (var (name, value) in JsonNode.Parse(options)!.AsObject())
            {
                body[name] = value?.DeepClone();
            }
        }

        return body.ToJsonString();
    }

    // A response read as the captures show it arrives.
    //
    // The answer is the text of the `text` blocks in order; a `thinking` block beside them is
    // never stored as a section. The counts are the interface's own: `input_tokens` is the
    // prompt the provider neither read from its cache nor wrote to it, and the two cache
    // counts are the rest. Thinking is billed inside `output_tokens`, so nothing is added for
    // it. A response carrying no usage is refused, because a call nobody can price is spend
    // the cap cannot see.
    public static ResearchAnswer Parse(string json, string section, DateTimeOffset answeredAt)
    {
        using var document = JsonDocument.Parse(json);

        var root = document.RootElement;

        if (!root.TryGetProperty("content", out var content) || content.ValueKind != JsonValueKind.Array)
        {
            throw new ProviderRefusal($"The response for {section} carries no content, so there is no answer to store.", transient: false);
        }

        if (!root.TryGetProperty("usage", out var usage) || usage.ValueKind != JsonValueKind.Object)
        {
            throw new ProviderRefusal(
                $"The response for {section} carries no usage, so it cannot be priced, and a call nobody can price is spend " +
                "the cap cannot see.",
                transient: false);
        }

        static int Count(JsonElement element, string name) =>
            element.TryGetProperty(name, out var count) && count.ValueKind == JsonValueKind.Number ? count.GetInt32() : 0;

        var text = string.Concat(content.EnumerateArray()
            .Where(block => block.TryGetProperty("type", out var type) && type.GetString() == "text"
                && block.TryGetProperty("text", out var said) && said.ValueKind == JsonValueKind.String)
            .Select(block => block.GetProperty("text").GetString())).Trim();

        var uncached = Count(usage, "input_tokens");
        var read = Count(usage, "cache_read_input_tokens");
        var written = Count(usage, "cache_creation_input_tokens");
        var output = Count(usage, "output_tokens");
        var stop = root.TryGetProperty("stop_reason", out var reason) && reason.ValueKind == JsonValueKind.String ? reason.GetString()! : "unstated";

        var answer = new ResearchAnswer(
            root.TryGetProperty("model", out var model) && model.ValueKind == JsonValueKind.String ? model.GetString()! : "unstated",
            text,
            uncached + read + written,
            read,
            uncached,
            output,
            0,
            stop,
            answeredAt,
            written);

        // Billed all the same, and thrown with the counts, so the call is priced at what the
        // provider charged for it rather than recorded as costing nothing.
        if (string.Equals(stop, "refusal", StringComparison.Ordinal))
        {
            throw new UnusableResearchAnswer(
                $"The model declined to answer {section}, and a section stored from a declined answer would be empty.",
                answer);
        }

        if (string.Equals(stop, "max_tokens", StringComparison.Ordinal))
        {
            throw new UnusableResearchAnswer(
                $"The answer for {section} stopped at its budget of tokens rather than ending, so what arrived is a section " +
                "cut short and it is not stored.",
                answer);
        }

        if (text.Length == 0)
        {
            throw new UnusableResearchAnswer(
                $"The answer for {section} carried no text: the stop reason was {stop} after {output} output token(s). A " +
                "section stored from this would be empty.",
                answer);
        }

        return answer;
    }

    // A recording of this interface: the response's `Date` header and its body as they
    // arrived, which is what a replay needs to price the call at the instant it was made.
    public const string RecordedDate = "date";
    public const string RecordedResponse = "response";

    public static ResearchAnswer ParseRecorded(string recording, string section)
    {
        using var document = JsonDocument.Parse(recording);

        var root = document.RootElement;
        var date = root.GetProperty(RecordedDate).GetString()!;

        return Parse(
            root.GetProperty(RecordedResponse).GetRawText(),
            section,
            DateTimeOffset.Parse(date, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal));
    }

    // A refusal from the provider, with its own words where it sent any, at a length a line
    // can hold. The interface's error is an object carrying `message`.
    public static string Refused(string job, string section, int status, string body)
    {
        var said = string.Empty;

        try
        {
            using var document = JsonDocument.Parse(body);

            if (document.RootElement.ValueKind == JsonValueKind.Object
                && document.RootElement.TryGetProperty("error", out var error)
                && error.ValueKind == JsonValueKind.Object
                && error.TryGetProperty("message", out var words)
                && words.ValueKind == JsonValueKind.String)
            {
                said = words.GetString()!.Trim();
            }
        }
        catch (JsonException)
        {
        }

        return said.Length == 0
            ? $"The {job} job's model refused {section} with status {status}."
            : $"The {job} job's model refused {section} with status {status}: {(said.Length > 300 ? said[..300] : said)}";
    }

    public decimal Price(ResearchAnswer answer) => settings.Pricing.Price(answer);

    public decimal Ceiling(ModelRequest wanted) => settings.Pricing.Ceiling(wanted, settings.AnswerTokens);
}
