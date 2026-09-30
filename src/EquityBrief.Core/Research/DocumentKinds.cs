namespace EquityBrief.Core.Research;

// What kind of document a prompt lists, read from where it came from and nothing else: a company's own filing or
// release, a news report, or an opinion or promotional piece. A section rests a point on the first two and cites the
// third only for what its writer argues, saying so, so the model is told which is which beside each title.
//
// The rule reads the address. The filings archive and the wires companies file their releases on are the company's
// own words. An opinion or promotional piece is one from a site that publishes contributors' and promoters' views,
// or from the part of a mixed site that does: Seeking Alpha's articles are its contributors' analysis, while its
// news items are reports. Every other address is a news report. A syndicated opinion piece, as a portal carries
// another site's, reads as a news report here, since the portal's address says nothing of what it carries.
// see: The research prompt repeats each section's ask after the documents, names its reader and marks each document by kind
public static class DocumentKinds
{
    public const string Filed = "company filing or release";
    public const string News = "news report";
    public const string Opinion = "opinion or promotional piece";

    // Hosts whose every page is the company's own filing or release.
    static readonly string[] FiledHosts = ["sec.gov", "businesswire.com", "prnewswire.com", "globenewswire.com", "accesswire.com"];

    // Hosts whose every page is a contributor's or a promoter's view.
    static readonly string[] OpinionHosts =
    [
        "fool.com", "zacks.com", "investorplace.com", "marketbeat.com", "tipranks.com", "simplywall.st",
        "247wallst.com", "insidermonkey.com", "gurufocus.com", "stocknews.com",
    ];

    // Parts of a mixed site that carry contributors' views, as a host and the first segment of the path.
    static readonly (string Host, string Path)[] OpinionPaths = [("seekingalpha.com", "article")];

    public static string Of(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var address))
        {
            return News;
        }

        var host = address.Host.ToLowerInvariant();

        bool On(string site) => host == site || host.EndsWith("." + site, StringComparison.Ordinal);

        if (FiledHosts.Any(On))
        {
            return Filed;
        }

        var first = address.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? string.Empty;

        return OpinionHosts.Any(On) || OpinionPaths.Any(part => On(part.Host) && string.Equals(first, part.Path, StringComparison.OrdinalIgnoreCase))
            ? Opinion
            : News;
    }
}
