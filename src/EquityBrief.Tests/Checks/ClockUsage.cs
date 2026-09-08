namespace EquityBrief.Tests.Checks;

// clock-usage. Nothing outside the clock reads the machine clock, and no
// schedule is expressed in local time.
public class ClockUsage
{
    // The one file allowed to read it.
    const string TheClock = "SystemClock.cs";

    [Fact]
    public void NothingOutsideTheClockReadsTheMachineClock()
    {
        var files = Repository.SourceFiles();

        // Scope. The property is carried by the files, and the floor is set far
        // enough below the count that ordinary growth never moves it.
        Assert.True(files.Count >= 15, $"Read {files.Count} source files, expected at least 15.");

        // Comments are stripped before the scan, because a comment is not a
        // read. The check flagged a sentence explaining why a zoneless instant
        // is refused, on the strength of the words inside it, which is the same
        // defect the statement reader had: a scan that finds a pattern in prose
        // is not evidence of behaviour. Stripping narrows the scope to code,
        // which is the only place a machine clock can actually be read.
        var uses = files
            .SelectMany(file => MachineClock.In(SourceStatements.WithoutComments(File.ReadAllText(file)), file))
            .Where(use => Path.GetFileName(use.File) != TheClock)
            .ToArray();

        Assert.Empty(uses);
    }

    [Fact]
    public void TheClockItselfDoesReadIt()
    {
        // Without this, the check above passes just as well over a system that
        // reads no clock at all, which is not the property being asserted.
        var uses = MachineClock.In(
            SourceStatements.WithoutComments(File.ReadAllText(Repository.SystemClock)),
            Repository.SystemClock);

        Assert.NotEmpty(uses);
    }

    [Fact]
    public void AReadInACommentIsNotARead()
    {
        // The other half of the widening. A real read is still found, and the
        // same words inside a comment are not, so this is not a scan that has
        // simply been switched off.
        var real = "var now = " + "DateTime" + ".Now;";
        var talkedAbout = "// a grep for " + "DateTime" + ".Now would never find it";

        Assert.NotEmpty(MachineClock.In(SourceStatements.WithoutComments(real), "real.cs"));
        Assert.Empty(MachineClock.In(SourceStatements.WithoutComments(talkedAbout), "comment.cs"));
        Assert.NotEmpty(MachineClock.In(talkedAbout, "comment.cs"));
    }

    [Fact]
    public void TheCheckReportsAMachineClockRead()
    {
        // The permanent proof that the assertion can fail. Built from parts for
        // the same reason the patterns are.
        Assert.Single(MachineClock.In("var when = " + "DateTime" + ".UtcNow;"));
        Assert.Single(MachineClock.In("var when = " + "DateTimeOffset" + ".Now;"));
    }

    [Fact]
    public void TheCheckReportsAScheduleWrittenInLocalTime()
    {
        Assert.Single(MachineClock.In("var due = stamp." + "ToLocalTime" + "();"));
        Assert.Single(MachineClock.In("var zone = " + "TimeZoneInfo" + ".Local;"));
    }

    [Fact]
    public void TheCheckPassesOverCodeThatUsesTheClock()
    {
        // A reader that flagged everything would satisfy the tests above and
        // fail the whole suite, so it has to be shown letting the right thing
        // through as well.
        Assert.Empty(MachineClock.In("var today = clock.SessionDateAt(clock.UtcNow);"));
        Assert.Empty(MachineClock.In("var stamp = instant.UtcDateTime;"));
    }
}
