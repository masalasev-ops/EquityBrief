using System.Globalization;
using System.Text.Json;
using EquityBrief.Core.Providers;
using EquityBrief.Core.Research;
using EquityBrief.Core.Time;

namespace EquityBrief.Worker.Research;

// The `measure-search` verb's work: the theme search measured on Tavily's search tool and on DeepSeek's side by side,
// as 6.11 measured Tavily's, over the quarter to the day. Each industry is searched a site at a time on both tools as a
// theme pass searches, and the whole list together on DeepSeek, both in one query and in the fewest queries the tool
// takes, every form counted as `ToolMeasurement` counts it. It writes nothing to the store; the report is a file under
// the folder it is handed, and each form's totals a line on the output.
// see: Theme material comes from a search tool, and per-name material never does
public static class SearchMeasurementRun
{
    public const string Tavily = "tavily";
    public const string DeepSeek = "deepseek";

    public const string EachSite = "each site alone";
    public const string OneQuery = "the list in one query";
    public const string Packed = "the list in the fewest queries the tool takes";

    // The fixture's four industries, which 6.11 measured, and Semiconductors, which it measured beside them.
    public static readonly string[] Industries =
        ["Consumer Electronics", "Scientific & Technical Instruments", "Software - Infrastructure", "Entertainment", "Semiconductors"];

    // The most a run's DeepSeek searches may cost: half the dollar the operator approved on 2026-11-01 for the
    // measurement, which runs twice.
    public const decimal MostARun = 0.50m;

    public sealed record Planned(string Tool, string Form, string Industry, IReadOnlyList<SearchQuery> Queries);

    // Every search a run makes, in the order it makes them, an industry at a time.
    public static IReadOnlyList<Planned> Plan(IReadOnlyList<string> industries, IReadOnlyList<string> industryList, DateOnly asOf) =>
    [
        .. industries.SelectMany(industry =>
        {
            var eachSite = ThemeSearch.For(industry, asOf, industryList);
            var words = eachSite[0].Query;
            var from = asOf.AddMonths(-ThemeSearch.WindowMonths);

            return new[]
            {
                new Planned(Tavily, EachSite, industry, eachSite),
                new Planned(DeepSeek, EachSite, industry, eachSite),
                new Planned(DeepSeek, OneQuery, industry, [new SearchQuery(words, from, asOf, industryList, ThemeSearch.MostPages)]),
                new Planned(DeepSeek, Packed, industry, DeepSeekSearchFeed.Packed(words, from, asOf, industryList, ThemeSearch.MostPages)),
            };
        }),
    ];

    // The most a run's DeepSeek searches could cost, every one of them answered with a result.
    public static decimal Most(IReadOnlyList<Planned> plan) =>
        plan.Where(planned => planned.Tool == DeepSeek).Sum(planned => planned.Queries.Count) * DeepSeekSearchFeed.PriceASearch;

    public static async Task<(string File, IReadOnlyList<ToolFormCount> Counts)> RunAsync(
        ISearchFeed tavily,
        ISearchFeed deepSeek,
        IClock clock,
        IReadOnlyList<string> industries,
        IReadOnlyList<string> industryList,
        string folder,
        CancellationToken cancellation = default)
    {
        var at = clock.UtcNow;
        var asOf = clock.SessionDateAt(at);
        var counts = new List<ToolFormCount>();
        var searched = new List<object>();

        foreach (var planned in Plan(industries, industryList, asOf))
        {
            var (count, searches) = await ToolMeasurement.MeasureAsync(
                planned.Tool == DeepSeek ? deepSeek : tavily,
                planned.Tool,
                planned.Form,
                planned.Industry,
                planned.Queries,
                industryList,
                asOf,
                at,
                cancellation);

            counts.Add(count);
            searched.Add(new
            {
                tool = planned.Tool,
                form = planned.Form,
                industry = planned.Industry,
                count,
                searches = searches.Select(search => new
                {
                    sites = search.Sites,
                    refused = search.Refused,
                    results = search.Answer?.Results.Select(result => new
                    {
                        url = result.Url,
                        title = result.Title,
                        text = result.Text?.Length,
                        snippet = result.Snippet.Length,
                        published = result.Published?.ToString("yyyy-MM-ddTHH:mm:sszzz", CultureInfo.InvariantCulture),
                        authority = result.Authority,
                        rerank = result.Rerank,
                    }),
                }),
            });
        }

        var report = new
        {
            at = at.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture),
            asOf = asOf.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            industries,
            sites = industryList,
            totals = Totals(counts),
            forms = searched,
        };

        Directory.CreateDirectory(folder);

        var file = Path.Combine(folder, "measure-search-" + at.UtcDateTime.ToString("yyyy-MM-ddTHHmmssZ", CultureInfo.InvariantCulture) + ".json");

        await File.WriteAllTextAsync(file, JsonSerializer.Serialize(report, Indented), cancellation);

