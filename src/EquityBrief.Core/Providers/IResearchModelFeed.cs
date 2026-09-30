using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace EquityBrief.Core.Providers;

// What the research model answered, as the parser read it, with the provider's own
// token counts and the instant the provider stamped on the answer.
//
// The counts are what a call is priced from, read off the payload rather than
// estimated, and `Created` is the provider's timestamp rather than this machine's,
// because a rate that changes at peak is decided by the provider's clock. A prompt the
// provider wrote to its cache on this call is counted apart, since a provider that
// charges for the write charges it at a rate of its own.
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
    DateTimeOffset Created,
    int CacheWriteTokens = 0);

// The text of an answer as a reader sees it, trimmed of white space and of the format
// characters Unicode sets apart from visible text, the zero-width space among them. An
// answer holding nothing a reader can see is no answer whichever model wrote it: told to
// write nothing, a writer answered with a single zero-width space, which a trim of white
// space alone keeps and the checker then read as a sentence. An answer holding nothing but the
// markers a sentence cites its documents by is no answer either: asked for an industry's cycle
// its pages did not support, a writer answered with a lone marker, which the checker read as a
// sentence citing a document and accepted.
// see: An industry cycle the model declined is named as declined for lack of industry sources
public static class AnswerText
{
    static readonly Regex MarkersOnly = new(@"^(?:\s*\[D\d+\]\s*)+$", RegexOptions.CultureInvariant);

    public static string Visible(string text)
    {
        static bool Invisible(char character) =>
            char.IsWhiteSpace(character) || char.GetUnicodeCategory(character) == UnicodeCategory.Format;

        var start = 0;
        var end = text.Length;

        while (start < end && Invisible(text[start]))
        {
            start++;
        }

        while (end > start && Invisible(text[end - 1]))
        {
            end--;
        }

        var visible = text[start..end];

        return MarkersOnly.IsMatch(visible) ? string.Empty : visible;
    }
}

// The research model did not answer at all. A type of its own for the reason the
// local model's is: a pass stops asking on it and carries on past a refusal.
public sealed class ResearchModelUnavailable(string message) : Exception(message);

// The research model answered, the provider counted and billed the tokens, and what came
// back cannot be stored: no answer at all, or an answer cut off at its budget.
//
// A type of its own and carrying the answer's counts, because a refusal of this kind is
// spend. 6.8 measured it: asked every section of one pass in the paid lane, three calls
// came back with nothing usable after 8,192, 8,192 and 2,546 completion tokens, $0.0136
// the provider billed against $0.0183 the calls it answered cost, and a ledger that
// recorded them as nothing would have let research spend past a cap by that much.
public sealed class UnusableResearchAnswer(string message, ResearchAnswer answer) : Exception(message)
{
    public ResearchAnswer Answer { get; } = answer;
}

// A paid job's model.
//
// One interface with an implementation per wire format, chosen by the profile a job's
// configuration names and never falling back from one to another. Which provider and
// which model answer are settings, so switching model is a change to configuration and
// not to code. It prices its own answers and bounds its own calls from the profile's
// rates, because the one component allowed to make a paid call judges the cap by those
// two figures without knowing whose model it is.
// see: A paid model is one interface with an implementation per wire format, and a job never falls back from the profile it names
// see: Every paid call is made through the spend cap, which holds each paid job's model
// see: A paid job names its model profile in one word the operator switches, and a profile is priced at its configured rates at its call's own timestamp
public interface IResearchModelFeed
{
    int Requests { get; }

    // The model as a section records it: the model and any options it was asked with,
    // because the same model asked two ways is two writers.
    string Identity { get; }

    Task<ResearchAnswer> CompleteAsync(ModelRequest request, CancellationToken cancellation = default);

    // Whether the provider answers at all, asked before a pass starts with a request
    // that bills nothing. Null where it answers, and what happened where it does not.
    // A pass does not start against a model that is not there, because one that
    // fetched its documents and wrote its free sections before finding out is a pass
    // that stopped rather than one that did not start.
    // see: A research pass does not start where the research model does not answer
    Task<string?> UnreachableAsync(CancellationToken cancellation = default);

    // How many times the provider was asked whether it answers, which is a request
    // and not a model call, so it is counted apart from `Requests`.
    int Probes { get; }

    // What an answer cost, in dollars, from its own token counts.
    decimal Price(ResearchAnswer answer);

    // The most a call could cost before it is made, in dollars.
    decimal Ceiling(ModelRequest request);
}

