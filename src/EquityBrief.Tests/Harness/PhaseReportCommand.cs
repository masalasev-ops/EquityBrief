using EquityBrief.Core.Time;
using EquityBrief.Tests.Checks;

namespace EquityBrief.Tests.Harness;

// What tools/verify-phase runs.
//
// It exits non-zero unless the report is green, and green includes that nothing
// is listed as unexamined. Out of scope is shown beside it and never added to
// it, because only one of the two is a defect.
internal static class PhaseReportCommand
{
    internal static int Run(string[] args)
    {
        // Normalised, because the bash entry point hands over a path with the
        // separators its own shell uses and the report should not print two kinds.
        var root = Path.GetFullPath(args.Length > 0 ? args[0] : Repository.Root);

        var document = File.ReadAllText(Path.Combine(root, "docs", "ARCHITECTURE.html"));

        // The suite's own result for this run, written by tools/verify-phase
        // before it gets here. Absent means nothing ran, which leaves every
        // claim a check would have reached unexamined rather than passing it.
        var outcomes = args.Length > 1
            ? SuiteOutcomes.FromTrx(args[1])
            : SuiteOutcomes.NothingRan;

        var report = PhaseReport.Build(
            ArchitectureTables.In(document),
            NightlyRunSteps.In(document),
            Fixtures.Of(root),
            CoverageReported.Coverage(),
            outcomes);

        // The clock, because nothing else in the system may read the machine.
        PhaseReportWriter.Write(report, root, SystemClock.ForUnitedStatesSessions().UtcNow);

        Console.WriteLine($"tables       {report.Tables.Count}");
        Console.WriteLine($"claims       {report.Claims.Count}");
        Console.WriteLine($"pass         {report.Count(Verdict.Pass)}");
        Console.WriteLine($"fail         {report.Count(Verdict.Fail)}");
        Console.WriteLine($"out of scope {report.Count(Verdict.OutOfScope)}");
        Console.WriteLine($"unexamined   {report.Count(Verdict.Unexamined)}");
        Console.WriteLine($"reconciled   {report.Reconciled} placements and verdicts, floor {Reconciliation.Floor}");
        Console.WriteLine(
            $"fixture      {report.Fixture.State}, {report.Fixture.Folders} captured, " +
            $"{report.Fixture.Constituents} constituents and {report.Fixture.Names} names");
        Console.WriteLine($"checks       {report.Coverage.Count} on the roster, {report.Coverage.Count(check => check.Carrier != CoverageReported.NotDueYet)} carried");
        Console.WriteLine(
            $"checks ran   {outcomes.Count(CheckRun.Passed)} passed, "
            + $"{outcomes.Count(CheckRun.Failed)} failed, "
            + $"{outcomes.Count(CheckRun.DidNotRun)} did not run");

        // The run as a whole beside the carried checks, because they are two
        // populations and the first line alone said the suite was clean over a
        // run with a failing test in a class that carries no check.
        Console.WriteLine($"suite        {report.Suite.Describe()}");
        Console.WriteLine(PhaseReportWriter.HtmlPath(root));
        Console.WriteLine(PhaseReportWriter.JsonPath(root));

        var green = report.Green;

        // Naming both counts, because they are different faults and the line
        // said only one of them. Until 0.7's repair that was harmless in the way
        // a dead branch is harmless: fail could not be above zero, so the only
        // reason the report could be red was the one the message gave.
        Console.WriteLine(green
            ? "verify-phase: green"
            : $"verify-phase: not green. {report.Count(Verdict.Fail)} claim(s) were checked and "
                + $"did not hold, {report.Count(Verdict.Unexamined)} were not checked, "
                + $"{report.ChecksNotPassing} carried check(s) did not run or did not hold, and "
                + $"{report.Suite.Describe()}. A phase is not done while any of those is above "
                + "zero.");

        return green ? 0 : 1;
    }
}
