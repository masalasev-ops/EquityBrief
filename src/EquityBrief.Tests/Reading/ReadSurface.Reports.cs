using System.Globalization;
using System.Net;
using System.Text.Json;
using EquityBrief.Api.Reading;
using EquityBrief.Core.Research;
using EquityBrief.Core.Time;
using EquityBrief.Tests.Checks;
using EquityBrief.Tests.Harness;
using EquityBrief.Web.App;
using EquityBrief.Web.Marks;
using EquityBrief.Worker.Research;

namespace EquityBrief.Tests.Reading;

// read-surface, how each report did and the section trials: each report's cells, costs, the rates over the newest
// twenty reports and the both-sides count worked by hand over constructed run logs and versions, read through the
// read API's own queries over a constructed store and back off the rendered page, and a trial's two sides drawn
// with their drafts side by side.
// see: The run page draws how each report's sections came out and each section's rates over the newest twenty reports
// see: A trial asks a second profile for named sections after a report and records its drafts beside the report, never in it
public partial class ReadSurface
{
    // The claims the 12.6 correction drawing how each report did adds, which this check reaches and the phase's
    // pair names. Declared before the reach that takes them in.
    internal static readonly string[] ReportClaims =
    [
        CheckReach.Key("15.10 Run", "How each report did, one row per report over the seven nights with its stock and day and what it cost"),
        CheckReach.Key("15.10 Run", "How each report did, a cell per section saying whether it passed first time or on retry or was left out with why or was not warranted, with what its own calls cost and none of a trial's"),
        CheckReach.Key("15.10 Run", "How each report did, the two cases' cell marked where a draft of the pass carried a figure on both sides, with how many of the newest twenty reports' two cases did"),
        CheckReach.Key("15.10 Run", "How each report did, each section's share passed first time and its share left out over the newest twenty reports that warranted it"),
        CheckReach.Key("15.10 Run", "Section trials, one row per section a trial or a review asked for with its report and each model's outcome and rounds and cost"),
        CheckReach.Key("15.10 Run", "Section trials, the drafts side by side folded beneath"),
        CheckReach.Key("15.10 Run", "Section trials, drawn only while a trial or a review has written a row"),
        CheckReach.Key(Scope.LimitsTable, "Report rates"),
    ];

    const string EclRun = "research-20260910T220000Z-ECL";
    const string CycleDeclined = "the theme could not be refreshed: declined for lack of industry sources: the searches found 2 page(s) about Specialty Chemicals, from statista.com and bls.gov, and the model wrote nothing from them";
    const string RisksRefused = "rejected twice: a figure the facts file does not hold: 12%; a figure the facts file does not hold: 12%";
    const string BothSidesDraft = "The bull case is that margins reached 18.5% [D1].\n\nThe bear case is that margins of 18.5% may not hold [D1].";
    const string CleanDraft = "The bull case is that orders keep rising [D1].\n\nThe bear case is that supply stays short [D1].";
    const string TrialDraft = "The bull case is that demand is broad [D1].\n\nThe bear case is that pricing is soft [D1].";

    static string PassDetailJson(
        string ticker,
        string day,
        string[] warranted,
        (string Section, int Version, bool Retry)[] written,
        (string Section, string Reason)[]? notWritten = null) =>
        JsonSerializer.Serialize(new
        {
            ticker,
            asOf = day,
            outcome = "written",
            warranted,
            written = written.Select(one => new { section = one.Section, version = one.Version, model = "deepseek-flash", retry = one.Retry }),
            notWritten = (notWritten ?? []).Select(one => new { section = one.Section, reason = one.Reason }),
        });

