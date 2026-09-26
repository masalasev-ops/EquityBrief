using System.Globalization;
using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using EquityBrief.Api.Reading;
using EquityBrief.Web.Marks;
using EquityBrief.Worker.Fundamentals;

namespace EquityBrief.Tests.Reading;

// read-surface, the 5.8 correction: the numbers open on a snapshot of the newest filing, a figure to a
// row, with every other filed figure folded beneath it and no provider named in the words.
// see: The numbers open on a snapshot of the newest filing with every other filed figure folded beneath it, and the report names no provider
public partial class ReadSurface
{
    // The snapshot's rows as the page drew them: what each is, the stored value its element carries,
    // the figure drawn and what it is measured against beneath it.
    static IReadOnlyList<(string Label, string Key, string Stored, string Drawn, string? Beneath)> SnapshotRows(string region)
    {
        var snapshot = Regex.Match(region, "<dl class=\"snapshot\" data-snapshot-of=\"[^\"]*\">(.*?)</dl>", RegexOptions.Singleline);

        Assert.True(snapshot.Success, "The numbers draw no snapshot, and this is what reads it.");

        return
        [
            .. Regex.Matches(snapshot.Groups[1].Value, "<div><dt>([^<]*)</dt><dd(?: class=\"degraded\")? data-([A-Za-z]+)=\"([^\"]*)\">(?:<b>([^<]*)</b>(?:<small>([^<]*)</small>)?|not filed)</dd></div>")
                .Select(row => (
                    WebUtility.HtmlDecode(row.Groups[1].Value),
                    row.Groups[2].Value,
                    row.Groups[3].Value,
                    WebUtility.HtmlDecode(row.Groups[4].Value),
                    row.Groups[5].Success ? WebUtility.HtmlDecode(row.Groups[5].Value) : null)),
        ];
    }

    static string SignedPercent(string stored)
    {
        var value = decimal.Parse(stored, NumberStyles.Float, CultureInfo.InvariantCulture);

        return (value > 0 ? "+" : string.Empty) + Figures.Percent(value);
    }

