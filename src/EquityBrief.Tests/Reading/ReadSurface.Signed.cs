using System.Net;
using System.Text.RegularExpressions;
using EquityBrief.Tests.Checks;

namespace EquityBrief.Tests.Reading;

// read-surface, 19.2: every figure a page writes with its sign stands in an element of the one class pair, a rise in the
// gain's green and a fall in the loss's red, read off the rendered pages element by element, a page's scripts, styles and
// a picture's tooltips left out, and a chart's axis labels and a peer's likeness, which are no figure that rose or fell;
// and the reader is shown to find a signed figure written in ink or in the other class and to pass one in its own.
// see: A figure that rose or fell is drawn green or red with its sign on every page
public partial class ReadSurface
{
    // What a signed figure may stand in outside the pair: a chart's axis labels, which name a place on a scale, and a
    // peer's likeness, the correlation of two stocks' returns, which neither rose nor fell.
    static readonly string[] AxisLabels = ["m-tick", "m-axis", "likeness"];

    // Every figure a page writes with its sign, beside the classes of the nearest element holding it.
    static IReadOnlyList<(string Figure, string Classes)> SignedFigures(string page)
    {
        var body = Regex.Replace(page, @"<(script|style|title)\b.*?</\1>", " ", RegexOptions.Singleline);
        var found = new List<(string Figure, string Classes)>();
        var open = new Stack<string>();

        foreach (Match token in Regex.Matches(body, @"<(/?)([a-zA-Z][\w-]*)([^>]*)>|([^<]+)"))
        {
            if (token.Groups[4].Success)
            {
                foreach (Match figure in Regex.Matches(WebUtility.HtmlDecode(token.Groups[4].Value), @"(?<![\w.,/:&;-])[+\-−]\d[\d.,]*"))
                {
                    found.Add((figure.Value, open.Count > 0 ? open.Peek() : string.Empty));
                }

                continue;
            }

            var name = token.Groups[2].Value.ToLowerInvariant();

            if (token.Groups[1].Value == "/")
            {
                if (open.Count > 0)
                {
                    open.Pop();
                }

                continue;
            }

            if (VoidElements.Contains(name) || token.Groups[3].Value.EndsWith('/'))
            {
                continue;
            }

            open.Push(Regex.Match(token.Groups[3].Value, "class=\"([^\"]*)\"") is { Success: true } classes ? classes.Groups[1].Value : string.Empty);
        }

        return found;
    }

    // The signed figures a page writes outside the class its sign gives, a rise outside the gain's and a fall outside the
    // loss's, each with the classes it stood in.
    static IReadOnlyList<string> Uncoloured(string page) =>
    [
        .. SignedFigures(page)
            .Where(figure => !Regex.IsMatch(figure.Classes, figure.Figure.StartsWith('+') ? @"(^|\s)gain(\s|$)" : @"(^|\s)loss(\s|$)"))
            .Where(figure => !AxisLabels.Any(axis => Regex.IsMatch(figure.Classes, $@"(^|\s){axis}(\s|$)")))
            .Select(figure => $"{figure.Figure} in \"{figure.Classes}\""),
    ];

    [Fact]
    public void TheSignedFigureReaderFindsOneWrittenInInkAndPassesOneInThePair()
    {
        Assert.Equal(["+1.25 in \"num\""], Uncoloured("<table><tr><td class=\"num\">+1.25</td></tr></table>"));
        Assert.Equal(["-0.40 in \"\""], Uncoloured("<p>a mean -0.40 of the risk</p>"));
        Assert.Empty(Uncoloured("<p>a mean <span class=\"loss\">-0.40</span> of the risk, <b class=\"bh-figure gain\">+0.19</b></p>"));

        // A rise drawn in the loss's red and a fall in the gain's green each stand outside the class their sign gives.
        Assert.Equal(["+0.19 in \"loss\"", "-0.40 in \"gain\""], Uncoloured("<p><span class=\"loss\">+0.19</span> and <span class=\"gain\">-0.40</span></p>"));

        // A date, a range of years, a word joined by a hyphen and a tooltip carry no signed figure.
        Assert.Empty(Uncoloured("<p>2026-10-13, the 52-week high, 2019-2026<svg><title>-3.0% on the day</title></svg></p>"));

        // An axis label places a scale.
        Assert.Empty(Uncoloured("<svg><text class=\"m-tick\" x=\"1\" y=\"2\">-10%</text></svg>"));
    }

