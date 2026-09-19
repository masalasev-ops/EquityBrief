using System.Globalization;
using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using EquityBrief.Core.Research;
using EquityBrief.Web.App;
using EquityBrief.Web.Marks;

namespace EquityBrief.Tests.Reading;

// read-surface, the 5.8 correction of 2026-09-19: every figure a reader sees is drawn at the places
// it is read at with the stored value whole on its element, and the names holding research are
// listed on a route of their own and found, with every other member, from the masthead's search.
public partial class ReadSurface
{
    [Fact]
    public void AFigureIsDrawnAtThePlacesItIsReadAt()
    {
        Assert.Equal("$96.2B", Figures.Money(96221000000.00m));
        Assert.Equal("$5.30T", Figures.Money(5296402989056m));
        Assert.Equal("$59.7B", Figures.Money(59688000000m, "USD"));
        Assert.Equal("EUR 12.3B", Figures.Money(12_300_000_000m, "EUR"));
        Assert.Equal("-$2.5M", Figures.Money(-2_500_000m));
        Assert.Equal("$940,000", Figures.Money(940_000m));

        // The scale is the largest the figure rounds to at least one in, so a figure a shade under
        // a billion is not drawn as a thousand millions.
        Assert.Equal("$1.0B", Figures.Money(999_960_000m));

        Assert.Equal("75.0%", Figures.Percent(0.749753m));
        Assert.Equal("54.1%", Figures.Percent(0.5413m));
        Assert.Equal("213.57", Figures.Price(213.571m));
        Assert.Equal("1,234.50", Figures.Price(1234.5m));
        Assert.Equal("0.85", Figures.Ratio(0.8476m));
        Assert.Equal("27.0", Figures.Multiple(27.0123m));
        Assert.Equal("7.86", Figures.PerShare(7.8576m));
        Assert.Equal("190.3M", Figures.Shares(190287428m));

        // A stored value from a list of values: a number of a million or more in its scale, one
        // written to more than two places to two, and anything else as it is stored.
        Assert.Equal("190.3M", Figures.Read("190287428"));
        Assert.Equal("213.57", Figures.Read("213.571"));
        Assert.Equal("222.27", Figures.Read("222.27"));
        Assert.Equal("2", Figures.Read("2"));
        Assert.Equal("not on file", Figures.Read("not on file"));

        // A sentence the nightly store wrote, its prices read to two places and its other numbers
        // left as written.
        Assert.Equal(
            "the session after the print closes down more than 2 typical days and inside 213.57 to 230.21",
            Figures.InSentence("the session after the print closes down more than 2 typical days and inside 213.571 to 230.2124"));
    }

