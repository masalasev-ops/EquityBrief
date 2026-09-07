using EquityBrief.Core.Time;

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

        var tables = ArchitectureTables.In(
            File.ReadAllText(Path.Combine(root, "docs", "ARCHITECTURE.html")));

        var report = PhaseReport.Build(tables, Fixtures.Of(root));

        // The clock, because nothing else in the system may read the machine.
        PhaseReportWriter.Write(report, root, SystemClock.ForUnitedStatesSessions().UtcNow);

        Console.WriteLine($"tables       {report.Tables.Count}");
        Console.WriteLine($"claims       {report.Claims.Count}");
        Console.WriteLine($"pass         {report.Count(Verdict.Pass)}");
        Console.WriteLine($"fail         {report.Count(Verdict.Fail)}");
        Console.WriteLine($"out of scope {report.Count(Verdict.OutOfScope)}");
        Console.WriteLine($"unexamined   {report.Count(Verdict.Unexamined)}");
        Console.WriteLine($"fixture      {report.Fixture.State}, {report.Fixture.Folders} captured");
        Console.WriteLine(PhaseReportWriter.HtmlPath(root));
        Console.WriteLine(PhaseReportWriter.JsonPath(root));

        var green = report.Count(Verdict.Fail) == 0 && report.Count(Verdict.Unexamined) == 0;

        Console.WriteLine(green
            ? "verify-phase: green"
            : "verify-phase: not green. A phase is not done while anything is unexamined.");

        return green ? 0 : 1;
    }
}
