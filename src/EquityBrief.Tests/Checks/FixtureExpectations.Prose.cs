using System.Globalization;
using System.Reflection;
using System.Text;
using System.Text.Json;
using EquityBrief.Core.Bars;
using EquityBrief.Core.Facts;
using EquityBrief.Core.Providers;
using EquityBrief.Core.Research;
using EquityBrief.Core.Time;
using EquityBrief.Tests.Harness;
using EquityBrief.Worker;
using EquityBrief.Worker.Facts;
using EquityBrief.Worker.Research;
using Microsoft.Extensions.Configuration;

namespace EquityBrief.Tests.Checks;

// fixture-expectations, 6.6: the prose writer over the recorded local model, the
// cause of a move held to the documents published inside it, and section 18's
// two rows about the local lane failing, each over the fixture's own dates.
//
// Every pass here runs against recordings and a feed that holds no client, so the
// replay reaches no network, and every request the tests make is one a recording
// answers: a request nobody recorded refuses by name, which is what would show a
// prompt that drifted from the one captured.
public partial class FixtureExpectations
{
    static readonly DateTimeOffset ProseNight = new(2026, 9, 8, 21, 10, 0, TimeSpan.Zero);

    internal static IClock ProseClock => FixedClock.At(ProseNight, SessionZones.UnitedStates);

    internal static LocalModelSettings LocalSettings(int? contextTokens = null) => new(null, null, null, contextTokens, null);

    static string[] Listed(JsonElement array) => [.. array.EnumerateArray().Select(item => item.GetString()!)];

    static string Iso(DateOnly date) => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    // The store the claim tests read, with the release KEYS filed as a stored row.
    internal static async Task<(TemporaryStore Store, StoredDocument Release)> WithRelease()
    {
        var store = await ClaimAdmissibility.WithSources();

        var url = ClaimAdmissibility.Exhibits().Single(exhibit => exhibit.Title.EndsWith("release-KEYS.htm", StringComparison.Ordinal)).Url;
        var row = Query(store, $"SELECT id, url, title, published_on, fetched_at, body, admissibility FROM source_document WHERE id = '{SourceDocuments.Id(url)}';").Single();

        using var connection = store.Open();
        using var command = connection.CreateCommand();

        command.CommandText = "SELECT id, url, title, published_on, fetched_at, body, admissibility FROM source_document WHERE id = $id;";
        command.Parameters.AddWithValue("$id", SourceDocuments.Id(url));

        using var reader = command.ExecuteReader();

        Assert.True(reader.Read(), row);

        return (store, new StoredDocument(
            reader.GetString(0),
            reader.GetString(1),
            reader.GetString(2),
            DateOnly.ParseExact(reader.GetString(3), "yyyy-MM-dd", CultureInfo.InvariantCulture),
            DateTimeOffset.Parse(reader.GetString(4), CultureInfo.InvariantCulture),
            reader.GetString(5),
            reader.GetString(6)));
    }

    // What a caller hands the writer: the release, for every researched section the
    // lane holds.
    internal static Dictionary<string, IReadOnlyList<StoredDocument>> Handed(StoredDocument release) =>
        new(StringComparer.Ordinal)
        {
            ["The cause of each large move"] = [release],
            ["What the company sells"] = [release],
            ["The segment commentary"] = [release],
        };

    // The release store after the two passes the recording holds, each followed by
    // the checker: the store a reader of KEYS's page opens once the local lane has
    // written it.
    internal static async Task<TemporaryStore> WithWrittenRelease()
    {
        var (store, document) = await WithRelease();

        var writer = new ProseWriter(new RecordedLocalModelFeed(Folder()), LocalSettings(), ProseWriter.DefaultLane, ProseClock, store.DatabaseFile);
        var checker = new ClaimChecker(ProseClock, store.DatabaseFile);

        for (var pass = 1; pass <= 2; pass++)
        {
            await writer.WriteAsync("KEYS", Handed(document), $"prose-written-{pass}");
            await checker.RunAsync($"claims-written-{pass}");
        }

        return store;
    }