    // ECL's pass on 2026-09-10: the cause passed at its first draft; the two cases were refused at the first, whose
    // draft carried 18.5% on both sides, and passed on the retry; the risks were refused twice; the short version's
    // model returned nothing; the industry cycle was declined; the other four sections stood from earlier. A trial
    // asked a second model for the two cases beside it. And GM's pass the same evening, whose only call the cap
    // paused, which is no report.
    static readonly RunStageRow[] EclPass =
    [
        LogRow(EclRun, ReadApi.ResearchStage, "2026-09-10T22:00:00Z", "2026-09-10T22:10:00Z", detail: PassDetailJson(
            "ECL",
            "2026-09-10",
            [ClaimRules.CauseSection, ClaimRules.CycleSection, ClaimRules.TwoCasesSection, RiskFields.Section, ClaimRules.Sections[^1]],
            [(ClaimRules.CauseSection, 3, false), (ClaimRules.TwoCasesSection, 5, false), (ClaimRules.TwoCasesSection, 6, true), (RiskFields.Section, 2, false), (RiskFields.Section, 3, true)],
            [(ClaimRules.CycleSection, CycleDeclined), (ClaimRules.Sections[^1], "the research model returned nothing")])),
        LogRow(EclRun, ReadApi.ThemeStage, "2026-09-10T22:01:00Z", "2026-09-10T22:02:00Z", "declined", detail: JsonSerializer.Serialize(new { theme = "Specialty Chemicals", asOf = "2026-09-10", outcome = "declined", written = Array.Empty<object>(), notWritten = Array.Empty<object>() })),
        LogRow(EclRun, "research call: " + ClaimRules.CycleSection, "2026-09-10T22:01:30Z", "2026-09-10T22:01:40Z", spend: "0.0006"),
        LogRow(EclRun, "research call: " + ClaimRules.CauseSection, "2026-09-10T22:03:00Z", "2026-09-10T22:03:10Z", spend: "0.0010"),
        LogRow(EclRun, "research call: " + ClaimRules.TwoCasesSection, "2026-09-10T22:03:20Z", "2026-09-10T22:03:30Z", spend: "0.0020"),
        LogRow(EclRun, "research call: " + RiskFields.Section, "2026-09-10T22:03:40Z", "2026-09-10T22:03:50Z", spend: "0.0040"),
        LogRow(EclRun, "research call: " + ClaimRules.TwoCasesSection + ", round 2", "2026-09-10T22:04:00Z", "2026-09-10T22:04:10Z", spend: "0.0030"),
        LogRow(EclRun, "research call: " + RiskFields.Section + ", round 2", "2026-09-10T22:04:20Z", "2026-09-10T22:04:30Z", spend: "0.0050"),
        LogRow(EclRun, "research call: " + ClaimRules.Sections[^1] + ", round 2", "2026-09-10T22:05:00Z", "2026-09-10T22:05:10Z", "refused"),
        LogRow(EclRun, "research call: " + ClaimRules.TwoCasesSection + ", " + TrialCalls.Round, "2026-09-10T22:11:00Z", "2026-09-10T22:11:10Z", spend: "0.0500"),
        LogRow(EclRun, SectionTrial.StageFor(ClaimRules.TwoCasesSection), "2026-09-10T22:11:00Z", "2026-09-10T22:11:10Z", "first time", detail: JsonSerializer.Serialize(new
        {
            ticker = "ECL",
            section = ClaimRules.TwoCasesSection,
            profile = "claude-sonnet-no-thinking",
            model = "claude-sonnet-5-5 thinking off",
            compared = new { version = 5, model = "deepseek-flash", status = "rejected", prose = BothSidesDraft },
            rounds = new[] { new { round = 1, prose = TrialDraft, verdict = "accepted", price = "0.0500" } },
            outcome = "first time",
            calls = 1,
            cost = "0.0500",
        })),
        LogRow("research-20260910T230000Z-GM", ReadApi.ResearchStage, "2026-09-10T23:00:00Z", "2026-09-10T23:00:10Z", detail: PassDetailJson("GM", "2026-09-10", [ClaimRules.TwoCasesSection], [])),
        LogRow("research-20260910T230000Z-GM", "research call: " + ClaimRules.TwoCasesSection, "2026-09-10T23:00:05Z", "2026-09-10T23:00:05Z", "paused"),
    ];