        return (file, counts);
    }

    // Each tool and form summed over the industries, in the order the run made them.
    public static IReadOnlyList<ToolFormCount> Totals(IReadOnlyList<ToolFormCount> counts) =>
    [
        .. counts
            .GroupBy(count => (count.Tool, count.Form))
            .Select(form => new ToolFormCount(
                form.Key.Tool,
                form.Key.Form,
                "the " + form.Count().ToString(CultureInfo.InvariantCulture) + " industries",
                form.Sum(count => count.Searches),
                form.Sum(count => count.Refused),
                form.Sum(count => count.Empty),
                form.Sum(count => count.FromSiteAsked),
                form.Sum(count => count.FromOtherSites),
                form.Sum(count => count.WithText),
                form.Sum(count => count.WithDate),
                form.Sum(count => count.Admitted),
                form.Sum(count => count.AboutTheIndustry))),
    ];

    public static string Line(ToolFormCount count) =>
        FormattableString.Invariant(
            $"measure-search: {count.Tool}, {count.Form}, {count.Industry}: {count.Searches} search(es), {count.Refused} refused, {count.Empty} returning nothing; {count.FromSiteAsked} page(s) from the site asked and {count.FromOtherSites} from others, {count.WithText} with text, {count.WithDate} with a publish date; {count.Admitted} admitted, {count.AboutTheIndustry} about the industry");

    // Each request DeepSeek's search is sent and the answer it sends, kept whole in a folder of the run's own as they
    // went, which is what its reader is tested against. Neither body carries the key, which travels in the request's
    // header alone, and the feed hands no header here.
    public static Func<string, int, string, CancellationToken, Task> Keeping(string folder)
    {
        var answers = 0;

        return async (sent, status, body, cancellation) =>
        {
            var stem = FormattableString.Invariant($"deepseek-{Interlocked.Increment(ref answers):D3}");

            Directory.CreateDirectory(folder);

            foreach (var (name, text) in new[] { (stem + "-request.json", sent), (stem + FormattableString.Invariant($"-{status}.json"), body) })
            {
                await File.WriteAllTextAsync(Path.Combine(folder, name), text, cancellation);
            }
        };
    }

    // The verb: both tools live, DeepSeek's key the one the secrets file holds for the deepseek model profile, read here
    // and handed to the feed's header alone, and the run refused before any search where its DeepSeek searches could cost
    // more than a run may spend. Nothing is written to the store, and the report goes under the working folder's artifacts.
    public static async Task<int> VerbAsync(string[] args, Func<string, string?> setting, IClock clock)
    {
        var lists = SourceLists.Read(Path.Combine(Directory.GetCurrentDirectory(), SourceLists.FileName));
        var folder = Path.Combine(Directory.GetCurrentDirectory(), "artifacts");
        string[] industries = VerbArguments.Value(args, "--industries")?.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries) ?? Industries;
        var plan = Plan(industries, lists.Industry, clock.SessionDateAt(clock.UtcNow));

        if (Most(plan) > MostARun)
        {
            Console.Error.WriteLine(FormattableString.Invariant(
                $"measure-search: refused, its DeepSeek searches could cost ${Most(plan)}, past the ${MostARun} a run may spend."));

            return 1;
        }

        ISearchFeed tavily;
        DeepSeekSearchFeed deepSeek;

        try
        {
            tavily = TavilySearchFeed.Live(setting(TavilySearchFeed.ApiKeyName));
            deepSeek = DeepSeekSearchFeed.Live(
                DeepSeekSearchFeed.KeyFrom(setting),
                kept: args.Contains("--capture")
                    ? Keeping(Path.Combine(folder, "measure-search-captures-" + clock.UtcNow.UtcDateTime.ToString("yyyy-MM-ddTHHmmssZ", CultureInfo.InvariantCulture)))
                    : null);
        }
        catch (InvalidOperationException refusal)
        {
            Console.Error.WriteLine("measure-search: " + refusal.Message);

            return 1;
        }

        Console.WriteLine(FormattableString.Invariant(
            $"measure-search: {plan.Where(planned => planned.Tool == Tavily).Sum(planned => planned.Queries.Count)} Tavily search(es) and {plan.Where(planned => planned.Tool == DeepSeek).Sum(planned => planned.Queries.Count)} DeepSeek search(es), the DeepSeek ones costing at most ${Most(plan)}"));

        var (file, counts) = await RunAsync(tavily, deepSeek, clock, industries, lists.Industry, folder);

        foreach (var count in counts.Concat(Totals(counts)))
        {
            Console.WriteLine(Line(count));
        }

        Console.WriteLine(FormattableString.Invariant(
            $"measure-search: {tavily.Requests} Tavily search(es), {deepSeek.Requests} DeepSeek search(es) of which {deepSeek.Billed} answered with a result, costing ${deepSeek.Billed * DeepSeekSearchFeed.PriceASearch}; report {file}"));

        return 0;
    }

    static readonly JsonSerializerOptions Indented = new() { WriteIndented = true };
}