    // The session a span of this many sessions ending on a date was measured from,
    // counted back over the exchange's own calendar rather than over stored bars.
    static DateOnly SessionsBefore(DateOnly ended, int sessions)
    {
        var day = ended;

        for (var counted = 0; counted < sessions;)
        {
            day = day.AddDays(-1);

            if (ExchangeClosures.IsSession(day))
            {
                counted++;
            }
        }

        return day;
    }

    static IReadOnlyList<(string Section, string Reason)> Lines(JsonElement detail, string list) =>
    [
        .. detail.GetProperty(list).EnumerateArray()
            .Select(line => (line.GetProperty("section").GetString()!, line.GetProperty("reason").GetString()!)),
    ];

    // ---- the replay ----

    [Fact]
    public async Task TheReplayWritesTheLocalLaneForEachNameItPassesOverTheRecordedModelWithNoNetwork()
    {
        var replay = Expected("prose").GetProperty("replay");
        var feed = new RecordedLocalModelFeed(Folder());

        using var store = await FixtureReplay.ReplayedAsync(feed);

        var names = Listed(replay.GetProperty("names"));

        Assert.Equal(names, FixtureReplay.ProsePassNames(store));

        // No network, asked of the objects: the recorded model holds no client and no
        // handler, and the on-demand set resolved from the fixture reaches nothing.
        Assert.DoesNotContain(
            typeof(RecordedLocalModelFeed).GetFields(BindingFlags.Instance | BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public),
            field => typeof(HttpClient).IsAssignableFrom(field.FieldType) || typeof(HttpMessageHandler).IsAssignableFrom(field.FieldType));
        Assert.False(OnDemandFeeds.FromFixture(Folder(), Providers.ResearchModelFeedTests.Shipped()).ReachesTheNetwork);

        Assert.Equal(names.Length * replay.GetProperty("modelCallsPerName").GetInt32(), feed.Requests);

        foreach (var name in names)
        {
            Assert.Equal(
                [.. Listed(replay.GetProperty("written")).Select(section =>
                    $"{section}|1|{LocalModelSettings.DefaultModel}|{replay.GetProperty("verdicts").GetProperty(name).GetString()}")],
                Query(store, $"SELECT section, version, model, status FROM research_section WHERE ticker = '{name}' ORDER BY section, version;"));

            var detail = JsonDocument.Parse(Query(store, $"SELECT detail FROM run_log WHERE stage = 'prose' AND run_id = 'replay-prose-{name}';").Single()).RootElement;

            Assert.Equal(replay.GetProperty("modelCallsPerName").GetInt32(), detail.GetProperty("modelCalls").GetInt32());
            Assert.Equal(
                [.. replay.GetProperty("notWritten").EnumerateObject().Select(line => (line.Name, line.Value.GetString()!)).OrderBy(line => line.Name, StringComparer.Ordinal)],
                Lines(detail, "notWritten").OrderBy(line => line.Section, StringComparer.Ordinal).ToArray());
        }
    }

    // ---- the release ----

