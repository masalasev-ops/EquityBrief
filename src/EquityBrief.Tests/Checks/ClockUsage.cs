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

    // A date parsed without a culture resolves against the machine's locale, so
    // "21/03/2026" is a March date on one machine and a refusal on another. That
    // is the same class of fault as an instant resolving against the machine's
    // zone, and it belongs to this check for the same reason: nothing may depend
    // on how this machine happens to be configured for time.
    internal static IReadOnlyList<string> CultureFreeDateParsing(string source, string file)
    {
        var code = SourceStatements.WithoutComments(source);

        // Matched on the literal call rather than by regex. The pattern this
        // replaced was written through a scripted edit and its escapes did not
        // survive it, so it matched nothing and the permanent proof beneath is
        // what caught that.
        string[] types = ["DateOnly", "DateTime", "DateTimeOffset", "TimeOnly"];
        string[] calls = [".Parse(", ".TryParse(", ".ParseExact(", ".TryParseExact("];

        return code
            .Split(';')
            .Where(statement => types.Any(type => calls.Any(call =>
                statement.Contains(type + call, StringComparison.Ordinal))))
            .Where(statement => !statement.Contains("CultureInfo", StringComparison.Ordinal))
            .Select(statement => $"{Path.GetFileName(file)}: {System.Text.RegularExpressions.Regex.Replace(statement, @"\s+", " ").Trim()}")
            .ToArray();
    }

    [Fact]
    public void NothingParsesADateAgainstTheMachinesLocale()
    {
        var files = Repository.SourceFiles();

        Assert.True(files.Count >= 15, $"Read {files.Count} source files, expected at least 15.");

        var loose = files
            .SelectMany(file => CultureFreeDateParsing(File.ReadAllText(file), file))
            .ToArray();

        Assert.Empty(loose);
    }

    [Fact]
    public void TheCheckReportsADateParsedAgainstTheMachinesLocale()
    {
        // Permanent proof, and a counter-case so this is not a reader that flags
        // every parse. The invariant one is what the shipped code does.
        var loose = "var d = " + "DateOnly" + ".TryParse(text, out var parsed);";
        var pinned = "var d = " + "DateOnly" + ".TryParseExact(text, \"yyyy-MM-dd\", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed);";

        Assert.Single(CultureFreeDateParsing(loose, "loose.cs"));
        Assert.Empty(CultureFreeDateParsing(pinned, "pinned.cs"));
        Assert.Empty(CultureFreeDateParsing("// " + loose, "comment.cs"));
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
