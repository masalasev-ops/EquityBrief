using System.Text.RegularExpressions;

namespace EquityBrief.Tests.Checks;

// two-platform. The suite passes on both platforms: macOS on a hosted runner,
// and Windows on the operator's machine by tools/ci.ps1, which each checkpoint's
// record states (see: Windows is verified on the operator's machine, and the hosted runners are macOS and Linux).
//
// The passing is a run's own result and cannot be asserted from inside another
// run. What can be asserted, and what this was widened to at 1.4, is that a leg
// cannot report green without having run the suite. The roster row claims the
// suite passes on both; a check that only asked whether the workflow mentioned
// a runner name would hold over a workflow whose macOS leg was skipped, or
// whose failure was swallowed, and the row would still read as satisfied.
//
// The Windows half moved off the workflow on 2026-09-15, when the hosted
// Windows leg was removed. What stands in for it is the record: a checkpoint
// written since then that does not say tools/ci.ps1 ran green has no Windows
// result behind it, and nothing else in the repository would notice.
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
    public void TheWorkflowRunsMacOSAndNamesNoWindowsRunner()
    {
        var workflow = Workflow();

        Assert.Contains("macos-latest", workflow, StringComparison.Ordinal);
        Assert.Contains("tools/ci.sh", workflow, StringComparison.Ordinal);

        // Two runners and no more, stated so a third, a hosted Windows leg
        // among them, is a decision rather than an accident: the platform this
        // workflow verifies, and the named instrument below. Windows is
        // verified on the operator's machine and its half is read off the
        // record rather than off this file.
        Assert.Equal(["macos-latest", "ubuntu-latest"], Runners(workflow));
    }

    // The last checkpoint entry written while a hosted Windows leg still ran.
    // Every checkpoint entry after it has to carry the Windows run itself.
    static readonly Regex LastHostedWindowsEntry = new(@"^7\.2 - the phase 7 report\s+\d{4}-\d{2}-\d{2}$");

    static readonly Regex LocalWindowsRun = new(@"`tools/ci\.ps1`\s+green");

    // The checkpoint entries written after the last hosted Windows leg, and the
    // headings of those that do not say tools/ci.ps1 ran green. A checkpoint
    // entry is headed with a checkpoint and a dash and does not open "Not a
    // checkpoint entry", so a planning pass, a ruling and a sign-off are not
    // read: none of them lands a checkpoint's code.
    internal static (int Read, IReadOnlyList<string> Missing) WindowsUnrecorded(string progress)
    {
        var entries = Regex.Matches(progress, @"^### (?<heading>[^\r\n]*)(?<body>(?:(?!^### )[\s\S])*)", RegexOptions.Multiline);

        // The anchor is keyed on a heading's opening, so exactly one heading
        // has to match: none leaves nothing to start after, and two leave the
        // start to whichever the reader met first.
        var anchors = entries.Where(entry => LastHostedWindowsEntry.IsMatch(entry.Groups["heading"].Value.TrimEnd())).ToArray();

        if (anchors.Length != 1)
        {
            throw new InvalidOperationException(
                $"Found {anchors.Length} entries headed as the last one written while a hosted Windows leg ran, expected 1.");
        }

        var after = entries.Where(entry => entry.Index > anchors[0].Index);
        var checkpoints = after
            .Where(entry => Regex.IsMatch(entry.Groups["heading"].Value, @"^\d+\.\d+ - "))
            .Where(entry => !entry.Groups["body"].Value.TrimStart().StartsWith("Not a checkpoint entry", StringComparison.Ordinal))
            .ToArray();

        var missing = checkpoints
            .Where(entry => !LocalWindowsRun.IsMatch(entry.Groups["body"].Value))
            .Select(entry => entry.Groups["heading"].Value.TrimEnd())
            .ToArray();

        return (checkpoints.Length, missing);
    }

    [Fact]
    public void ACheckpointEntryWithoutTheWindowsRunIsFoundAndOnlyAfterTheLastHostedLeg()
    {
        // The permanent proof, over constructed records.
        const string Before = "### 7.1 - the repair   2026-09-15\nVerified:   `tools/ci.sh` green\n\n";
        const string Anchor = "### 7.2 - the phase 7 report   2026-09-15\nVerified:   `tools/ci.ps1` green\n\n";
        static string Entry(string heading, string body) => $"### {heading}   2026-09-16\n{body}\n\n";

        // An entry before the anchor is not read, whatever it says.
        var (read, missing) = WindowsUnrecorded(Before + Anchor);
        Assert.Equal(0, read);
        Assert.Empty(missing);

        // A checkpoint entry saying so passes, wrapped across lines as the record wraps.
        (read, missing) = WindowsUnrecorded(Before + Anchor + Entry("8.1 - resolution", "Verified:   `tools/ci.ps1`\n            green end to end"));
        Assert.Equal(1, read);
        Assert.Empty(missing);

        // One that ran only the other script, or none, is found.
        Assert.Equal(["8.1 - resolution   2026-09-16"], WindowsUnrecorded(Before + Anchor + Entry("8.1 - resolution", "Verified:   `tools/ci.sh` green")).Missing);
        Assert.Equal(["8.1 - resolution   2026-09-16"], WindowsUnrecorded(Before + Anchor + Entry("8.1 - resolution", "Verified:   `tools/ci.ps1` red at first")).Missing);

        // A planning pass, a ruling and a sign-off land no checkpoint's code and are not read.
        var others = Entry("8.0 planning - the loop", "Not a checkpoint entry. It plans phase 8.")
            + Entry("7.2 ruling - a ruling", "Not a checkpoint entry. It rules.")
            + Entry("Phase 7 sign-off", "Signed.");
        (read, missing) = WindowsUnrecorded(Before + Anchor + others);
        Assert.Equal(0, read);
        Assert.Empty(missing);

        // Nor is an entry headed with a checkpoint and a dash that opens "Not a
        // checkpoint entry", which the record holds under 3.1 three times. The
        // three above are left out by their headings alone, so without this case
        // the opening is read by nothing the proof reaches, which is what the
        // sweep's mutation dropping it found.
        (read, missing) = WindowsUnrecorded(Before + Anchor + Entry("8.1 - a note on the checkpoint", "Not a checkpoint entry. It builds nothing."));
        Assert.Equal(0, read);
        Assert.Empty(missing);

        // And the anchor is exactly one heading.
        Assert.Throws<InvalidOperationException>(() => WindowsUnrecorded(Before));
        Assert.Throws<InvalidOperationException>(() => WindowsUnrecorded(Before + Anchor + Anchor));
    }

    [Fact]
    public void EveryCheckpointEntrySinceTheLastHostedWindowsLegRecordsTheWindowsRun()
    {
        // Over the real record. The population is the checkpoint entries
        // written since the hosted Windows leg was removed, which was 0 when
        // it was removed and grows by one a checkpoint, so it carries no floor
        // and is reported as context: the proof above is what shows the
        // reader can fail.
        var (read, missing) = WindowsUnrecorded(Corpus.Read("docs/PROGRESS.md"));

        Assert.True(
            missing.Count == 0,
            $"Read {read} checkpoint entries since the last hosted Windows leg, and {missing.Count} do not say " +
            $"`tools/ci.ps1` ran green, so nothing shows the suite passed on Windows for them: {string.Join("; ", missing)}");
    }

    // Every YAML condition key in the workflow. The key form only, so a shell
    // conditional inside a run block is not one of them.
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
        // make a workflow choosing its entry point by platform unwritable, as
        // this one did while it ran a Windows leg: a shell conditional inside a
        // run block is not a YAML key.
        Assert.Equal(0, ConditionsIn("    steps:\n      - name: verify\n        shell: pwsh\n        run: |\n          if ($IsWindows) { ./tools/ci.ps1 } else { ./tools/ci.sh }\n"));
        Assert.Equal(0, ConditionsIn("jobs:\n  matrix:\n    steps:\n      - run: ./tools/ci.sh\n"));
    }

    [Fact]
    public void NoLegCanReportGreenWithoutRunningTheSuite()
    {
        // The widening the 0.7 review carried here. These are the ways a leg
        // passes without asserting anything, and each of them leaves the
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
        // states the count it expects in advance.
        var conditions = ConditionsIn(workflow);

        Assert.True(
            conditions == 0,
            $"The workflow carries {conditions} YAML condition(s), expected 0. A condition on a job or a " +
            "step can skip a leg at runtime, and a skipped job leaves the run green, so the leg reports " +
            "nothing and two-platform's roster row would still read as satisfied.");

        // The two legs are separate jobs rather than a matrix, so one failing
        // cancels nothing in the other and no fail-fast setting is needed to
        // keep a fault on one from hiding a different fault on the other.
        Assert.DoesNotContain("matrix:", workflow, StringComparison.Ordinal);

        // And every leg runs a CI script rather than a bare dotnet test. A
        // green dotnet test does not satisfy done condition 2, which is why the
        // scripts exist. Two legs, each running tools/ci.sh.
        IReadOnlyList<string> invocations =
        [
            .. Regex.Matches(workflow, @"^\s*(?:- )?run: (.+)$", RegexOptions.Multiline)
                .Select(match => match.Groups[1].Value.Trim()),
        ];

        Assert.Equal(["./tools/ci.sh", "./tools/ci.sh"], invocations);
    }

    [Fact]
    public void TheLinuxJobIsThereAsAnInstrumentAndNotAsAThirdPlatform()
    {
        // Linux is not a platform this tool supports. The job exists so the
        // pipeline opens its files on a case-sensitive filesystem, which is the
        // one fault neither of the operator's machines can see.
        var workflow = Workflow();

        // The jobs by name, each with the one runner it names, so the Linux
        // runner sits in the instrument's job and nowhere else.
        var jobs = Regex.Matches(workflow, @"^  (?<job>[a-z-]+):\s*$(?<body>(?:(?!^  [a-z-]+:\s*$)[\s\S])*)", RegexOptions.Multiline)
            .ToDictionary(match => match.Groups["job"].Value, match => Runners(match.Groups["body"].Value), StringComparer.Ordinal);
        IReadOnlyList<string> names = [.. jobs.Keys.OrderBy(name => name, StringComparer.Ordinal)];

        Assert.Equal(["case-sensitivity", "macos"], names);
        Assert.Equal(["macos-latest"], jobs["macos"]);
        Assert.Equal(["ubuntu-latest"], jobs["case-sensitivity"]);
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