    [Fact]
    public async Task APassOverTheReleaseWritesTheCauseOfTheOneMoveTheReleaseFallsInsideAndRetriesOnce()
    {
        var release = Expected("prose").GetProperty("release");
        var (store, document) = await WithRelease();

        using (store)
        {
            Assert.Equal(release.GetProperty("filedOn").GetString(), document.PublishedOn is { } filed ? Iso(filed) : null);

            var facts = FactsFile.Read(Query(store, $"SELECT payload FROM facts WHERE ticker = 'KEYS' AND session_date = '{Iso(ProseClock.SessionDateAt(ProseNight))}';").Single());

            // The pairs code hands the model, against the ones worked out by hand from
            // the filing date and the exchange calendar.
            var paired = SectionPrompt.MovesWithDocuments(facts, [new PromptDocument(document.Id, document.Title, document.PublishedOn, document.Body!)]);

            Assert.Equal(
                [.. release.GetProperty("movesInside").EnumerateArray().Select(move =>
                    $"{move.GetProperty("move").GetString()}|{move.GetProperty("measuredFrom").GetString()}|{move.GetProperty("ended").GetString()}")],
                paired.Select(pair => $"{pair.Move.Name}|{Iso(pair.Move.From!.Value)}|{Iso(pair.Move.To)}").ToArray());

            Assert.All(paired, pair => Assert.Equal(["D1"], pair.Markers));

            var feed = new RecordedLocalModelFeed(Folder());
            var writer = new ProseWriter(feed, LocalSettings(), ProseWriter.DefaultLane, ProseClock, store.DatabaseFile);
            var checker = new ClaimChecker(ProseClock, store.DatabaseFile);

            var passes = release.GetProperty("passes").EnumerateArray().ToArray();

            Assert.Equal(2, passes.Length);

            for (var pass = 0; pass < passes.Length; pass++)
            {
                var expected = passes[pass];
                var before = feed.Requests;

                var outcome = await writer.WriteAsync("KEYS", Handed(document), $"prose-release-{pass + 1}");
                var moved = await checker.RunAsync($"claims-release-{pass + 1}");

                Assert.Equal(Listed(expected.GetProperty("written")), outcome.Written.Select(section => section.Section).ToArray());
                Assert.Equal(expected.GetProperty("modelCalls").GetInt32(), feed.Requests - before);
                Assert.Equal(expected.GetProperty("modelCalls").GetInt32(), outcome.ModelCalls);

                // The run log's count of rows is the store's: one row per section written.
                Assert.Equal(
                    expected.GetProperty("written").GetArrayLength().ToString(CultureInfo.InvariantCulture),
                    Query(store, $"SELECT rows_written FROM run_log WHERE run_id = 'prose-release-{pass + 1}';").Single());
                Assert.Empty(outcome.NotWritten);

                Assert.Equal(
                    expected.TryGetProperty("retried", out var retried) ? Listed(retried) : [],
                    outcome.Written.Where(section => section.Retry).Select(section => section.Section).ToArray());

                Assert.Equal(
                    expected.TryGetProperty("skipped", out var skipped)
                        ? [.. skipped.EnumerateObject().Select(line => (line.Name, line.Value.GetString()!))]
                        : [],
                    outcome.Skipped.Select(section => (section.Section, section.Reason)).ToArray());

                Assert.Equal(
                    [.. expected.GetProperty("verdicts").EnumerateObject().Select(verdict => $"{verdict.Name}|{verdict.Value.GetString()}").Order(StringComparer.Ordinal)],
                    moved.Checked.Select(section => $"{section.Section}|{section.Status}").Order(StringComparer.Ordinal).ToArray());
            }

            // The accepted cause names the one move the release falls inside and no other.
            var cause = Query(store, $"SELECT prose FROM research_section WHERE ticker = 'KEYS' AND section = '{ClaimRules.CauseSection}' AND status = 'accepted';").Single();
            var dates = ClaimRules.Figures(cause).Where(figure => figure.Kind == FigureKind.Date && figure.Date is not null).Select(figure => figure.Date!.Value).ToHashSet();

            Assert.Equal(
                Listed(release.GetProperty("causeNames")),
                MoveWindows.In(facts).Where(move => dates.Contains(move.To)).Select(move => Iso(move.To)).ToArray());
        }
    }

    [Fact]
    public async Task EachMoveIsMeasuredFromTheSessionItsSpanCountsBackToOnTheExchangeCalendar()
    {
        // Derived rather than read back: each start is the session as many exchange
        // sessions before the end as the move spans, counted over the closure table,
        // which is the close the annotator divided by for a series with no hole.
        using var store = await WithFacts();

        var moves = 0;

        foreach (var name in FixtureExpectation.Names)
        {
            var facts = FactsFile.Read(Query(store, $"SELECT payload FROM facts WHERE ticker = '{name}';").Single());

            foreach (var move in MoveWindows.In(facts))
            {
                var sessions = int.Parse(facts.Single(fact => fact.Name == move.Name + " sessions").Value, CultureInfo.InvariantCulture);

                Assert.Equal(Iso(SessionsBefore(move.To, sessions)), move.From is { } from ? Iso(from) : "no start");

                moves++;
            }
        }

        // Eight moves a name over four names, stated in advance.
        Assert.Equal(32, moves);
    }

