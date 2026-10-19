using System.Text;
using EquityBrief.Core.Providers;

namespace EquityBrief.Tests.Harness;

// The local model asked live, each answer kept byte for byte under its request's key in a folder, for a comparison the
// operator records by hand: the live feed over a client whose handler keeps the body of each completion it is answered
// with, written once the call returns or refuses, so a replay through the recorded feed reads the same answers and the
// same refusals. A request asked again in the same run is answered from its first recording and not asked again, as the
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
        keeping = new Keeping { InnerHandler = runtime ?? new HttpClientHandler() };
        live = new OpenAiCompatibleModelFeed(
            new HttpClient(keeping)
            {
                BaseAddress = new Uri(settings.BaseAddress.EndsWith('/') ? settings.BaseAddress : settings.BaseAddress + "/"),
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

    // Keeps the body of the completion a call is answered with, and hands the caller the same bytes.
    sealed class Keeping : DelegatingHandler
    {
        public string? Body { get; set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var response = await base.SendAsync(request, cancellationToken);

            if (request.RequestUri!.AbsolutePath.EndsWith("/" + OpenAiCompatibleModelFeed.Path, StringComparison.Ordinal))
            {
                Body = await response.Content.ReadAsStringAsync(cancellationToken);
                response.Content = new StringContent(Body, Encoding.UTF8, "application/json");
            }

            return response;
        }
    }
}
