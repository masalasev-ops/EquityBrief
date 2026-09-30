using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using EquityBrief.Api.Reading;
using EquityBrief.Core.Research;
using EquityBrief.Core.Time;
using EquityBrief.Tests.Checks;
using EquityBrief.Web.App;
using EquityBrief.Web.Marks;

namespace EquityBrief.Tests.Reading;

// read-surface, the comparison command: the drafts a trial or a review wrote beside a report written to a file of the
// report's own and drawn on no page; each section's rates over the ten reports each side of the addendum's merge
// written once the tenth after it has been written; the two cases' figures on both sides over the twenty reports since
// their ask changed written at the twentieth, beside the drafts measured before; and nothing written to the store.
// see: The drafts compared beside a report and the research template's before and after counts are written to files by a command, and drawn on no page
public partial class ReadSurface
{
    // One report at an instant, under a ticker of its own: its pass's row warranting the sections given, each written
    // once at the first draft, accepted or left out as given, with one answered call; the two cases' draft carrying a
    // figure on both sides where asked.
    static void AReport(List<RunStageRow> rows, List<WrittenVersion> versions, DateTimeOffset at, string ticker, (string Section, bool Passed)[] sections, bool bothSides = false)
    {
        var run = PassRun.IdFor(at, ticker);
        var day = at.UtcDateTime.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        string Instant(DateTimeOffset instant) => instant.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);