    static void EclVersions(TemporaryStore store)
    {
        foreach (var (section, version, status, prose, reason) in new (string, int, string, string, string?)[]
        {
            (ClaimRules.CauseSection, 2, "accepted", "An earlier cause [D1].", null),
            (ClaimRules.CauseSection, 3, "accepted", "The move followed the release [D1].", null),
            (ClaimRules.TwoCasesSection, 5, "rejected", BothSidesDraft, "a figure the facts file does not hold: 18.5%"),
            (ClaimRules.TwoCasesSection, 6, "accepted", CleanDraft, null),
            (RiskFields.Section, 2, "rejected", "The first risk is pricing [D1].", "a figure the facts file does not hold: 12%"),
            (RiskFields.Section, 3, "fallback", "The first risk is pricing [D1].", RisksRefused),
        })
        {
            store.Execute(
                "INSERT INTO research_section (ticker, section, version, as_of, model, status, prose, source_ids, reject_reason) " +
                $"VALUES ('ECL', '{section}', {version}, '2026-09-10', 'deepseek-flash', '{status}', '{prose.Replace("'", "''", StringComparison.Ordinal)}', '[]', {(reason is null ? "NULL" : "'" + reason + "'")});");
        }
    }

    static TemporaryStore EclStore()
    {
        var store = Logged([.. FinishedNight, .. EclPass]);

        EclVersions(store);

        return store;
    }

    static ReportCell CellOf(ReportRow report, string section) => Assert.Single(report.Cells, cell => cell.Section == section);

    [Fact]
    public async Task EachReportsSectionsAreReadAsTheyCameOutWithWhatTheirOwnCallsCost()
    {
        using var store = EclStore();
        var api = Api(store);
        var night = new DateOnly(2026, 9, 10);

        // The worker's own stage words, which the read surface states because it cannot reference them.
        Assert.Equal(ThemeResearchRunner.Stage, ReadApi.ThemeStage);
        Assert.Equal(SectionTrial.Stage, ReadApi.TrialStage);

        var view = RunScreen.Reports(await api.ReportRowsAsync(), await api.ReportVersionsAsync(), night.AddDays(-6), night);

        // GM's pass made no call that answered, so it is no report.
        var ecl = Assert.Single(view.Reports);

        Assert.Equal(("ECL", night), (ecl.Ticker, ecl.Day));
        Assert.Equal(ClaimRules.Sections, ecl.Cells.Select(cell => cell.Section));

        Assert.Equal((ReportsView.FirstTime, 0.0010m, 1), (CellOf(ecl, ClaimRules.CauseSection).Outcome, CellOf(ecl, ClaimRules.CauseSection).Cost, CellOf(ecl, ClaimRules.CauseSection).Drafts));
        Assert.Equal((ReportsView.OnRetry, 0.0050m, 2), (CellOf(ecl, ClaimRules.TwoCasesSection).Outcome, CellOf(ecl, ClaimRules.TwoCasesSection).Cost, CellOf(ecl, ClaimRules.TwoCasesSection).Drafts));
        Assert.Equal((ReportsView.LeftOut, NameScreen.Refused(RisksRefused), 0.0090m), (CellOf(ecl, RiskFields.Section).Outcome, CellOf(ecl, RiskFields.Section).Why, CellOf(ecl, RiskFields.Section).Cost));
        Assert.Equal((ReportsView.LeftOut, "the research model returned nothing", 0m), (CellOf(ecl, ClaimRules.Sections[^1]).Outcome, CellOf(ecl, ClaimRules.Sections[^1]).Why, CellOf(ecl, ClaimRules.Sections[^1]).Cost));
        Assert.Equal((ReportsView.LeftOut, CycleDeclined, 0.0006m), (CellOf(ecl, ClaimRules.CycleSection).Outcome, CellOf(ecl, ClaimRules.CycleSection).Why, CellOf(ecl, ClaimRules.CycleSection).Cost));

        foreach (var stood in new[] { "What the company sells", "The segment commentary", "The key under each figure", "The dated calendar items" })
        {
            Assert.Equal((ReportsView.NotWarranted, 0m), (CellOf(ecl, stood).Outcome, CellOf(ecl, stood).Cost));
        }

        // The report's cost is every call of the pass, the refused one costing nothing.
        Assert.Equal(0.0156m, ecl.Cost);

        // Read back off the rendered page: the fold and the row, each cell's outcome, the reasons numbered beneath.
        using var host = new PassHost(store.Root) { Clock = FixedClock.At(DateTimeOffset.Parse("2026-09-11T12:00:00Z", CultureInfo.InvariantCulture), SessionZones.UnitedStates) };
        using var client = host.CreateClient();
        var page = WebUtility.HtmlDecode(await client.GetStringAsync("/screens/run/2026-09-10"));

        Assert.Contains("data-fold=\"reports\"", page, StringComparison.Ordinal);
        Assert.Contains("<tr data-run=\"" + EclRun + "\" data-ticker=\"ECL\">", page, StringComparison.Ordinal);
        Assert.Contains("data-section=\"" + ClaimRules.TwoCasesSection + "\" data-outcome=\"" + ReportsView.OnRetry + "\" data-cost=\"0.0050\"", page, StringComparison.Ordinal);
        Assert.Contains("data-section=\"" + ClaimRules.CauseSection + "\" data-outcome=\"" + ReportsView.FirstTime + "\" data-cost=\"0.0010\"", page, StringComparison.Ordinal);
        Assert.Contains("ECL, Cycle: " + CycleDeclined, page, StringComparison.Ordinal);
        Assert.Contains("ECL, Risks: " + NameScreen.Refused(RisksRefused), page, StringComparison.Ordinal);
        Assert.Contains("Showing 1 of 1 report, those written over the last seven nights", page, StringComparison.Ordinal);
    }

