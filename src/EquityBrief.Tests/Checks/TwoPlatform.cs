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

    // Every YAML condition key in the workflow. The key form only, so the pwsh
    // step's own inline `if (` is not one of them.
    //
    // The optional dash is the first thing the proof below caught. A job-level
    // condition is `    if:` and a step-level one is `      - if:`, because a
    // step is a list item and its first key carries the dash. A pattern anchored
    // on whitespace alone reads the job form and misses the step form, which is
    // the narrower of the two and the easier one to add by accident.
    static int ConditionsIn(string workflow) =>
        Regex.Matches(workflow, @"^\s+(?:-\s+)?if:", RegexOptions.Multiline).Count;

    [Fact]
    public void TheConditionReaderCatchesASkippableLegAndLeavesTheInlineShellAlone()
    {
        // The permanent proof, in both directions. The forward direction is what
        // the blocklist this replaced could not do: none of these three carries
        // any of the literals it looked for, and every one of them skips a leg.
        Assert.Equal(1, ConditionsIn("jobs:\n  matrix:\n    if: runner.os != 'macOS'\n    steps:\n      - run: ./tools/ci.sh\n"));
        Assert.Equal(1, ConditionsIn("jobs:\n  matrix:\n    steps:\n      - if: ${{ false }}\n        run: ./tools/ci.sh\n"));
        Assert.Equal(1, ConditionsIn("jobs:\n  matrix:\n    if: github.event_name == 'schedule'\n    steps:\n      - run: ./tools/ci.sh\n"));

        // And the reverse, since a matcher catching every `if` anywhere would
        // make the shipped workflow unwritable: its pwsh step chooses the entry
        // point for the platform it is on, which is a shell conditional inside a
        // run block and not a YAML key.
        Assert.Equal(0, ConditionsIn("    steps:\n      - name: verify\n        shell: pwsh\n        run: |\n          if ($IsWindows) { ./tools/ci.ps1 } else { ./tools/ci.sh }\n"));
        Assert.Equal(0, ConditionsIn("jobs:\n  matrix:\n    steps:\n      - run: ./tools/ci.sh\n"));
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

        // Every YAML condition, and not the three literals that stood here.
        //
        // `if: false` was one of a blocklist, and a blocklist answers about the
        // entries on it. A leg is skipped at runtime by any condition at all:
        // `if: runner.os != 'macOS'`, `if: ${{ false }}`, a condition on an
        // event name, or one reading a variable nobody sets. Each produces a job
        // whose conclusion is `skipped`, and a skipped job does not fail its
        // run, so the run is green and the leg asserted nothing. That is the
        // exact fault the comment above this test names, and the blocklist could
        // not see it.
        //
        // Counted rather than absent-checked, because a sweep expecting nothing
        // states the count it expects in advance. The inline `if (` inside the
        // pwsh step is not a YAML key and is not matched: the pattern requires
        // the key form, being `if:` at the start of an indented line.
        var conditions = ConditionsIn(workflow);

        Assert.True(
            conditions == 0,
            $"The workflow carries {conditions} YAML condition(s), expected 0. A condition on a job or a " +
            "step can skip a leg at runtime, and a skipped job leaves the run green, so the leg reports " +
            "nothing and two-platform's roster row would still read as satisfied.");

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
