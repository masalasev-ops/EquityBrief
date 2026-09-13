using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace EquityBrief.Core.Providers;

// What the research model answered, as the parser read it, with the provider's own
// token counts and the instant the provider stamped on the answer.
//
// The counts are what a call is priced from, read off the payload rather than
// estimated, and `Created` is the provider's timestamp rather than this machine's,
// because a rate that changes at peak is decided by the provider's clock.
// see: Code owns every number
public sealed record ResearchAnswer(
    string Model,
    string Text,
    int PromptTokens,
    int CacheHitTokens,
    int CacheMissTokens,
    int CompletionTokens,
    int ReasoningTokens,
    string FinishReason,
    DateTimeOffset Created);

// The research model did not answer at all. A type of its own for the reason the
// local model's is: a pass stops asking on it and carries on past a refusal.
public sealed class ResearchModelUnavailable(string message) : Exception(message);

// The paid lane's model.
//
// One interface with an implementation per wire format, chosen by configuration and
// never falling back from one to another. Which provider and which model answer are
// settings, so switching model is a change to configuration and not to code. It prices
// its own answers and bounds its own calls from the configured rates, because the one
// component allowed to make a paid call judges the cap by those two figures without
// knowing whose model it is.
// see: The research model is one interface with an implementation per wire format, chosen by configuration and never falling back
// see: Every paid call is made through the spend cap, which holds the research model
// see: The research model is named only in configuration, and a call is priced at the configured rates its own timestamp falls in
public interface IResearchModelFeed
{
    int Requests { get; }

    // The model as a section records it: the model and any options it was asked with,
    // because the same model asked two ways is two writers.
    string Identity { get; }

    Task<ResearchAnswer> CompleteAsync(ModelRequest request, CancellationToken cancellation = default);

    // What an answer cost, in dollars, from its own token counts.
    decimal Price(ResearchAnswer answer);

    // The most a call could cost before it is made, in dollars.
    decimal Ceiling(ModelRequest request);
}

// What a research model's provider charges, as configuration states it: dollars per
// million tokens for a cached prompt token, an uncached one and an output token, and
// the hours at which those rates are multiplied.
//
// A setting rather than a constant, because it is the provider's price and moves when
// the provider moves it, and because a different model is a different price. Peak hours
// are UTC hours on the days named; a provider with no peak pricing names none.
public sealed record ResearchPricing
{
    // What a chat template adds to a prompt before its text is counted, as tokens. The
    // captured calls carried 25 more prompt tokens with the provider's thinking template
    // than without it; an allowance ten times that keeps a ceiling a ceiling for a
    // prompt shorter than its template.
    public const int TemplateTokens = 256;

    public ResearchPricing(
        decimal cacheHit,
        decimal cacheMiss,
        decimal output,
        IReadOnlyList<(int From, int To)> peakHours,
        IReadOnlyCollection<DayOfWeek> peakDays,
        decimal peakMultiple)
    {
        if (cacheHit < 0m || cacheMiss <= 0m || output <= 0m)
        {
            throw new InvalidOperationException(
                "A research model priced at nothing for its prompt or its output is a model whose calls the spend cap " +
                "cannot see, so a rate at or below zero is refused rather than read.");
        }

        if (peakHours.Any(window => window.From < 0 || window.To > 24 || window.From >= window.To))
        {
            throw new InvalidOperationException(
                "A peak window is a start hour before an end hour, each between 0 and 24 in UTC.");
        }

        if (peakMultiple < 1m)
        {
            throw new InvalidOperationException("A peak multiple below one would price peak hours as cheaper than the rest.");
        }

        CacheHit = cacheHit;
        CacheMiss = cacheMiss;
        Output = output;
        PeakHours = [.. peakHours];
        PeakDays = [.. peakDays];
        PeakMultiple = peakHours.Count == 0 ? 1m : peakMultiple;
    }

