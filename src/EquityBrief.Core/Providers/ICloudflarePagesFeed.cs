using EquityBrief.Core.Cloudflare;

namespace EquityBrief.Core.Providers;

// Cloudflare Pages by direct upload, for the read-only copy alone: the copy's files deployed to its own project, the host's
// headers file beside them, and the deployment's address answered. Asked by the publish verb, by hand or on the scheduler's
// run after the off-peak run, and by no night.
// see: A read-only copy of the site is published to Cloudflare Pages from the read surface's own screens and holds no control
public interface ICloudflarePagesFeed
{
    Task<string> DeployAsync(IReadOnlyList<PagesFile> files, string headers, Action<string> say);

    // A line the host or the feed said, with the token and the account taken out of it.
    string Scrubbed(string said);
}
