using System.Collections.Concurrent;
using EquityBrief.Core.Time;
using EquityBrief.Web.App;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace EquityBrief.Api.Reading;

// Every request's route and its server time in milliseconds, kept in memory for the read surface's life and read by the
// Run page, which draws the slowest routes with their median and slowest times since the surface started. It writes no
// store table, so a time is gone when the process ends. Each route keeps its newest times only, at most the number
// below, so a surface left running for weeks holds a bounded list: a route past it drops its oldest time as each new
// one is kept, and the Run page says how many it reads over.
// see: Each request's route and server time is kept in memory for the read surface's life and drawn on the Run page, and no store holds it
public sealed class RouteTimes(IClock clock)
{
    public const int KeptARoute = SinglePageApp.RouteTimesKept;

    // How many routes the Run page draws, the slowest first.
    public const int Drawn = 12;

    readonly ConcurrentDictionary<string, ConcurrentQueue<double>> times = new(StringComparer.Ordinal);

    public DateTimeOffset Since { get; } = clock.UtcNow;

    // A request's route as the Run page names it: the pattern it matched, the chart page's interval beside it since each
    // interval reads its own rows, and the path for a request no route matched.
    public static string Of(HttpContext context)
    {
        var route = context.GetEndpoint() is RouteEndpoint endpoint && endpoint.RoutePattern.RawText is { Length: > 0 } pattern
            ? pattern
            : context.Request.Path.Value ?? "/";

        return route.StartsWith("/screens/chart/", StringComparison.Ordinal)
            ? route + " " + EquityBrief.Core.Chart.ChartSeries.Word(EquityBrief.Core.Chart.ChartSeries.Read(context.Request.Query["interval"].FirstOrDefault()))
            : route;
    }

    public void Record(string route, double milliseconds)
    {
        var kept = times.GetOrAdd(route, _ => new ConcurrentQueue<double>());

        kept.Enqueue(milliseconds);

        while (kept.Count > KeptARoute && kept.TryDequeue(out _))
        {
        }
    }

    // The routes the surface answered since it started, the slowest first by its slowest time, each with how many of its
    // times are read, their median and their slowest.
    public IReadOnlyList<RouteTime> Slowest(int count = Drawn) =>
        [
            .. times
                .Select(pair => (Route: pair.Key, Times: pair.Value.ToArray()))
                .Where(route => route.Times.Length > 0)
                .Select(route => new RouteTime(route.Route, route.Times.Length, Median(route.Times), route.Times.Max()))
                .OrderByDescending(route => route.Slowest)
                .ThenBy(route => route.Route, StringComparer.Ordinal)
                .Take(count),
        ];

    public static double Median(IReadOnlyList<double> values)
    {
        var ordered = values.Order().ToArray();
        var middle = ordered.Length / 2;

        return ordered.Length % 2 == 1 ? ordered[middle] : (ordered[middle - 1] + ordered[middle]) / 2;
    }
}
