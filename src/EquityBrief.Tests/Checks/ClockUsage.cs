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

    // The other direction, and it was missing until the phase 5 sign-off.
    //
    // The matcher above reads `.Parse(` and its three siblings, which are all
    // static calls carrying their type's name, so they can be matched on the
    // type. Formatting is an instance call and carries no type name, so it
    // needs its own reader, and having none meant the check asserted half of
    // what its roster row claims.
    //
    // The fault it guards is not cosmetic. Under a culture whose default
    // calendar is not Gregorian, being th-TH, ar-SA or fa-IR, `yyyy` renders a
    // different year, so a night writes `2569-09-10` into `run_log.started_at`,
    // the read surface filters that night's rows out at the session-date
    // comparison, and the operational header draws nothing with no error
    // anywhere. Every `session_date` write already passed a culture and every
    // `started_at` and `ended_at` write did not, which is the split that says
    // the discipline was present and the guard was absent.
    internal static IReadOnlyList<string> CultureFreeDateFormatting(string source, string file)
    {
        var code = SourceStatements.WithoutComments(source);

        // Keyed on the call having no second argument rather than on the absence
        // of the word CultureInfo.
        //
        // A name is the wrong thing to look for: the renderer holds the culture
        // in a field called `Invariant`, so a reader searching the statement for
        // `CultureInfo` reports two correct sites and would have to be told the
        // alias, which is a second place one fact lives and goes stale the day
        // someone renames it. What actually makes a format culture-free is that
        // the call passes a provider, so the shape to match is a `ToString` that
        // closes straight after its format string.
        //
        // Keyed on the format itself for the same reason: the receiver is a
        // value of any name, and a format carrying a year, a month or a day
        // component is a date being rendered whatever it is called.
        string[] components = ["yyyy", "MM-dd", "HH:mm"];

        return System.Text.RegularExpressions.Regex
            .Matches(code, @"\.ToString\(\s*""(?<format>[^""]*)""\s*\)")
            .Where(match => components.Any(component =>
                match.Groups["format"].Value.Contains(component, StringComparison.Ordinal)))
            .Select(match => $"{Path.GetFileName(file)}: {match.Value}")
            .ToArray();
    }

    [Fact]
    public void NothingRendersADateAgainstTheMachinesLocale()
    {
        var files = Repository.SourceFiles();

        Assert.True(files.Count >= 15, $"Read {files.Count} source files, expected at least 15.");

        var loose = files
            .SelectMany(file => CultureFreeDateFormatting(File.ReadAllText(file), file))
            .ToArray();

        Assert.Empty(loose);
    }

    [Fact]
    public void TheCheckReportsADateRenderedAgainstTheMachinesLocale()
    {
        // Permanent proof in both directions, because a sweep whose expected
        // result is nothing is passed every time by a matcher that matches
        // nothing at all. The loose form is what every run log write in this
        // repository carried until the sign-off read them.
        var loose = "command.Parameters.AddWithValue(\"$started_at\", clock.UtcNow." + "ToString(\"yyyy-MM-ddTHH:mm:ssZ\"));";
        var pinned = "command.Parameters.AddWithValue(\"$started_at\", clock.UtcNow." + "ToString(\"yyyy-MM-ddTHH:mm:ssZ\", CultureInfo.InvariantCulture));";

        Assert.Single(CultureFreeDateFormatting(loose, "Probe.cs"));
        Assert.Empty(CultureFreeDateFormatting(pinned, "Probe.cs"));

        // A provider held under another name passes, which is what the renderer
        // does and what a reader looking for the word CultureInfo would report.
        Assert.Empty(CultureFreeDateFormatting("var drawn = listed." + "ToString(\"yyyy-MM-dd\", Invariant);", "Probe.cs"));

        // And a number is not a date. A reader keyed on `.ToString("` alone
        // would report every formatted figure in the renderer, which would make
        // the rule unwritable rather than enforced.
        Assert.Empty(CultureFreeDateFormatting("var drawn = share." + "ToString(\"0.##\");", "Probe.cs"));

        // A comment naming the pattern is not a use of it, which is the reason
        // the source is stripped first.
        Assert.Empty(CultureFreeDateFormatting("// ToString(\"yyyy-MM-dd\") is what this used to do;", "Probe.cs"));
    }

    // The third form of the rendering direction, and the one the sign-off's
    // first repair missed while counting fifty-six renderings fixed.
    //
    // An interpolation hole carrying a date format, `{session:yyyy-MM-dd}`,
    // formats against the current culture exactly as `ToString("yyyy-MM-dd")`
    // does, and it carries no `ToString` for the reader above to find. Every
    // provider request URL in the tree was written this way, so on a machine
    // whose calendar is not Gregorian the night would ask the provider for a
    // year that has not happened. A hole is culture-free only where its whole
    // literal is handed to something that supplies the invariant culture:
    // `FormattableString.Invariant(`, `string.Create(CultureInfo.InvariantCulture,`
    // or the renderer's `Append(Invariant, `. That is read off the text
    // immediately before the literal.
    //
    // What this cannot reach is a hole with no format at all over a date
    // value, `{tonight}`, which renders the culture's short date and carries
    // nothing a text reader can key on without the type. That is this
    // reader's stated scope rather than a property it claims.
    internal static IReadOnlyList<string> CultureFreeDateInterpolation(string source, string file)
    {
        var code = SourceStatements.WithoutComments(source);

        string[] components = ["yyyy", "MM-dd", "HH:mm", "HHmmss"];

        return System.Text.RegularExpressions.Regex
            .Matches(code, @"(?<before>[^\n]{0,48})\$@?""(?<body>(?:[^""\\\n]|\\.)*)""")
            .Where(literal => System.Text.RegularExpressions.Regex
                .Matches(literal.Groups["body"].Value, @"(?<!\{)\{[^{}:]+:(?<format>[^{}]+)\}")
                .Any(hole => components.Any(component =>
                    hole.Groups["format"].Value.Contains(component, StringComparison.Ordinal))))
            .Where(literal => !System.Text.RegularExpressions.Regex.IsMatch(
                literal.Groups["before"].Value,
                @"(?:Invariant\(|Invariant,|InvariantCulture,)\s*$"))
            .Select(literal => $"{Path.GetFileName(file)}: ${'"'}{literal.Groups["body"].Value}{'"'}")
            .ToArray();
    }

    [Fact]
    public void NothingInterpolatesADateAgainstTheMachinesLocale()
    {
        var files = Repository.SourceFiles();

        Assert.True(files.Count >= 15, $"Read {files.Count} source files, expected at least 15.");

        var loose = files
            .SelectMany(file => CultureFreeDateInterpolation(File.ReadAllText(file), file))
            .ToArray();

        Assert.Empty(loose);
    }

    [Fact]
    public void TheCheckReportsADateInterpolatedAgainstTheMachinesLocale()
    {
        // Permanent proof in both directions. The loose form is every provider
        // request URL this repository carried until the phase 5 sign-off.
        var url = "var request = " + "$\"{Endpoint}?from={from:yyyy-MM-dd}&to={to:yyyy-MM-dd}&fmt=json\";";
        var runId = "runId ??= " + "$\"night-{clock.UtcNow:yyyyMMddTHHmmssZ}\";";

        Assert.Single(CultureFreeDateInterpolation(url, "Probe.cs"));
        Assert.Single(CultureFreeDateInterpolation(runId, "Probe.cs"));

        // The three pinned forms pass.
        Assert.Empty(CultureFreeDateInterpolation("var u = FormattableString.Invariant(" + "$\"a{from:yyyy-MM-dd}\");", "Probe.cs"));
        Assert.Empty(CultureFreeDateInterpolation("var u = string.Create(CultureInfo.InvariantCulture, " + "$\"a{from:yyyy-MM-dd}\");", "Probe.cs"));
        Assert.Empty(CultureFreeDateInterpolation("header.Append(Invariant, " + "$\"<td>{night:yyyy-MM-dd}</td>\");", "Probe.cs"));

        // A number is not a date, an escaped brace is not a hole, and a comment
        // naming the pattern is not a use of it.
        Assert.Empty(CultureFreeDateInterpolation("var s = " + "$\"{seconds:0.###} second(s)\";", "Probe.cs"));
        Assert.Empty(CultureFreeDateInterpolation("var s = " + "$\"{{literal:yyyy}}\";", "Probe.cs"));
        Assert.Empty(CultureFreeDateInterpolation("// $\"{x:yyyy-MM-dd}\" is what this used to do", "Probe.cs"));
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