    // A run of reports on consecutive days, newest last, each warranting the sections it is given and writing each at
    // its first draft, accepted or refused as the day asks.
    static (RunStageRow[] Rows, WrittenVersion[] Versions) Reports(int days, Func<int, IEnumerable<(string Section, bool Passed)>> sections)
    {
        var rows = new List<RunStageRow>();
        var versions = new List<WrittenVersion>();

        for (var day = 1; day <= days; day++)
        {
            var date = new DateOnly(2026, 8, 1).AddDays(day - 1);
            var dated = date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            var run = "research-" + date.ToString("yyyyMMdd", CultureInfo.InvariantCulture) + "T220000Z-" + Named(day);
            var written = sections(day).ToArray();

            rows.Add(LogRow(run, ReadApi.ResearchStage, dated + "T22:00:00Z", dated + "T22:05:00Z", detail: PassDetailJson(
                Named(day),
                dated,
                [.. written.Select(one => one.Section)],
                [.. written.Select(one => (one.Section, 1, false))])));
            rows.Add(LogRow(run, "research call: " + written[0].Section, dated + "T22:01:00Z", dated + "T22:01:10Z", spend: "0.001"));
            versions.AddRange(written.Select(one => new WrittenVersion(Named(day), one.Section, 1, one.Passed ? "accepted" : "fallback", "prose [D1].", one.Passed ? null : "rejected twice: an uncited sentence")));
        }

        return ([.. rows], [.. versions]);

        static string Named(int day) => "N" + day.ToString("00", CultureInfo.InvariantCulture);
    }

