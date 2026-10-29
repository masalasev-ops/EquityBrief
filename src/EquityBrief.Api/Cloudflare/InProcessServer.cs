using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Http.Features;

namespace EquityBrief.Api.Cloudflare;

// The server the read surface runs on when it writes the read-only copy: no socket and no port, each screen the copy holds
// handed to the surface's own request pipeline inside its own process and its answer read back, so the copy is drawn by
// the code that draws the live page and asks nothing of any network.
// see: A read-only copy of the site is published to Cloudflare Pages from the read surface's own screens with no control, a link it does not hold drawn as plain text
public sealed class InProcessServer : IServer
{
    Func<string, Task<(int Status, string Body)>>? answer;

    public IFeatureCollection Features { get; } = new FeatureCollection();

    public Task StartAsync<TContext>(IHttpApplication<TContext> application, CancellationToken cancellationToken)
        where TContext : notnull
    {
        answer = async route =>
        {
            var cut = route.IndexOf('?', StringComparison.Ordinal);
            var features = new FeatureCollection();
            using var body = new MemoryStream();
            var response = new HttpResponseFeature();

            features.Set<IHttpRequestFeature>(new HttpRequestFeature
            {
                Method = "GET",
                Scheme = "http",
                Protocol = "HTTP/1.1",
                Path = cut < 0 ? route : route[..cut],
                QueryString = cut < 0 ? string.Empty : route[cut..],
                Headers = new Microsoft.AspNetCore.Http.HeaderDictionary { ["Host"] = "copy.invalid" },
                Body = Stream.Null,
            });
            features.Set<IHttpResponseFeature>(response);
            features.Set<IHttpResponseBodyFeature>(new Microsoft.AspNetCore.Http.StreamResponseBodyFeature(body));

            var context = application.CreateContext(features);

            try
            {
                await application.ProcessRequestAsync(context).ConfigureAwait(false);
            }
            catch (Exception failed)
            {
                application.DisposeContext(context, failed);

                throw;
            }

            application.DisposeContext(context, null);

            return (response.StatusCode, System.Text.Encoding.UTF8.GetString(body.ToArray()));
        };

        return Task.CompletedTask;
    }

    // One route's answer, as the live shell would read it.
    public Task<(int Status, string Body)> AskAsync(string route) =>
        answer?.Invoke(route) ?? throw new InvalidOperationException("The surface has not started on the in-process server.");

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public void Dispose()
    {
    }
}
