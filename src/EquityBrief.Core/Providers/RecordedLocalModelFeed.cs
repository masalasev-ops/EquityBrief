namespace EquityBrief.Core.Providers;

// The local model, answered from recorded responses.
//
// One file per section call, named for the request's key, holding the response
// the runtime sent byte for byte. Read by the parser the live client uses, so a
// replay exercises the same reading a live call does.
// see: A research pass is recorded per section call, keyed on the canonicalised request
//
// A request the recording does not hold refuses by name rather than answering, and
// it holds no client, so there is no network for it to reach instead. A replay runs
// over a recording that should be complete, and a missing call answered with
// anything would read as the model having written it.
public sealed class RecordedLocalModelFeed(string folder) : ILocalModelFeed
{
    public const string FilePrefix = "model-section-";

    public int Requests { get; private set; }

    public IReadOnlyList<ModelRequest> Asked => asked;

    readonly List<ModelRequest> asked = [];

    public static string FileFor(ModelRequest request) => FilePrefix + request.Key + ".json";

    public Task<ModelAnswer> CompleteAsync(ModelRequest request, CancellationToken cancellation = default)
    {
        Requests++;
        asked.Add(request);

        var file = Path.Combine(folder, FileFor(request));

        return File.Exists(file)
            ? Task.FromResult(OpenAiCompatibleModelFeed.Parse(File.ReadAllText(file), request.Section))
            : throw new InvalidOperationException(
                $"No recording answers the {request.Lane} lane's call for {request.Section} on {request.Model}, " +
                $"keyed {request.Key}, in '{folder}'. The recording is keyed on the whole request, so a prompt or " +
                "a document list that changed is a request nobody recorded, and it is refused here rather than " +
                "answered by the network or by a different recording.");
    }
}
