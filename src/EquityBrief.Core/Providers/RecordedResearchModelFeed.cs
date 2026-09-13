namespace EquityBrief.Core.Providers;

// The research model, answered from recorded responses.
//
// One file per call, named for the request's key, holding the provider's response byte
// for byte, read by the parser the live feed uses and priced at the configured rates,
// so a replay prices a call exactly as the live pass that recorded it did.
// see: A research pass is recorded per section call, keyed on the canonicalised request
//
// A request the recording does not hold refuses by name, and the double holds no
// client, so there is no network for it to reach instead.
public sealed class RecordedResearchModelFeed(string folder, ResearchModelSettings settings) : IResearchModelFeed
{
    public const string FilePrefix = "research-call-";

    public int Requests { get; private set; }

    public string Identity => settings.Identity;

    public IReadOnlyList<ModelRequest> Asked => asked;

    readonly List<ModelRequest> asked = [];

    public static string FileFor(ModelRequest request) => FilePrefix + request.Key + ".json";

    public Task<ResearchAnswer> CompleteAsync(ModelRequest request, CancellationToken cancellation = default)
    {
        Requests++;
        asked.Add(request);

        var file = Path.Combine(folder, FileFor(request));

        return File.Exists(file)
            ? Task.FromResult(OpenAiCompatibleResearchFeed.Parse(File.ReadAllText(file), request.Section))
            : throw new InvalidOperationException(
                $"No recording answers the {request.Lane} lane's call for {request.Section} on {request.Model}, keyed " +
                $"{request.Key}, in '{folder}'. The recording is keyed on the whole request, so a prompt, a document list " +
                "or an option that changed is a request nobody recorded, and it is refused here rather than answered by the " +
                "network or by a different recording.");
    }

    public decimal Price(ResearchAnswer answer) => settings.Pricing.Price(answer);

    public decimal Ceiling(ModelRequest request) => settings.Pricing.Ceiling(request, settings.AnswerTokens);
}