    // ---- the lane, both directions ----

    [Fact]
    public async Task ASectionTheLaneDoesNotHoldIsNeverAskedForAndEverySectionItHoldsIs()
    {
        var lane = Listed(Expected("prose").GetProperty("lane"));

        Assert.Equal(lane, ProseWriter.DefaultLane);

        var (store, document) = await WithRelease();

        using (store)
        {
            // A lane of one, handed documents for sections it does not hold, the short
            // version among them: asked for the one, writing the one, and naming nothing
            // else on the run log, not even as unwritten.
            var handed = new Dictionary<string, IReadOnlyList<StoredDocument>>(Handed(document), StringComparer.Ordinal)
            {
                ["The short version"] = [document],
            };

            var feed = new RecordedLocalModelFeed(Folder());

            var outcome = await new ProseWriter(feed, LocalSettings(), ["What the company sells"], ProseClock, store.DatabaseFile)
                .WriteAsync("KEYS", handed, "prose-lane-of-one");

            Assert.Equal(["What the company sells"], feed.Asked.Select(request => request.Section).ToArray());
            Assert.Equal(["What the company sells"], Query(store, "SELECT DISTINCT section FROM research_section WHERE ticker = 'KEYS';"));

            var detail = JsonDocument.Parse(Query(store, "SELECT detail FROM run_log WHERE run_id = 'prose-lane-of-one';").Single()).RootElement;

            Assert.Equal(
                ["What the company sells"],
                detail.GetProperty("written").EnumerateArray().Select(line => line.GetProperty("section").GetString()!)
                    .Concat(Lines(detail, "notWritten").Select(line => line.Section))
                    .Concat(Lines(detail, "skipped").Select(line => line.Section))
                    .ToArray());

            Assert.Empty(outcome.NotWritten);
        }

        var (whole, release) = await WithRelease();

        using (whole)
        {
            // The other direction: the lane configuration holds is what is asked, whole
            // and in its order.
            var configured = new ConfigurationBuilder().AddInMemoryCollection(
                lane.Select((section, index) => new KeyValuePair<string, string?>($"{LocalLane.SectionsKey}:{index}", section))).Build();

            var feed = new RecordedLocalModelFeed(Folder());

            await new ProseWriter(feed, LocalLane.Settings(configured), LocalLane.Sections(configured), ProseClock, whole.DatabaseFile)
                .WriteAsync("KEYS", Handed(release), "prose-lane-whole");

            Assert.Equal(lane, feed.Asked.Select(request => request.Section).ToArray());
        }

        // A lane configuration lists nothing for is this machine's default, and one
        // naming a section figure 12.2 does not is refused when it is read.
        Assert.Equal(ProseWriter.DefaultLane, LocalLane.Sections(new ConfigurationBuilder().Build()));

        var misspelt = new ConfigurationBuilder().AddInMemoryCollection(
            [new KeyValuePair<string, string?>($"{LocalLane.SectionsKey}:0", "The short verison")]).Build();

        Assert.Contains("'The short verison'", Assert.Throws<InvalidOperationException>(() => LocalLane.Sections(misspelt)).Message, StringComparison.Ordinal);
    }

    // ---- section 18: what the machine cannot hold ----

