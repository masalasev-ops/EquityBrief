using System.Globalization;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace EquityBrief.Core.Providers;

// A paid job's model over the OpenAI chat completions format, which is the format most
// providers of a hosted model serve.
//
// It names no provider and no model. Where it sends the request, which model it asks
// for, the options the provider takes, the key and the prices are all the profile's
// settings, so switching model is a change to configuration. What it was written against
// is three responses captured from the provider the deepseek profile names, before a line
// of it existed, and the reader accepts the two ways providers in this format report a
// cached prompt, the provider's own fields and the OpenAI format's.
// see: A paid job names its model profile in one word the operator switches, and a profile is priced at its configured rates at its call's own timestamp
// see: A paid model is one interface with an implementation per wire format, and a job never falls back from the profile it names
public sealed class OpenAiCompatibleResearchFeed(HttpClient client, ResearchModelSettings settings, ProviderRequest? request = null) : IResearchModelFeed
{
    public const string Path = "chat/completions";

    // The format's own list of the models a key may call, which is what a probe asks
    // for: it bills nothing and answers only where the address and the key both do.
    public const string ModelsPath = "models";

    // How long a probe waits. A list of models is a few hundred bytes, so a provider
    // that has not answered one in thirty seconds is not going to answer a section.
    public static readonly TimeSpan ProbeTimeout = TimeSpan.FromSeconds(30);

    // One attempt, for the local model's reason: a call that timed out may already be
    // billed, and a retry bills it again for the same answer.
    readonly ProviderRequest request = request
        ?? new ProviderRequest(new RetryPolicy(1, TimeSpan.Zero, settings.Timeout, settings.Timeout + settings.Timeout));

    public int Requests { get; private set; }

    public int Probes { get; private set; }

    public string Identity => settings.Identity;

    // The key travels in the authorisation header, and never in an address, so no
    // request line, log line or stored row can carry it.
    public static OpenAiCompatibleResearchFeed Live(ResearchModelSettings settings)
    {
        var client = new HttpClient
        {
            BaseAddress = new Uri(settings.BaseAddress),
            Timeout = settings.Timeout + settings.Timeout,
        };

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", settings.ApiKey);

        return new OpenAiCompatibleResearchFeed(client, settings);
    }

    public async Task<ResearchAnswer> CompleteAsync(ModelRequest wanted, CancellationToken cancellation = default)
    {
        Requests++;

        var body = await request.SendAsync(
            async token =>
            {
                try
                {
                    using var content = new StringContent(Body(wanted, settings), Encoding.UTF8, "application/json");
                    using var message = new HttpRequestMessage(HttpMethod.Post, Path) { Content = content };
                    using var response = await client.SendAsync(message, HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(false);

                    if (!response.IsSuccessStatusCode)
                    {
                        throw new ProviderRefusal(
                            Refused(wanted.Section, (int)response.StatusCode, await response.Content.ReadAsStringAsync(token).ConfigureAwait(false)),
                            transient: false);
                    }

                    return string.Equals(response.Content.Headers.ContentType?.MediaType, StreamMediaType, StringComparison.OrdinalIgnoreCase)
                        ? await Streamed(await response.Content.ReadAsStreamAsync(token).ConfigureAwait(false), wanted.Section, token).ConfigureAwait(false)
                        : await response.Content.ReadAsStringAsync(token).ConfigureAwait(false);
                }
                catch (HttpRequestException unreachable)
                {
                    throw new ResearchModelUnavailable($"The research model could not be reached for {wanted.Section}: {unreachable.Message}");
                }
                catch (IOException broken)
                {
                    throw new ResearchModelUnavailable($"The research model could not be reached for {wanted.Section}: {broken.Message}");
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested && !cancellation.IsCancellationRequested)
                {
                    throw new ProviderRefusal(
                        $"The research model did not answer {wanted.Section} within {settings.Timeout.TotalSeconds.ToString(CultureInfo.InvariantCulture)} seconds.",
                        transient: false);
                }
            },
            cancellation).ConfigureAwait(false);

        return Parse(body, wanted.Section);
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
                : Refused("the model list", (int)response.StatusCode, await response.Content.ReadAsStringAsync(bounded.Token).ConfigureAwait(false));
        }
        catch (HttpRequestException unreachable)
        {
            return $"The research model could not be reached: {unreachable.Message}";
        }
        catch (OperationCanceledException) when (!cancellation.IsCancellationRequested)
        {
            return $"The research model did not answer a request for its model list within {ProbeTimeout.TotalSeconds.ToString(CultureInfo.InvariantCulture)} seconds.";
        }
    }

    public const string ResponseFormatField = "response_format";

    public const string StreamOptionsField = "stream_options";

