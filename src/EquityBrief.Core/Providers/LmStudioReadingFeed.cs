using System.Diagnostics;
using System.Globalization;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using EquityBrief.Core.Releases;

namespace EquityBrief.Core.Providers;

// One reading asked of the local model: the model, the instruction, the document's text, the seed and the answer's
// budget, at temperature zero.
public sealed record ReadingRequest(string Model, string Instruction, string Text, int Seed, int AnswerTokens);

// What the runtime answered for one reading, as its native answer states it: the model that answered, the answer's
// text, the reason it finished, its tokens, the runtime's name and version, and the seconds the call took.
public sealed record ReadingReply(
    string? Model,
    string? Content,
    string FinishReason,
    int PromptTokens,
    int CompletionTokens,
    int ReasoningTokens,
    string? RuntimeName,
    string? RuntimeVersion,
    double Seconds);

// A reading the runtime refused, or did not answer in time: the cause the answer is stored as refused with and the
// runtime's own words. Not the runtime gone, which ends the run.
public sealed class ReadingRefused(string cause, string message) : Exception(message)
{
    public string Cause { get; } = cause;
}

// The runtime gone for the run: LM Studio not running, the model it names not held, or the model not loaded, each
// what the run's own row says it came to.
public sealed class ReadingRuntimeUnavailable(string outcome, string message) : Exception(message)
{
    public const string NotRunning = "runtime not running";
    public const string ModelNotHeld = "model not held";
    public const string NotLoaded = "model not loaded";

    public string Outcome { get; } = outcome;
}

// The local model asked for readings, behind an interface so a test answers it as the runtime would.
// see: The local run is a process of its own at two slots outside the night and refuses any model file, runtime or prompt but its pin
public interface IReadingModelFeed
{
    int Requests { get; }

    // Makes the model ready before the first reading: true where this feed loaded it, false where it was loaded already.
    Task<bool> PrepareAsync(CancellationToken cancellation = default);

    // Unloads the model where this feed loaded it, so the runtime is left as the run found it.
    Task ReleaseAsync(bool loadedHere, CancellationToken cancellation = default);

    Task<ReadingReply> AskAsync(ReadingRequest request, CancellationToken cancellation = default);
}

// LM Studio's native chat answer, which states the model that answered, the runtime's name and version and the
// seconds it generated for on every answer, where its OpenAI-compatible answer states neither. Written against the
// answers captured from the operator's runtime on 2026-10-18: with reasoning_effort none every answer spent no
// reasoning token and stated its runtime; a request naming a model the runtime does not hold is answered by whatever
// model it has loaded, which is why the answering model is read rather than assumed; and a prompt past the loaded
// context is refused with a 400 naming the context. The model is listed, loaded and unloaded through LM Studio's own
// interface as the report lane's is, at its profile's context and one model at a time, and unloaded at the end only
// where this feed loaded it.
public sealed class LmStudioReadingFeed(HttpClient client, LocalModelSettings settings) : IReadingModelFeed
{
    // The native chat answer, beside the OpenAI-compatible address the profile names.
    public const string ChatPath = "../api/v0/chat/completions";

    public int Requests { get; private set; }

    public static LmStudioReadingFeed Live(LocalModelSettings settings) =>
        new(
            new HttpClient
            {
                BaseAddress = new Uri(settings.BaseAddress.EndsWith('/') ? settings.BaseAddress : settings.BaseAddress + "/"),
                Timeout = settings.Load + settings.Timeout + settings.Timeout,
            },
            settings);

    public async Task<bool> PrepareAsync(CancellationToken cancellation = default)
    {
        var listed = await AskRuntimeAsync(HttpMethod.Get, OpenAiCompatibleModelFeed.ModelsPath, null, settings.Timeout, cancellation).ConfigureAwait(false);

        if (listed is not { Succeeded: true } answered || OpenAiCompatibleModelFeed.Held(answered.Text, settings.Model) is not { } held)
        {
            throw new ReadingRuntimeUnavailable(ReadingRuntimeUnavailable.NotRunning, $"LM Studio did not list its models at {settings.BaseAddress}.");
        }

        if (!held.Listed)
        {
            throw new ReadingRuntimeUnavailable(ReadingRuntimeUnavailable.ModelNotHeld, $"LM Studio holds no model named {settings.Model}, which the local profile {settings.Profile} names.");
        }

        if (held.Loaded && (held.Context is not { } context || context >= settings.ContextTokens))
        {
            return false;
        }

        foreach (var other in held.Others.Concat(held.Instances))
        {
            var unloaded = await AskRuntimeAsync(HttpMethod.Post, OpenAiCompatibleModelFeed.UnloadPath, JsonSerializer.Serialize(new { instance_id = other }), settings.Timeout, cancellation).ConfigureAwait(false);

            if (unloaded is not { Succeeded: true })
            {
                throw new ReadingRuntimeUnavailable(ReadingRuntimeUnavailable.NotLoaded, $"LM Studio would not unload {other} to load {settings.Model}.");
            }
        }

        var loaded = await AskRuntimeAsync(
            HttpMethod.Post,
            OpenAiCompatibleModelFeed.LoadPath,
            JsonSerializer.Serialize(new { model = settings.Model, context_length = settings.ContextTokens }),
            settings.Load,
            cancellation).ConfigureAwait(false);

        return loaded switch
        {
            null => throw new ReadingRuntimeUnavailable(ReadingRuntimeUnavailable.NotLoaded, $"{settings.Model} did not finish loading within {Seconds(settings.Load)} seconds."),
            { Succeeded: false } => throw new ReadingRuntimeUnavailable(ReadingRuntimeUnavailable.NotLoaded, $"LM Studio refused to load {settings.Model}."),
            _ => true,
        };
    }

