using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using EquityBrief.Core.Facts;
using EquityBrief.Core.Research;
using EquityBrief.Core.Time;
using EquityBrief.Tests.Harness;
using EquityBrief.Worker.Facts;
using EquityBrief.Worker.Research;
using Microsoft.Data.Sqlite;

namespace EquityBrief.Tests.Checks;

// claim-admissibility, the claim half, which its roster row said from 6.3 was
// asserted from 6.4. A poisoned paragraph and an unsourced claim are refused, the
// retry is one, and nothing refused is written, each asserted over the store
// rather than over a return value.
//
// The same check as the document half and a second file of the same class,
// because the roster names one check and one carrier. The two halves read
// different fixtures and fail apart: every document could be judged correctly
// while a section citing a refused one was accepted.
public partial class ClaimAdmissibility
{
    static readonly DateTimeOffset Night = new(2026, 9, 8, 21, 10, 0, TimeSpan.Zero);

    // ---- the fixture prose ----

    internal sealed record Written(string Name, string Section, IReadOnlyList<string> Sources, string Prose);

    static JsonElement Prose() =>
        JsonDocument.Parse(File.ReadAllText(Path.Combine(Folder(), "research-prose.json"))).RootElement;

    internal static IReadOnlyList<Written> Sections() =>
    [
        .. Prose().GetProperty("sections").EnumerateArray().Select(section => new Written(
            section.GetProperty("name").GetString()!,
            section.GetProperty("section").GetString()!,
            [.. section.GetProperty("sources").EnumerateArray().Select(source => source.GetString()!)],
            section.GetProperty("prose").GetString()!)),
    ];

    internal static Written SectionNamed(string name) => Sections().Single(section => section.Name == name);

    internal static string Ticker => Prose().GetProperty("ticker").GetString()!;

    internal static string AsOf => Prose().GetProperty("asOf").GetString()!;

