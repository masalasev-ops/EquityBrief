using System.Globalization;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace EquityBrief.Core.Providers;

// The local model, over the OpenAI-compatible chat completions endpoint the
// operator's runtime serves.
//
// The interface LM Studio, llama.cpp's server and Ollama all serve, so the runtime
// is the operator's choice. What this was written against is two responses
// captured from LM Studio serving qwen/qwen3.5-9b before a line of it existed, and
// both of them are why it is shaped the way it is.
// see: The local lane calls the one model its settings flag as the default, and a profile it cannot read is the local model unavailable
// see: The local lane loads its model at its profile's context before its first call of a night or a pass, and loads again a model held at a smaller one
public sealed class OpenAiCompatibleModelFeed(
    HttpClient client,
    LocalModelSettings settings,
    ProviderRequest? request = null) : ILocalModelFeed
{
    // One attempt. A model call that timed out has already spent its time on the
    // operator's card, and a retry spends it again for the same answer.
    readonly ProviderRequest request = request
        ?? new ProviderRequest(new RetryPolicy(1, TimeSpan.Zero, settings.Timeout, settings.Timeout + settings.Timeout));

    // The first call to a model that may still be loading, bounded by the load's allowance as well as its own.
    readonly ProviderRequest whileLoading = request
        ?? new ProviderRequest(new RetryPolicy(1, TimeSpan.Zero, settings.Timeout + settings.Load, settings.Timeout + settings.Load + settings.Timeout));

    public const string Path = "chat/completions";

    // LM Studio's own interface to its models, beside the OpenAI-compatible one the lane's address names.
    public const string ModelsPath = "../api/v1/models";

    public const string LoadPath = "../api/v1/models/load";

    public const string UnloadPath = "../api/v1/models/unload";

    // The word LM Studio's load answers with once the model is ready.
    public const string LoadedStatus = "loaded";

    // Whether the runtime has been read for this feed's model, whether the next call may still be waiting on its
    // load, and why the model is unavailable where reading it found so, which every later call answers with
    // rather than reading and loading again.
    bool read;

    bool loading;

    string? failed;

    // The answer budget of one section call. A section is a paragraph or a sentence
    // per business unit, and a thousand tokens holds several of either.
    public const int AnswerTokens = 1024;

    public int Requests { get; private set; }

    public static OpenAiCompatibleModelFeed Live(LocalModelSettings settings) =>
        settings.Unreadable is not null
            ? new(new HttpClient(), settings)
            : new(
                new HttpClient
                {
                    BaseAddress = new Uri(settings.BaseAddress.EndsWith('/') ? settings.BaseAddress : settings.BaseAddress + "/"),
                    Timeout = settings.Load + settings.Timeout + settings.Timeout,
                },
                settings);

    public async Task<ModelAnswer> CompleteAsync(ModelRequest wanted, CancellationToken cancellation = default)
    {
        Requests++;

        // Settings naming no model the lane can call reach no runtime: the call is the local model unavailable
        // with why, so the pass and the overnight queue leave the lane's sections with it and the night goes on.
        if (settings.Unreadable is { } unread)
        {
            throw new LocalModelUnavailable($"The local model is not called: {unread}");
        }

        if (failed is { } why)
        {
            throw new LocalModelUnavailable(why);
        }

        if (!read)
        {
            try
            {
                loading = !await ReadyAsync(wanted.Section, cancellation).ConfigureAwait(false);
            }
            catch (LocalModelUnavailable gone)
            {
                failed = gone.Message;

                throw;
            }

            read = true;
        }

        var waiting = loading;
        var bound = waiting ? settings.Timeout + settings.Load : settings.Timeout;

        loading = false;

        var body = await (waiting ? whileLoading : request).SendAsync(
            async token =>
            {
                try
                {
                    using var content = new StringContent(Body(wanted), Encoding.UTF8, "application/json");
                    using var response = await client.PostAsync(Path, content, token).ConfigureAwait(false);

                    var text = await response.Content.ReadAsStringAsync(token).ConfigureAwait(false);

                    return response.IsSuccessStatusCode
                        ? text
                        : throw new ProviderRefusal(Refused(wanted.Section, (int)response.StatusCode, text), transient: false);
                }
                catch (HttpRequestException unreachable)
                {
                    // The runtime not answering at all, which is section 18's local
                    // model unavailable: every section still to be asked for in the
                    // pass is left unwritten with this reason.
                    throw new LocalModelUnavailable(
                        $"The local model could not be reached for {wanted.Section}: {unreachable.Message}");
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested && !cancellation.IsCancellationRequested)
                {
                    // The call's own bound, rather than the caller giving up. A model
                    // that took longer than the timeout over one section may answer the
                    // next, so this is a refusal of the section and not of the runtime.
                    throw new ProviderRefusal(
                        waiting
                            ? $"The local model did not answer {wanted.Section} within {bound.TotalSeconds.ToString(CultureInfo.InvariantCulture)} seconds, its load included."
                            : $"The local model did not answer {wanted.Section} within {bound.TotalSeconds.ToString(CultureInfo.InvariantCulture)} seconds.",
                        transient: false);
                }
            },
            cancellation).ConfigureAwait(false);

        return Parse(body, wanted.Section);
    }

    // Whether the model is ready before the first call, read off LM Studio's own interface. A model the
    // runtime holds and has not loaded, or holds loaded at a smaller context than the profile names, is loaded
    // at the profile's context, any other model loaded and its own smaller instance first unloaded so the
    // runtime holds one at a time, and the load is waited for within the profile's allowance, so a pass never
    // calls a model mid-load or one that cannot hold the prompts the lane plans. Read once a feed, before the
    // first call of a pass or of a night's overnight queue. True once the load answers that the model is
    // loaded. False where the model is already listed at the profile's context or more, or at one the list
    // does not state, which it is from the moment a load starts, or where the runtime states no load state,
    // being another runtime or not answering the list in time: its first call then carries the allowance. A
    // model the runtime does not hold, a load it refuses and one past the allowance are the local model
    // unavailable, so every section is left unwritten with why and no later call reads or loads again.
    async Task<bool> ReadyAsync(string section, CancellationToken cancellation)
    {
        var listed = await AskAsync(HttpMethod.Get, ModelsPath, null, settings.Timeout, section, cancellation).ConfigureAwait(false);

        if (listed is not { Succeeded: true } answered || Held(answered.Text, settings.Model) is not { } held)
        {
            return false;
        }

        if (!held.Listed)
        {
            throw new LocalModelUnavailable(
                $"The runtime holds no model named {settings.Model}, which the local profile {settings.Profile} names, so the lane's sections are not written.");
        }

        if (held.Loaded && (held.Context is not { } context || context >= settings.ContextTokens))
        {
            return false;
        }

        foreach (var other in held.Others.Concat(held.Instances))
        {
            var unloaded = await AskAsync(HttpMethod.Post, UnloadPath, JsonSerializer.Serialize(new { instance_id = other }), settings.Timeout, section, cancellation).ConfigureAwait(false);

            if (unloaded is not { Succeeded: true })
            {
                throw new LocalModelUnavailable(
                    $"The runtime would not unload {other} to load {settings.Model}: {(unloaded is { } said ? Said(said.Text) : "it did not answer in time.")}");
            }
        }

        var loaded = await AskAsync(
            HttpMethod.Post,
            LoadPath,
            JsonSerializer.Serialize(new { model = settings.Model, context_length = settings.ContextTokens }),
            settings.Load,
            section,
            cancellation).ConfigureAwait(false);

        return loaded switch
        {
            null => throw new LocalModelUnavailable(
                $"The local model {settings.Model} did not finish loading within {settings.Load.TotalSeconds.ToString(CultureInfo.InvariantCulture)} seconds, so the lane's sections are not written."),
            { Succeeded: false } refused => throw new LocalModelUnavailable(
                $"The runtime refused to load {settings.Model}: {Said(refused.Text)}"),
            { } done => LoadedFrom(done.Text),
        };
    }

    // One request to the runtime's own interface within its bound: whether it succeeded with what it said,
    // or none where the bound passed first. Nothing listening is the local model unavailable.
    async Task<(bool Succeeded, string Text)?> AskAsync(HttpMethod method, string path, string? json, TimeSpan bound, string section, CancellationToken cancellation)
    {
        using var limit = CancellationTokenSource.CreateLinkedTokenSource(cancellation);

        limit.CancelAfter(bound);

        try
        {
            using var message = new HttpRequestMessage(method, path);

            if (json is not null)
            {
                message.Content = new StringContent(json, Encoding.UTF8, "application/json");
            }

            using var response = await client.SendAsync(message, limit.Token).ConfigureAwait(false);

            return (response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync(limit.Token).ConfigureAwait(false));
        }
        catch (HttpRequestException unreachable)
        {
            throw new LocalModelUnavailable($"The local model could not be reached for {section}: {unreachable.Message}");
        }
        catch (OperationCanceledException) when (!cancellation.IsCancellationRequested)
        {
            return null;
        }
    }

    // A model as the runtime's list holds it: whether it is listed, its own instances loaded or loading, the
    // smallest context any of them states, none where none states one, and every other model's loaded
    // instance, so the runtime is left holding one. None where the answer is not LM Studio's list, which is a
    // runtime stating no load state.
    public sealed record RuntimeModels(bool Listed, IReadOnlyList<string> Instances, int? Context, IReadOnlyList<string> Others)
    {
        public bool Loaded => Instances.Count > 0;
    }

    public static RuntimeModels? Held(string json, string model)
    {
        try
        {
            using var document = JsonDocument.Parse(json);

            if (document.RootElement.ValueKind != JsonValueKind.Object
                || !document.RootElement.TryGetProperty("models", out var models)
                || models.ValueKind != JsonValueKind.Array)
            {
                return null;
            }

            var listed = false;
            string[] own = [];
            int? context = null;
            var others = new List<string>();

            foreach (var entry in models.EnumerateArray())
            {
                var key = entry.TryGetProperty("key", out var named) && named.ValueKind == JsonValueKind.String ? named.GetString() : null;
                JsonElement[] loaded = entry.TryGetProperty("loaded_instances", out var held) && held.ValueKind == JsonValueKind.Array
                    ? [.. held.EnumerateArray().Where(instance => instance.TryGetProperty("id", out var id) && id.ValueKind == JsonValueKind.String && id.GetString()!.Length > 0)]
                    : [];
                string[] instances = [.. loaded.Select(instance => instance.GetProperty("id").GetString()!)];

                if (string.Equals(key, model, StringComparison.Ordinal))
                {
                    listed = true;
                    own = instances;
                    context = loaded
                        .Select(instance => instance.TryGetProperty("config", out var config) && config.ValueKind == JsonValueKind.Object
                            && config.TryGetProperty("context_length", out var length) && length.ValueKind == JsonValueKind.Number && length.TryGetInt32(out var tokens)
                                ? tokens
                                : (int?)null)
                        .Min();
                }
                else
                {
                    others.AddRange(instances);
                }
            }

            return new RuntimeModels(listed, own, context, others);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    // Whether a load's answer says the model is loaded; any other answer leaves its first call the allowance.
    static bool LoadedFrom(string answer)
    {
        try
        {
            using var document = JsonDocument.Parse(answer);

            return document.RootElement.ValueKind == JsonValueKind.Object
                && document.RootElement.TryGetProperty("status", out var status)
                && status.ValueKind == JsonValueKind.String
                && string.Equals(status.GetString(), LoadedStatus, StringComparison.Ordinal);
        }
        catch (JsonException)
        {
            return false;
        }
    }

    // The runtime's own words on a refusal of its interface, cut to a line.
    static string Said(string? body)
    {
        var said = string.Empty;

        try
        {
            using var document = JsonDocument.Parse(body ?? string.Empty);

            if (document.RootElement.ValueKind == JsonValueKind.Object && document.RootElement.TryGetProperty("error", out var error))
            {
                said = error.ValueKind switch
                {
                    JsonValueKind.String => error.GetString()!,
                    JsonValueKind.Object when error.TryGetProperty("message", out var words) && words.ValueKind == JsonValueKind.String => words.GetString()!,
                    _ => string.Empty,
                };
            }
        }
        catch (JsonException)
        {
        }

        said = said.Trim();

        return said.Length == 0 ? "it gave no reason." : (said.Length > RefusalExcerpt ? said[..RefusalExcerpt] : said);
    }

    // The request as it is sent.
    //
    // `reasoning_effort` is none because the captured model is a thinking model:
    // without it, a one-sentence request spent its whole budget reasoning and came
    // back with an empty answer. Of four ways to turn reasoning off on the runtime
    // it was captured from, this field is the one that did; the other three were
    // accepted and ignored. Temperature zero, so a pass over one evidence set is as
    // repeatable as the runtime makes it, which is what a recording keyed on the
    // request assumes.
    public static string Body(ModelRequest wanted) =>
        JsonSerializer.Serialize(new
        {
            model = wanted.Model,
            messages = new object[]
            {
                new { role = "system", content = wanted.System },
                new { role = "user", content = wanted.Prompt },
            },
            temperature = 0,
            max_tokens = AnswerTokens,
            stream = false,
            reasoning_effort = "none",
        });

    // The finish reason a runtime gives an answer that ran out of budget.
    public const string CutOff = "length";

    // The longest stretch of a runtime's own refusal carried into a reason.
    const int RefusalExcerpt = 300;

    // A refusal from the runtime, with its own words where it sent any.
    //
    // Read as the overflow capture shows it arrives: a 400 whose `error` is a string
    // wrapping the engine's message, which names the prompt's token count and the
    // loaded context. The OpenAI shape, an object carrying `message`, is read too,
    // because the runtime is the operator's choice. What reaches the run log is the
    // message and not the body, cut to a length a line can hold.
    public static string Refused(string section, int status, string body)
    {
        var said = string.Empty;

        try
        {
            using var document = JsonDocument.Parse(body);

            if (document.RootElement.ValueKind == JsonValueKind.Object
                && document.RootElement.TryGetProperty("error", out var error))
            {
                said = error.ValueKind switch
                {
                    JsonValueKind.String => error.GetString()!,
                    JsonValueKind.Object when error.TryGetProperty("message", out var words) && words.ValueKind == JsonValueKind.String => words.GetString()!,
                    _ => string.Empty,
                };
            }
        }
        catch (JsonException)
        {
            // A body that is not JSON is stated as absent rather than quoted, since
            // what a runtime sends in its place is a page nobody wrote for a log.
        }

        said = said.Trim();

        return said.Length == 0
            ? $"The local model refused {section} with status {status}."
            : $"The local model refused {section} with status {status}: {(said.Length > RefusalExcerpt ? said[..RefusalExcerpt] : said)}";
    }

    // A response read as the captures showed it arrives.
    //
    // The answer is `content`. An empty one is refused rather than returned, and the
    // refusal says why, because the captured thinking response is exactly that: a
    // budget spent in `reasoning_content`, nothing in `content`, and a finish reason
    // of length. Returned as text it would be stored as a section with no prose. A
    // runtime that inlines its reasoning in the answer between think tags has it
    // taken out, because a reader of the section is owed the answer and not the
    // working.
    public static ModelAnswer Parse(string json, string section)
    {
        using var document = JsonDocument.Parse(json);

        var root = document.RootElement;

        if (!root.TryGetProperty("choices", out var choices)
            || choices.ValueKind != JsonValueKind.Array
            || choices.GetArrayLength() == 0)
        {
            throw new ProviderRefusal(
                $"The local model's response for {section} carries no choices, so there is no answer to store.",
                transient: false);
        }

        var choice = choices[0];
        var message = choice.GetProperty("message");
        var finish = choice.TryGetProperty("finish_reason", out var reason) && reason.ValueKind == JsonValueKind.String
            ? reason.GetString()!
            : "unstated";

        var text = message.TryGetProperty("content", out var content) && content.ValueKind == JsonValueKind.String
            ? AnswerText.Visible(Regex.Replace(content.GetString()!, @"<think>[\s\S]*?</think>", string.Empty))
            : string.Empty;

        var usage = root.TryGetProperty("usage", out var used) ? used : default;

        int Tokens(string name) =>
            usage.ValueKind == JsonValueKind.Object && usage.TryGetProperty(name, out var count) && count.ValueKind == JsonValueKind.Number
                ? count.GetInt32()
                : 0;

        if (text.Length == 0)
        {
            var reasoning = message.TryGetProperty("reasoning_content", out var worked) && worked.ValueKind == JsonValueKind.String
                ? worked.GetString()!.Length
                : 0;

            throw new ProviderRefusal(
                $"The local model returned no answer for {section}: the finish reason was {finish} after " +
                $"{Tokens("completion_tokens")} completion token(s), and {reasoning} character(s) of reasoning " +
                "arrived with nothing in the answer. A section stored from this would be empty.",
                transient: false,
                unusable: true);
        }

        // An answer that stopped on its budget rather than on its own end was cut,
        // and a section cut mid-sentence can pass the claim checker, because every
        // figure and citation in the part that arrived may be sound. Refused, where
        // the thinking capture's empty answer is refused above with its own reason.
        if (string.Equals(finish, CutOff, StringComparison.Ordinal))
        {
            throw new ProviderRefusal(
                $"The local model's answer for {section} stopped at its budget of {AnswerTokens} tokens rather than " +
                "ending, so what arrived is a section cut short and it is not stored.",
                transient: false,
                unusable: true);
        }

        return new ModelAnswer(
            root.TryGetProperty("model", out var model) && model.ValueKind == JsonValueKind.String ? model.GetString()! : "unstated",
            text,
            Tokens("prompt_tokens"),
            Tokens("completion_tokens"),
            finish);
    }
}