    public async Task ReleaseAsync(bool loadedHere, CancellationToken cancellation = default)
    {
        if (loadedHere)
        {
            await AskRuntimeAsync(HttpMethod.Post, OpenAiCompatibleModelFeed.UnloadPath, JsonSerializer.Serialize(new { instance_id = settings.Model }), settings.Timeout, cancellation).ConfigureAwait(false);
        }
    }

    public async Task<ReadingReply> AskAsync(ReadingRequest request, CancellationToken cancellation = default)
    {
        Requests++;

        using var limit = CancellationTokenSource.CreateLinkedTokenSource(cancellation);

        limit.CancelAfter(settings.Timeout);

        var watch = Stopwatch.StartNew();

        try
        {
            using var content = new StringContent(Body(request), Encoding.UTF8, "application/json");
            using var response = await client.PostAsync(ChatPath, content, limit.Token).ConfigureAwait(false);
            var text = await response.Content.ReadAsStringAsync(limit.Token).ConfigureAwait(false);

            return response.IsSuccessStatusCode
                ? Parse(text, watch.Elapsed.TotalSeconds)
                : throw new ReadingRefused(ReleaseAnswers.RuntimeRefused, OpenAiCompatibleModelFeed.Refused("the reading", (int)response.StatusCode, text));
        }
        catch (HttpRequestException unreachable)
        {
            throw new ReadingRuntimeUnavailable(ReadingRuntimeUnavailable.NotRunning, $"LM Studio could not be reached at {settings.BaseAddress}: {unreachable.Message}");
        }
        catch (OperationCanceledException) when (!cancellation.IsCancellationRequested)
        {
            throw new ReadingRefused(ReleaseAnswers.NotAnsweredInTime, $"The local model did not answer within {Seconds(settings.Timeout)} seconds.");
        }
    }

    // The request as it is sent: the instruction and the document, temperature zero, the seed, the answer's budget, no
    // stream, and reasoning off.
    public static string Body(ReadingRequest request) =>
        JsonSerializer.Serialize(new
        {
            model = request.Model,
            messages = new object[]
            {
                new { role = "system", content = request.Instruction },
                new { role = "user", content = request.Text },
            },
            temperature = ReadingSettings.Temperature,
            seed = request.Seed,
            max_tokens = request.AnswerTokens,
            stream = false,
            reasoning_effort = "none",
        });

    // The native answer as the captures show it arrives: the answering model at the top, the answer's text and its
    // finish reason in the first choice, the tokens under the usage, and the runtime's name and version.
    public static ReadingReply Parse(string json, double seconds)
    {
        using var document = JsonDocument.Parse(json);

        var root = document.RootElement;
        var choice = root.TryGetProperty("choices", out var choices) && choices.ValueKind == JsonValueKind.Array && choices.GetArrayLength() > 0
            ? choices[0]
            : default;
        var message = choice.ValueKind == JsonValueKind.Object && choice.TryGetProperty("message", out var held) ? held : default;
        var usage = root.TryGetProperty("usage", out var used) ? used : default;
        var details = usage.ValueKind == JsonValueKind.Object && usage.TryGetProperty("completion_tokens_details", out var detail) ? detail : default;
        var runtime = root.TryGetProperty("runtime", out var ran) ? ran : default;

        return new ReadingReply(
            Text(root, "model"),
            Text(message, "content"),
            Text(choice, "finish_reason") ?? "unstated",
            Tokens(usage, "prompt_tokens"),
            Tokens(usage, "completion_tokens"),
            Tokens(details, "reasoning_tokens"),
            Text(runtime, "name"),
            Text(runtime, "version"),
            seconds);
    }

    static int Tokens(JsonElement holder, string name) =>
        holder.ValueKind == JsonValueKind.Object && holder.TryGetProperty(name, out var count) && count.ValueKind == JsonValueKind.Number ? count.GetInt32() : 0;

    static string? Text(JsonElement holder, string name) =>
        holder.ValueKind == JsonValueKind.Object && holder.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    static string Seconds(TimeSpan span) => span.TotalSeconds.ToString(CultureInfo.InvariantCulture);

    async Task<(bool Succeeded, string Text)?> AskRuntimeAsync(HttpMethod method, string path, string? json, TimeSpan bound, CancellationToken cancellation)
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
            throw new ReadingRuntimeUnavailable(ReadingRuntimeUnavailable.NotRunning, $"LM Studio is not running at {settings.BaseAddress}: {unreachable.Message}");
        }
        catch (OperationCanceledException) when (!cancellation.IsCancellationRequested)
        {
            return null;
        }
    }
}