        rows.Add(LogRow(run, ReadApi.ResearchStage, Instant(at), Instant(at.AddMinutes(5)), detail: PassDetailJson(
            ticker,
            day,
            [.. sections.Select(one => one.Section)],
            [.. sections.Select(one => (one.Section, 1, false))])));
        rows.Add(LogRow(run, "research call: " + sections[0].Section, Instant(at.AddMinutes(1)), Instant(at.AddMinutes(2)), spend: "0.001"));
        versions.AddRange(sections.Select(one => new WrittenVersion(
            ticker,
            one.Section,
            1,
            one.Passed ? "accepted" : "fallback",
            one.Section == ClaimRules.TwoCasesSection ? (bothSides ? BothSidesDraft : CleanDraft) : "prose [D1].",
            one.Passed ? null : "rejected twice: an uncited sentence")));
    }

    static string Named(string side, int place) => side + place.ToString("00", CultureInfo.InvariantCulture);

    [Fact]
    public void TheRatesBeforeAndAfterTheAddendumAreWrittenOnceTheTenthReportAfterItHasBeenWritten()
    {
        var merged = ComparisonView.AddendumMergedAt;

        Assert.Equal(new DateTimeOffset(2026, 9, 30, 11, 40, 11, TimeSpan.Zero), merged);
        Assert.Equal(10, ComparisonView.RatesWindow);

        // Eleven reports before the merge, the newest a second before it: the cause passed first time on the newest
        // four and was left out on the six before them, and passed on the oldest, which the window of ten leaves out.
        // Worked by hand: before, 4 of 10 first time and 6 of 10 left out.
        var rows = new List<RunStageRow>();
        var versions = new List<WrittenVersion>();

        for (var place = 1; place <= 11; place++)
        {
            var at = place == 1 ? merged.AddSeconds(-1) : merged.AddDays(-place);

            AReport(rows, versions, at, Named("B", place), [(ClaimRules.CauseSection, place <= 4 || place == 11)]);
        }

        // Ten after, the first at the merge's own second: the cause passed first time on all but the fifth, and the
        // calendar was warranted by the first three, passing on the first two. Worked by hand: after, the cause 9 of 10
        // first time and 1 of 10 left out, the calendar 2 of 3 and 1 of 3, and before, the calendar 0 of 0.
        void After(int place) =>
            AReport(rows, versions, merged.AddDays(place - 1), Named("A", place), place <= 3
                ? [(ClaimRules.CauseSection, place != 5), (ClaimRules.CalendarSection, place <= 2)]
                : [(ClaimRules.CauseSection, place != 5)]);

        for (var place = 1; place <= 9; place++)
        {
            After(place);
        }

        // Nine after: no file yet.
        var nine = RunScreen.Comparisons(rows, versions);

        Assert.Equal((10, 9, false), (nine.Before.Count, nine.After.Count, nine.RatesReady));
        Assert.DoesNotContain(new ComparisonFiles(new MarkRenderer()).Files(nine), file => file.Name == ComparisonFiles.RatesFile);

        After(10);

        var ten = RunScreen.Comparisons(rows, versions);

        Assert.True(ten.RatesReady);
        Assert.Equal(Named("B", 1), ten.Before[0].Ticker);
        Assert.DoesNotContain(ten.Before, report => report.Ticker == Named("B", 11));
        Assert.Equal(Named("A", 1), ten.After[^1].Ticker);

        SectionRate Of(IReadOnlyList<SectionRate> rates, string section) => Assert.Single(rates, rate => rate.Section == section);

        Assert.Equal((10, 4, 6), (Of(ten.RatesBefore, ClaimRules.CauseSection).Reports, Of(ten.RatesBefore, ClaimRules.CauseSection).FirstTime, Of(ten.RatesBefore, ClaimRules.CauseSection).LeftOut));
        Assert.Equal((10, 9, 1), (Of(ten.RatesAfter, ClaimRules.CauseSection).Reports, Of(ten.RatesAfter, ClaimRules.CauseSection).FirstTime, Of(ten.RatesAfter, ClaimRules.CauseSection).LeftOut));
        Assert.Equal((0, 0, 0), (Of(ten.RatesBefore, ClaimRules.CalendarSection).Reports, Of(ten.RatesBefore, ClaimRules.CalendarSection).FirstTime, Of(ten.RatesBefore, ClaimRules.CalendarSection).LeftOut));
        Assert.Equal((3, 2, 1), (Of(ten.RatesAfter, ClaimRules.CalendarSection).Reports, Of(ten.RatesAfter, ClaimRules.CalendarSection).FirstTime, Of(ten.RatesAfter, ClaimRules.CalendarSection).LeftOut));

        // Read back off the file.
        var file = WebUtility.HtmlDecode(Assert.Single(new ComparisonFiles(new MarkRenderer()).Files(ten), one => one.Name == ComparisonFiles.RatesFile).Document);

        Assert.Contains("data-section=\"" + ClaimRules.CauseSection + "\" data-first-before=\"4\" data-first-after=\"9\" data-left-out-before=\"6\" data-left-out-after=\"1\" data-reports-before=\"10\" data-reports-after=\"10\"", file, StringComparison.Ordinal);
        Assert.Contains("<td class=\"r\">4 of 10</td><td class=\"r\">9 of 10</td><td class=\"r\">6 of 10</td><td class=\"r\">1 of 10</td>", file, StringComparison.Ordinal);
        Assert.Contains("<td class=\"r\">0 of 0</td><td class=\"r\">2 of 3</td><td class=\"r\">0 of 0</td><td class=\"r\">1 of 3</td>", file, StringComparison.Ordinal);
        Assert.Contains("data-comparison=\"rates\"", file, StringComparison.Ordinal);
    }

    [Fact]
    public void TheTwoCasesFiguresOnBothSidesAreWrittenAtTheTwentiethReportSinceTheAskChanged()
    {
        var asked = ComparisonView.AskChangedAt;

        Assert.Equal(new DateTimeOffset(2026, 9, 30, 4, 54, 22, TimeSpan.Zero), asked);
        Assert.Equal((20, 5, 8), (ComparisonView.BothSidesWindow, ComparisonView.BaselineBothSides, ComparisonView.BaselineDrafted));

        var rows = new List<RunStageRow>();
        var versions = new List<WrittenVersion>();

        // A report a second before the ask changed, carrying a figure on both sides, which is not counted; and one after
        // it that did not draft the two cases, which is not counted either.
        AReport(rows, versions, asked.AddSeconds(-1), "EARLY", [(ClaimRules.TwoCasesSection, true)], bothSides: true);
        AReport(rows, versions, asked.AddHours(1), "NOCASES", [(ClaimRules.CauseSection, true)]);

        // Reports drafting the two cases from the ask's own second, every third carrying a figure on both sides. Worked
        // by hand: of the first nineteen, the 3rd, 6th, 9th, 12th, 15th and 18th, six; of the first twenty the same six,
        // the twenty-first, which also carries one, falling outside the window.
        void Drafted(int place) =>
            AReport(rows, versions, asked.AddDays(place - 1), Named("C", place), [(ClaimRules.TwoCasesSection, true)], bothSides: place % 3 == 0);

        for (var place = 1; place <= 19; place++)
        {
            Drafted(place);
        }

        var nineteen = RunScreen.Comparisons(rows, versions);

        Assert.Equal((19, 6, false), (nineteen.SinceTheAsk.Count, nineteen.BothSides, nineteen.BothSidesReady));
        Assert.DoesNotContain(new ComparisonFiles(new MarkRenderer()).Files(nineteen), file => file.Name == ComparisonFiles.BothSidesFile);

        Drafted(20);
        Drafted(21);

        var twenty = RunScreen.Comparisons(rows, versions);

        Assert.Equal((20, 6, true), (twenty.SinceTheAsk.Count, twenty.BothSides, twenty.BothSidesReady));
        Assert.DoesNotContain(twenty.SinceTheAsk, report => report.Ticker is "EARLY" or "NOCASES" || report.Ticker == Named("C", 21));

        var file = WebUtility.HtmlDecode(Assert.Single(new ComparisonFiles(new MarkRenderer()).Files(twenty), one => one.Name == ComparisonFiles.BothSidesFile).Document);

        Assert.Contains("6 of the 20 reports since the ask changed carried a figure on both sides of the two cases, first draft or retry, against 5 of 8 accepted drafts measured before it.", file, StringComparison.Ordinal);
        Assert.Equal(20, file.Split("<li data-run=").Length - 1);
        Assert.Equal(6, file.Split("data-both-sides=\"yes\"").Length - 1);
    }

    [Fact]
    public async Task EachReportsComparedDraftsAreWrittenToAFileOfItsOwnAndDrawnOnNoPage()
    {
        using var store = EclStore();
        var api = Api(store);
        var folder = Path.Combine(store.Root, ComparisonFiles.Folder);

        // The store's bytes, read beside the connection the read API keeps open on it.
        byte[] Hashed()
        {
            using var file = new FileStream(store.DatabaseFile, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);

            return SHA256.HashData(file);
        }

        var before = Hashed();

        var lines = await ComparisonCommand.WriteAsync(api, new ComparisonFiles(new MarkRenderer()), folder);

        // One file, ECL's report of 2026-09-10, and a line for each comparison whose window is not yet full.
        Assert.Equal(["ECL_comparison_2026-09-10.html"], Directory.GetFiles(folder).Select(Path.GetFileName));
        Assert.Contains("wrote ECL_comparison_2026-09-10.html", lines);
        Assert.Contains(lines, line => line.StartsWith("section rates: 0 of 10 reports written since the addendum merged and 1 of 10 before it", StringComparison.Ordinal));
        Assert.Contains(lines, line => line.StartsWith("two cases on both sides: 0 of 20 reports drafting them since the ask changed", StringComparison.Ordinal));

        // The file carries the pass's side and the trial's with their drafts side by side, every disclosure open and
        // no link into the application.
        var file = WebUtility.HtmlDecode(await File.ReadAllTextAsync(Path.Combine(folder, "ECL_comparison_2026-09-10.html")));

        Assert.Contains("data-comparison=\"drafts\"", file, StringComparison.Ordinal);
        Assert.Contains("data-side=\"pass\" data-model=\"deepseek-flash\" data-outcome=\"" + ReportsView.OnRetry + "\" data-rounds=\"2\" data-cost=\"0.0050\"", file, StringComparison.Ordinal);
        Assert.Contains("data-side=\"trial\" data-model=\"claude-sonnet-5-5 thinking off\" data-outcome=\"first time\" data-rounds=\"1\" data-cost=\"0.0500\"", file, StringComparison.Ordinal);
        Assert.Contains("<details open class=\"trial-drafts\"><summary>The 2 drafts side by side</summary>", file, StringComparison.Ordinal);
        Assert.DoesNotContain("href=\"#/", file, StringComparison.Ordinal);
        Assert.Contains(".trial-sides,.trial-pair{display:grid;grid-auto-flow:column;grid-auto-columns:minmax(0,1fr);gap:16px}", file, StringComparison.Ordinal);

        // Nothing written to the store.
        Assert.Equal(before, Hashed());

        // Written again, the one file is the same file.
        await ComparisonCommand.WriteAsync(api, new ComparisonFiles(new MarkRenderer()), folder);

        Assert.Single(Directory.GetFiles(folder));

        // The run page draws the report region and no section a trial asked for, though the trial wrote its row.
        using var host = new PassHost(store.Root) { Clock = FixedClock.At(DateTimeOffset.Parse("2026-09-11T12:00:00Z", CultureInfo.InvariantCulture), SessionZones.UnitedStates) };
        using var client = host.CreateClient();
        var page = await client.GetStringAsync("/screens/run/2026-09-10");

        Assert.Contains("data-fold=\"reports\"", page, StringComparison.Ordinal);
        Assert.DoesNotContain("data-card=\"trials\"", page, StringComparison.Ordinal);
        Assert.DoesNotContain("class=\"trials\"", page, StringComparison.Ordinal);
    }

    [Fact]
    public void TwoReportsOfOneStockOnOneDayAreTwoFiles()
    {
        Assert.Equal("MDT_comparison_2026-09-29.html", ComparisonFiles.DraftsFile("MDT", new DateOnly(2026, 9, 29), 1));
        Assert.Equal("MDT_comparison_2026-09-29_2.html", ComparisonFiles.DraftsFile("MDT", new DateOnly(2026, 9, 29), 2));
        Assert.Equal("BRK.B_comparison_2026-09-29.html", ComparisonFiles.DraftsFile("BRK.B", new DateOnly(2026, 9, 29), 1));
    }
}
