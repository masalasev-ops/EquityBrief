using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace EquityBrief.Core.Providers;

// One section call to a model, and everything that decides what it answers.
//
// The recording key is a hash of all of it, which is the settled record format: a
// recording answers the request it was made for and nothing else, and it is keyed
// on the request rather than on the order calls were made, so a pass that writes
// its sections in a different order still replays.
// see: A research pass is recorded per section call, keyed on the canonicalised request
public sealed record ModelRequest(
    string Lane,
    string Section,
    string Model,
    IReadOnlyList<string> DocumentIds,
    string System,
    string Prompt)
{
    // Canonical JSON of the six fields in a stated order, hashed. The document ids
    // are in the order the prompt numbers them, because that order is part of what
    // the model was asked: D1 is a different claim from D2.
    public string Key =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new
        {
            lane = Lane,
            section = Section,
            model = Model,
            documents = DocumentIds,
            system = System,
            prompt = Prompt,
        }))))[..32];
}

// What a model answered, as the parser read it.
//
// `Model` is the identifier the endpoint says answered, which is what a section
// stores: a runtime that served a different model from the one asked for is a
// section whose author is the one that answered.
public sealed record ModelAnswer(string Model, string Text, int PromptTokens, int CompletionTokens, string FinishReason);

// The runtime did not answer at all: nothing listened at the address, or the
// connection failed before a response began.
//
// A type of its own rather than a refusal with particular words in it, because the
// prose writer stops asking for the rest of a pass on this and carries on past
// every other refusal, and a decision keyed on a message's wording is one that a
// reworded message changes with nothing failing.
public sealed class LocalModelUnavailable(string message) : Exception(message);

// The local lane's model, on the operator's own machine.
//
// Behind an interface for the reason every feed here is: the live client and the
// recorded double are one shape, so a pass replays from a recording with no
// network and the same parser reads both.
// see: The local model answers at an OpenAI-compatible endpoint, and which model answers is configuration
public interface ILocalModelFeed
{
    int Requests { get; }

    Task<ModelAnswer> CompleteAsync(ModelRequest request, CancellationToken cancellation = default);
}

// The local lane's configuration: where the model answers, which model, how long
// a call may take, how much context the machine holds, and never a key.
public sealed record LocalModelSettings
{
    public const string Section = "EquityBrief:Models:Local";
    public const string BaseAddressKey = Section + ":BaseAddress";
    public const string ModelKey = Section + ":Model";
    public const string TimeoutKey = Section + ":TimeoutSeconds";
    public const string ContextTokensKey = Section + ":ContextTokens";
    public const string ApiKeyKey = Section + ":ApiKey";

    // What this machine was measured with. LM Studio's own report of the loaded
    // model on 2026-09-13 gave a context of 50,176 tokens for qwen/qwen3.5-9b, and
    // the runtime's refusal of a longer prompt named the same figure. The seven
    // section calls recorded that day each answered in under four seconds, over
    // prompts of one to eight and a half thousand tokens, so the timeout is a bound
    // on a runtime that has hung rather than on one working through a section.
    public const string DefaultBaseAddress = "http://127.0.0.1:1234/v1/";
    public const string DefaultModel = "qwen/qwen3.5-9b";
    public const int DefaultTimeoutSeconds = 300;
    public const int DefaultContextTokens = 50176;

    public LocalModelSettings(string? baseAddress, string? model, int? timeoutSeconds, int? contextTokens, string? apiKey)
    {
        // A key for this lane is refused rather than sent. A local endpoint that
        // authenticates is an endpoint on somebody else's machine, and the queue's
        // zero-cost property rests on this lane being free.
        if (!string.IsNullOrWhiteSpace(apiKey))
        {
            throw new InvalidOperationException(
                $"A key is configured at '{ApiKeyKey}', and the local lane takes none. A local model that " +
                "asks for a key is a model on somebody else's machine, which would put a cost on the one lane " +
                "the overnight queue is allowed to call. Remove the key, or point this lane at the operator's " +
                "own runtime.");
        }

        BaseAddress = string.IsNullOrWhiteSpace(baseAddress) ? DefaultBaseAddress : baseAddress;
        Model = string.IsNullOrWhiteSpace(model) ? DefaultModel : model;
        Timeout = TimeSpan.FromSeconds(timeoutSeconds is > 0 ? timeoutSeconds.Value : DefaultTimeoutSeconds);
        ContextTokens = contextTokens is > 0 ? contextTokens.Value : DefaultContextTokens;
    }

    public string BaseAddress { get; }

    public string Model { get; }

    public TimeSpan Timeout { get; }

    public int ContextTokens { get; }
}