    public decimal CacheHit { get; }

    public decimal CacheMiss { get; }

    public decimal Output { get; }

    public IReadOnlyList<(int From, int To)> PeakHours { get; }

    public IReadOnlyCollection<DayOfWeek> PeakDays { get; }

    public decimal PeakMultiple { get; }

    // Whether an instant falls in a peak window: an hour at or after a window's start
    // and before its end, on a day the pricing names.
    public bool IsPeak(DateTimeOffset instant)
    {
        var utc = instant.UtcDateTime;

        return PeakDays.Contains(utc.DayOfWeek) && PeakHours.Any(window => utc.Hour >= window.From && utc.Hour < window.To);
    }

    // What an answer cost: the cached prompt tokens, the uncached ones and the
    // completion, each at its rate, multiplied where the provider's own timestamp falls
    // at peak.
    public decimal Price(ResearchAnswer answer) =>
        (answer.CacheHitTokens * CacheHit + answer.CacheMissTokens * CacheMiss + answer.CompletionTokens * Output)
        * (IsPeak(answer.Created) ? PeakMultiple : 1m) / 1_000_000m;

    // The most a call could cost before it is made: every byte of the prompt and the
    // template's allowance as uncached tokens, the whole answer budget as output, at the
    // peak multiple. A tokeniser over bytes cannot make more tokens of a text than it has
    // bytes, and a provider cannot bill more output than the budget allows.
    public decimal Ceiling(ModelRequest request, int answerTokens)
    {
        var bytes = Encoding.UTF8.GetByteCount(request.System) + Encoding.UTF8.GetByteCount(request.Prompt);

        return ((bytes + TemplateTokens) * CacheMiss + answerTokens * Output) * PeakMultiple / 1_000_000m;
    }
}

// The research model's settings: the wire format, where the provider answers, which
// model, any options the provider takes with the request, how long a call may take, the
// answer's budget, the key, and the prices.
//
// None of it is written in code. The shipped configuration names the provider this
// installation uses, and a different provider or model is a different value in that
// file and in the secrets file beside it.
// see: The research model is named only in configuration, and a call is priced at the configured rates its own timestamp falls in
public sealed record ResearchModelSettings
{
    public const string Section = "EquityBrief:Models:Research";
    public const string FormatKey = Section + ":Format";
    public const string BaseAddressKey = Section + ":BaseAddress";
    public const string ModelKey = Section + ":Model";
    public const string OptionsKey = Section + ":Options";
    public const string TimeoutKey = Section + ":TimeoutSeconds";
    public const string AnswerTokensKey = Section + ":AnswerTokens";
    public const string ApiKeyKey = Section + ":ApiKey";

    public const string PricesSection = Section + ":Prices";
    public const string CacheHitKey = PricesSection + ":CacheHit";
    public const string CacheMissKey = PricesSection + ":CacheMiss";
    public const string OutputKey = PricesSection + ":Output";
    public const string PeakHoursKey = PricesSection + ":PeakHours";
    public const string PeakDaysKey = PricesSection + ":PeakDays";
    public const string PeakMultipleKey = PricesSection + ":PeakMultiple";

    // The one wire format this build implements, which is the OpenAI chat completions
    // format most providers serve.
    public const string OpenAiFormat = "openai";

    public const int DefaultTimeoutSeconds = 600;
    public const int DefaultAnswerTokens = 8192;

    // The request fields the feed owns, which options may not set, because an option
    // replacing the model or the messages would be a call the recording key does not
    // describe.
    public static readonly string[] OwnedFields = ["model", "messages", "max_tokens", "stream"];