// What a paid model's provider charges, as a profile's configuration states it: dollars
// per million tokens for a cached prompt token, an uncached one, a prompt token written to
// the provider's cache and an output token, and the hours at which those rates are
// multiplied.
//
// A setting rather than a constant, because it is the provider's price and moves when
// the provider moves it, and because a different model is a different price. Peak hours
// are UTC hours on the days named; a provider with no peak pricing names none, and one
// that charges nothing apart for a cache write names no write rate.
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
        decimal peakMultiple,
        decimal cacheWrite = 0m)
    {
        if (cacheHit < 0m || cacheMiss <= 0m || output <= 0m || cacheWrite < 0m)
        {
            throw new InvalidOperationException(
                "A paid model priced at nothing for its prompt or its output is a model whose calls the spend cap " +
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
        CacheWrite = cacheWrite;
        Output = output;
        PeakHours = [.. peakHours];
        PeakDays = [.. peakDays];
        PeakMultiple = peakHours.Count == 0 ? 1m : peakMultiple;
    }

    public decimal CacheHit { get; }

    public decimal CacheMiss { get; }

    public decimal CacheWrite { get; }

    public decimal Output { get; }

    // The names a profile's prices are written under, beneath its `Prices` section.
    public const string CacheHitField = "CacheHit";
    public const string CacheMissField = "CacheMiss";
    public const string CacheWriteField = "CacheWrite";
    public const string OutputField = "Output";
    public const string PeakHoursField = "PeakHours";
    public const string PeakDaysField = "PeakDays";
    public const string PeakMultipleField = "PeakMultiple";

    public IReadOnlyList<(int From, int To)> PeakHours { get; }

    public IReadOnlyCollection<DayOfWeek> PeakDays { get; }

    public decimal PeakMultiple { get; }

    // The prices as a profile's configuration states them under its `Prices` section, read
    // through the two ways a caller holds its configuration: the value at a key, and the
    // values listed under one. Read in one place, so the worker's passes and the queue page
    // the read surface draws hold the same windows. None where configuration states no
    // price, which the settings refuse by name. Peak hours are written as "01-04", a UTC
    // start hour and end hour; days by their English names.
    public static ResearchPricing? From(string section, Func<string, string?> value, Func<string, IEnumerable<string?>> values)
    {
        string Key(string field) => section + ":" + field;

        var hit = value(Key(CacheHitField));
        var miss = value(Key(CacheMissField));
        var write = value(Key(CacheWriteField));
        var output = value(Key(OutputField));

        if (string.IsNullOrWhiteSpace(miss) && string.IsNullOrWhiteSpace(output) && string.IsNullOrWhiteSpace(hit))
        {
            return null;
        }

        var hours = values(Key(PeakHoursField))
            .Select(child => Window(child ?? string.Empty, Key(PeakHoursField)))
            .ToArray();

        var days = values(Key(PeakDaysField))
            .Select(child => Enum.TryParse<DayOfWeek>(child, ignoreCase: true, out var day)
                ? day
                : throw new InvalidOperationException($"'{Key(PeakDaysField)}' names '{child}', which is not a day of the week."))
            .ToArray();

        var multiple = value(Key(PeakMultipleField));

        return new ResearchPricing(
            Money(hit, Key(CacheHitField)) ?? 0m,
            Money(miss, Key(CacheMissField)) ?? 0m,
            Money(output, Key(OutputField)) ?? 0m,
            hours,
            days,
            Money(multiple, Key(PeakMultipleField)) ?? 1m,
            Money(write, Key(CacheWriteField)) ?? 0m);
    }

    static (int From, int To) Window(string value, string key)
    {
        var parts = value.Split('-');

        return parts.Length == 2
            && int.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out var from)
            && int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var to)
                ? (from, to)
                : throw new InvalidOperationException(
                    $"'{key}' holds '{value}', and a peak window is written as a UTC start hour and end hour, as 01-04.");
    }

    static decimal? Money(string? value, string key)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return decimal.TryParse(value, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var amount)
            ? amount
            : throw new InvalidOperationException(
                $"'{key}' is '{value}', which is not an amount written with a decimal point. It is read as written rather than " +
                "replaced by a default.");
    }

    // Whether an instant falls in a peak window: an hour at or after a window's start
    // and before its end, on a day the pricing names.
    public bool IsPeak(DateTimeOffset instant)
    {
        var utc = instant.UtcDateTime;

        return PeakDays.Contains(utc.DayOfWeek) && PeakHours.Any(window => utc.Hour >= window.From && utc.Hour < window.To);
    }

    // The first instant at or after this one that is not at peak: the instant itself where
    // it is not, and otherwise the start of the first hour after it that no window holds,
    // which is what a refusal to run paid work at peak says it waits for.
    public DateTimeOffset OffPeakFrom(DateTimeOffset instant)
    {
        if (!IsPeak(instant))
        {
            return instant;
        }

        var utc = instant.ToUniversalTime();
        var hour = new DateTimeOffset(utc.Year, utc.Month, utc.Day, utc.Hour, 0, 0, TimeSpan.Zero);

        // A week of hours holds every window on every day, so a pricing whose windows
        // cover every hour of every day it names still ends on a day it does not name.
        for (var step = 1; step <= 24 * 8; step++)
        {
            if (!IsPeak(hour.AddHours(step)))
            {
                return hour.AddHours(step);
            }
        }

        throw new InvalidOperationException("Every hour of every day is at peak, so there is no off-peak window for paid work to wait for.");
    }

    // The first instant at or after this one at which a pass as long as the bound starts off peak
    // and ends before a peak window opens: the instant itself where it is off peak and the bound
    // ends before the next window, and otherwise the end of the window the pass would reach, read
    // again from there. A bound of nothing moves only a start that falls inside a window. The drain
    // takes a request at this instant and the queue page states it, so the two cannot disagree.
    // see: A pass starts only where the longest pass the store holds would end before a peak window opens
    public DateTimeOffset StartFor(DateTimeOffset instant, TimeSpan bound)
    {
        var at = OffPeakFrom(instant);

        // A week holds every window twice over on every day it names, so a bound that fits
        // between two windows anywhere in it is found inside this many steps.
        for (var step = 0; step < 64; step++)
        {
            if (PeakOpensAfter(at) is not { } opens || at + bound < opens)
            {
                return at;
            }

            at = OffPeakFrom(opens);
        }

        throw new InvalidOperationException(
            $"No stretch between peak windows in a week is longer than a pass of {bound}, so no pass can start and end off peak.");
    }

    // The same instant under several pricings at once, for a pass whose sections are written by more than one
    // profile: each pricing's own start read again from the latest any of them gave, until none of them moves it,
    // so the pass starts and ends off peak under every one.
    // see: Research names a profile per section as well as per job, and a Claude profile states its thinking
    public static DateTimeOffset StartFor(IReadOnlyCollection<ResearchPricing> pricings, DateTimeOffset instant, TimeSpan bound)
    {
        var at = instant;

        // Each round moves the instant to the end of a window some pricing names, and a week holds every window of
        // every pricing, so a start that exists is found well inside this many rounds.
        for (var round = 0; round < 64 * Math.Max(1, pricings.Count); round++)
        {
            var latest = at;

            foreach (var pricing in pricings)
            {
                var starts = pricing.StartFor(at, bound);

                if (starts > latest)
                {
                    latest = starts;
                }
            }

            if (latest == at)
            {
                return at;
            }

            at = latest;
        }

        throw new InvalidOperationException(
            $"No stretch in a week is off peak under every profile a pass's sections name for a pass of {bound}, so no pass can start and end off peak.");
    }

    // The instant the next peak window opens after this one, which is always the start of an hour,
    // or none where the pricing names no window in the week ahead.
    public DateTimeOffset? PeakOpensAfter(DateTimeOffset instant)
    {
        var utc = instant.ToUniversalTime();
        var hour = new DateTimeOffset(utc.Year, utc.Month, utc.Day, utc.Hour, 0, 0, TimeSpan.Zero);

        for (var step = 1; step <= 24 * 8; step++)
        {
            if (IsPeak(hour.AddHours(step)))
            {
                return hour.AddHours(step);
            }
        }

        return null;
    }

    // What an answer cost: the cached prompt tokens, the uncached ones, the ones written to
    // the cache and the completion, each at its rate, multiplied where the provider's own
    // timestamp falls at peak.
    public decimal Price(ResearchAnswer answer) =>
        (answer.CacheHitTokens * CacheHit + answer.CacheMissTokens * CacheMiss + answer.CacheWriteTokens * CacheWrite
            + answer.CompletionTokens * Output)
        * (IsPeak(answer.Created) ? PeakMultiple : 1m) / 1_000_000m;

    // The most a call could cost before it is made: every byte of the prompt and the
    // template's allowance at the dearer of the uncached and cache-write rates, the whole
    // answer budget as output, at the peak multiple. A tokeniser over bytes cannot make
    // more tokens of a text than it has bytes, and a provider cannot bill more output than
    // the budget allows.
    public decimal Ceiling(ModelRequest request, int answerTokens)
    {
        var bytes = Encoding.UTF8.GetByteCount(request.System) + Encoding.UTF8.GetByteCount(request.Prompt);

        return ((bytes + TemplateTokens) * Math.Max(CacheMiss, CacheWrite) + answerTokens * Output) * PeakMultiple / 1_000_000m;
    }
}

