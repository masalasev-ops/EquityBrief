using System.Net;
using System.Text.RegularExpressions;
using EquityBrief.Tests.Harness;
using EquityBrief.Web.Marks;

namespace EquityBrief.Tests.Reading;

// read-surface, the 3.4 correction: the level summary reads from the highest band to the lowest with the
// close as a row between the resistance and the support, each band's collapsed line says what its evidence
// is, an average and a shelf are named by kind with no date, and the strength carries a bar a point long
// with the key saying what a point is.
public partial class ReadSurface
{
    // Worked by hand over five constructed bands and a close of 400: one resistance above and four
    // supports below, one of them the 200-day average alone.
    [Fact]
    public void TheLevelSummaryReadsDownwardThroughTheCloseAndSaysWhatEachBandsEvidenceIs()
    {
        SummaryBand[] bands =
        [
            new(353.69m, 355.81m, "support", false, 4, true,
            [
                new SummaryMember("swing low", 353.69m, new DateOnly(2026, 7, 29)),
                new SummaryMember("touch", 354.5m, new DateOnly(2026, 8, 3)),
                new SummaryMember("swing low", 355.81m, new DateOnly(2026, 8, 5)),
            ]),
            new(289.3515m, 289.3515m, "support", false, 1, false, [new SummaryMember("sma200", 289.3515m, new DateOnly(2026, 9, 25))]),
            new(428.88m, 428.88m, "resistance", true, 2, true, [new SummaryMember("swing high", 428.88m, new DateOnly(2026, 8, 20))]),
            new(382.11m, 383.9m, "support", true, 7, true,
            [
                new SummaryMember("swing low", 382.11m, new DateOnly(2026, 9, 10)),
                new SummaryMember("swing high", 383.9m, new DateOnly(2026, 9, 10)),
                new SummaryMember("sma50", 383.2m, new DateOnly(2026, 9, 25)),
                new SummaryMember("retracement 61.8", 382.5m, new DateOnly(2026, 9, 10)),
                new SummaryMember("shelf", 383.0m, new DateOnly(2026, 9, 25)),
            ]),
            new(371.5m, 371.5m, "support", false, 3, true, [new SummaryMember("swing low", 371.5m, new DateOnly(2026, 8, 12))]),
        ];

        var summary = WebUtility.HtmlDecode(new MarkRenderer().LevelSummary("HUM", bands, [], close: 400m));

        // Highest price first, the close between the one resistance band and the supports.
        Assert.Equal(
            ["band 428.88", "close 400", "band 382.11", "band 371.5", "band 353.69", "band 289.3515"],
            Regex.Matches(summary, "<tr class=\"(band|close-row)\" data-(?:low-edge|close)=\"([^\"]+)\"").Select(match => (match.Groups[1].Value == "band" ? "band " : "close ") + match.Groups[2].Value));
        Assert.Contains("<tr class=\"close-row\" data-close=\"400\"><td>400.00</td><td colspan=\"4\">the close</td></tr>", summary, StringComparison.Ordinal);

        string Line(string lowEdge) =>
            Regex.Match(summary, $"data-low-edge=\"{Regex.Escape(lowEdge)}\"[^>]*>.*?<summary>([^<]*)</summary>").Groups[1].Value;

        // The turns counted by kind, with the first and last dates, or the one date where there is one turn
        // or the turns share a session; the rest by kind.
        Assert.Equal("turned the price 3 times: 2 swing lows, 1 touch; first 2026-07-29, last 2026-08-05", Line("353.69"));
        Assert.Equal("turned the price once: 1 swing high; on 2026-08-20", Line("428.88"));
        Assert.Equal("turned the price twice: 1 swing high, 1 swing low; on 2026-09-10; also the 50-day average, the 61.8% retracement and a heavy volume shelf", Line("382.11"));

        // A band made of the 200-day average alone is named by kind, and neither its line nor its member
        // carries the night's date, which is not a date of its own.
        Assert.Equal("the 200-day average", Line("289.3515"));

        var averageRow = Regex.Match(summary, "data-low-edge=\"289.3515\".*?</tr>").Value;

        Assert.Contains("<li class=\"member\" data-kind=\"sma200\" data-date=\"2026-09-25\" data-price=\"289.3515\">the 200-day average at 289.35</li>", averageRow, StringComparison.Ordinal);
        Assert.DoesNotContain(">the 200-day average at 289.35 on", averageRow, StringComparison.Ordinal);
        Assert.Contains(">a heavy volume shelf at 383.00</li>", summary, StringComparison.Ordinal);
        Assert.Contains(">the 61.8% retracement at 382.50, of the move that ended 2026-09-10</li>", summary, StringComparison.Ordinal);
        Assert.Contains(">swing low at 353.69 on 2026-07-29</li>", summary, StringComparison.Ordinal);
        Assert.DoesNotContain("member(s)", summary, StringComparison.Ordinal);

        // The bar is three pixels a point beside the number, and the key says what a point is.
        Assert.Contains("<td class=\"strength\" data-strength=\"7\"><span class=\"str-bar\" style=\"width:21px\" aria-hidden=\"true\"></span>7</td>", summary, StringComparison.Ordinal);
        Assert.Contains("<td class=\"strength\" data-strength=\"1\"><span class=\"str-bar\" style=\"width:3px\" aria-hidden=\"true\"></span>1</td>", summary, StringComparison.Ordinal);
        Assert.Contains("<p class=\"level-key\" data-recent=\"20\"><b>Strength.</b> One point for each piece of evidence in a band: each swing, each visit the price paid it, and each average, retracement and volume shelf inside it. One more where any of it came in the last 20 sessions, one where a retracement and a swing agree, and one where a heavy volume shelf sits in it.", summary, StringComparison.Ordinal);

        // With no close to draw, no close row and the same order.
        var closeless = new MarkRenderer().LevelSummary("HUM", bands);

        Assert.DoesNotContain("close-row", closeless, StringComparison.Ordinal);

        // Every band a resistance: the close row is drawn last, beneath them all.
        var above = new MarkRenderer().LevelSummary("HUM", [bands[2]], [], close: 400m);

        Assert.True(above.IndexOf("close-row", StringComparison.Ordinal) > above.IndexOf("data-low-edge=\"428.88\"", StringComparison.Ordinal));
    }