    [Fact]
    public async Task EverySignedFigureOnEveryPageIsDrawnGreenOrRedWithItsSign()
    {
        var pages = new List<(string Page, string Html)>();

        // The replayed fixture: Tonight, the Run page, Past picks, Universe, the Watch list, every name's page and from
        // 19.11 its chart page at each interval.
        using (var replayed = await FixtureReplay.ReplayedAsync())
        {
            using var host = new Host(replayed.Root);
            using var client = host.CreateClient();

            foreach (var route in new[] { "/screens/tonight", "/screens/run", "/screens/picks", "/screens/universe", "/screens/watch" })
            {
                pages.Add((route, await client.GetStringAsync(route)));
            }

            foreach (var ticker in Strings(replayed, "SELECT DISTINCT ticker FROM bar ORDER BY ticker;"))
            {
                pages.Add(($"/screens/name/{ticker}", await client.GetStringAsync($"/screens/name/{ticker}")));
                pages.Add(($"/exports/name/{ticker}", await client.GetStringAsync($"/exports/name/{ticker}")));

                foreach (var interval in new[] { "daily", "weekly", "monthly" })
                {
                    pages.Add(($"/screens/chart/{ticker}?interval={interval}", await client.GetStringAsync($"/screens/chart/{ticker}?interval={interval}")));
                }
            }
        }

        // The S&P 400's and 600's nights with the night's cards written, the S&P 400's breakout carrying a record whose
        // worst close fell below its buy: their Tonight, their Past picks and their Run pages.
        using (var universes = UniversesStore())
        {
            universes.Execute(
                "INSERT INTO rule_record (index_code, family, rule, settings, recorded_at, first_session, last_session, membership, unit, trades, won, average, median_sessions, ended_by, worst_close) VALUES " +
                "('MID', 'breakout', 'the breakout at high=126', 'high=126', '2026-10-02T12:00:00Z', '2019-01-02', '2026-10-01', 'as it stood', 'risks', 400, NULL, 0.21, 12, '[100,100,100,100]', -0.4);");
            await new EquityBrief.Worker.Cards.DecisionCards(new WaitedClock(new DateTimeOffset(2026, 10, 2, 23, 50, 0, TimeSpan.Zero)), universes.DatabaseFile).RunAsync("cards");

            using var host = new Host(universes.Root);
            using var client = host.CreateClient();

            foreach (var word in new[] { "400", "600" })
            {
                foreach (var route in new[] { $"/screens/tonight/{IndexNight}?universe={word}", $"/screens/picks?universe={word}", $"/screens/run/{IndexNight}?universe={word}" })
                {
                    pages.Add((route, await client.GetStringAsync(route)));
                }
            }
        }

        // The families' night with the night's cards written: Tonight's cards and Past picks' results, a trailing breakout's
        // among them.
        using (var families = await FamilyPagesStore())
        {
            await new EquityBrief.Worker.Cards.DecisionCards(new WaitedClock(new DateTimeOffset(2026, 10, 2, 23, 50, 0, TimeSpan.Zero)), families.DatabaseFile).RunAsync("cards");

            using var host = new Host(families.Root);
            using var client = host.CreateClient();

            foreach (var route in new[] { $"/screens/tonight/{TheSwitch}", "/screens/picks", $"/screens/run/{TheSwitch}" })
            {
                pages.Add((route, await client.GetStringAsync(route)));
            }
        }

        // The Ledger's years and the Loop's alarms over constructed rows, a year and a period on each side of nought.
        using (var constructed = new TemporaryStore().Migrated())
        {
            LedgerYearRow(constructed, "pullback", 2025, 20, 5, 0, 0, 20, "-0.1", "-0.2", "-1,-0.8,-0.5,-0.2,0,0.2,0.5,1,2");
            LedgerYearRow(constructed, "pullback", 2026, 40, 10, 8, 4, 30, "0.4", "0.1", "-1,-0.5,0,0.25,0.5,1,1.5,2,3");
            constructed.Execute(
                "INSERT INTO loop_alarm (index_code, family, period, trades, edge, edge_floor, counted, under, streak, flagged, reference, run_id) VALUES " +
                $"('GSPC', 'drift', '2026-07-01', 6, -0.4, -0.1, 1, 1, 1, 0, '{LargeOctober}', 'night-1'), ('GSPC', 'drift', '2026-08-01', 5, 0.3, -0.1, 1, 0, 0, 0, '{LargeOctober}', 'night-2');");
            Run(constructed, LargeOctober, "GSPC", "2026-10");
            Proposed(constructed, LargeOctober, "GSPC", "drift", "the autopsy's exit, ranked 1", true, Exit(7));

            using var host = new Host(constructed.Root);
            using var client = host.CreateClient();

            pages.Add(("/screens/ledger", await client.GetStringAsync("/screens/ledger")));
            pages.Add(("/screens/loop", await client.GetStringAsync("/screens/loop?universe=500")));
        }

        // Each page read, and the pages together writing signed figures, so the reader is shown to have read some.
        var signed = pages.Sum(page => SignedFigures(page.Html).Count);
        var uncoloured = pages.SelectMany(page => Uncoloured(page.Html).Select(figure => $"{page.Page}: {figure}")).ToArray();

        Assert.True(signed >= 40, $"The pages write {signed} signed figures, expected at least 40.");
        Assert.True(uncoloured.Length == 0, "Signed figures outside the pair: " + string.Join("\n", uncoloured.Take(80)));
    }
}