// A paid job's model as configuration names it: the job, the profile its `Use` names, and
// that profile's wire format, where its provider answers, which model, any options the
// provider takes with the request, the key, the prices and the provider's earliest
// published retirement date, with the job's own answer budget and how long a call may take.
//
// None of it is written in code. The shipped configuration names each profile and the one
// each job uses, and a different provider or model for a job is one word in that file,
// with its key in the secrets file beside it.
// see: A paid job names its model profile in one word the operator switches, and a profile is priced at its configured rates at its call's own timestamp
public sealed record ResearchModelSettings
{
    // The OpenAI chat completions format most providers serve, and Claude's own messages
    // interface, the two wire formats this build implements.
    public const string OpenAiFormat = "openai";
    public const string AnthropicFormat = "anthropic";

    public static readonly string[] Formats = [OpenAiFormat, AnthropicFormat];

    public const int DefaultTimeoutSeconds = 600;
    public const int DefaultAnswerTokens = 8192;

    // The request fields a format's feed owns, which options may not set, because an option
    // replacing the model or the messages would be a call the recording key does not
    // describe.
    public static IReadOnlyList<string> OwnedFields(string format) =>
        string.Equals(format, AnthropicFormat, StringComparison.Ordinal)
            ? ["model", "messages", "system", "max_tokens", "stream", AnthropicMessagesFeed.OutputConfigField]
            : ["model", "messages", "max_tokens", "stream", OpenAiCompatibleResearchFeed.StreamOptionsField, OpenAiCompatibleResearchFeed.ResponseFormatField];

