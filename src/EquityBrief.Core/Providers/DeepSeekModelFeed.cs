using System.Globalization;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace EquityBrief.Core.Providers;

// The research model's settings: which provider answers, which of its models, in which
// mode, how long a call may take, and the key.
//
// Everything that names the provider sits in this file, because the scan that keeps a
// model off the nightly path reads every shipped file for the provider's name and
// carves only the files that are model feeds. A setting naming the provider anywhere
// else would be a file on the night that names a model, which is the thing the scan
// exists to find.
// see: The night's zero-model-call rule bounds the arithmetic, and the overnight queue is carved out of it by name
// see: The research model is one interface with an implementation per wire format, chosen by configuration and never falling back
public sealed record ResearchModelSettings
{
    public const string Section = "EquityBrief:Models:Research";
    public const string ProviderKey = Section + ":Provider";
    public const string ModelKey = Section + ":Model";
    public const string ThinkingKey = Section + ":Thinking";
    public const string TimeoutKey = Section + ":TimeoutSeconds";

    // The key's path, beside the other providers' in the secrets file.
    public const string ApiKeyName = "EquityBrief:Providers:DeepSeek:ApiKey";

    // The one provider this build has a wire format for.
    public const string DeepSeek = "deepseek";

    public const string DefaultBaseAddress = "https://api.deepseek.com/";

    // The cheaper of the two models the provider lists, and a default rather than a
    // choice: which tier writes which section is the lane comparison 6.8 runs, by the
    // decision that chose the provider.
    // see: Two models for two jobs, and the research model is DeepSeek V4
    public const string DefaultModel = "deepseek-flash";

    // The provider's own default mode, which is thinking, for the same reason.
    public const string Enabled = "enabled";
    public const string Disabled = "disabled";

    // A call over the whole evidence set, with thinking, is minutes rather than
    // seconds at the worst the provider allows, and a timeout here is a bound on a
    // call that has hung.
    public const int DefaultTimeoutSeconds = 600;

    public ResearchModelSettings(string? provider, string? model, string? thinking, int? timeoutSeconds, string? apiKey)
    {
        var named = string.IsNullOrWhiteSpace(provider) ? DeepSeek : provider.Trim();

        if (!string.Equals(named, DeepSeek, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"'{ProviderKey}' names '{named}', and the one provider this build has a wire format for is " +
                $"'{DeepSeek}'. A provider with no implementation is refused rather than answered by another, because " +
                "a section whose model is not the one configured is a section nobody can say who wrote.");
        }

        if (string.IsNullOrWhiteSpace(apiKey))
        {
            throw new InvalidOperationException(
                $"No research model key. Set '{ApiKeyName}' in appsettings.Secrets.json beside appsettings.json, or " +
                "in the environment. It is refused here, at startup, rather than at the first pass, because a pass " +
                "refused by the provider reads as research that failed rather than research never configured.");
        }

        var chosen = string.IsNullOrWhiteSpace(model) ? DefaultModel : model.Trim();

        if (!DeepSeekModelFeed.Rates.ContainsKey(chosen))
        {
            throw new InvalidOperationException(
                $"'{ModelKey}' names '{chosen}', which carries no price in this build, and the models that do are " +
                $"{string.Join(" and ", DeepSeekModelFeed.Rates.Keys.Select(known => $"'{known}'"))}. A model nobody " +
                "can price is refused rather than called, because a call recorded as costing nothing is spend the " +
                "cap cannot see.");
        }

        var mode = string.IsNullOrWhiteSpace(thinking) ? Enabled : thinking.Trim().ToLowerInvariant();

        if (mode is not (Enabled or Disabled))
        {
            throw new InvalidOperationException(
                $"'{ThinkingKey}' is '{thinking}', and the two it may be are '{Enabled}' and '{Disabled}'.");
        }

        Provider = DeepSeek;
        Model = chosen;
        Thinking = mode == Enabled;
        Timeout = TimeSpan.FromSeconds(timeoutSeconds is > 0 ? timeoutSeconds.Value : DefaultTimeoutSeconds);
        ApiKey = apiKey;
    }

    public string Provider { get; }

    public string Model { get; }

    public bool Thinking { get; }

    public TimeSpan Timeout { get; }

    internal string ApiKey { get; }

    // The model as a section stores it: the same model thinking and not thinking are
    // two writers, and the recording key reads this, so a pass in one mode is never
    // answered by a recording made in the other.
    public string Identity => Thinking ? Model + " thinking" : Model;

