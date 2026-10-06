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
// see: The local lane calls the one model its settings flag as the default, and a profile it cannot read is the local model unavailable
public interface ILocalModelFeed
{
    int Requests { get; }

    Task<ModelAnswer> CompleteAsync(ModelRequest request, CancellationToken cancellation = default);
}

// The local lane's configuration: the profile the settings flag as the default,
// where its model answers, which model, how long a call may take, how much context
// the model is loaded with, how long its load may take, and never a key. Every
// value is the settings' own and none has a default here, so a file naming no model
// calls none rather than one nobody chose.
// see: The local lane calls the one model its settings flag as the default, and a profile it cannot read is the local model unavailable
public sealed record LocalModelSettings
{
    public const string Section = "EquityBrief:Models:Local";
    public const string ProfilesKey = Section + ":Profiles";
    public const string ApiKeyKey = Section + ":ApiKey";

    // A profile's own keys, each under its word beneath the profiles.
    public const string BaseAddressName = "BaseAddress";
    public const string ModelName = "Model";
    public const string TimeoutName = "TimeoutSeconds";
    public const string ContextTokensName = "ContextTokens";
    public const string LoadName = "LoadSeconds";
    public const string IsDefaultName = "IsDefault";

    // The longest a call or a load may be given, a day: a bound past it is a slip in the file rather than a wait,
    // and three of it still fit the transport's own limit.
    public const int MostSeconds = 86400;

    public LocalModelSettings(string profile, string baseAddress, string model, int timeoutSeconds, int contextTokens, int loadSeconds)
    {
        BaseAddress = Address(profile, Stated(profile, BaseAddressName, baseAddress));
        Model = Stated(profile, ModelName, model);
        Timeout = TimeSpan.FromSeconds(Seconds(profile, TimeoutName, timeoutSeconds));
        ContextTokens = Whole(profile, ContextTokensName, contextTokens);
        Load = TimeSpan.FromSeconds(Seconds(profile, LoadName, loadSeconds));
        Profile = profile;
    }

    // The profile's word, as the settings name it.
    public string Profile { get; }

    public string BaseAddress { get; }

    public string Model { get; }

    public TimeSpan Timeout { get; }

    public int ContextTokens { get; }

    // How long the runtime may take to load the model before a pass calls it.
    public TimeSpan Load { get; }

    // Why the settings name no model the lane can call, where they do not: then every call is the local model
    // unavailable with this reason, and no other value is one the lane reads.
    public string? Unreadable { get; }

    // A lane whose profiles could not be read. It names no address or model, its context holds any section so
    // each is left for this reason rather than for its size, and its feed answers every call as unavailable.
    public static LocalModelSettings Unread(string reason) => new(reason);

    LocalModelSettings(string reason)
    {
        Profile = string.Empty;
        BaseAddress = string.Empty;
        Model = string.Empty;
        Timeout = TimeSpan.FromSeconds(1);
        ContextTokens = int.MaxValue;
        Load = TimeSpan.FromSeconds(1);
        Unreadable = reason;
    }

    // A profile's key as the settings file spells it.
    public static string KeyOf(string profile, string name) => $"{ProfilesKey}:{profile}:{name}";

    static string Stated(string profile, string name, string value) =>
        string.IsNullOrWhiteSpace(value)
            ? throw new InvalidOperationException(
                $"'{KeyOf(profile, name)}' is blank, and the local lane reads it from the settings alone. Set it in the profile.")
            : value;

    static int Whole(string profile, string name, int value) =>
        value > 0
            ? value
            : throw new InvalidOperationException(
                $"'{KeyOf(profile, name)}' is {value}, which is not a whole number above zero.");

    static int Seconds(string profile, string name, int value) =>
        Whole(profile, name, value) <= MostSeconds
            ? value
            : throw new InvalidOperationException(
                $"'{KeyOf(profile, name)}' is {value} seconds, more than a day, the longest the lane waits.");

    // An address the runtime can be asked at: absolute, over HTTP.
    static string Address(string profile, string value) =>
        Uri.TryCreate(value, UriKind.Absolute, out var address) && (address.Scheme == Uri.UriSchemeHttp || address.Scheme == Uri.UriSchemeHttps)
            ? value
            : throw new InvalidOperationException(
                $"'{KeyOf(profile, BaseAddressName)}' is '{value}', which is not an address over HTTP such as http://127.0.0.1:1234/v1/.");
}