    public ResearchModelSettings(
        string? format,
        string? baseAddress,
        string? model,
        string? apiKey,
        ResearchPricing? pricing,
        string? options = null,
        int? timeoutSeconds = null,
        int? answerTokens = null)
    {
        var named = string.IsNullOrWhiteSpace(format) ? OpenAiFormat : format.Trim();

        if (!string.Equals(named, OpenAiFormat, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"'{FormatKey}' names '{named}', and the one wire format this build implements is '{OpenAiFormat}'. A format " +
                "with no implementation is refused rather than answered by another, because a section whose model is not " +
                "the one configured is a section nobody can say who wrote.");
        }

        if (string.IsNullOrWhiteSpace(baseAddress)
            || !Uri.TryCreate(baseAddress.Trim(), UriKind.Absolute, out var address)
            || address.Scheme is not ("https" or "http"))
        {
            throw new InvalidOperationException(
                $"'{BaseAddressKey}' is '{baseAddress}', and the research model needs the absolute address its provider " +
                "answers at. It is refused at startup rather than at the first pass.");
        }

        if (string.IsNullOrWhiteSpace(model))
        {
            throw new InvalidOperationException($"'{ModelKey}' names no model, and the research model needs one.");
        }

        if (string.IsNullOrWhiteSpace(apiKey))
        {
            throw new InvalidOperationException(
                $"No research model key. Set '{ApiKeyKey}' in appsettings.Secrets.json beside appsettings.json, or in the " +
                "environment. It is refused here, at startup, rather than at the first pass, because a pass refused by the " +
                "provider reads as research that failed rather than research never configured.");
        }

        Pricing = pricing ?? throw new InvalidOperationException(
            $"No prices for the research model. Set '{CacheHitKey}', '{CacheMissKey}' and '{OutputKey}' to what the provider " +
            "charges per million tokens. A model nobody can price is refused rather than called, because a call recorded as " +
            "costing nothing is spend the cap cannot see.");

        Format = OpenAiFormat;
        BaseAddress = address.AbsoluteUri.EndsWith('/') ? address.AbsoluteUri : address.AbsoluteUri + "/";
        Model = model.Trim();
        Options = Parsed(options);
        Timeout = TimeSpan.FromSeconds(timeoutSeconds is > 0 ? timeoutSeconds.Value : DefaultTimeoutSeconds);
        AnswerTokens = answerTokens is > 0 ? answerTokens.Value : DefaultAnswerTokens;
        ApiKey = apiKey;
    }

    public string Format { get; }

    public string BaseAddress { get; }

    public string Model { get; }

    // The provider's own request options as compact JSON, or null where none are set.
    public string? Options { get; }

    public TimeSpan Timeout { get; }

    public int AnswerTokens { get; }

    public ResearchPricing Pricing { get; }

    internal string ApiKey { get; }

    // The model as a section stores it, with the options it was asked with where there
    // are any. The recording key reads this, so a call asked one way is never answered
    // by a recording made another.
    public string Identity => Options is null ? Model : Model + " " + Options;

    // Never rendered with the key, because a key that can be printed reaches a log.
    public override string ToString() => $"ResearchModelSettings({Identity} at {BaseAddress}, key withheld)";

    static string? Parsed(string? options)
    {
        if (string.IsNullOrWhiteSpace(options))
        {
            return null;
        }

        JsonNode? node;

        try
        {
            node = JsonNode.Parse(options);
        }
        catch (JsonException)
        {
            node = null;
        }

        if (node is not JsonObject parsed)
        {
            throw new InvalidOperationException(
                $"'{OptionsKey}' is not a JSON object. Options are the provider's own request fields, written as the " +
                "object the provider takes, and anything else would be sent as something nobody wrote.");
        }

        var owned = parsed.Select(field => field.Key).Where(field => OwnedFields.Contains(field, StringComparer.Ordinal)).ToArray();

        if (owned.Length > 0)
        {
            throw new InvalidOperationException(
                $"'{OptionsKey}' sets {string.Join(", ", owned)}, which the feed writes itself from the request and the " +
                "settings. An option replacing one would be a call the recording key does not describe.");
        }

        return parsed.ToJsonString();
    }
}