    // A store the whole pipeline filled, with the facts file rebuilt after the
    // fundamentals so it carries the figures a written section quotes, and the
    // documents the prose cites stored as the intake would store them.
    //
    // The replay is the canonical one `fixture-replay` runs rather than a second
    // chain written here, because two replays of one pipeline disagree
    // eventually. It fetches fundamentals after the night, which is where that
    // fetch belongs, so the facts file is assembled once more afterwards: a re-run
    // replaces a night's facts file where the store now computes a different one.
    // see: A re-run replaces a night's facts file where the store now computes a different one
    internal static async Task<TemporaryStore> WithSources()
    {
        var store = await FixtureReplay.ReplayedAsync();

        await new FactsAssembler(FixedClock.At(Night, SessionZones.UnitedStates), store.DatabaseFile)
            .RunAsync("replay-facts-with-fundamentals");

        var documents = SourceDocuments.Of(
            [.. Exhibits(), .. Refusable().Select(one => one.Document)],
            From,
            To,
            FetchedAt);

        foreach (var row in documents.Rows)
        {
            Execute(
                store,
                "INSERT INTO source_document (id, url, title, published_on, fetched_at, body, admissibility) " +
                "VALUES ($id, $url, $title, $published_on, $fetched_at, $body, $admissibility);",
                ("$id", row.Id),
                ("$url", row.Url),
                ("$title", row.Title),
                ("$published_on", row.PublishedOn?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)),
                ("$fetched_at", row.FetchedAt.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture)),
                ("$body", row.Body),
                ("$admissibility", row.Admissibility));
        }

        return store;
    }

    // The id a source named in the prose fixture resolves to: a release exhibit by
    // its file name, or a constructed document by its name.
    internal static string SourceId(string named)
    {
        const string Constructed = "inadmissible: ";

        if (named.StartsWith(Constructed, StringComparison.Ordinal))
        {
            return SourceDocuments.Id(Refusable().Single(one => one.Name == named[Constructed.Length..]).Document.Url);
        }

        return SourceDocuments.Id(Exhibits().Single(exhibit => exhibit.Title.EndsWith(named, StringComparison.Ordinal)).Url);
    }

    // A section as a writer would insert it: pending, with its source list in the
    // order the prose cites it.
    internal static void Pending(TemporaryStore store, Written written, int version, string? asOf = null) =>
        Execute(
            store,
            "INSERT INTO research_section (ticker, section, version, as_of, model, status, prose, source_ids, reject_reason) " +
            "VALUES ($ticker, $section, $version, $as_of, 'a writer', 'pending', $prose, $sources, NULL);",
            ("$ticker", Ticker),
            ("$section", written.Section),
            ("$version", version.ToString(CultureInfo.InvariantCulture)),
            ("$as_of", asOf ?? AsOf),
            ("$prose", written.Prose),
            ("$sources", JsonSerializer.Serialize(written.Sources.Select(SourceId).ToArray())));

    internal sealed record StoredSection(int Version, string AsOf, string Model, string Status, string Prose, string SourceIds, string? Reason);

    internal static IReadOnlyList<StoredSection> Stored(TemporaryStore store, string section)
    {
        using var connection = new SqliteConnection($"Data Source={store.DatabaseFile}");
        connection.Open();

        using var command = connection.CreateCommand();

        command.CommandText =
            "SELECT version, as_of, model, status, prose, source_ids, reject_reason FROM research_section " +
            "WHERE ticker = $ticker AND section = $section ORDER BY version;";
        command.Parameters.AddWithValue("$ticker", Ticker);
        command.Parameters.AddWithValue("$section", section);

        var rows = new List<StoredSection>();
        using var reader = command.ExecuteReader();

        while (reader.Read())
        {
            rows.Add(new StoredSection(
                reader.GetInt32(0), reader.GetString(1), reader.GetString(2), reader.GetString(3),
                reader.GetString(4), reader.GetString(5), reader.IsDBNull(6) ? null : reader.GetString(6)));
        }

        return rows;
    }

    static void Execute(TemporaryStore store, string sql, params (string Name, string? Value)[] parameters)
    {
        using var connection = new SqliteConnection($"Data Source={store.DatabaseFile}");
        connection.Open();

        using var command = connection.CreateCommand();

        command.CommandText = sql;

        foreach (var (name, value) in parameters)
        {
            command.Parameters.AddWithValue(name, (object?)value ?? DBNull.Value);
        }

        command.ExecuteNonQuery();
    }

    static ClaimChecker Checker(TemporaryStore store) =>
        new(FixedClock.At(Night, SessionZones.UnitedStates), store.DatabaseFile);

    // ---- each prose case, over the store ----

    [Fact]
    public async Task APoisonedParagraphIsRefusedNamingTheOneFigureTheFactsFileDoesNotHold()
    {
        using var store = await WithSources();

        Pending(store, SectionNamed("a poisoned paragraph"), 1);
        await Checker(store).RunAsync("check-poisoned");

        var row = Assert.Single(Stored(store, ClaimRules.ComputedSection));

        Assert.Equal(ClaimChecker.Rejected, row.Status);

        // Exactly that figure and nothing else, which is what the one change in
        // the fixture buys: a checker that found something wrong somewhere in
        // eleven figures would pass a looser assertion.
        Assert.Equal($"{ClaimRules.UnmatchedFigure}: 68.4%", row.Reason);
    }

    [Fact]
    public async Task TheCleanComputedParagraphIsAcceptedWithEveryFigureRoundedFromTheFactsFile()
    {
        using var store = await WithSources();

        Pending(store, SectionNamed("a clean computed paragraph"), 1);
        await Checker(store).RunAsync("check-clean");

        var row = Assert.Single(Stored(store, ClaimRules.ComputedSection));

        Assert.Equal(ClaimChecker.Accepted, row.Status);
        Assert.Null(row.Reason);

        // And the paragraph carries the shapes the rule has to read, so an
        // acceptance is a statement about each of them rather than about a
        // paragraph with nothing in it: eleven numbers, being eight figures, two
        // windows and a date.
        var figures = ClaimRules.Sentences(SectionNamed("a clean computed paragraph").Prose)
            .SelectMany(sentence => ClaimRules.Figures(sentence.Text))
            .ToArray();

        Assert.Equal(8, figures.Count(figure => figure.Kind == FigureKind.Figure));
        Assert.Equal(2, figures.Count(figure => figure.Kind == FigureKind.Window));
        Assert.Equal(1, figures.Count(figure => figure.Kind == FigureKind.Date));
    }

    [Fact]
    public async Task AnUnsourcedClaimIsRefusedNamingTheSentenceThatCitesNothing()
    {
        using var store = await WithSources();

        Pending(store, SectionNamed("an unsourced claim"), 1);
        await Checker(store).RunAsync("check-unsourced");

        var row = Assert.Single(Stored(store, "What the company sells"));

        Assert.Equal(ClaimChecker.Rejected, row.Status);
        Assert.Equal(
            $"{ClaimRules.Uncited}: Analysts expect the next design cycle to be the strongest in five years.",
            row.Reason);
    }

    [Fact]
    public async Task TheCleanResearchedParagraphIsAcceptedAndAClaimOnARefusedDocumentIsNot()
    {
        using var store = await WithSources();

        Pending(store, SectionNamed("a clean researched paragraph"), 1);
        await Checker(store).RunAsync("check-researched");

        Assert.Equal(ClaimChecker.Accepted, Assert.Single(Stored(store, "What the company sells")).Status);

        // The same section's next version citing a stored row admissibility
        // refused. Stored is not enough, which is the half a bare provenance rule
        // would pass.
        Pending(store, SectionNamed("a claim resting on a refused document"), 2);
        await Checker(store).RunAsync("check-refused-source");

        var refused = Stored(store, "What the company sells").Single(row => row.Version == 2);

        Assert.Equal(ClaimChecker.Rejected, refused.Status);
        Assert.Equal($"{ClaimRules.RefusedSource}: D2 ({Admissibility.PriceForecast})", refused.Reason);
    }

    [Fact]
    public async Task ASectionWithNoAdmissibleSourceFallsBackOnItsFirstCheckWithTheLineThatSaysSo()
    {
        using var store = await WithSources();

        Pending(store, SectionNamed("a section with no admissible source"), 1);
        await Checker(store).RunAsync("check-no-source");

        var row = Assert.Single(Stored(store, "The two cases"));

        // Fallback rather than rejected, so there is no retry to spend: a rewrite
        // cannot create a source.
        Assert.Equal(ClaimChecker.Fallback, row.Status);
        Assert.Equal(ClaimRules.NoAdmissibleSource, row.Reason);
    }

    // ---- the retry ----

    [Fact]
    public async Task ASectionRefusedTwiceFallsBackWithBothAttemptsOffendingTextKept()
    {
        using var store = await WithSources();

        var attempts = Attempts("rejected twice");

        Pending(store, SectionNamed(attempts.First), 1);
        await Checker(store).RunAsync("check-first");

        Pending(store, SectionNamed(attempts.Second), 2);
        var outcome = await Checker(store).RunAsync("check-second");

        var rows = Stored(store, attempts.Section);

        Assert.Equal([ClaimChecker.Rejected, ClaimChecker.Fallback], [.. rows.Select(row => row.Status)]);

        // Both attempts' offending text, the first on its own row and the second
        // on the fallback, and the run log line naming both, which is section
        // 18's promise.
        Assert.Contains("68.4%", rows[0].Reason!, StringComparison.Ordinal);
        Assert.StartsWith(ClaimChecker.RejectedTwice, rows[1].Reason!, StringComparison.Ordinal);
        Assert.Contains("68.4%", rows[1].Reason!, StringComparison.Ordinal);

        var detail = RunLogDetail(store, "check-second");

        Assert.Contains("fell back", detail, StringComparison.Ordinal);
        Assert.Contains("the first attempt: " + rows[0].Reason, detail, StringComparison.Ordinal);
        Assert.Equal(1, outcome.FellBack);
    }

    [Fact]
    public async Task ARefusalFollowedByACleanRetryIsAccepted()
    {
        using var store = await WithSources();

        var attempts = Attempts("rejected then accepted");

        Pending(store, SectionNamed(attempts.First), 1);
        await Checker(store).RunAsync("check-first");

        Pending(store, SectionNamed(attempts.Second), 2);
        await Checker(store).RunAsync("check-second");

        Assert.Equal(
            [ClaimChecker.Rejected, ClaimChecker.Accepted],
            [.. Stored(store, attempts.Section).Select(row => row.Status)]);
    }

    [Fact]
    public async Task TheRetryIsOneRatherThanUnboundedAssertedOverTheStore()
    {
        // Six consecutive refusals of one section on one day. The store never
        // holds two rejected versions in a row: every refusal after a refusal
        // falls back, and every refusal after a fallback is a fresh first attempt.
        // Asserted over the rows rather than over any count the checker returned.
        using var store = await WithSources();

        var poisoned = SectionNamed("a poisoned paragraph");

        for (var version = 1; version <= 6; version++)
        {
            Pending(store, poisoned, version);
            await Checker(store).RunAsync("check-" + version.ToString(CultureInfo.InvariantCulture));
        }

        var statuses = Stored(store, ClaimRules.ComputedSection).Select(row => row.Status).ToArray();

        Assert.Equal(
            [ClaimChecker.Rejected, ClaimChecker.Fallback, ClaimChecker.Rejected, ClaimChecker.Fallback, ClaimChecker.Rejected, ClaimChecker.Fallback],
            statuses);

        Assert.DoesNotContain(
            statuses.Zip(statuses.Skip(1)),
            pair => pair.First == ClaimChecker.Rejected && pair.Second == ClaimChecker.Rejected);

        // And a refusal on a later day is a new pass, not the retry of an old one.
        Pending(store, poisoned, 7);
        await Checker(store).RunAsync("check-later-day-first");

        Pending(store, poisoned, 8, asOf: "2026-09-09");
        await Checker(store).RunAsync("check-later-day");

        Assert.Equal(ClaimChecker.Rejected, Stored(store, ClaimRules.ComputedSection).Single(row => row.Version == 8).Status);
    }

    [Fact]
    public async Task NothingRefusedIsWrittenAndTheCheckerChangesNoColumnButTheTwoItOwns()
    {
        // Asserted over the store: the prose, the model, the date and the source
        // list a writer inserted are what stays, whatever the checker decided, and
        // no refused section reads as accepted.
        using var store = await WithSources();

        var version = 0;

        foreach (var written in Sections())
        {
            Pending(store, written, ++version);
        }

        var before = AllRows(store);

        await Checker(store).RunAsync("check-all");

        var after = AllRows(store);

        Assert.Equal(before.Count, after.Count);

        foreach (var (was, now) in before.Zip(after))
        {
            Assert.Equal(was.Prose, now.Prose);
            Assert.Equal(was.Model, now.Model);
            Assert.Equal(was.AsOf, now.AsOf);
            Assert.Equal(was.SourceIds, now.SourceIds);
            Assert.Equal(ClaimChecker.Pending, was.Status);
        }

        var accepted = after.Where(row => row.Status == ClaimChecker.Accepted).Select(row => row.Prose).ToArray();

        Assert.Equal(2, accepted.Length);
        Assert.Contains(SectionNamed("a clean computed paragraph").Prose, accepted);
        Assert.Contains(SectionNamed("a clean researched paragraph").Prose, accepted);

        Assert.DoesNotContain(SectionNamed("a poisoned paragraph").Prose, accepted);
        Assert.DoesNotContain(SectionNamed("an unsourced claim").Prose, accepted);
        Assert.DoesNotContain(SectionNamed("a claim resting on a refused document").Prose, accepted);

        // The statement itself names two columns, which is SCHEMA's declaration
        // read against the source rather than against a run that happened not to
        // exercise a third.
        var source = File.ReadAllText(Path.Combine(
            Repository.Root, "src", "EquityBrief.Worker", "Research", "ClaimChecker.cs"));

        var sets = Regex.Matches(source, @"UPDATE\s+(research_section|theme_section)\s+SET\s+([^\n]+)\s+WHERE", RegexOptions.IgnoreCase);

        Assert.Equal(2, sets.Count);

        foreach (Match set in sets)
        {
            var columns = Regex.Matches(set.Groups[2].Value, @"(\w+)\s*=")
                .Select(column => column.Groups[1].Value)
                .ToArray();

            Assert.Equal(["status", "reject_reason"], columns);
        }
    }

    [Fact]
    public async Task ASectionIsCheckedAgainstTheFactsFileOfTheDayItWasWritten()
    {
        // A section written the day before a facts file changed is checked against
        // the file it could have been written from. A later file with tomorrow's
        // close in it would refuse a sentence that was right when written.
        using var store = await WithSources();

        var clean = SectionNamed("a clean computed paragraph");

        Execute(
            store,
            "INSERT INTO facts (ticker, session_date, payload, payload_hash, material_changes) VALUES ($ticker, '2026-09-09', $payload, 'later', '[]');",
            ("$ticker", Ticker),
            ("$payload", FactsFile.Serialise(Ticker, new DateOnly(2026, 9, 9), [new Fact("close", "1.00", "bar")])));

        Pending(store, clean, 1);
        await Checker(store).RunAsync("check-its-own-day");

        Assert.Equal(ClaimChecker.Accepted, Assert.Single(Stored(store, ClaimRules.ComputedSection)).Status);

        // The same prose dated on the later day is held to that day's file and
        // refused, which is the direction a query taking the newest file would get
        // right by luck.
        Pending(store, clean, 2, asOf: "2026-09-09");
        await Checker(store).RunAsync("check-later-file");

        Assert.Equal(ClaimChecker.Rejected, Stored(store, ClaimRules.ComputedSection).Single(row => row.Version == 2).Status);
    }

    [Fact]
    public async Task AThemeSectionIsHeldAgainstNoFactsFileSoEveryFigureInItIsRefused()
    {
        // The rule as written, over the theme store, and the carried obligation
        // asserted as it stands rather than left to be discovered: a theme has no
        // facts file, so a figure in a theme section is refused.
        // owes: A theme section's figures checked against a facts file a theme has
        using var store = await WithSources();

        var exhibit = SourceId("release-KEYS.htm");

        Execute(
            store,
            "INSERT INTO theme_section VALUES ('test and measurement', 'The industry cycle', 1, $as_of, 'a writer', 'pending', $prose, $sources, NULL, '[\"electronic equipment\"]');",
            ("$as_of", AsOf),
            ("$prose", "Orders across the industry turned up in the July quarter [D1]."),
            ("$sources", JsonSerializer.Serialize(new[] { exhibit })));

        Execute(
            store,
            "INSERT INTO theme_section VALUES ('test and measurement', 'The industry cycle', 2, $as_of, 'a writer', 'pending', $prose, $sources, NULL, '[\"electronic equipment\"]');",
            ("$as_of", "2026-09-10"),
            ("$prose", "Revenue across the industry was $1.85 billion in the quarter [D1]."),
            ("$sources", JsonSerializer.Serialize(new[] { exhibit })));

        await Checker(store).RunAsync("check-theme");

        using var connection = new SqliteConnection($"Data Source={store.DatabaseFile}");
        connection.Open();

        using var command = connection.CreateCommand();
        command.CommandText = "SELECT version, status, reject_reason FROM theme_section ORDER BY version;";

        using var reader = command.ExecuteReader();

        Assert.True(reader.Read());
        Assert.Equal(ClaimChecker.Accepted, reader.GetString(1));

        Assert.True(reader.Read());
        Assert.Equal(ClaimChecker.Rejected, reader.GetString(1));
        Assert.Equal($"{ClaimRules.UnmatchedFigure}: $1.85 billion", reader.GetString(2));
    }

    // ---- the reader, over constructed text ----

    [Fact]
    public void TheSectionsAreTheLaneTablesOwnRowsInBothDirections()
    {
        // Figure 12.2's table is where a section is named, so the checker's list is
        // read against it rather than kept as a second statement of it.
        var table = ArchitectureTables
            .In(File.ReadAllText(Repository.Architecture))
            .Single(candidate => candidate.Rows.Count > 0
                && candidate.Rows[0].Count >= 2
                && candidate.Rows[0][0] == "Section"
                && candidate.Rows[0][1] == "Lane");

        var named = table.Body.Where(row => row.Count > 1).Select(row => row[0]).ToArray();

        Assert.Equal(named, ClaimRules.Sections);
        Assert.Contains(ClaimRules.ComputedSection, named);

        // The computed section is the one the table places in the local lane with
        // nothing to weigh, read off its own row.
        var computed = table.Body.Single(row => row[0] == ClaimRules.ComputedSection);

        Assert.Contains("nothing to weigh", computed[3], StringComparison.Ordinal);
    }

    [Fact]
    public void AFigureIsARoundingAtThePrecisionTheProseStatesAndNeverANumberNobodyComputed()
    {
        Fact[] facts =
        [
            new("latest quarter revenue", "1846000000.00", "fundamental"),
            new("latest quarter gross margin", "0.658722", "fundamental"),
            new("close", "333.42", "bar"),
            new("immediate support strength", "2", "level"),
            new("sma200", "289.30685", "indicator"),
            new("latest quarter end", "2026-07-31", "fundamental"),
            new("largest move sessions", "5", "move"),
        ];

        bool Holds(string prose) => ClaimRules.Figures(prose).All(figure => figure.Kind switch
        {
            FigureKind.Figure => ClaimRules.Matches(figure, facts),
            FigureKind.Window => ClaimRules.IsAWindow(figure, facts),
            FigureKind.Date => ClaimRules.IsADate(figure, facts),
            FigureKind.Unmatchable => false,
            _ => true,
        });

        // A rounding at the stated precision holds, in both units and as a
        // percentage of a stored fraction.
        Assert.True(Holds("revenue of $1.85 billion"));
        Assert.True(Holds("revenue of $1.8 billion"));
        Assert.True(Holds("revenue of $2 billion"));
        Assert.True(Holds("revenue of $1,846 million"));
        Assert.True(Holds("a gross margin of 65.9%"));
        Assert.True(Holds("a gross margin of 66 per cent"));
        Assert.True(Holds("closed at 333.42"));
        Assert.True(Holds("the 200-day average at 289.31"));
        Assert.True(Holds("the quarter ended 2026-07-31"));
        Assert.True(Holds("the quarter ended July 31"));
        Assert.True(Holds("over 5 sessions"));

        // A rounding at a precision the prose does not state does not.
        Assert.False(Holds("revenue of $1.9 billion"));
        Assert.False(Holds("a gross margin of 65.8%"));
        Assert.True(Holds("closed at 333.4"));
        Assert.False(Holds("closed at 333.5"));
        Assert.False(Holds("the 100-day average"));
        Assert.False(Holds("the quarter ended 2026-06-30"));

        // A whole-number strength is not a stored fraction, so 200% does not match
        // a strength of 2, which is one of the misreads the measurement found.
        Assert.False(Holds("up 200%"));

        // And the words that are not figures are read as what they are.
        Assert.True(Holds("in fiscal 2026 and Q3, on the S&P 500, reported over the twelve months to July, at 4:00 p.m., its 3rd year"));
        Assert.True(Holds("since 2021"));

        // A number written in words from eleven up is refused, and one to ten is
        // language that is not read, which is the stated cost.
        Assert.False(Holds("hundreds of millions of dollars"));
        Assert.False(Holds("twelve new products"));
        Assert.True(Holds("two segments"));
    }

    [Fact]
    public void ThePercentageBeforeAWeekIsAFigureAndDigitsInsideANameAreNotRead()
    {
        // Two misreads of the live measurement, kept as the cases that caught
        // them, and the name case the reader was written to leave alone.
        Assert.Equal(FigureKind.Figure, Assert.Single(ClaimRules.Figures("shares rose 10.86% week over week")).Kind);
        Assert.Equal(FigureKind.Window, Assert.Single(ClaimRules.Figures("the 52-week high")).Kind);

        Assert.Empty(ClaimRules.Figures("the iPhone17 and an H100 on 5G"));

        var time = ClaimRules.Figures("a call at 2:00 p.m. PT");

        Assert.Equal(FigureKind.Label, Assert.Single(time).Kind);
    }

    [Fact]
    public void ASentenceEndsWhereAWriterEndsItAndACitationAfterTheFullStopBelongsToIt()
    {
        var sentences = ClaimRules.Sentences(
            "Dell vs. HPE is the comparison Keysight Inc. draws in the U.S. market [D1]. Revenue grew. [D2] " +
            "It sells test gear [D1][D2].");

        Assert.Equal(3, sentences.Count);
        Assert.Equal([1], sentences[0].Citations);
        Assert.Equal([2], sentences[1].Citations);
        Assert.Equal([1, 2], sentences[2].Citations);

        // A decimal is not a sentence boundary.
        Assert.Single(ClaimRules.Sentences("Revenue was $1.85 billion and margin 65.9% [D1]."));
    }

    [Fact]
    public void ACitationPastTheListOrToARowNotStoredIsRefusedByName()
    {
        var admitted = new StoredDocument("a", "https://a.test/a", "a", new DateOnly(2026, 9, 1), FetchedAt, "text", Admissibility.Accepted);

        var verdict = ClaimRules.Check(
            "What the company sells",
            "It sells test gear [D1]. It also sells software [D3]. And services [D2].",
            [],
            [admitted, null]);

        Assert.False(verdict.Passes);
        Assert.Equal(
            [ClaimRules.CitationOutOfRange, ClaimRules.NotStored],
            [.. verdict.Findings.Select(finding => finding.Reason)]);

        // The computed section is not asked for a citation, and is still refused a
        // figure the file does not hold.
        Assert.True(ClaimRules.Check(ClaimRules.ComputedSection, "It is an ordinary sentence.", [], []).Passes);
        Assert.False(ClaimRules.Check(ClaimRules.ComputedSection, "It closed at 12.34.", [], []).Passes);
    }

    static (string Section, string First, string Second) Attempts(string name)
    {
        var attempt = Prose().GetProperty("attempts").EnumerateArray().Single(one => one.GetProperty("name").GetString() == name);

        Assert.False(string.IsNullOrWhiteSpace(attempt.GetProperty("carries").GetString()));

        return (
            attempt.GetProperty("section").GetString()!,
            attempt.GetProperty("first").GetString()!,
            attempt.GetProperty("second").GetString()!);
    }

    static IReadOnlyList<StoredSection> AllRows(TemporaryStore store) =>
        [.. Sections().Select(written => written.Section).Distinct().SelectMany(section => Stored(store, section)).OrderBy(row => row.Version)];

    static string RunLogDetail(TemporaryStore store, string runId)
    {
        using var connection = new SqliteConnection($"Data Source={store.DatabaseFile}");
        connection.Open();

        using var command = connection.CreateCommand();
        command.CommandText = "SELECT detail FROM run_log WHERE run_id = $run_id AND stage = $stage;";
        command.Parameters.AddWithValue("$run_id", runId);
        command.Parameters.AddWithValue("$stage", ClaimChecker.Stage);

        return (string)command.ExecuteScalar()!;
    }
}