    [Fact]
    public void EachSectionsRatesAreReadOverTheNewestTwentyReportsThatWarrantedIt()
    {
        // Twenty-two reports. The cause passed first time on the oldest twelve and was left out on the newest ten, so
        // over the newest twenty it passed on ten and was left out on ten; over all twenty-two it would be twelve.
        // The calendar was warranted by the newest three alone, and the key under each figure by none.
        var (rows, versions) = Reports(22, day => day >= 20
            ? [(ClaimRules.CauseSection, day <= 12), (ClaimRules.CalendarSection, true)]
            : [(ClaimRules.CauseSection, day <= 12)]);

        var view = RunScreen.Reports(rows, versions, new DateOnly(2026, 8, 16), new DateOnly(2026, 8, 22));
        SectionRate RateOf(string section) => Assert.Single(view.Rates, rate => rate.Section == section);

        Assert.Equal(20, ReportsView.RateWindow);
        Assert.Equal((20, 10, 10), (RateOf(ClaimRules.CauseSection).Reports, RateOf(ClaimRules.CauseSection).FirstTime, RateOf(ClaimRules.CauseSection).LeftOut));
        Assert.Equal((3, 3, 0), (RateOf(ClaimRules.CalendarSection).Reports, RateOf(ClaimRules.CalendarSection).FirstTime, RateOf(ClaimRules.CalendarSection).LeftOut));
        Assert.Equal((0, 0, 0), (RateOf("The key under each figure").Reports, RateOf("The key under each figure").FirstTime, RateOf("The key under each figure").LeftOut));

        // The table draws the reports of the seven nights, and every report is held for the rates.
        Assert.Equal((7, 22), (view.Reports.Count, view.Held));

        // A night before the newest reads only the reports written up to it: on 2026-08-12 the cause passed on all
        // twelve, fewer than twenty, and the count read is stated.
        var earlier = RunScreen.Reports(rows, versions, new DateOnly(2026, 8, 6), new DateOnly(2026, 8, 12));

        Assert.Equal((12, 12, 0), (Assert.Single(earlier.Rates, rate => rate.Section == ClaimRules.CauseSection).Reports, Assert.Single(earlier.Rates, rate => rate.Section == ClaimRules.CauseSection).FirstTime, Assert.Single(earlier.Rates, rate => rate.Section == ClaimRules.CauseSection).LeftOut));

        // Read off the rendered table.
        var drawn = WebUtility.HtmlDecode(new MarkRenderer().ReportsRegion(view, SinglePageApp.NameRoute));

        Assert.Contains("data-section=\"" + ClaimRules.CauseSection + "\" data-reports=\"20\" data-first=\"10\" data-left-out=\"10\"", drawn, StringComparison.Ordinal);
        Assert.Contains("<td class=\"r\">10 of 20</td><td class=\"r\">10 of 20</td>", drawn, StringComparison.Ordinal);
        Assert.Contains("<td class=\"r\">3 of 3</td><td class=\"r\">0 of 3</td>", drawn, StringComparison.Ordinal);
        Assert.Contains("Showing 7 of 22 reports", drawn, StringComparison.Ordinal);
    }