    // Never rendered, because a key that can be printed is a key that reaches a log.
    public override string ToString() => $"ResearchModelSettings({Provider}, {Identity}, key withheld)";
}

// The published rates for one model, off-peak, in dollars per million tokens.
public sealed record ModelRates(decimal CacheHit, decimal CacheMiss, decimal Output);

// The research model over the provider's OpenAI-format chat completions endpoint.
//
// Written against three responses captured from the provider before any of it existed,
// and priced from the provider's own page, read on 2026-09-13 at 14:57 UTC. The
// transport is written at 6.7 rather than 6.8 because the provider's name, its models,
// its prices and its wire have to share one carved file, and the recorded double reads
// this file's parser; it first makes a pass at 6.8.
// see: A paid call is priced from the provider's own token counts at the rate its own timestamp falls in
public sealed class DeepSeekModelFeed(HttpClient client, ResearchModelSettings settings, ProviderRequest? request = null) : IResearchModelFeed
{
    public const string Path = "chat/completions";

    // The answer budget of one call. Thinking counts inside it, and a section over the
    // whole evidence set reasons for longer than it writes.
    public const int AnswerTokens = 8192;

    // Rates at peak are twice these, which the provider states as off-peak being half.
    public const decimal PeakMultiple = 2m;

    // What the chat template adds to a prompt before the text is counted, as tokens.
    // The captured probes carried 48 prompt tokens for 165 bytes of text without
    // thinking and 73 with it, so thinking's template alone added 25; an allowance ten
    // times that keeps the ceiling a ceiling for a prompt whose text is shorter than
    // its template.
    public const int TemplateTokens = 256;

    public static IReadOnlyDictionary<string, ModelRates> Rates { get; } = new Dictionary<string, ModelRates>(StringComparer.Ordinal)
    {
        ["deepseek-flash"] = new(0.003m, 0.15m, 0.60m),
        ["deepseek-v4-pro"] = new(0.022m, 0.66m, 1.98m),
    };

    // One attempt, for the local model's reason: a call that timed out may already be
    // billed, and a retry bills it again for the same answer.
    readonly ProviderRequest request = request
        ?? new ProviderRequest(new RetryPolicy(1, TimeSpan.Zero, settings.Timeout, settings.Timeout + settings.Timeout));

    public int Requests { get; private set; }

    public string Identity => settings.Identity;

    // The key travels in the authorisation header, and never in an address, so no
    // request line, log line or stored row can carry it.
    public static DeepSeekModelFeed Live(ResearchModelSettings settings)
    {
        var client = new HttpClient
        {
            BaseAddress = new Uri(ResearchModelSettings.DefaultBaseAddress),
            Timeout = settings.Timeout + settings.Timeout,
        };

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", settings.ApiKey);

        return new DeepSeekModelFeed(client, settings);
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

    // The request as it is sent.
    //
    // The model is the provider's own identifier, without the mode the identity carries,
    // and the mode is the provider's own field. Temperature is sent only where thinking is
    // off, because the provider's thinking mode takes none.
    public static string Body(ModelRequest wanted, ResearchModelSettings settings)
    {
        var messages = new object[]
        {
            new { role = "system", content = wanted.System },
            new { role = "user", content = wanted.Prompt },
        };

        return settings.Thinking
            ? JsonSerializer.Serialize(new
            {
                model = settings.Model,
                messages,
                max_tokens = AnswerTokens,
                stream = false,
                thinking = new { type = ResearchModelSettings.Enabled },
            })
            : JsonSerializer.Serialize(new
            {
                model = settings.Model,
                messages,
                max_tokens = AnswerTokens,
                stream = false,
                temperature = 0,
                thinking = new { type = ResearchModelSettings.Disabled },
            });
    }

    // A response read as the captures show it arrives.
    //
    // The answer is `content`, with the reasoning in a field of its own that is never
    // stored as a section. The usage carries the prompt split into tokens the provider
    // had cached and tokens it had not, which are priced apart, and the completion
    // count includes the reasoning, which is billed as output. A response carrying no
    // usage is refused, because a call nobody can price is spend the cap cannot see.
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

        var choice = choices[0];
        var message = choice.GetProperty("message");
        var finish = choice.TryGetProperty("finish_reason", out var reason) && reason.ValueKind == JsonValueKind.String ? reason.GetString()! : "unstated";
        var text = message.TryGetProperty("content", out var content) && content.ValueKind == JsonValueKind.String ? content.GetString()!.Trim() : string.Empty;

        int Count(JsonElement element, string name) =>
            element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var count) && count.ValueKind == JsonValueKind.Number
                ? count.GetInt32()
                : 0;

