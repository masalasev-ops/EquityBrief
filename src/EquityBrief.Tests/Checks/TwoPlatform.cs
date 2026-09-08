using System.Text.RegularExpressions;

namespace EquityBrief.Tests.Checks;

// two-platform. The suite passes on both runners.
//
// The passing is CI's own result and cannot be asserted from inside one run.
// What can be asserted, and what this was widened to at 1.4, is that a leg
// cannot report green without having run the suite. The roster row claims the
// suite passes on both; a check that only asked whether the workflow mentioned
// two runner names would hold over a workflow whose macOS leg was skipped, or
// whose failure was swallowed, and the row would still read as satisfied.
//
// This is the same shape as coverage-reported's own argument. A check that
// stops running reports the same green as one that ran, and a matrix leg is a
// check that runs a whole script.
public class TwoPlatform
{
    static string Workflow() => File.ReadAllText(Repository.Workflow);

    // Every runner the workflow names, with the job it belongs to.
    static IReadOnlyList<string> Runners(string workflow) =>
    [
        .. Regex.Matches(workflow, @"(?<![A-Za-z-])((?:windows|macos|ubuntu)-[a-z0-9.]+)")
            .Select(match => match.Groups[1].Value)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(name => name, StringComparer.Ordinal),
    ];

    [Fact]
    public void TheWorkflowStillRunsBothRunners()
    {
        var workflow = Workflow();

        Assert.Contains("windows-latest", workflow, StringComparison.Ordinal);
        Assert.Contains("macos-latest", workflow, StringComparison.Ordinal);
        Assert.Contains("tools/ci.ps1", workflow, StringComparison.Ordinal);
        Assert.Contains("tools/ci.sh", workflow, StringComparison.Ordinal);

        // Three runners and no more, stated so a fourth is a decision rather
        // than an accident: the two this tool supports, and the named
        // instrument below.
        Assert.Equal(["macos-latest", "ubuntu-latest", "windows-latest"], Runners(workflow));
    }

    [Fact]
    public void NoLegCanReportGreenWithoutRunningTheSuite()
    {
        // The widening the 0.7 review carried here. These are the ways a matrix
        // leg passes without asserting anything, and each of them leaves the
        // workflow looking exactly as it does now to a check that only reads
        // runner names.
        var workflow = Workflow();

        Assert.DoesNotContain("continue-on-error", workflow, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("|| true", workflow, StringComparison.Ordinal);
        Assert.DoesNotContain("if: false", workflow, StringComparison.Ordinal);

        // fail-fast false is deliberate and is the opposite of a swallowed
        // failure: it lets the other leg finish so a fault on one platform does
        // not hide a different fault on the other. It is named here so a later
        // reader does not remove it as a way of failing sooner.
        Assert.Contains("fail-fast: false", workflow, StringComparison.Ordinal);

        // And every leg runs a CI script rather than a bare dotnet test. A
        // green dotnet test does not satisfy done condition 2, which is why the
        // scripts exist.
        var steps = Regex.Matches(workflow, @"^\s*(?:- )?run: (.+)$|^\s+if \(\$IsWindows\) \{ (.+?) \} else \{ (.+?) \}",
            RegexOptions.Multiline);

        var invocations = steps
            .SelectMany(match => match.Groups.Values.Skip(1).Where(group => group.Success).Select(group => group.Value.Trim()))
            .Where(command => command.Contains("tools/", StringComparison.Ordinal))
            .ToArray();

        Assert.True(invocations.Length >= 3, $"Found {invocations.Length} CI script invocations in the workflow, expected at least 3.");
        Assert.All(invocations, command => Assert.Contains("tools/ci.", command, StringComparison.Ordinal));
    }

    [Fact]
    public void TheLinuxJobIsThereAsAnInstrumentAndNotAsAThirdPlatform()
    {
        // Linux is not a platform this tool supports. The job exists so the
        // pipeline opens its files on a case-sensitive filesystem, which is the
        // one fault neither of the operator's machines can see.
        var workflow = Workflow();

        Assert.Contains("ubuntu-latest", workflow, StringComparison.Ordinal);
        Assert.Contains("case-sensitivity", workflow, StringComparison.Ordinal);

        // It is outside the matrix, which is what keeps the roster row's "both"
        // meaning two. A third leg inside the matrix would make two-platform a
        // claim about three.
        var matrix = Regex.Match(workflow, @"matrix:\s*\n\s*os: \[(.+?)\]");

        Assert.True(matrix.Success, "The workflow no longer declares an os matrix, so this read nothing.");
        Assert.Equal(2, matrix.Groups[1].Value.Split(',').Length);
        Assert.DoesNotContain("ubuntu", matrix.Groups[1].Value, StringComparison.Ordinal);
    }

    [Fact]
    public void TheHistoryIsFetchedOnEveryLegThatReadsIt()
    {
        // changelog-reconciles reads the history, and a shallow clone gives it
        // one commit. A leg without this passes the check by having nothing to
        // assert over, which is under-reporting on a runner rather than on this
        // machine and is invisible from here without asking.
        var workflow = Workflow();

        var checkouts = Regex.Matches(workflow, @"uses: actions/checkout@").Count;
        var depths = Regex.Matches(workflow, @"fetch-depth: 0").Count;

        Assert.True(checkouts >= 2, $"Found {checkouts} checkouts in the workflow, expected at least 2.");
        Assert.Equal(checkouts, depths);
    }
}
