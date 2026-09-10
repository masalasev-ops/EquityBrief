using System.Diagnostics;

namespace EquityBrief.Tests.Tools;

// Shell.Run reads both of a child's streams at once.
//
// The obligation carried out of 0.7. Reading standard output to the end and
// then standard error blocks forever against a child that fills the error
// pipe: the reader waits for stdout to close, the child waits for room in
// stderr, and neither moves. Every other child the suite starts writes a few
// lines, which is why this was latent rather than absent.
public class ShellStreamTests
{
    [Fact]
    public async Task AChildThatFillsBothPipesIsReadRatherThanDeadlocked()
    {
        var bash = Shell.Bash();

        if (bash is null)
        {
            Assert.True(!OperatingSystem.IsLinux(), "No bash on PATH, and this platform should have one.");

            return;
        }

        var probe = Path.Combine(Repository.Root, "tools", "flood-probe");

        // Bounded, because the failure this guards against is a hang rather
        // than a wrong answer. A test that deadlocks reports nothing at all;
        // one that times out names what it was doing.
        //
        // The bound is measured rather than stated. It read 60 seconds until
        // 5.0, which asserts that the runner is at least as fast as the machine
        // the test was written on, and `two-platform` has twice caught that
        // shape on the night's deadline test. The control runs the same script
        // through the same reader over a hundredth of the lines, which is 4 kB
        // per stream and so cannot itself block on a pipe buffer.
        var control = Stopwatch.StartNew();
        var small = Shell.Run(bash, [probe, "40"]);

        control.Stop();

        // The control is asserted before it is used as a bound. A control that
        // failed to start would return in no time and set a bound nothing could
        // meet, which is a red about the arrangement wearing the clothes of a
        // red about the deadlock.
        Assert.Equal(7, small.ExitCode);
        Assert.Equal(40, Lines(small.StandardOutput));
        Assert.Equal(40, Lines(small.StandardError));

        // A hundred times the control because the full run writes a hundred
        // times the lines, and the control also pays the process start the full
        // run pays once, so this bounds it from above on either machine. The
        // five seconds is scheduler variance on a loaded runner rather than
        // work.
        var bound = (control.Elapsed * 100) + TimeSpan.FromSeconds(5);

        var run = Task.Run(() => Shell.Run(bash, [probe]));
        var finished = await Task.WhenAny(run, Task.Delay(bound));

        Assert.True(
            ReferenceEquals(finished, run),
            $"Shell.Run did not return within {bound} against a child writing about 400 kB to " +
            $"each stream, measured from a control of {control.Elapsed} over a hundredth of the " +
            "lines. That is the deadlock this test exists for: one stream read to completion " +
            "while the child blocks writing to the other.");

        var result = await run;

        // Both streams arrive whole, and the exit code with them.
        Assert.Equal(7, result.ExitCode);
        Assert.Equal(4000, Lines(result.StandardOutput));
        Assert.Equal(4000, Lines(result.StandardError));
    }

    static int Lines(string text) =>
        text.Split((char)10, StringSplitOptions.RemoveEmptyEntries).Length;
}
