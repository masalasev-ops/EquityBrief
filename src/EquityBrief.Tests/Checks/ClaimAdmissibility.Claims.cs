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
        Assert.Equal($"{ClaimRules.UnmatchedFigure}: 66.3%", row.Reason);
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
        Assert.Contains("66.3%", rows[0].Reason!, StringComparison.Ordinal);
        Assert.StartsWith(ClaimChecker.RejectedTwice, rows[1].Reason!, StringComparison.Ordinal);
        Assert.Contains("66.3%", rows[1].Reason!, StringComparison.Ordinal);

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

        // Three clean paragraphs from 6.6, when the cause of the one move the
        // release falls inside joined the two written at 6.4.
        Assert.Equal(3, accepted.Length);
        Assert.Contains(SectionNamed("a clean computed paragraph").Prose, accepted);
        Assert.Contains(SectionNamed("a clean researched paragraph").Prose, accepted);
        Assert.Contains(SectionNamed("a cause of the move the release falls inside").Prose, accepted);

        Assert.DoesNotContain(SectionNamed("a poisoned paragraph").Prose, accepted);
        Assert.DoesNotContain(SectionNamed("an unsourced claim").Prose, accepted);
        Assert.DoesNotContain(SectionNamed("a claim resting on a refused document").Prose, accepted);
        Assert.DoesNotContain(SectionNamed("a cause citing a release filed after the move").Prose, accepted);
        Assert.DoesNotContain(SectionNamed("a cause naming no move").Prose, accepted);

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
        // The rule over the theme store, as 6.9 settled it: a theme has no facts
        // file and nothing the store holds is computed for an industry, so a figure
        // in a theme section is refused and a sentence of words citing a document is
        // accepted. 6.4 wrote this as the carried rule and 6.9 kept it as the rule.
        // see: A theme section states no figure, because nothing the store holds is computed for an industry
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

    [Fact]
    public async Task TheVerdictsAreWhatTheFixturesOwnClaimsExpectationSaysTheRulesProduce()
    {
        // Done condition 7's derived expectation. Every paragraph is checked in one
        // run, so a checker that refused everything satisfies the refusals and
        // fails the two acceptances, and one that accepted everything fails the
        // rest.
        var expected = Expected("claims");

        Assert.Equal(Ticker, expected.GetProperty("ticker").GetString());

        using var store = await WithSources();

        var sections = Sections();
        var version = 0;
        var versions = new Dictionary<string, int>(StringComparer.Ordinal);

        // Each paragraph on a day of its own. Three of them are versions of one
        // section, and on one day the third would be read as the retry of the
        // second, which is the rule working rather than the verdict this file
        // states for a first attempt. The facts file on or before each of those
        // days is the fixture night's.
        var day = DateOnly.ParseExact(AsOf, "yyyy-MM-dd", CultureInfo.InvariantCulture);

        foreach (var written in sections)
        {
            versions[written.Name] = ++version;
            Pending(store, written, version, day.AddDays(version - 1).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        }

        await Checker(store).RunAsync("check-expected");

        var rows = sections
            .Select(written => written.Section)
            .Distinct()
            .SelectMany(section => Stored(store, section))
            .ToDictionary(row => row.Version);

        var verdicts = expected.GetProperty("verdicts");

        Assert.Equal(sections.Count, verdicts.EnumerateObject().Count());

        foreach (var verdict in verdicts.EnumerateObject())
        {
            var row = rows[versions[verdict.Name]];
            var reason = verdict.Value.GetProperty("reason");
            var named = verdict.Value.TryGetProperty("offending", out var offending)
                && offending.ValueKind == JsonValueKind.String;

            Assert.Equal(verdict.Value.GetProperty("status").GetString(), row.Status);

            var stated = reason.ValueKind == JsonValueKind.Null
                ? null
                : named
                    ? $"{reason.GetString()}: {offending.GetString()}"
                    : reason.GetString();

            Assert.Equal(stated, row.Reason);
        }

        // The attempts, each in a store of its own so one pair's versions cannot
        // be read as the other's retry.
        foreach (var pair in expected.GetProperty("attempts").EnumerateObject())
        {
            using var attempted = await WithSources();
            var (section, first, second) = Attempts(pair.Name);

            Pending(attempted, SectionNamed(first), 1);
            await Checker(attempted).RunAsync("check-first");
            Pending(attempted, SectionNamed(second), 2);
            await Checker(attempted).RunAsync("check-second");

            Assert.Equal(
                [.. pair.Value.EnumerateArray().Select(status => status.GetString()!)],
                [.. Stored(attempted, section).Select(row => row.Status)]);
        }

        var numbers = expected.GetProperty("numbersInTheCleanComputedParagraph");
        var figures = ClaimRules.Sentences(SectionNamed("a clean computed paragraph").Prose)
            .SelectMany(sentence => ClaimRules.Figures(sentence.Text))
            .ToArray();

        Assert.Equal(numbers.GetProperty("figures").GetInt32(), figures.Count(figure => figure.Kind == FigureKind.Figure));
        Assert.Equal(numbers.GetProperty("windows").GetInt32(), figures.Count(figure => figure.Kind == FigureKind.Window));
        Assert.Equal(numbers.GetProperty("dates").GetInt32(), figures.Count(figure => figure.Kind == FigureKind.Date));

        Assert.Equal(
            ["status", "reject_reason"],
            [.. expected.GetProperty("columnsTheCheckerWrites").EnumerateArray().Select(column => column.GetString()!)]);

        Assert.Equal(1, expected.GetProperty("retriesPerPass").GetInt32());
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

        var rows = table.Body.Where(row => row.Count > 1).ToArray();
        var named = rows.Select(row => row[0]).ToArray();

        Assert.Equal(named, ClaimRules.Sections);
        Assert.Contains(ClaimRules.ComputedSection, named);

        // From 6.6 the table says what code works out, what the model is asked to
        // write and what each section must pass, so the parts of a row the code
        // carries are read against it in both directions. Five columns, the last
        // being the rules.
        Assert.All(rows, row => Assert.Equal(5, row.Count));

        // The lane each row states is this machine's default, which is the list the
        // writer is handed when configuration names none.
        Assert.Equal(
            ProseWriter.DefaultLane,
            rows.Where(row => row[1] == "local").Select(row => row[0]).ToArray());
        Assert.All(rows, row => Assert.Contains(row[1], new[] { "local", "paid" }));

        // Every section a row says a model writes has a prompt to ask it in.
        Assert.All(named, section => Assert.True(SectionPrompt.Asks.ContainsKey(section), section));

        // The segment row's code and model cells name the period the facts file carries,
        // which is a quarter only where the filing files one.
        var segments = rows.Single(row => row[0] == Evidence.Segments);

        Assert.Contains("latest period of the segment table", segments[2], StringComparison.Ordinal);
        Assert.DoesNotContain("for the quarter", segments[3], StringComparison.Ordinal);

        // The rules column, against the checker. The one section held to no citation
        // is the one row saying it cites no document, and the one section held to its
        // move's span is the one row naming a document published inside that move.
        Assert.Equal(
            [.. ClaimRules.Sections.Where(section => !ClaimRules.IsResearched(section))],
            rows.Where(row => row[4].Contains("It cites no document", StringComparison.Ordinal)).Select(row => row[0]).ToArray());

        Assert.Equal(
            [ClaimRules.CauseSection],
            rows.Where(row => row[4].Contains("published inside that move", StringComparison.Ordinal)).Select(row => row[0]).ToArray());
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

        // A window written in words from eleven up is read as the number it names and held as
        // one in digits is: the 200 of sma200 by its words, and a length no name carries refused.
        Assert.True(Holds("the two-hundred-day average"));
        Assert.True(Holds("the two hundred day average"));
        Assert.False(Holds("the one-hundred-day average"));
        Assert.False(Holds("over twenty sessions"));
        Assert.Equal(21m, ClaimRules.ValueInWords("twenty-one"));
    }

    [Fact]
    public void AMovesSpanHoldsBothItsEdgesAndACauseCitingADocumentOutsideItIsRefused()
    {
        Fact[] facts =
        [
            new("move 8 measured from", "2026-08-17", "move"),
            new("move 8 per cent", "-13.980341", "move"),
            new("move 8 session", "2026-08-24", "move"),
            new("move 8 sessions", "5", "move"),
            new("largest move session", "2026-02-24", "move"),
        ];

        var windows = MoveWindows.In(facts);
        var eighth = Assert.Single(windows, window => window.Name == "move 8");

        Assert.Equal(new DateOnly(2026, 8, 17), eighth.From);
        Assert.Equal(new DateOnly(2026, 8, 24), eighth.To);

        // Both edges inside, a day either side outside.
        Assert.False(MoveWindows.Holds(eighth, new DateOnly(2026, 8, 16)));
        Assert.True(MoveWindows.Holds(eighth, new DateOnly(2026, 8, 17)));
        Assert.True(MoveWindows.Holds(eighth, new DateOnly(2026, 8, 24)));
        Assert.False(MoveWindows.Holds(eighth, new DateOnly(2026, 8, 25)));

        // A move whose start the file does not carry holds nothing, its own end
        // included, because a span with one edge cannot be shown to hold a date.
        var startless = Assert.Single(windows, window => window.Name == "largest move");

        Assert.Null(startless.From);
        Assert.False(MoveWindows.Holds(startless, startless.To));

        // And through the checker, the start half: a document published the session
        // before a move began is refused for it, and one published on its first day
        // is not.
        StoredDocument Published(DateOnly on) =>
            new("d1", "https://www.sec.gov/Archives/edgar/data/1/1/release.htm", "a release", on, FetchedAt, "text", Admissibility.Accepted);

        const string Sentence = "The move ending 2026-08-24 fell 13.98 per cent after the release [D1].";

        var early = ClaimRules.Check(ClaimRules.CauseSection, Sentence, facts, [Published(new DateOnly(2026, 8, 14))]);

        Assert.Equal(ClaimRules.CauseOutsideItsMove, Assert.Single(early.Findings).Reason);
        Assert.Equal("D1 published 2026-08-14, outside the move ending 2026-08-24 measured from 2026-08-17", early.Findings[0].Offending);

        Assert.True(ClaimRules.Check(ClaimRules.CauseSection, Sentence, facts, [Published(new DateOnly(2026, 8, 17))]).Passes);

        // The rule is the cause section's alone: the same sentence in another
        // researched section is held to the citation and number rules and passes.
        Assert.True(ClaimRules.Check("What the company sells", Sentence, facts, [Published(new DateOnly(2026, 8, 14))]).Passes);
    }

    [Fact]
    public void ThePercentageBeforeAWeekIsAFigureAndDigitsInsideANameAreNotRead()
    {
        // Two misreads of the live measurement, kept as the cases that caught
        // them, and the name case the reader was written to leave alone.
        Assert.Equal(FigureKind.Figure, Assert.Single(ClaimRules.Figures("shares rose 10.86% week over week")).Kind);
        Assert.Equal(FigureKind.Window, Assert.Single(ClaimRules.Figures("the 52-week high")).Kind);

        Assert.Empty(ClaimRules.Figures("the iPhone17 and an H100 on 5G"));

        // A third, from the first key a model wrote at 6.6: the length of an
        // indicator's window written in periods, taken for a figure and passed
        // because a move of 13.89 per cent rounds to 14. Read as a window, it holds
        // where a figure's name carries it and is refused where none does.
        var period = Assert.Single(
            ClaimRules.Figures("the relative strength index for 14 periods sits at 45.43"),
            figure => figure.Magnitude == 14m);

        Assert.Equal(FigureKind.Window, period.Kind);
        Assert.True(ClaimRules.IsAWindow(period, [new("rsi14", "45.431383", "indicator")]));
        Assert.False(ClaimRules.IsAWindow(period, [new("move 6 per cent", "-13.888116", "move")]));

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
    public void ANameEndingInAnAbbreviationDoesNotEndTheSentenceItIsIn()
    {
        // Two sentences the research model wrote for NFLX's cause in 6.11's production runs, which
        // the checker split after "Bros." and after "Jr." into a fragment naming no document and a
        // remainder naming no move's session, so the section was refused for two things its prose
        // did not do. Two members of the index are named with an abbreviation the list did not
        // hold, and a person's name with a suffix.
        var sentences = ClaimRules.Sentences(
            "The session ending 2026-03-03 came after Republican state attorneys general pressed the Department of Justice to scrutinize Netflix's Warner Bros. bid, arguing the deal would produce undue market concentration in streaming and theatrical releases [D3]. " +
            "The session ending 2026-03-02 came as coverage described Netflix's appetite for fights between older athletes, such as the rematch between Floyd Mayweather Jr. and Manny Pacquiao [D4]. " +
            "The Estee Lauder Cos. reported in the same week, as did a company Sr. managers run [D1].");

        Assert.Equal(3, sentences.Count);
        Assert.Equal([3], sentences[0].Citations);
        Assert.Equal([4], sentences[1].Citations);
        Assert.Equal([1], sentences[2].Citations);
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

    [Fact]
    public void AFigureHeldOnlyForAPeriodLongerThanAQuarterIsRefusedUnderAnotherPeriodsNameAndPassesUnderItsOwn()
    {
        // A facts file after an annual report: twelve-month segment figures beside the
        // fundamentals' own quarter, whose net income the segment operating income shares.
        // see: A segment figure held for a period longer than a quarter is asked for by that period and refused where its sentence names a period of another length
        Fact[] facts =
        [
            new("latest quarter end", "2026-06-30", "fundamental"),
            new("latest quarter revenue", "76400000000", "fundamental"),
            new("latest quarter net income", "37000000000", "fundamental"),
            new("segment total Revenue 12 months to 2026-06-30", "270000000000", "fundamental"),
            new("segment Productivity Revenue 12 months to 2026-06-30", "80000000000", "fundamental"),
            new("segment Productivity Operating income 12 months to 2026-06-30", "37000000000", "fundamental"),
        ];

        StoredDocument?[] sources = [new StoredDocument("a", "https://a.test/a", "a", new DateOnly(2026, 7, 30), FetchedAt, "text", Admissibility.Accepted)];

        (string Offending, string Reason)[] Findings(IReadOnlyList<Fact> file, string prose) =>
            [.. ClaimRules.Check("The segment commentary", prose, file, sources).Findings.Select(finding => (finding.Offending, finding.Reason))];

        (string, string)[] Named(string figure) => [(figure, ClaimRules.FigureNamedForAnotherPeriod)];

        // A year's figure under a shorter period's name is refused and names the figure: a
        // quarter by its word, its label, its adjective and its three months, a half as the
        // first half and by its label, and nine months.
        Assert.Equal(Named("$80 billion"), Findings(facts, "Productivity reported revenue of $80 billion for the quarter. [D1]"));
        Assert.Equal(Named("$270 billion"), Findings(facts, "Revenue was $270 billion in Q4. [D1]"));
        Assert.Equal(Named("$80 billion"), Findings(facts, "Productivity's quarterly revenue was $80 billion. [D1]"));
        Assert.Equal(Named("$80 billion"), Findings(facts, "In the three months to June 30, 2026, Productivity reported revenue of $80 billion. [D1]"));
        Assert.Equal(Named("$80 billion"), Findings(facts, "Productivity reported revenue of $80 billion for the first half. [D1]"));
        Assert.Equal(Named("$80 billion"), Findings(facts, "Productivity reported revenue of $80 billion in H1. [D1]"));
        Assert.Equal(Named("$80 billion"), Findings(facts, "Productivity reported revenue of $80 billion for the nine months to June 30, 2026. [D1]"));

        // Under its own period it passes, the months in digits as the prompt asks and in words
        // as a filing writes them, since the length of a period the file names is not a figure.
        // Its own period beside another is refused, which is the cost the decision states.
        Assert.Empty(Findings(facts, "Productivity reported revenue of $80 billion for the 12 months to 2026-06-30. [D1]"));
        Assert.Empty(Findings(facts, "Productivity reported revenue of $80 billion for the twelve months to June 30, 2026. [D1]"));
        Assert.Equal(Named("$80 billion"), Findings(facts, "Productivity reported revenue of $80 billion for the twelve months to June 30, 2026, more than in any quarter. [D1]"));

        // A period in digits the file does not name is also a figure it does not hold.
        Assert.Equal(
            [("$80 billion", ClaimRules.FigureNamedForAnotherPeriod), ("6", ClaimRules.UnmatchedFigure)],
            Findings(facts, "Productivity reported revenue of $80 billion for the 6 months to 2026-06-30. [D1]"));

        // The fundamentals' own quarter under a quarter's name passes, and so does a figure a
        // quarter's fact also holds.
        Assert.Empty(Findings(facts, "Revenue was $76.4 billion for the quarter. [D1]"));
        Assert.Empty(Findings(facts, "Productivity operating income was $37 billion for the quarter. [D1]"));

        // And over a segment table of a quarter no period's name is read, which is where the
        // rule ends: a quarter's figure under a year's name passes as it always did.
        Fact[] quarterly = [.. facts.Select(fact => fact with { Name = fact.Name.Replace("12 months to 2026-06-30", "2026-06-30", StringComparison.Ordinal) })];

        Assert.Empty(Findings(quarterly, "Productivity reported revenue of $80 billion for the quarter. [D1]"));
        Assert.Empty(Findings(quarterly, "Productivity reported revenue of $80 billion for the twelve months to June 30, 2026. [D1]"));
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