        var reasoning = usage.TryGetProperty("completion_tokens_details", out var details) ? Count(details, "reasoning_tokens") : 0;

        if (text.Length == 0)
        {
            throw new ProviderRefusal(
                $"The research model returned no answer for {section}: the finish reason was {finish} after {Count(usage, "completion_tokens")} " +
                $"completion token(s), {reasoning} of them reasoning. A section stored from this would be empty.",
                transient: false);
        }

        if (string.Equals(finish, "length", StringComparison.Ordinal))
        {
            throw new ProviderRefusal(
                $"The research model's answer for {section} stopped at its budget of {AnswerTokens} tokens rather than ending, so " +
                "what arrived is a section cut short and it is not stored.",
                transient: false);
        }

        return new ResearchAnswer(
            root.TryGetProperty("model", out var model) && model.ValueKind == JsonValueKind.String ? model.GetString()! : "unstated",
            text,
            Count(usage, "prompt_tokens"),
            Count(usage, "prompt_cache_hit_tokens"),
            Count(usage, "prompt_cache_miss_tokens"),
            Count(usage, "completion_tokens"),
            reasoning,
            finish,
            root.TryGetProperty("created", out var created) && created.ValueKind == JsonValueKind.Number
                ? DateTimeOffset.FromUnixTimeSeconds(created.GetInt64())
                : throw new ProviderRefusal(
                    $"The research model's response for {section} carries no creation instant, and whether it was billed at peak " +
                    "is read from that instant.",
                    transient: false));
    }

    // A refusal from the provider, with its own words where it sent any, at a length a
    // line can hold. The provider's error is an object carrying `message`.
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

    // Whether an instant falls in the provider's peak hours: 01:00 to 04:00 and 06:00
    // to 10:00 UTC, Monday to Friday, each window closed at its start and open at its
    // end.
    public static bool IsPeak(DateTimeOffset instant)
    {
        var utc = instant.UtcDateTime;

        return utc.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday)
            && (utc.Hour is >= 1 and < 4 || utc.Hour is >= 6 and < 10);
    }

    public decimal Price(ResearchAnswer answer) => PriceOf(settings.Model, answer);

    public decimal Ceiling(ModelRequest wanted) => CeilingOf(settings.Model, wanted);

    // What an answer cost: the cached prompt tokens, the uncached ones and the
    // completion, each at its rate, doubled where the provider's own timestamp falls at
    // peak. Priced by the model this installation asked for, which is what the provider
    // bills a retired name at.
    public static decimal PriceOf(string model, ResearchAnswer answer)
    {
        var rates = RatesOf(model);
        var multiple = IsPeak(answer.Created) ? PeakMultiple : 1m;

        return (answer.CacheHitTokens * rates.CacheHit + answer.CacheMissTokens * rates.CacheMiss + answer.CompletionTokens * rates.Output)
            * multiple / 1_000_000m;
    }

    // The most a call could cost before it is made: every byte of the prompt and the
    // template's allowance as uncached tokens, the whole answer budget as output, all at
    // peak. A tokeniser over bytes cannot make more tokens of a text than it has bytes,
    // and the provider cannot bill more output than the budget allows.
    public static decimal CeilingOf(string model, ModelRequest wanted)
    {
        var rates = RatesOf(model);
        var bytes = Encoding.UTF8.GetByteCount(wanted.System) + Encoding.UTF8.GetByteCount(wanted.Prompt);

        return ((bytes + TemplateTokens) * rates.CacheMiss + AnswerTokens * rates.Output) * PeakMultiple / 1_000_000m;
    }

    static ModelRates RatesOf(string model) =>
        Rates.TryGetValue(model, out var rates)
            ? rates
            : throw new InvalidOperationException($"'{model}' carries no price in this build, so no call to it can be priced.");
}

// The research model a configuration resolves to, named by what it is rather than by
// whose it is, so the composition that asks for one names no provider.
public static class ResearchModelFeeds
{
    public static IResearchModelFeed Live(ResearchModelSettings settings) => DeepSeekModelFeed.Live(settings);
}