    // Off the fixture name's own page: the rows run from the highest band to the lowest with the close
    // between the resistance and the support, every collapsed line says what the evidence is, and a band
    // holding only averages or a shelf carries no date in its line.
    [Fact]
    public async Task TheNamePagesLevelSummaryRunsDownwardThroughTheCloseAndNamesEachBandsEvidence()
    {
        using var store = await WithBands();
        using var host = new Host(store.Root);
        using var client = host.CreateClient();

        var page = WebUtility.HtmlDecode(await client.GetStringAsync($"/screens/name/{Name}"));
        var table = Regex.Match(page, "<table class=\"level-summary\".*?</table>", RegexOptions.Singleline).Value;

        Assert.NotEmpty(table);

        var rows = Regex.Matches(table, "<tr class=\"(band|close-row)\"(?: data-low-edge=\"([^\"]+)\"[^>]*data-role=\"([^\"]+)\")?")
            .Select(match => (Kind: match.Groups[1].Value, Low: match.Groups[2].Success ? decimal.Parse(match.Groups[2].Value, System.Globalization.CultureInfo.InvariantCulture) : 0m, Role: match.Groups[3].Value))
            .ToArray();

        var close = rows.Select((row, at) => (row, at)).Single(one => one.row.Kind == "close-row").at;
        var above = rows[..close];
        var below = rows[(close + 1)..];

        Assert.NotEmpty(above);
        Assert.NotEmpty(below);
        Assert.All(above, row => Assert.Equal("resistance", row.Role));
        Assert.All(below, row => Assert.Equal("support", row.Role));
        Assert.Equal(above.Select(row => row.Low).OrderByDescending(low => low), above.Select(row => row.Low));
        Assert.Equal(below.Select(row => row.Low).OrderByDescending(low => low), below.Select(row => row.Low));

        foreach (Match band in Regex.Matches(table, "<tr class=\"band\".*?</tr>", RegexOptions.Singleline))
        {
            var line = Regex.Match(band.Value, "<summary>([^<]*)</summary>").Groups[1].Value;
            var kinds = Regex.Matches(band.Value, "data-kind=\"([^\"]+)\"").Select(match => match.Groups[1].Value).ToArray();

            Assert.Matches("^(turned the price (once|twice|\\d+ times): .+; (on|first) \\d{4}-\\d{2}-\\d{2}.*|(the |a heavy).+)$", line);

            if (kinds.All(kind => kind.StartsWith("sma", StringComparison.Ordinal) || kind == "shelf"))
            {
                Assert.DoesNotMatch("\\d{4}-\\d{2}-\\d{2}", line);
            }
        }

        Assert.Contains("<p class=\"level-key\"", page, StringComparison.Ordinal);
        Assert.DoesNotContain("member(s)", table, StringComparison.Ordinal);
    }
}
