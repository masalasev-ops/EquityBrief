using System.Globalization;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace EquityBrief.Core.Providers;

// The research model over the OpenAI chat completions format, which is the format most
// providers of a hosted model serve.
//
// It names no provider and no model. Where it sends the request, which model it asks
// for, the options the provider takes, the key and the prices are all settings, so
// switching model is a change to configuration. What it was written against is three
// responses captured from the provider the shipped configuration names, before a line
// of it existed, and the reader accepts the two ways providers in this format report a
// cached prompt, the provider's own fields and the OpenAI format's.
// see: The research model is named only in configuration, and a call is priced at the configured rates its own timestamp falls in
// see: The research model is one interface with an implementation per wire format, chosen by configuration and never falling back
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
                    using var response = await client.PostAsync(Path, content, token).ConfigureAwait(false);

                    var text = await response.Content.ReadAsStringAsync(token).ConfigureAwait(false);

                    return response.IsSuccessStatusCode
                        ? text
                        : throw new ProviderRefusal(Refused(wanted.Section, (int)response.StatusCode, text), transient: false);
                }
                catch (HttpRequestException unreachable)
                {
                    throw new ResearchModelUnavailable($"The research model could not be reached for {wanted.Section}: {unreachable.Message}");
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

    // The request as it is sent: the configured model, the two messages, the answer's
    // budget and no stream, with the provider's own options merged in beside them. The
    // options cannot replace those four, which the settings refuse at startup.
    public static string Body(ModelRequest wanted, ResearchModelSettings settings)
    {
        var body = new JsonObject
        {
            ["model"] = settings.Model,
            ["messages"] = new JsonArray(
                new JsonObject { ["role"] = "system", ["content"] = wanted.System },
                new JsonObject { ["role"] = "user", ["content"] = wanted.Prompt }),
            ["max_tokens"] = settings.AnswerTokens,
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
        var text = message.TryGetProperty("content", out var content) && content.ValueKind == JsonValueKind.String ? content.GetString()!.Trim() : string.Empty;

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

// The research model a configuration resolves to, by its wire format.
public static class ResearchModelFeeds
{
    public static IResearchModelFeed Live(ResearchModelSettings settings) => settings.Format switch
    {
        ResearchModelSettings.OpenAiFormat => OpenAiCompatibleResearchFeed.Live(settings),
        _ => throw new InvalidOperationException($"No research model feed implements the format '{settings.Format}'."),
    };
}