    [Fact]
    public async Task TheNumbersOpenOnTwelveFiguresOfTheNewestFilingAndFoldEveryOtherFiledFigureBeneathIt()
    {
        using var store = await WithFundamentals(withTheArchive: true);

        var region = NameScreen.Numbers(await Api(store).FundamentalsAsync(Name));

        using var payload = JsonDocument.Parse(StoredPayload(store, Name));

        var root = payload.RootElement;
        var currency = root.TryGetProperty("currency", out var held) ? held.GetString() : null;

        string? Stored(string part, string? field = null) =>
            field is null
                ? root.TryGetProperty(part, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null
                : root.TryGetProperty(part, out var holder) && holder.ValueKind == JsonValueKind.Object && holder.TryGetProperty(field, out var inner) && inner.ValueKind == JsonValueKind.String ? inner.GetString() : null;

        decimal Of(string stored) => decimal.Parse(stored, NumberStyles.Float, CultureInfo.InvariantCulture);

        // Twelve rows, in the order a reader wants them, each keyed on the stored field it draws.
        var rows = SnapshotRows(region);

        Assert.Equal(
            ["revenue", "netIncome", "epsActual", "reportDate", "grossMargin", "netMargin", "marketCapitalisation", "forwardAnnualRate", "trailingPe", "forwardPe", "cash", "netDebt"],
            [.. rows.Select(row => row.Key)]);
        Assert.Equal($"Revenue, quarter to {Stored("periodEnd")}", rows[0].Label);

        // Each figure is the stored value whole on its element, drawn at the places it is read at,
        // with what it is measured against beneath it, all read back against the stored payload.
        (string Key, string? Stored, Func<decimal, string> Read, string? Beneath)[] expected =
        [
            ("revenue", Stored("quarter", "revenue"), value => Figures.Money(value, currency), Stored("growth", "revenue") is { } revenueGrowth ? SignedPercent(revenueGrowth) + " on a year earlier" : null),
            ("netIncome", Stored("quarter", "netIncome"), value => Figures.Money(value, currency), Stored("growth", "netIncome") is { } incomeGrowth ? SignedPercent(incomeGrowth) + " on a year earlier" : null),
            ("epsActual", Stored("earnings", "epsActual"), Figures.PerShare, Stored("earnings", "epsEstimate") is { } estimate ? "against an estimate of " + Figures.PerShare(Of(estimate)) : null),
            ("grossMargin", Stored("quarter", "grossMargin"), Figures.Percent, null),
            ("netMargin", Stored("quarter", "netMargin"), Figures.Percent, null),
            ("marketCapitalisation", Stored("marketCapitalisation"), value => Figures.Money(value, currency), null),
            ("trailingPe", Stored("valuation", "trailingPe"), Figures.Multiple, Stored("epsBases", "trailing") is { } trailing ? "trailing earnings of " + Figures.PerShare(Of(trailing)) + " a share" : null),
            ("forwardPe", Stored("valuation", "forwardPe"), Figures.Multiple, Stored("epsBases", "nextYear") is { } nextYear ? "next year's estimate " + Figures.PerShare(Of(nextYear)) + " a share" : null),
            ("cash", Stored("balanceSheet", "cash"), value => Figures.Money(value, currency), null),
            ("netDebt", Stored("balanceSheet", "netDebt"), value => Figures.Money(value, currency), null),
        ];

        foreach (var (key, stored, read, beneath) in expected)
        {
            var row = rows.Single(drawn => drawn.Key == key);

            if (stored is null)
            {
                Assert.Equal("absent", row.Stored);

                continue;
            }

            Assert.Equal(stored, row.Stored);
            Assert.Equal(read(Of(stored)), row.Drawn);
            Assert.Equal(beneath, row.Beneath);
        }

        // The next report is the estimated quarter's date, with the consensus the provider files.
        var next = rows.Single(row => row.Key == "reportDate");

        Assert.Equal(Stored("estimated", "reportDate") ?? "absent", next.Stored);

        // The five quarters under their own heading, then management's passage and every other filed
        // table folded shut, each table under its own heading.
        var quarters = region.IndexOf("<table class=\"numbers-quarters\"", StringComparison.Ordinal);

        Assert.True(quarters > 0 && region.LastIndexOf($"<div class=\"sub\">The last {NameScreen.QuartersShown} quarters</div>", quarters, StringComparison.Ordinal) > 0);

        var folded = Regex.Match(region, "<details class=\"numbers-detail\"><summary>Every figure from the filing</summary>(.*?)</details></section>", RegexOptions.Singleline);

        Assert.True(folded.Success, "The other filed figures are not folded beneath the snapshot.");

        foreach (var (heading, table) in new[] { ("Balance sheet", "numbers-balance-sheet"), ("Valuation", "numbers-valuation"), ("Segments", "numbers-segments") })
        {
            Assert.Contains($"<div class=\"sub\">{heading}</div><div class=\"tbl-wrap\"><table class=\"{table}\"", folded.Groups[1].Value, StringComparison.Ordinal);
        }

        Assert.Contains("<button type=\"button\" class=\"fold-hide\">Hide</button>", folded.Groups[1].Value, StringComparison.Ordinal);

        // The segments name each group once, over its lines, grouped by the table's own groups: one
        // heading for the company's own lines and one for each group holding a figure for the quarter
        // the table states, being its shortest period's newest end, read off the payload.
        var segments = root.GetProperty("segments");
        var periods = segments.GetProperty("periods").EnumerateArray().ToArray();
        var shortest = periods.Min(period => period.GetProperty("months").GetInt32());
        var ended = periods
            .Where(period => period.GetProperty("months").GetInt32() == shortest)
            .Select(period => period.GetProperty("ended").GetString()!)
            .Max(StringComparer.Ordinal);

        bool InTheQuarter(JsonElement figures) =>
            figures.EnumerateArray().Any(figure => figure.GetProperty("months").GetInt32() == shortest && figure.GetProperty("ended").GetString() == ended);

        var headed = (InTheQuarter(segments.GetProperty("consolidated")) ? 1 : 0)
            + segments.GetProperty("groups").EnumerateArray().Count(group => InTheQuarter(group.GetProperty("figures")));
        var labels = Regex.Matches(region, "<tr class=\"segment-group\" data-segment=\"([^\"]*)\"><th colspan=\"2\">([^<]*)</th></tr>")
            .Select(group => group.Groups[1].Value)
            .ToArray();

        Assert.True(headed >= 2, $"The fixture's table heads {headed} group(s), and this is what reads them.");
        Assert.Equal(headed, labels.Length);
        Assert.Equal("The company", labels[0]);
        Assert.All(
            Regex.Matches(region, "<tr data-segment=\"([^\"]*)\"><td>").Select(line => line.Groups[1].Value),
            label => Assert.Contains(label, labels));

        // And no provider is named in the section's words.
        Assert.DoesNotContain(FundamentalsFetcher.Provider, Regex.Replace(region, "<[^>]+>", " "), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void AFilingsOwnMarksAreReadWhereItStatesThemAndAPartItDoesNotCarryIsSaidToBeNotFiled()
    {
        // A filing constructed to carry what the fixture's may not: a segment line the table sets with
        // a colon and no figure, a guidance passage marking its points and one beneath another, a
        // dividend of zero, and a quarter with no growth filed.
        const string Passage = "Headline results for the quarter • Revenue rose on demand • Margins held ◦ Services led the gain • Guidance affirmed";

        var payload = JsonSerializer.Serialize(new
        {
            periodEnd = "2026-06-30",
            currency = "USD",
            quarter = new { revenue = "1000000000", netIncome = "100000000", grossMargin = "0.5", netMargin = "0.1" },
            dividend = new { forwardAnnualRate = "0", forwardYield = "0", payoutRatio = "0" },
            guidance = new { document = "exhibit991.htm", filedOn = "2026-07-29", located = true, heading = "Outlook", passage = Passage },
            segments = new
            {
                report = "R4.htm",
                periods = new[] { new { months = 3, ended = "2026-06-30" } },
                consolidated = new object[]
                {
                    new { lineItem = "Total revenues", months = 3, ended = "2026-06-30", value = "1000000000", unit = (string?)null },
                    new { lineItem = "Operating expenses:", months = 3, ended = "2026-06-30", value = (string?)null, unit = (string?)null },
                    new { lineItem = "Benefits", months = 3, ended = "2026-06-30", value = "400000000", unit = (string?)null },
                },
                groups = new[]
                {
                    new
                    {
                        label = "Operating Segments | Insurance",
                        figures = new object[] { new { lineItem = "Total revenues", months = 3, ended = "2026-06-30", value = "900000000", unit = (string?)null } },
                    },
                },
            },
        });

        var region = NameScreen.Numbers([new FilingRow("ZZZZ", new DateOnly(2026, 7, 29), payload, "{}")]);

        // Each group named once over its lines, and the colon line drawn as the heading it is rather
        // than as a figure not filed.
        Assert.Equal(
            ["The company", "Operating Segments | Insurance"],
            [.. Regex.Matches(region, "<tr class=\"segment-group\" data-segment=\"[^\"]*\"><th colspan=\"2\">([^<]*)</th></tr>").Select(group => group.Groups[1].Value)]);
        Assert.Contains("<tr class=\"segment-heading\" data-segment=\"The company\"><td colspan=\"2\">Operating expenses:</td></tr>", region, StringComparison.Ordinal);
        Assert.DoesNotContain("data-segment-figure=\"absent\"", region, StringComparison.Ordinal);
        Assert.DoesNotContain("<td>The company</td>", region, StringComparison.Ordinal);

        // The passage folded, a point to an item where it marks its points and the one beneath another
        // set in, what stands before the first mark opening it, the items the passage cut and never edited.
        var fold = Regex.Match(region, "<details class=\"guidance-fold\"><summary>[^<]+</summary><blockquote class=\"guidance\".*?</blockquote><button type=\"button\" class=\"fold-hide\">Hide</button></details>", RegexOptions.Singleline);

        Assert.True(fold.Success, "The guidance passage is not folded.");
        Assert.Contains("<p class=\"guidance-passage\">Headline results for the quarter</p>", fold.Value, StringComparison.Ordinal);
        Assert.Contains(
            "<ul class=\"guidance-passage\"><li>Revenue rose on demand</li><li>Margins held</li><li class=\"beneath\">Services led the gain</li><li>Guidance affirmed</li></ul>",
            fold.Value,
            StringComparison.Ordinal);

        // A company paying none is said to pay none, and a figure the filing carries no part for is said
        // to be not filed rather than drawn as a blank.
        var rows = SnapshotRows(region);

        Assert.Equal("none paid", rows.Single(row => row.Key == "forwardAnnualRate").Drawn);
        Assert.Null(rows.Single(row => row.Key == "revenue").Beneath);
        Assert.Equal("absent", rows.Single(row => row.Key == "cash").Stored);
        Assert.Contains("<dd class=\"degraded\" data-cash=\"absent\">not filed</dd>", region, StringComparison.Ordinal);
    }
}
