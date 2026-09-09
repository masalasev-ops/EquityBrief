using System.Reflection;
using System.Xml.Linq;
using EquityBrief.Tests.Checks;

namespace EquityBrief.Tests.Harness;

// What a check did in the run the report reads.
internal enum CheckRun
{
    // It did not run. Its claims are UNEXAMINED and never PASS, which is what
    // section 19.3 says a claim that was not checked is. This is the value a
    // check gets by absence, so a check nothing knows about cannot pass.
    DidNotRun,
    Passed,
    Failed,
}

// One check's result, with the failing test named where there is one. The
// message is carried because 19.3 says a failure shows the diff beside it, and
// a FAIL row naming only the check tells a reader which instrument to go and
// run rather than what it found.
internal sealed record CheckResult(CheckRun Run, string Test = "", string Message = "");

// What the run itself reported, read from the trx's own Counters element.
//
// A second population, and the reason it is here: the carrier map answers for
// the checks the roster carries, and a large minority of this suite's tests live
// in classes that carry no check. TheCarrierOfEveryCheckOwnsItsTestsAndNoOthers
// counts them rather than stating the figure here, where it would go stale on the
// next test written. A failure in one of those moves no claim, so without this the
// report stayed green and exited 0 over a red suite, which is the same fault the
// repair was written to remove, one level out.
internal sealed record SuiteRun(int Total, int Executed, int Failed, int NotExecuted)
{
    internal static SuiteRun Nothing { get; } = new(0, 0, 0, 0);

    // Executed above zero is part of it, because a run that executed nothing
    // reports no failures either, and an empty result is what a filter matching
    // no test produces.
    internal bool Clean => Failed == 0 && NotExecuted == 0 && Executed > 0;

    internal string Describe() =>
        Executed == 0
            ? "no suite result was read, so nothing was checked"
            : $"{Executed} of {Total} tests ran, {Failed} failed and {NotExecuted} did not run";
}

// The suite's result, read back so the report can say whether a claim was
// checked rather than only which instrument was declared to reach it.
//
// Before this existed the report asserted a verdict from a reach declaration:
// Scope named a check, the reconciliation confirmed that check declared reach
// over the subject, and nothing asked whether the check had run or held.
// Verdict.Fail was assigned nowhere in the report path, so the "fail 0" line was
// structural rather than measured and could not take another value. The proof is
// on record from the phase 1 sign-off: deleting the membership filter left five
// tests failing, and the report printed an identical block and still called the
// claim about storing bars for current members PASS.
internal sealed class SuiteOutcomes
{
    readonly IReadOnlyDictionary<string, CheckResult> byCheck;

    SuiteOutcomes(IReadOnlyDictionary<string, CheckResult> byCheck, SuiteRun run)
    {
        this.byCheck = byCheck;
        Run = run;
    }

    // What the run as a whole did, beside what each carried check did. Both are
    // needed and neither subsumes the other: a check can fail with no claim
    // attached, and a claim can go unchecked in a run that reported no failure.
    internal SuiteRun Run { get; }

    // Nothing ran, which is what the report falls back to when it is given no
    // result to read. It is the safe direction rather than a convenience: with
    // no run behind it every claim a check would have reached is UNEXAMINED, and
    // a report carrying one is not green. A default of "everything passed" is
    // the defect this repair removed.
    internal static SuiteOutcomes NothingRan { get; } =
        new(new Dictionary<string, CheckResult>(StringComparer.Ordinal), SuiteRun.Nothing);

    internal static SuiteOutcomes Of(
        IReadOnlyDictionary<string, CheckResult> byCheck, SuiteRun? run = null) =>
        new(
            new Dictionary<string, CheckResult>(byCheck, StringComparer.Ordinal),
            run ?? CleanRunOf(byCheck.Count));

    // A run in which every carried check ran and nothing else did anything
    // else. Only for the tests whose subject is the report rather than the
    // suite; a real run reads its counters from the file.
    internal static SuiteRun CleanRunOf(int checks) => new(checks, checks, 0, 0);

    // Every carried check passing. For the tests whose subject is the report's
    // attribution rather than the suite's execution, and named for what it is:
    // a stub that says everything passed is exactly the fiat this repair
    // removed, so it belongs only where execution is not the property under
    // test. The execution half is asserted by the two proofs in
    // ArchitectureConformance that write a report and read the file back.
    internal static SuiteOutcomes EveryCarriedCheckPassed() =>
        Of(CoverageReported.Coverage()
            .Where(check => check.Carrier != CoverageReported.NotDueYet)
            .ToDictionary(
                check => check.Check,
                _ => new CheckResult(CheckRun.Passed),
                StringComparer.Ordinal));

    internal CheckResult For(string check) =>
        byCheck.TryGetValue(check, out var result) ? result : new CheckResult(CheckRun.DidNotRun);

    internal int Count(CheckRun run) => byCheck.Values.Count(result => result.Run == run);

