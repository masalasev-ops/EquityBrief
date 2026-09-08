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
        var bash = Shell.Locate("bash");

        if (bash is null)
        {
            Assert.True(!OperatingSystem.IsLinux(), "No bash on PATH, and this platform should have one.");

            return;
        }

        var probe = Path.Combine(Repository.Root, "tools", "flood-probe");

        // Bounded, because the failure this guards against is a hang rather
        // than a wrong answer. A test that deadlocks reports nothing at all;
        // one that times out names what it was doing.
        var run = Task.Run(() => Shell.Run(bash, [probe]));
        var finished = await Task.WhenAny(run, Task.Delay(TimeSpan.FromSeconds(60)));

        Assert.True(
            ReferenceEquals(finished, run),
            "Shell.Run did not return within 60 seconds against a child writing about 400 kB to " +
            "each stream. That is the deadlock this test exists for: one stream read to " +
            "completion while the child blocks writing to the other.");

        var result = await run;

        // Both streams arrive whole, and the exit code with them.
        Assert.Equal(7, result.ExitCode);
        Assert.Equal(4000, Lines(result.StandardOutput));
        Assert.Equal(4000, Lines(result.StandardError));
    }

    static int Lines(string text) =>
        text.Split((char)10, StringSplitOptions.RemoveEmptyEntries).Length;
}
