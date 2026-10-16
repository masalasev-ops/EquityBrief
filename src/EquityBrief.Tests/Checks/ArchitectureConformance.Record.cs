using System.Text.Json;
using System.Text.RegularExpressions;
using EquityBrief.Tests.Harness;

namespace EquityBrief.Tests.Checks;

// architecture-conformance, 19.14: the phase report holds the figures the newest entry wrote before the run over the
// tree carrying it to the ones it measured, green only where each is the one measured, so no commit after the run
// fills a figure in. Shown over constructed records and a report built over the document itself.
// see: An entry states the figures its verifying run measures before that run, and the phase report holds them to the run
public partial class ArchitectureConformance
{
    // An entry's Verified line as a checkpoint writes it, every figure in its place and wrapped as the record wraps.
    static string EntryStating(int tests, int tables, int claims, int passing, int placements, int floor, int checks) =>
        "### 9.9 - a checkpoint   2026-10-16\n" +
        "Built:      something.\n" +
        "Verified:   `tools/ci.ps1` green and `tools/verify-phase.ps1` green over the tree carrying this entry: all six\n" +
        $"            steps, 0 warnings, 0 errors, {tests} of {tests} tests ran with none failed, migrations 0 to 96 with\n" +
        $"            none pending; the phase report {tables} tables, {claims} claims, {passing} passing, none out of scope\n" +
        $"            and none unexamined, {placements} placements and verdicts reconciled against a floor of {floor},\n" +
        $"            {checks} checks on the roster run and passed.\n" +
        "Carried:    nothing.\n";

    static string RecordEndingOn(string newest) =>
        "# PROGRESS.md\n\n## Entries\n\n" +
        "### 9.8 - an earlier checkpoint   2026-10-15\n" +
        "Verified:   `tools/ci.ps1` green and `tools/verify-phase.ps1` green over the tree carrying this entry: 1 of 1\n" +
        "            tests ran; the phase report 1 tables, 1 claims, 1 passing.\n\n" +
        newest;

    [Fact]
    public void ThePhaseReportHoldsTheNewestEntrysStatedFiguresToTheOnesItMeasured()
    {
        var report = ReportFrom(SuiteOutcomes.Of(EveryCarriedCheck(CheckRun.Passed), new SuiteRun(2410, 2410, 0, 0)));

        int[] measured =
        [
            2410,
            report.Tables.Count,
            report.Claims.Count,
            report.Count(Verdict.Pass),
            report.Reconciled,
            Reconciliation.Floor,
            report.Coverage.Count,
        ];

        string Stating(int[] figures) =>
            EntryStating(figures[0], figures[1], figures[2], figures[3], figures[4], figures[5], figures[6]);

        // Stated as measured: every figure read, and the report green on them, the earlier entry's figures read by
        // nothing.
        var stated = RecordFigures.Of(RecordEndingOn(Stating(measured)), report);

        Assert.True(stated.StatesItsOwnRun);
        Assert.Equal("9.9 - a checkpoint 2026-10-16", stated.Entry);
        Assert.Equal(
            ["tests ran", "tests in the run", "tables", "claims", "passing", "out of scope", "placements", "floor", "checks"],
            stated.Figures.Select(figure => figure.Name));
        Assert.True(stated.Agree);
        Assert.True(report.Green);
        Assert.True((report with { Record = stated }).Green);

        // Each figure one off, alone, is named with both figures and turns the report red.
        for (var at = 0; at < measured.Length; at++)
        {
            var off = (int[])measured.Clone();
            off[at]++;

            var differing = RecordFigures.Of(RecordEndingOn(Stating(off)), report);

            Assert.False(differing.Agree, $"Figure {at} one off agreed.");
            Assert.False((report with { Record = differing }).Green);
            Assert.Contains($"{off[at]} ", differing.Describe(), StringComparison.Ordinal);
            Assert.Contains($"where this run measured {measured[at]}", differing.Describe(), StringComparison.Ordinal);
        }

        // "None out of scope" over a run whose report holds a claim out of scope is a figure off as well.
        Assert.False(RecordFigures.Of(RecordEndingOn(Stating(measured)), report with { Claims = [.. report.Claims.Take(1).Select(claim => claim with { Verdict = Verdict.OutOfScope }), .. report.Claims.Skip(1)] }).Agree);

        // An entry naming its own run with no test count, or no claim count, states less than the run it names.
        var bare = RecordFigures.Of(
            RecordEndingOn("### 9.9 - a checkpoint   2026-10-16\nVerified:   `tools/ci.ps1` green and `tools/verify-phase.ps1` green over the tree carrying this entry.\n"),
            report);

        Assert.True(bare.StatesItsOwnRun);
        Assert.False(bare.Agree);
        Assert.Contains("no figure of tests ran", bare.Describe(), StringComparison.Ordinal);

        // A newest entry naming no run over its own tree, a ruling's here, is read for nothing and holds nothing back.
        var ruling = RecordFigures.Of(
            RecordEndingOn(
                "### 9.9 ruling - a ruling   2026-10-16\n\nNot a checkpoint entry: it lands nothing.\n" +
                "Verified:   `tools/ci.ps1` green over main as 9.8's entry records, and the checks reading this record run\n" +
                "            filtered over the tree carrying it: 164 of 164 tests.\n"),
            report);

        Assert.False(ruling.StatesItsOwnRun);
        Assert.Empty(ruling.Figures);
        Assert.True(ruling.Agree);

        // And the written report carries what it read on both surfaces.
        var red = report with { Record = RecordFigures.Of(RecordEndingOn(Stating([measured[0] + 1, .. measured[1..]])), report) };

        using var elsewhere = new TemporaryDirectory();

        PhaseReportWriter.Write(red, elsewhere.Path, DateTimeOffset.UnixEpoch);

        using var written = JsonDocument.Parse(File.ReadAllText(PhaseReportWriter.JsonPath(elsewhere.Path)));

        Assert.False(written.RootElement.GetProperty("green").GetBoolean());
        Assert.False(written.RootElement.GetProperty("record").GetProperty("agree").GetBoolean());
        Assert.Equal(
            2411,
            written.RootElement.GetProperty("record").GetProperty("figures")[0].GetProperty("stated").GetInt32());
        Assert.Contains(
            "2411 tests ran where this run measured 2410",
            File.ReadAllText(PhaseReportWriter.HtmlPath(elsewhere.Path)),
            StringComparison.Ordinal);
    }

    [Fact]
    public void TheRecordsNewestEntryIsReadForTheFiguresItStates()
    {
        // Over the real record: whatever its newest entry is, its heading is read, and an entry stating its own run
        // states at least the tests and the claims the report counts.
        var record = RecordFigures.Of(Corpus.Read("docs/PROGRESS.md"), ReportFrom(SuiteOutcomes.NothingRan));

        Assert.Equal(
            Regex.Replace(DuePoints.Entries(Corpus.Read("docs/PROGRESS.md"))[^1].Groups["heading"].Value, @"\s+", " ").Trim(),
            record.Entry);

        if (record.StatesItsOwnRun)
        {
            Assert.Contains(record.Figures, figure => figure.Name == "tests ran");
            Assert.Contains(record.Figures, figure => figure.Name == "claims");
        }
    }
}
