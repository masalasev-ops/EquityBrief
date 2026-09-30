using System.Globalization;
using System.Text.Json;
using EquityBrief.Core.Providers;
using EquityBrief.Core.Research;
using EquityBrief.Core.Time;

namespace EquityBrief.Worker.Research;

// The `measure-sources` verb's work: a sector's proposed sites searched as a theme pass searches, one site and one
// industry at a time over the quarter to the day, each result judged by admissibility and read by the density
// rule, and each site's verdict by the rule a site joins the list by. It writes nothing to the store; the report
// is a file under the folder it is handed, and each site's verdict a line on the output.
// see: A theme search adds its sector's sites, and a site joins the list only where a measurement found industry material on it
public static class SourceMeasurementRun
{
    public sealed record SiteVerdict(string Site, bool Joins, int About, int Admitted, int Stored, int Results);

    public static async Task<(string File, IReadOnlyList<SiteVerdict> Verdicts)> RunAsync(
        ISearchFeed search,
        IClock clock,
        string sector,
        IReadOnlyList<string> sites,
        IReadOnlyList<string> industries,
        string folder,
        CancellationToken cancellation = default)
    {
        var at = clock.UtcNow;
        var asOf = clock.SessionDateAt(at);
        var measured = new List<MeasuredSearch>();

        foreach (var site in sites)
        {
            foreach (var industry in industries)
            {
                measured.Add(await SourceMeasurement.MeasureAsync(search, site, industry, asOf, at, cancellation));
            }
        }

        var pages = measured.SelectMany(search => search.Pages).ToArray();

        var verdicts = sites.Select(site =>
        {
            var own = measured.Where(one => one.Site == site).ToArray();
            var kept = own.SelectMany(one => one.Pages).ToArray();

            return new SiteVerdict(
                site,
                SourceMeasurement.Joins(site, pages, industries),
                kept.Count(page => page.Admitted && page.Mentions >= ThemeSearch.MentionsPerTenThousand),
                kept.Count(page => page.Admitted),
                kept.Length,
                own.Sum(one => one.Results));
        }).ToArray();

        var report = new
        {
            sector,
            asOf = asOf.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            industries,
            searches = measured.Count,
            rule = "a site joins where, for at least one of these industries, it returned at least one page admissibility admitted and the density rule reads as about that industry",
            sites = verdicts.Select(verdict => new
            {
                site = verdict.Site,
                joins = verdict.Joins,
                aboutTheIndustry = verdict.About,
                admitted = verdict.Admitted,
                stored = verdict.Stored,
                results = verdict.Results,
                searches = measured.Where(one => one.Site == verdict.Site).Select(one => new
                {
                    industry = one.Industry,
                    results = one.Results,
                    offList = one.OffList,
                    shortOfADocument = one.Snippets,
                    pages = one.Pages.Select(page => new { url = page.Url, title = page.Title, admitted = page.Admitted, mentions = Math.Round(page.Mentions, 2) }),
                }),
            }),
        };

        Directory.CreateDirectory(folder);

        var file = Path.Combine(folder, "measure-sources-" + Slug(sector) + "-" + asOf.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) + ".json");

        await File.WriteAllTextAsync(file, JsonSerializer.Serialize(report, Indented), cancellation);

        return (file, verdicts);
    }

    static readonly JsonSerializerOptions Indented = new() { WriteIndented = true };

    static string Slug(string sector) =>
        string.Concat(sector.ToLowerInvariant().Select(character => char.IsLetterOrDigit(character) ? character : '-'));
}