    // The request as it is sent: the configured model, the two messages, the answer's
    // budget and the answer streamed with its counts at the end, with the provider's own
    // options merged in beside them. The options cannot replace those five, which the
    // settings refuse at startup.
    //
    // Streamed because an answer asked for whole arrives only once the model has finished
    // reasoning, and nothing flows until then: a connection carrying nothing for a minute
    // is cut on the way, so every answer that took longer was lost.
    // see: A paid model on the OpenAI format is asked to stream its answer, because a connection silent for a minute is cut
    public static string Body(ModelRequest wanted, ResearchModelSettings settings)
    {
        var body = new JsonObject
        {
            ["model"] = settings.Model,
            ["messages"] = new JsonArray(
                new JsonObject { ["role"] = "system", ["content"] = wanted.System },
                new JsonObject { ["role"] = "user", ["content"] = wanted.Prompt }),
            ["max_tokens"] = settings.AnswerTokens,
            ["stream"] = true,
            [StreamOptionsField] = new JsonObject { ["include_usage"] = true },
        };

        // The risks and a news label are asked for as a JSON object, which the format's own mode holds an answer to;
        // the label's schema is told in the instruction's words, since this mode takes none.
        // see: Each risk is returned as fields and confirmed by a listed fact or an event of one kind, and no two risks share either
        // see: A label's reason is one sentence holding no digit, and an answer code cannot read is asked once more and then kept as unreadable with its cause
        if (EquityBrief.Core.Research.RiskFields.IsRisks(wanted.Section) || EquityBrief.Core.News.NewsInstruction.IsLabel(wanted.Section))
        {
            body[ResponseFormatField] = new JsonObject { ["type"] = "json_object" };
        }

        if (settings.Options is { } options)
        {
            foreach (var (name, value) in JsonNode.Parse(options)!.AsObject())
            {
                body[name] = value?.DeepClone();
            }
        }

        return body.ToJsonString();
    }

    // The media type a streamed answer arrives as. A provider answering the request whole
    // instead is read whole.
    public const string StreamMediaType = "text/event-stream";

    const string DataField = "data:";
    const string StreamEnd = "[DONE]";

    // A streamed answer read into the response the provider sends whole, so one parser reads
    // both: the answer's pieces joined in order, the finish reason, and the counts the last
    // event carries. Reasoning streamed beside the answer is never stored, so it is not kept.
    // Blank lines and comments, which a provider sends to hold the connection while a request
    // waits, carry nothing. A stream that ends before the event saying it is done is an
    // answer cut short and is not read as one.
    public static async Task<string> Streamed(Stream stream, string section, CancellationToken cancellation)
    {
        using var reader = new StreamReader(stream, Encoding.UTF8);

        var answer = new StringBuilder();
        JsonNode? model = null;
        JsonNode? created = null;
        JsonNode? finish = null;
        JsonNode? usage = null;

        while (await reader.ReadLineAsync(cancellation).ConfigureAwait(false) is { } line)
        {
            if (!line.StartsWith(DataField, StringComparison.Ordinal))
            {
                continue;
            }

            var data = line[DataField.Length..].Trim();

            if (string.Equals(data, StreamEnd, StringComparison.Ordinal))
            {
                return new JsonObject
                {
                    ["model"] = model,
                    ["created"] = created,
                    ["choices"] = new JsonArray(new JsonObject
                    {
                        ["index"] = 0,
                        ["message"] = new JsonObject { ["role"] = "assistant", ["content"] = answer.ToString() },
                        ["finish_reason"] = finish,
                    }),
                    ["usage"] = usage,
                }.ToJsonString();
            }

            var chunk = JsonNode.Parse(data)!.AsObject();

            model ??= chunk["model"]?.DeepClone();
            created ??= chunk["created"]?.DeepClone();

            if (chunk["usage"] is JsonObject counts)
            {
                usage = counts.DeepClone();
            }

            if (chunk["choices"] is JsonArray { Count: > 0 } choices && choices[0] is JsonObject choice)
            {
                if (choice["delta"]?["content"] is JsonValue piece && piece.GetValueKind() == JsonValueKind.String)
                {
                    answer.Append(piece.GetValue<string>());
                }

                if (choice["finish_reason"] is JsonValue reason && reason.GetValueKind() == JsonValueKind.String)
                {
                    finish = reason.DeepClone();
                }
            }
        }

        throw new ResearchModelUnavailable(
            $"The research model's answer for {section} ended before the provider said it was done, so what arrived is an " +
            "answer cut short and it is not stored.");
    }