    // Read from the trx the suite writes. The file is the run's own record of
    // what it did, so the report reads a result rather than recomputing one.
    internal static SuiteOutcomes FromTrx(string path)
    {
        if (!File.Exists(path))
        {
            // Not an exception. A missing result is a run that told the report
            // nothing, and the report says so by leaving every claim
            // unexamined rather than by refusing to be written at all: an
            // operator whose suite failed to build still gets a page saying
            // which claims went unchecked.
            return NothingRan;
        }

        var results = XDocument.Load(path)
            .Descendants()
            .Where(element => element.Name.LocalName == "UnitTestResult")
            .Select(element => new TestOutcome(
                element.Attribute("testName")?.Value ?? string.Empty,
                element.Attribute("outcome")?.Value ?? string.Empty,
                Message(element)))
            .Where(outcome => outcome.Test.Length > 0)
            .ToArray();

        if (results.Length == 0)
        {
            return NothingRan;
        }

        return new SuiteOutcomes(
            CoverageReported.Coverage()
                .Where(check => check.Carrier != CoverageReported.NotDueYet)
                .ToDictionary(
                    check => check.Check,
                    check => ResultOf(check.Carrier, results),
                    StringComparer.Ordinal),
            RunIn(XDocument.Load(path)));
    }

    // The run's own tally, from the element the format writes it in rather than
    // recounted from the rows. Failed sums the four ways a test can run and not
    // hold, because reading only the "failed" column takes an Error or a
    // Timeout for a pass.
    internal static SuiteRun RunIn(XDocument trx)
    {
        var counters = trx.Descendants()
            .FirstOrDefault(element => element.Name.LocalName == "Counters");

        if (counters is null)
        {
            return SuiteRun.Nothing;
        }

        int Count(string name) =>
            int.TryParse(counters.Attribute(name)?.Value, out var value) ? value : 0;

        return new SuiteRun(
            Count("total"),
            Count("executed"),
            Count("failed") + Count("error") + Count("timeout") + Count("aborted"),
            Count("notExecuted"));
    }

    static string Message(XElement result) =>
        result.Descendants()
            .FirstOrDefault(element => element.Name.LocalName == "Message")?
            .Value
            .Trim()
        ?? string.Empty;

    internal sealed record TestOutcome(string Test, string Outcome, string Detail);

    // A check's tests are the ones its carrier class declares. Matched on the
    // type's full name and a dot rather than on the class name alone, because a
    // key that is the opening of a value answers about everything sharing that
    // prefix: "Store" would take "StoreWrites" with it, and the dot is what
    // makes the two disjoint. TheCarrierOfEveryCheckOwnsItsTestsAndNoOthers
    // asserts that in both directions over the real suite.
    internal static CheckResult ResultOf(string carrier, IReadOnlyList<TestOutcome> results)
    {
        var prefix = CarrierType(carrier).FullName + ".";

        var mine = results
            .Where(result => result.Test.StartsWith(prefix, StringComparison.Ordinal))
            .ToArray();

        // Passing is the only outcome that counts as passing, and everything
        // else is read against it rather than listed.
        //
        // The first version of this asked whether any result said "Failed" and
        // took a passing sibling as the answer otherwise, which let every other
        // outcome the format carries read as a pass: Error, Timeout, Aborted
        // and NotExecuted alike. A skipped test is the sharp case, because
        // [Fact(Skip = "...")] leaves the suite's own exit code at zero as well,
        // so two attributes were the whole distance between this repair working
        // and not working. A check that narrows its own scope keeps passing is
        // the shape CLAUDE.md names as the one that survives, and this was it.
        var notPassing = mine
            .Where(result => !string.Equals(result.Outcome, "Passed", StringComparison.OrdinalIgnoreCase))
            .ToArray();

        // Not run and run badly are different, and only the second names a
        // test. A carrier whose rows are all NotExecuted did not run; one
        // carrying an Error or a Timeout ran and did not hold.
        var broke = notPassing.FirstOrDefault(result =>
            !string.Equals(result.Outcome, "NotExecuted", StringComparison.OrdinalIgnoreCase));

        if (broke is not null)
        {
            return new CheckResult(
                CheckRun.Failed,
                broke.Test,
                broke.Detail.Length > 0 ? broke.Detail : $"the test reported {broke.Outcome}");
        }

        // Any test of this carrier skipped is the check not run, even beside a
        // passing sibling. Half a check is not a check, and the half that was
        // skipped is the half nobody is looking at.
        return mine.Length > 0 && notPassing.Length == 0
            ? new CheckResult(CheckRun.Passed)
            : new CheckResult(CheckRun.DidNotRun);
    }

    // The class carrying a check, resolved to a type rather than compared as a
    // string, so a class renamed out from under the map stops the harness here
    // instead of quietly owning no tests and reading as a check that did not
    // run.
    internal static Type CarrierType(string carrier)
    {
        var found = Assembly.GetExecutingAssembly().GetTypes()
            .Where(type => type.IsPublic && type.Name == carrier)
            .ToArray();

        return found.Length == 1
            ? found[0]
            : throw new InvalidOperationException(
                $"'{carrier}' is named as the class carrying a check and resolves to {found.Length} " +
                "public types in the suite, where it has to resolve to exactly one. A carrier that " +
                "resolves to none owns no tests and would read as a check that did not run.");
    }
}