    [Fact]
    public void AFigureOnBothSidesIsCountedFromEveryDraftOfTheTwoCasesAndNotWhereTheSidesCannotBeCut()
    {
        // Worked by hand: 7.25% and 7.3% agree within the coarser rounding, half a tenth; $5.94 and 5.94% do not,
        // one being a percentage; two different figures are neither; and a draft that does not open on the case for
        // cannot be cut, so it is read as nothing rather than as carrying nothing.
        Assert.Equal(["7.25%"], ClaimRules.FiguresOnBothSides("The bull case is that revenue grew 7.25% [D1].\n\nThe bear case is that growth near 7.3% may slow [D1]."));
        Assert.Empty(ClaimRules.FiguresOnBothSides("The bull case is that earnings reached $5.94 [D1].\n\nThe bear case is that 5.94% of sales went to rebates [D1].")!);
        Assert.Empty(ClaimRules.FiguresOnBothSides("The bull case is that margins reached 18.5% [D1].\n\nThe bear case is that debt rose 40% [D1].")!);
        Assert.Null(ClaimRules.FiguresOnBothSides("Revenue grew 7.25% [D1].\n\nThe bear case is that growth near 7.3% may slow [D1]."));

        // Three reports: the first's refused draft carried a figure on both sides and its accepted retry did not; the
        // second's one draft carried none; the third's could not be cut. One of the three drafted carried one.
        RunStageRow Pass(string run, string ticker, (string Section, int Version, bool Retry)[] written) =>
            LogRow(run, ReadApi.ResearchStage, "2026-09-10T22:00:00Z", "2026-09-10T22:05:00Z", detail: PassDetailJson(ticker, "2026-09-10", [ClaimRules.TwoCasesSection], written));

        RunStageRow Call(string run) => LogRow(run, "research call: " + ClaimRules.TwoCasesSection, "2026-09-10T22:01:00Z", "2026-09-10T22:01:10Z", spend: "0.001");

        RunStageRow[] rows =
        [
            Pass("research-20260910T220000Z-A", "A", [(ClaimRules.TwoCasesSection, 1, false), (ClaimRules.TwoCasesSection, 2, true)]), Call("research-20260910T220000Z-A"),
            Pass("research-20260910T220000Z-B", "B", [(ClaimRules.TwoCasesSection, 1, false)]), Call("research-20260910T220000Z-B"),
            Pass("research-20260910T220000Z-C", "C", [(ClaimRules.TwoCasesSection, 1, false)]), Call("research-20260910T220000Z-C"),
        ];

        WrittenVersion[] versions =
        [
            new("A", ClaimRules.TwoCasesSection, 1, "rejected", BothSidesDraft, "a figure the facts file does not hold: 18.5%"),
            new("A", ClaimRules.TwoCasesSection, 2, "accepted", CleanDraft, null),
            new("B", ClaimRules.TwoCasesSection, 1, "accepted", CleanDraft, null),
            new("C", ClaimRules.TwoCasesSection, 1, "rejected", "Margins reached 18.5% [D1].\n\nThe bear case is that margins of 18.5% may not hold [D1].", ClaimRules.TwoCasesWithoutSides),
        ];

        var view = RunScreen.Reports(rows, versions, new DateOnly(2026, 9, 4), new DateOnly(2026, 9, 10));

        Assert.Equal((1, 3), (view.BothSides, view.TwoCases));
        Assert.True(CellOf(Assert.Single(view.Reports, report => report.Ticker == "A"), ClaimRules.TwoCasesSection).BothSides);
        Assert.False(CellOf(Assert.Single(view.Reports, report => report.Ticker == "C"), ClaimRules.TwoCasesSection).BothSides);

        var drawn = WebUtility.HtmlDecode(new MarkRenderer().ReportsRegion(view, SinglePageApp.NameRoute));

        Assert.Contains("1 of the newest 3 reports' two cases carried a figure on both sides, first draft or retry.", drawn, StringComparison.Ordinal);
        Assert.Contains("<small class=\"both\">both sides</small>", drawn, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ATrialsSectionsAreDrawnWithBothModelsDraftsSideBySide()
    {
        using var store = EclStore();
        var api = Api(store);
        var night = new DateOnly(2026, 9, 10);

        var view = RunScreen.Reports(await api.ReportRowsAsync(), await api.ReportVersionsAsync(), night.AddDays(-6), night);
        var trial = Assert.Single(view.Trials);

        // The pass's side read off its own cell and versions, the trial's off its row.
        Assert.Equal(("ECL", ClaimRules.TwoCasesSection), (trial.Ticker, trial.Section));
        Assert.Equal(("deepseek-flash", ReportsView.OnRetry, 2, 0.0050m), (trial.Pass.Model, trial.Pass.Outcome, trial.Pass.Rounds, trial.Pass.Cost));
        Assert.Equal([BothSidesDraft, CleanDraft], trial.Pass.Drafts);
        var asked = Assert.Single(trial.Asked);

        Assert.Equal(("claude-sonnet-5-5 thinking off", "first time", 1, 0.0500m, TrialSide.OfTrial), (asked.Model, asked.Outcome, asked.Rounds, asked.Cost, asked.Side));
        Assert.Equal([TrialDraft], asked.Drafts);
        Assert.Null(trial.Review);

        // Drawn as a column a side folded beneath the row, one beneath another on a narrow screen, each round's
        // paragraphs whole.
        var drawn = WebUtility.HtmlDecode(new MarkRenderer().TrialsRegion(view.Trials, SinglePageApp.NameRoute));

        Assert.Contains("<details class=\"trial-drafts\"><summary>The 2 drafts side by side</summary><div class=\"trial-pair\">", drawn, StringComparison.Ordinal);
        Assert.Contains("<div class=\"trial-column\" data-side=\"pass\"><h4>deepseek-flash</h4><div class=\"trial-draft\" data-round=\"1\"><p class=\"trial-round\">Round 1</p><p>The bull case is that margins reached 18.5% [D1].</p><p>The bear case is that margins of 18.5% may not hold [D1].</p></div><div class=\"trial-draft\" data-round=\"2\">", drawn, StringComparison.Ordinal);
        Assert.Contains("<div class=\"trial-column\" data-side=\"trial\"><h4>claude-sonnet-5-5 thinking off</h4><div class=\"trial-draft\" data-round=\"1\"><p class=\"trial-round\">Round 1</p><p>The bull case is that demand is broad [D1].</p>", drawn, StringComparison.Ordinal);
        Assert.Contains("data-side=\"trial\" data-model=\"claude-sonnet-5-5 thinking off\" data-outcome=\"first time\" data-rounds=\"1\" data-cost=\"0.0500\"", drawn, StringComparison.Ordinal);
        Assert.Contains(".trial-sides,.trial-pair{display:grid;grid-auto-flow:column;grid-auto-columns:minmax(0,1fr);gap:16px}", Stylesheet.Css, StringComparison.Ordinal);
        Assert.Contains("@media (max-width:640px){ .trial-sides,.trial-pair{grid-auto-flow:row;grid-auto-columns:auto} }", Stylesheet.Css, StringComparison.Ordinal);

        // On the page while a trial has written a row, and not at all where none has.
        using (var host = new PassHost(store.Root) { Clock = FixedClock.At(DateTimeOffset.Parse("2026-09-11T12:00:00Z", CultureInfo.InvariantCulture), SessionZones.UnitedStates) })
        using (var client = host.CreateClient())
        {
            Assert.Contains("data-card=\"trials\"", await client.GetStringAsync("/screens/run/2026-09-10"), StringComparison.Ordinal);
        }

        store.Execute($"DELETE FROM run_log WHERE stage LIKE '{ReadApi.TrialStage}:%';");

        // A second later, since the surface records its own start under the instant it came up at.
        using (var host = new PassHost(store.Root) { Clock = FixedClock.At(DateTimeOffset.Parse("2026-09-11T12:00:01Z", CultureInfo.InvariantCulture), SessionZones.UnitedStates) })
        using (var client = host.CreateClient())
        {
            var page = await client.GetStringAsync("/screens/run/2026-09-10");

            Assert.Contains("data-fold=\"reports\"", page, StringComparison.Ordinal);
            Assert.DoesNotContain("data-card=\"trials\"", page, StringComparison.Ordinal);
        }

        Assert.Equal(string.Empty, new MarkRenderer().TrialsRegion([], SinglePageApp.NameRoute));
    }

    [Fact]
    public async Task ATrialsSpendIsKeptOutOfEveryReportsCost()
    {
        using var store = EclStore();
        var api = Api(store);
        var night = new DateOnly(2026, 9, 10);

        // A retry of the trial beside its first call, both under the pass's run.
        store.Execute(
            "INSERT INTO run_log (run_id, stage, started_at, ended_at, outcome, rows_written, model_calls, network_requests, spend, detail) " +
            $"VALUES ('{EclRun}', 'research call: {ClaimRules.TwoCasesSection}, {TrialCalls.Round}, round 2', '2026-09-10T22:12:00Z', '2026-09-10T22:12:10Z', 'ok', 0, 1, 1, '0.0700', '');");

        var rows = await api.ReportRowsAsync();
        var ecl = Assert.Single(RunScreen.Reports(rows, await api.ReportVersionsAsync(), night.AddDays(-6), night).Reports);

        // The run holds 0.1356 in calls, of which 0.1200 were the trial's: the report costs the rest, and the two
        // cases' cell its own two rounds alone.
        Assert.Equal(0.1356m, rows.Where(row => row.RunId == EclRun && row.Stage.StartsWith(ReadApi.PaidCallStage + ":", StringComparison.Ordinal)).Sum(row => decimal.Parse(row.Spend, CultureInfo.InvariantCulture)));
        Assert.Equal(0.0156m, ecl.Cost);
        Assert.Equal(0.0050m, CellOf(ecl, ClaimRules.TwoCasesSection).Cost);
        Assert.Contains("data-cost=\"0.0156\">$0.0156</td>", WebUtility.HtmlDecode(new MarkRenderer().ReportsRegion(new ReportsView([ecl], 1, [], 0, 0, []), SinglePageApp.NameRoute)), StringComparison.Ordinal);
    }
}