    public ResearchModelSettings(
        string job,
        string profile,
        string? format,
        string? baseAddress,
        string? model,
        string? keyName,
        string? apiKey,
        ResearchPricing? pricing,
        string? options = null,
        int? timeoutSeconds = null,
        int? answerTokens = null,
        DateOnly? retires = null,
        DateOnly? retiresReadOn = null,
        string? workspace = null,
        string? thinking = null)
    {
        var named = string.IsNullOrWhiteSpace(format) ? OpenAiFormat : format.Trim();

        if (!Formats.Contains(named, StringComparer.Ordinal))
        {
            throw new InvalidOperationException(
                $"'{ModelProfiles.Field(profile, ModelProfiles.FormatField)}' names '{named}', and the wire formats this build " +
                $"implements are {string.Join(" and ", Formats.Select(one => $"'{one}'"))}. A format with no implementation is " +
                "refused rather than answered by another, because a section whose model is not the one configured is a " +
                "section nobody can say who wrote.");
        }

        if (string.IsNullOrWhiteSpace(baseAddress)
            || !Uri.TryCreate(baseAddress.Trim(), UriKind.Absolute, out var address)
            || address.Scheme is not ("https" or "http"))
        {
            throw new InvalidOperationException(
                $"'{ModelProfiles.Field(profile, ModelProfiles.BaseAddressField)}' is '{baseAddress}', and the {job} job's model " +
                "needs the absolute address its provider answers at. It is refused at startup rather than at the first call.");
        }

        if (string.IsNullOrWhiteSpace(model))
        {
            throw new InvalidOperationException(
                $"'{ModelProfiles.Field(profile, ModelProfiles.ModelField)}' names no model, and the {job} job needs one.");
        }

        if (string.IsNullOrWhiteSpace(keyName))
        {
            throw new InvalidOperationException(
                $"'{ModelProfiles.Field(profile, ModelProfiles.KeyField)}' names no key, and the profile '{profile}' needs the " +
                "name of the section of the secrets file its key sits under.");
        }

        if (string.IsNullOrWhiteSpace(apiKey))
        {
            throw new InvalidOperationException(
                $"The {job} job uses the profile '{profile}', whose key '{keyName}' the secrets file does not hold. Set " +
                $"'{ModelProfiles.KeyPath(keyName.Trim())}' in appsettings.Secrets.json beside appsettings.json, or in the " +
                "environment. The job stops here and no other profile answers for it, because a section whose model is not " +
                "the one configured is a section nobody can say who wrote.");
        }

        Pricing = pricing ?? throw new InvalidOperationException(
            $"No prices for the profile '{profile}'. Set '{ModelProfiles.Prices(profile)}' to what the provider charges per " +
            "million tokens. A model nobody can price is refused rather than called, because a call recorded as costing " +
            "nothing is spend the cap cannot see.");

        Job = job;
        Profile = profile;
        Format = named;
        BaseAddress = address.AbsoluteUri.EndsWith('/') ? address.AbsoluteUri : address.AbsoluteUri + "/";
        Model = model.Trim();
        KeyName = keyName.Trim();
        Options = Parsed(options, named, ModelProfiles.Field(profile, ModelProfiles.OptionsField));
        Timeout = TimeSpan.FromSeconds(timeoutSeconds is > 0 ? timeoutSeconds.Value : DefaultTimeoutSeconds);
        AnswerTokens = answerTokens is > 0 ? answerTokens.Value : DefaultAnswerTokens;
        Retires = retires;
        RetiresReadOn = retiresReadOn;
        Workspace = string.IsNullOrWhiteSpace(workspace) ? null : workspace.Trim();
        ApiKey = apiKey;
        Thinking = Thought(thinking, named, Options, profile);
    }