    [Fact]
    public async Task ASectionTheMachineCannotHoldIsRefusedBeforeAnyCallIsMadeAndTheRunLogNamesIt()
    {
        var hold = Expected("prose").GetProperty("cannotHold");
        var context = hold.GetProperty("contextTokens").GetInt32();
        var (store, document) = await WithRelease();

        using (store)
        {
            var feed = new RecordedLocalModelFeed(Folder());

            var outcome = await new ProseWriter(feed, LocalSettings(context), ProseWriter.DefaultLane, ProseClock, store.DatabaseFile)
                .WriteAsync("KEYS", Handed(document), "prose-cannot-hold");

            // Asked for the section that fits and for nothing else. Each refused section
            // has a recording keyed on the request a full context makes, since a context
            // is not part of what the model is asked, so a writer that attempted one
            // would have written it: not being asked is what shows the refusal came
            // before the call rather than after it failed.
            Assert.Equal(Listed(hold.GetProperty("asked")), feed.Asked.Select(request => request.Section).ToArray());
            Assert.Equal(Listed(hold.GetProperty("refused")), outcome.NotWritten.Select(section => section.Section).ToArray());
            Assert.All(outcome.NotWritten, section => Assert.StartsWith(ProseWriter.CannotHold + ": ", section.Reason, StringComparison.Ordinal));

            // Nothing is stored for a refused section, so it is absent as usual.
            Assert.Equal(Listed(hold.GetProperty("asked")), Query(store, "SELECT DISTINCT section FROM research_section WHERE ticker = 'KEYS';"));

            // The run log names each section and the reason.
            var detail = JsonDocument.Parse(Query(store, "SELECT detail FROM run_log WHERE run_id = 'prose-cannot-hold';").Single()).RootElement;

            Assert.Equal(outcome.NotWritten.Select(section => (section.Section, section.Reason)).ToArray(), Lines(detail, "notWritten"));

            // The derivation the expectation states, held against the prompts rather than
            // taken on its word: the release's text outside digits and outside ASCII
            // exceeds the room alone, and the asked prompt is shorter in characters than
            // the room, so it fits however its characters count.
            var room = context - OpenAiCompatibleModelFeed.AnswerTokens;
            var plain = document.Body!.EnumerateRunes().Count(rune => rune.IsAscii && !Rune.IsDigit(rune));

            Assert.True(plain / SectionPrompt.CharactersPerToken > room, $"The release carries {plain} plain characters, which fit in {room} tokens.");

            var asked = feed.Asked.Single();

            Assert.True(asked.System.Length + asked.Prompt.Length <= room, $"The facts-only prompt is {asked.System.Length + asked.Prompt.Length} characters against {room}.");
        }
    }

    // ---- section 18: the local model unavailable ----

    [Fact]
    public async Task TheLocalModelUnavailableLeavesEverySectionInTheLaneUnwrittenAfterOneCall()
    {
        var gone = Expected("prose").GetProperty("unavailable");
        var (store, document) = await WithRelease();

        using (store)
        {
            var handler = new NothingListening();

            using var client = new HttpClient(handler) { BaseAddress = new Uri(LocalModelSettings.DefaultBaseAddress) };

            var outcome = await new ProseWriter(
                    new OpenAiCompatibleModelFeed(client, LocalSettings()),
                    LocalSettings(),
                    ProseWriter.DefaultLane,
                    ProseClock,
                    store.DatabaseFile)
                .WriteAsync("KEYS", Handed(document), "prose-unavailable");

            Assert.True(outcome.Unavailable);
            Assert.Equal(gone.GetProperty("modelCalls").GetInt32(), handler.Sent);
            Assert.Equal(gone.GetProperty("modelCalls").GetInt32(), outcome.ModelCalls);

            Assert.Equal(Listed(gone.GetProperty("notWritten")), outcome.NotWritten.Select(section => section.Section).ToArray());
            Assert.All(outcome.NotWritten, section => Assert.StartsWith(ProseWriter.Unavailable, section.Reason, StringComparison.Ordinal));

            Assert.Empty(Query(store, "SELECT section FROM research_section WHERE ticker = 'KEYS';"));
            Assert.Equal(gone.GetProperty("outcome").GetString(), Query(store, "SELECT outcome FROM run_log WHERE run_id = 'prose-unavailable';").Single());
        }
    }

    // A runtime with nothing listening, as the transport reports it.
    sealed class NothingListening : HttpMessageHandler
    {
        public int Sent { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Sent++;

            throw new HttpRequestException("No connection could be made because the target machine actively refused it.");
        }
    }

    // ---- what the machine can hold, measured ----