    [Fact]
    public async Task EveryFigureAReaderSeesOnTheNamePageIsDrawnAtThePlacesItIsReadAtWithTheStoredValueOnItsElement()
    {
        using var store = await WithFundamentals(withTheArchive: true);

        using var host = new Host(store.Root);
        using var client = host.CreateClient();

        var page = await client.GetStringAsync($"/screens/name/{Name}");

        // The regions the scan reads are drawn, so a scan finding nothing is not a page that drew
        // none of them.
        foreach (var region in new[] { "numbers-quarters", "numbers-balance-sheet", "numbers-valuation", "tranche-table", "level-summary", "arithmetic-table", "fact-strip" })
        {
            Assert.Contains($"class=\"{region}\"", page, StringComparison.Ordinal);
        }

        var seen = Visible(page);
        var raw = RawFigures.Matches(seen)
            .Select(match => seen[Math.Max(0, match.Index - 60)..Math.Min(seen.Length, match.Index + match.Length + 60)].Trim())
            .Distinct()
            .ToArray();

        Assert.True(
            raw.Length == 0,
            "Figures drawn at the places they are stored at rather than read at, each with the words around it: " + string.Join(" | ", raw.Take(12)));

        // Each of the newest quarter's figures reads as money in its scale or as a percentage, with
        // the stored value whole on the cell, read against the store.
        using var payload = JsonDocument.Parse(StoredPayload(store, Name));

        var quarter = payload.RootElement.GetProperty("quarter");
        var currency = payload.RootElement.TryGetProperty("currency", out var held) ? held.GetString() : null;

        foreach (var (name, read) in new (string Name, Func<decimal, string> Read)[]
        {
            ("revenue", value => Figures.Money(value, currency)),
            ("netIncome", value => Figures.Money(value, currency)),
            ("grossMargin", Figures.Percent),
            ("netMargin", Figures.Percent),
        })
        {
            var stored = quarter.GetProperty(name).GetString()!;
            var drawn = read(decimal.Parse(stored, NumberStyles.Float, CultureInfo.InvariantCulture));

            Assert.NotEqual(stored, drawn);
            Assert.Contains($"data-{name}=\"{stored}\">{drawn}</td>", page, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void WhyANameIsOnTheListStatesItsValuesAsTheyAreReadAndKeepsThemWholeOnTheElement()
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["close"] = "222.27",
            ["zone low"] = "213.571",
            ["zone high"] = "230.2124",
            ["volume"] = "190287428",
        };

        var why = new MarkRenderer().WhyItIsHere("NVDA", [new FiredReason("at entry zone", values)]);

        Assert.Contains(">close 222.27, zone low 213.57, zone high 230.21, volume 190.3M</span>", why, StringComparison.Ordinal);
        Assert.Contains("data-values=\"close 222.27, zone low 213.571, zone high 230.2124, volume 190287428\"", why, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TheNamesHoldingResearchAreListedAndFoundFromTheMastheadAndTheKeyAloneIsNotResearch()
    {
        using var store = await Populated();

        // AAPL holds two accepted sections a pass wrote, a draft the checker refused and a section
        // it left out, and MSFT holds the key alone, which the queue writes every night.
        store.Execute(
            "INSERT INTO research_section (ticker, section, version, as_of, model, status, prose, source_ids, reject_reason) VALUES " +
            "('AAPL', 'What the company sells', 1, '2026-09-04', 'm', 'accepted', 'x', '[]', NULL), " +
            "('AAPL', 'The risks, each with what would confirm it', 1, '2026-09-05', 'm', 'accepted', 'x', '[]', NULL), " +
            "('AAPL', 'The two cases', 1, '2026-09-07', 'm', 'rejected', 'x', '[]', 'refused'), " +
            "('AAPL', 'The short version', 1, '2026-09-08', 'm', 'fallback', '', '[]', 'no admissible source'), " +
            $"('MSFT', '{ClaimRules.ComputedSection}', 1, '2026-09-08', 'm', 'accepted', 'x', '[]', NULL);");

        var api = Api(store);

        var only = Assert.Single(await api.ResearchedAsync());

        Assert.Equal("AAPL", only.Ticker);
        Assert.Equal("Apple Inc.", only.Name);
        Assert.Equal(new DateOnly(2026, 9, 5), only.Written);
        Assert.Equal(2, only.Sections);

        // The universe carries the same day, and nothing for the name holding the key alone.
        var members = await api.UniverseAsync(Index, new DateOnly(2026, 9, 5));

        Assert.Equal(new DateOnly(2026, 9, 5), members.Single(row => row.Ticker == "AAPL").Researched);
        Assert.Null(members.Single(row => row.Ticker == "MSFT").Researched);

        using var host = new Host(store.Root);
        using var client = host.CreateClient();

        // The list, read off the route a person opens.
        var list = await client.GetStringAsync("/screens/researched");

        Assert.Contains("<tr data-ticker=\"AAPL\" data-written=\"2026-09-05\" data-sections=\"2\">", list, StringComparison.Ordinal);
        Assert.Contains($"href=\"{SinglePageApp.NameRoute}AAPL\"", list, StringComparison.Ordinal);
        Assert.DoesNotContain("data-ticker=\"MSFT\"", list, StringComparison.Ordinal);

        // The universe table says so under the name.
        var universe = await client.GetStringAsync("/screens/universe");

        Assert.Single(Regex.Matches(universe, "data-researched=\"2026-09-05\""));

        // The search's list: every current member by its ticker, the researched one labelled with
        // its day and no other labelled at all.
        var find = await client.GetStringAsync("/screens/find");

        Assert.Contains("<option value=\"AAPL\">Apple Inc., researched 2026-09-05</option>", find, StringComparison.Ordinal);
        Assert.Contains("<option value=\"MSFT\">Microsoft Corporation</option>", find, StringComparison.Ordinal);
        Assert.Single(Regex.Matches(find, "researched"));

        // The shell carries the search on every screen, reads its list after the first screen, links
        // the list from the masthead and says so where text matches no name.
        var shell = await client.GetStringAsync("/");

        Assert.Contains("<input id=\"find\" type=\"search\" list=\"findable\"", shell, StringComparison.Ordinal);
        Assert.Contains($"<a href=\"{SinglePageApp.ResearchedRoute}\" data-view=\"researched\">Researched</a>", shell, StringComparison.Ordinal);
        Assert.Contains("fetch('/screens/researched')", shell, StringComparison.Ordinal);
        Assert.Contains("show().then(() => fetch('/screens/find'))", shell, StringComparison.Ordinal);
        Assert.Contains("'No name in the index matches '", shell, StringComparison.Ordinal);

        // With nothing researched the list says so rather than drawing an empty table.
        Assert.Contains("data-researched=\"none\"", new SinglePageApp().ResearchedRegion([]), StringComparison.Ordinal);
    }

    // The words a reader sees, less the prose a model wrote and management's own passage, which are
    // drawn as the checker accepted them and as they were filed.
    static string Visible(string page)
    {
        var text = Regex.Replace(page, @"<(script|style)\b.*?</\1>", " ", RegexOptions.Singleline | RegexOptions.IgnoreCase);

        text = Regex.Replace(text, "<section class=\"written-section\".*?</section>", " ", RegexOptions.Singleline);
        text = Regex.Replace(text, "<blockquote class=\"guidance\".*?</blockquote>", " ", RegexOptions.Singleline);
        text = Regex.Replace(text, "<[^>]+>", " ");

        return WebUtility.HtmlDecode(text);
    }

    // A number written to more than two places, or a whole number of seven digits or more, standing
    // as a word of its own, which is a figure at the places it is stored at. Digits joined to letters
    // are a name, such as a filing's exhibit, rather than a figure.
    static readonly Regex RawFigures = new(@"(?<![\w.,])\d+\.\d{3,}(?!\w)|(?<![\w.,-])\d{7,}(?!\w)");
}