    // A response read as the captures show it arrives.
    //
    // The answer is `content`; reasoning a provider returns beside it is never stored as
    // a section. A cached prompt is read from the provider's own `prompt_cache_hit_tokens`
    // where it sends one and from the format's `prompt_tokens_details.cached_tokens`
    // otherwise, and the uncached prompt is the rest. Reasoning a provider counts inside
    // the completion is billed as output and nothing is added for it. A response carrying
    // no usage, or no creation instant, is refused, because a call nobody can price is
    // spend the cap cannot see.
    public static ResearchAnswer Parse(string json, string section)
    {
        using var document = JsonDocument.Parse(json);

        var root = document.RootElement;

        if (!root.TryGetProperty("choices", out var choices) || choices.ValueKind != JsonValueKind.Array || choices.GetArrayLength() == 0)
        {
            throw new ProviderRefusal($"The research model's response for {section} carries no choices, so there is no answer to store.", transient: false);
        }

        if (!root.TryGetProperty("usage", out var usage) || usage.ValueKind != JsonValueKind.Object)
        {
            throw new ProviderRefusal(
                $"The research model's response for {section} carries no usage, so it cannot be priced, and a call nobody can " +
                "price is spend the cap cannot see.",
                transient: false);
        }

        static int? Count(JsonElement element, string name) =>
            element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var count) && count.ValueKind == JsonValueKind.Number
                ? count.GetInt32()
                : null;

        var choice = choices[0];
        var message = choice.GetProperty("message");
        var finish = choice.TryGetProperty("finish_reason", out var reason) && reason.ValueKind == JsonValueKind.String ? reason.GetString()! : "unstated";
        var text = message.TryGetProperty("content", out var content) && content.ValueKind == JsonValueKind.String ? AnswerText.Visible(content.GetString()!) : string.Empty;

        var prompt = Count(usage, "prompt_tokens") ?? 0;
        var completion = Count(usage, "completion_tokens") ?? 0;
        var reasoning = usage.TryGetProperty("completion_tokens_details", out var completionDetails) ? Count(completionDetails, "reasoning_tokens") ?? 0 : 0;
        var cached = Count(usage, "prompt_cache_hit_tokens")
            ?? (usage.TryGetProperty("prompt_tokens_details", out var promptDetails) ? Count(promptDetails, "cached_tokens") : null)
            ?? 0;
        var uncached = Count(usage, "prompt_cache_miss_tokens") ?? prompt - cached;

        var answer = new ResearchAnswer(
            root.TryGetProperty("model", out var model) && model.ValueKind == JsonValueKind.String ? model.GetString()! : "unstated",
            text,
            prompt,
            cached,
            uncached,
            completion,
            reasoning,
            finish,
            root.TryGetProperty("created", out var created) && created.ValueKind == JsonValueKind.Number
                ? DateTimeOffset.FromUnixTimeSeconds(created.GetInt64())
                : throw new ProviderRefusal(
                    $"The research model's response for {section} carries no creation instant, and whether it was billed at peak " +
                    "is read from that instant.",
                    transient: false));

        // Nothing usable, and billed all the same: thrown with the counts, so the call is
        // priced at what the provider charged for it rather than recorded as costing nothing.
        if (text.Length == 0)
        {
            throw new UnusableResearchAnswer(
                $"The research model returned no answer for {section}: the finish reason was {finish} after {completion} " +
                $"completion token(s), {reasoning} of them reasoning. A section stored from this would be empty.",
                answer);
        }

        if (string.Equals(finish, "length", StringComparison.Ordinal))
        {
            throw new UnusableResearchAnswer(
                $"The research model's answer for {section} stopped at its budget rather than ending, so what arrived is a " +
                "section cut short and it is not stored.",
                answer);
        }

        return answer;
    }

    // A refusal from the provider, with its own words where it sent any, at a length a
    // line can hold. The format's error is an object carrying `message`.
    public static string Refused(string section, int status, string body)
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
            ? $"The research model refused {section} with status {status}."
            : $"The research model refused {section} with status {status}: {(said.Length > 300 ? said[..300] : said)}";
    }

    public decimal Price(ResearchAnswer answer) => settings.Pricing.Price(answer);

    public decimal Ceiling(ModelRequest wanted) => settings.Pricing.Ceiling(wanted, settings.AnswerTokens);
}

// The paid model a job's profile resolves to, by its wire format, and never another.
// see: A paid model is one interface with an implementation per wire format, and a job never falls back from the profile it names
public static class ResearchModelFeeds
{
    public static IResearchModelFeed Live(ResearchModelSettings settings) => settings.Format switch
    {
        ResearchModelSettings.OpenAiFormat => OpenAiCompatibleResearchFeed.Live(settings),
        ResearchModelSettings.AnthropicFormat => AnthropicMessagesFeed.Live(settings),
        _ => throw new InvalidOperationException($"No paid model feed implements the format '{settings.Format}'."),
    };

    // A recording read by the parser the live feed of its format uses.
    public static ResearchAnswer ParseRecorded(ResearchModelSettings settings, string recording, string section) => settings.Format switch
    {
        ResearchModelSettings.AnthropicFormat => AnthropicMessagesFeed.ParseRecorded(recording, section),
        _ => OpenAiCompatibleResearchFeed.Parse(recording, section),
    };
}