    [Fact]
    public async Task TheTokenEstimateIsAtOrAboveTheRuntimesOwnCountForEveryRecordedCall()
    {
        // The population is every section recording the fixture holds, reached by
        // making the requests that recorded them: the replay's passes and the release's
        // two. Both directions, so a recording nothing asks for is reported rather than
        // kept, and a count is stated in advance.
        var asked = new List<ModelRequest>();

        var replayed = new RecordedLocalModelFeed(Folder());

        using (await FixtureReplay.ReplayedAsync(replayed))
        {
            asked.AddRange(replayed.Asked);
        }

        var (store, document) = await WithRelease();

        using (store)
        {
            var feed = new RecordedLocalModelFeed(Folder());
            var writer = new ProseWriter(feed, LocalSettings(), ProseWriter.DefaultLane, ProseClock, store.DatabaseFile);

            await writer.WriteAsync("KEYS", Handed(document), "prose-measured-1");
            await new ClaimChecker(ProseClock, store.DatabaseFile).RunAsync("claims-measured-1");
            await writer.WriteAsync("KEYS", Handed(document), "prose-measured-2");

            asked.AddRange(feed.Asked);
        }

        var recorded = Directory.GetFiles(Folder(), RecordedLocalModelFeed.FilePrefix + "*.json").Select(Path.GetFileName).Order(StringComparer.Ordinal).ToArray();

        Assert.Equal(7, recorded.Length);
        Assert.Equal(recorded, asked.Select(RecordedLocalModelFeed.FileFor).Distinct().Order(StringComparer.Ordinal).ToArray());

        foreach (var request in asked)
        {
            using var response = JsonDocument.Parse(File.ReadAllText(Path.Combine(Folder(), RecordedLocalModelFeed.FileFor(request))));

            var counted = response.RootElement.GetProperty("usage").GetProperty("prompt_tokens").GetInt32();

            Assert.True(
                SectionPrompt.EstimatedTokens(request) >= counted,
                $"{request.Section} on {request.Key} is estimated at {SectionPrompt.EstimatedTokens(request)} tokens and the runtime counted {counted}.");
        }
    }

    // ---- the segment table in the facts file ----

    [Fact]
    public async Task TheFactsFileCarriesTheLatestQuarterOfTheSegmentTableTheArchiveSupplied()
    {
        var (store, _) = await WithRelease();

        using (store)
        {
            var payload = Query(store, "SELECT payload FROM fundamentals WHERE ticker = 'KEYS' ORDER BY filing_date DESC LIMIT 1;").Single();

            using var document = JsonDocument.Parse(payload);

            var segments = document.RootElement.GetProperty("segments");

            var latest = segments.GetProperty("periods").EnumerateArray()
                .Where(period => period.GetProperty("months").GetInt32() == 3)
                .Select(period => period.GetProperty("ended").GetString()!)
                .Max(StringComparer.Ordinal)!;

            var facts = FactsAssembler.Segments(document.RootElement);

            // Every segment fact is the latest quarter's, and every quarter line the table
            // carries for that period is a fact, counted off the table rather than stated.
            var lines = segments.GetProperty("consolidated").EnumerateArray()
                .Concat(segments.GetProperty("groups").EnumerateArray().SelectMany(group => group.GetProperty("figures").EnumerateArray()))
                .Count(line => line.GetProperty("months").GetInt32() == 3
                    && line.GetProperty("ended").GetString() == latest
                    && line.GetProperty("value").ValueKind == JsonValueKind.String);

            Assert.True(lines >= 2, $"The table carries {lines} lines for {latest}.");
            Assert.Equal(lines, facts.Count);
            Assert.All(facts, fact => Assert.EndsWith(" " + latest, fact.Name, StringComparison.Ordinal));
            Assert.All(facts, fact => Assert.Equal(FactsAssembler.FromFundamentals, fact.Source));
            Assert.Equal(facts.Count, facts.Select(fact => fact.Name).Distinct(StringComparer.Ordinal).Count());

            // And the file the writer reads carries them, which is what the segment
            // commentary needed and did not have before 6.6.
            var file = FactsFile.Read(Query(store, $"SELECT payload FROM facts WHERE ticker = 'KEYS' AND session_date = '{Iso(ProseClock.SessionDateAt(ProseNight))}';").Single());

            Assert.All(facts, fact => Assert.Contains(file, held => held.Name == fact.Name && held.Value == fact.Value));
        }
    }
}