    // How deeply a Claude profile thinks: off, or an effort level the interface takes, or the provider's default
    // where none is named. A thinking token budget is not among them, because the models this build calls refuse
    // one with a 400, so depth is set by effort.
    // see: Research names a profile per section as well as per job, and a Claude profile states its thinking
    public const string ThinkingOff = "off";

    public static readonly string[] Efforts = ["low", "medium", "high", "xhigh", "max"];

    public string? Thinking { get; }

    static string? Thought(string? thinking, string format, string? options, string profile)
    {
        if (string.IsNullOrWhiteSpace(thinking))
        {
            return null;
        }

        var said = thinking.Trim().ToLowerInvariant();
        var key = ModelProfiles.Field(profile, ModelProfiles.ThinkingField);

        if (!string.Equals(format, AnthropicFormat, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"'{key}' is '{thinking}', and the profile's format is '{format}'. A thinking setting is sent in Claude's own " +
                "messages interface alone, so it is refused here rather than sent as something the provider does not take.");
        }

        if (said != ThinkingOff && !Efforts.Contains(said, StringComparer.Ordinal))
        {
            throw new InvalidOperationException(
                $"'{key}' is '{thinking}', and a profile's thinking is '{ThinkingOff}' or an effort level, one of " +
                $"{string.Join(", ", Efforts)}. A token budget is not one: the models this build calls refuse it with a 400.");
        }

        if (options is not null && JsonNode.Parse(options) is JsonObject set && set.ContainsKey("thinking"))
        {
            throw new InvalidOperationException(
                $"'{key}' is set and the profile's options set thinking too. One setting states a profile's thinking, so " +
                "the two are refused rather than one silently replacing the other.");
        }

        return said;
    }

    // The workspace a key not scoped to one names on every request, read from beside the key
    // in the secrets file, or none where the key carries its own.
    internal string? Workspace { get; }

    public string Job { get; }

    public string Profile { get; }

    public string KeyName { get; }

    // The earliest date the provider publishes for retiring the model, and the day that was
    // read, or none where the provider publishes none.
    public DateOnly? Retires { get; }

    public DateOnly? RetiresReadOn { get; }

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
    public string Identity =>
        Model
        + (Thinking is null ? string.Empty : Thinking == ThinkingOff ? " thinking off" : " effort " + Thinking)
        + (Options is null ? string.Empty : " " + Options);

    // Never rendered with the key, because a key that can be printed reaches a log.
    public override string ToString() => $"ResearchModelSettings({Job} on {Profile}: {Identity} at {BaseAddress}, key withheld)";

    static string? Parsed(string? options, string format, string key)
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
                $"'{key}' is not a JSON object. Options are the provider's own request fields, written as the " +
                "object the provider takes, and anything else would be sent as something nobody wrote.");
        }

        var owned = parsed.Select(field => field.Key).Where(field => OwnedFields(format).Contains(field, StringComparer.Ordinal)).ToArray();

        if (owned.Length > 0)
        {
            throw new InvalidOperationException(
                $"'{key}' sets {string.Join(", ", owned)}, which the feed writes itself from the request and the " +
                "settings. An option replacing one would be a call the recording key does not describe.");
        }

        return parsed.ToJsonString();
    }
}
