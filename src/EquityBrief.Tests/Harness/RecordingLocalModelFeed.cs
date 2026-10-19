using System.Text;
using EquityBrief.Core.Providers;

namespace EquityBrief.Tests.Harness;

// The local model asked live, each answer kept byte for byte under its request's key in a folder, for a comparison the
// operator records by hand: the live feed over a client whose handler sends each completion to LM Studio's native chat
// endpoint and keeps the body it is answered with, written once the call returns or refuses, so a replay through the
// recorded feed reads the same answers and the same refusals. A request asked again in the same run is answered from its first recording and not asked again, as the
// replay answers it, since the runtime can answer one request two ways and a recording keyed on the request holds one.
// A run that names no recording never constructs one, so the suite reaches no runtime.
internal sealed class RecordingLocalModelFeed : ILocalModelFeed
{
    readonly Keeping keeping;
    readonly OpenAiCompatibleModelFeed live;
    readonly string folder;

    // The runtime is reached through the machine's own handler, or through the one a test hands in.
    public RecordingLocalModelFeed(LocalModelSettings settings, string folder, HttpMessageHandler? runtime = null)
    {
        this.folder = folder;

        var address = new Uri(settings.BaseAddress.EndsWith('/') ? settings.BaseAddress : settings.BaseAddress + "/");

        keeping = new Keeping { InnerHandler = runtime ?? new HttpClientHandler(), Native = new Uri(address, LmStudioReadingFeed.ChatPath) };
        live = new OpenAiCompatibleModelFeed(
            new HttpClient(keeping)
            {
                BaseAddress = address,
                Timeout = settings.Load + settings.Timeout + settings.Timeout,
            },
            settings);
    }

    // Every call, those answered from a recording this run made among them, as the recorded feed counts them.
    public int Requests { get; private set; }

    // Each recording written, by its file, with the section it answered.
    public List<(string File, string Section)> Kept { get; } = [];

    public async Task<ModelAnswer> CompleteAsync(ModelRequest request, CancellationToken cancellation = default)
    {
        Requests++;

        if (Kept.Any(kept => kept.File == RecordedLocalModelFeed.FileFor(request)))
        {
            return OpenAiCompatibleModelFeed.Parse(File.ReadAllText(Path.Combine(folder, RecordedLocalModelFeed.FileFor(request))), request.Section);
        }

        keeping.Body = null;

        try
        {
            return await live.CompleteAsync(request, cancellation);
        }
        finally
        {
            if (keeping.Body is { } body)
            {
                var file = RecordedLocalModelFeed.FileFor(request);

                Directory.CreateDirectory(folder);
                File.WriteAllText(Path.Combine(folder, file), body);
                Kept.Add((file, request.Section));
            }
        }
    }

    // Sends a completion to LM Studio's native chat endpoint, the one the release reading asks, with the request as the
    // lane builds it, since on the OpenAI-compatible endpoint the runtime answers a request for prose with a JSON object;
    // keeps the body it is answered with, and hands the caller the same bytes.
    sealed class Keeping : DelegatingHandler
    {
        public string? Body { get; set; }

        // The native endpoint as the release reading's feed resolves it against the lane's address.
        public required Uri Native { get; init; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var completion = request.RequestUri!.AbsolutePath.EndsWith("/" + OpenAiCompatibleModelFeed.Path, StringComparison.Ordinal);

            if (completion)
            {
                request.RequestUri = Native;
            }

            var response = await base.SendAsync(request, cancellationToken);

            if (completion)
            {
                Body = await response.Content.ReadAsStringAsync(cancellationToken);
                response.Content = new StringContent(Body, Encoding.UTF8, "application/json");
            }

            return response;
        }
    }
}
