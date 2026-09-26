using System.Globalization;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using EquityBrief.Core.Bars;
using EquityBrief.Core.Facts;
using EquityBrief.Core.Providers;
using EquityBrief.Core.Research;
using EquityBrief.Core.Time;
using EquityBrief.Tests.Harness;
using EquityBrief.Worker;
using EquityBrief.Worker.Facts;
using EquityBrief.Worker.Fundamentals;
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

    // The lane the release passes were recorded over, which held the cause of each
    // large move until 6.8's fixture comparison moved it to the paid lane. Read from the
    // expectation, which says so beside it.
    internal static string[] ReleaseLane => Listed(Expected("prose").GetProperty("releaseLane"));

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

        var writer = new ProseWriter(new RecordedLocalModelFeed(Folder()), LocalSettings(), ReleaseLane, ProseClock, store.DatabaseFile);
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

    // A local runtime whose first answers come back unusable, as a thinking model's answer spent
    // on its reasoning does, and which otherwise answers as the recording does.
    sealed class UnusableFirst(ILocalModelFeed inner, int unusable) : ILocalModelFeed
    {
        public int Requests { get; private set; }

        public Task<ModelAnswer> CompleteAsync(ModelRequest request, CancellationToken cancellation = default) =>
            ++Requests <= unusable
                ? throw new ProviderRefusal(
                    $"The local model returned no answer for {request.Section}: the finish reason was length after 1024 completion token(s).",
                    transient: false,
                    unusable: true)
                : inner.CompleteAsync(request, cancellation);
    }

    [Fact]
    public async Task AnAnswerThatComesBackUnusableIsAskedForOnceMoreAndASecondLeavesTheSectionOutSayingSo()
    {
        // One unusable answer: asked again, and the second is the section.
        var (once, document) = await WithRelease();

        using (once)
        {
            var feed = new UnusableFirst(new RecordedLocalModelFeed(Folder()), 1);
            var outcome = await new ProseWriter(feed, LocalSettings(), ["What the company sells"], ProseClock, once.DatabaseFile)
                .WriteAsync("KEYS", Handed(document), "prose-asked-again");

            Assert.Equal(2, feed.Requests);
            Assert.Equal(["What the company sells"], outcome.Written.Select(section => section.Section));
            Assert.Empty(outcome.NotWritten);
        }

        // Two: left out with the plain reason a person reads on the page, and what the model sent
        // on the run log's row rather than there.
        var (twice, release) = await WithRelease();

        using (twice)
        {
            var feed = new UnusableFirst(new RecordedLocalModelFeed(Folder()), 2);
            var outcome = await new ProseWriter(feed, LocalSettings(), ["What the company sells"], ProseClock, twice.DatabaseFile)
                .WriteAsync("KEYS", Handed(release), "prose-unusable-twice");

            Assert.Equal(2, feed.Requests);
            Assert.Empty(outcome.Written);
            Assert.Equal(ProseWriter.NoUsableAnswer, Assert.Single(outcome.NotWritten).Reason);
            Assert.DoesNotContain("finish reason", ProseWriter.NoUsableAnswer, StringComparison.Ordinal);
            Assert.Contains("finish reason was length", Query(twice, "SELECT detail FROM run_log WHERE run_id = 'prose-unusable-twice';").Single(), StringComparison.Ordinal);
        }
    }

    // A local runtime answering a section's first draft with one text and every later draft with
    // another, noting what it was asked.
    sealed class TwoDrafts(string first, string second) : ILocalModelFeed
    {
        public int Requests => Asked.Count;

        public List<ModelRequest> Asked { get; } = [];

        public Task<ModelAnswer> CompleteAsync(ModelRequest request, CancellationToken cancellation = default)
        {
            Asked.Add(request);

            return Task.FromResult(new ModelAnswer(request.Model, Asked.Count == 1 ? first : second, 0, 0, "stop"));
        }
    }

    [Fact]
    public async Task ASectionTheCheckerRefusedIsAskedOnceMoreToldWhyAndTheSecondDraftStands()
    {
        // The local lane's retry over a scripted runtime: the key's first draft quotes a close no
        // facts file holds and is refused, and the next pass asks once more, told why, and stores
        // the second draft, which quotes the close the file holds and is accepted.
        var (store, document) = await WithRelease();

        using (store)
        {
            var feed = new TwoDrafts("Keysight closed at 999.99 on the night.", "Keysight closed at 333.42 on the night.");
            var writer = new ProseWriter(feed, LocalSettings(), [ClaimRules.ComputedSection], ProseClock, store.DatabaseFile);
            var checker = new ClaimChecker(ProseClock, store.DatabaseFile);

            await writer.WriteAsync("KEYS", Handed(document), "prose-refused-1");
            var first = await checker.RunAsync("claims-refused-1");
            var again = await writer.WriteAsync("KEYS", Handed(document), "prose-refused-2");
            var second = await checker.RunAsync("claims-refused-2");

            Assert.Equal([$"{ClaimRules.ComputedSection}|{ClaimChecker.Rejected}"], first.Checked.Select(section => $"{section.Section}|{section.Status}"));
            Assert.Equal([ClaimRules.ComputedSection], again.Written.Where(section => section.Retry).Select(section => section.Section));
            Assert.DoesNotContain("previous draft", feed.Asked[0].Prompt, StringComparison.Ordinal);
            Assert.Contains($"Your previous draft of this section was refused by the checker for: {ClaimRules.UnmatchedFigure}", feed.Asked[1].Prompt, StringComparison.Ordinal);
            Assert.Equal([$"{ClaimRules.ComputedSection}|{ClaimChecker.Accepted}"], second.Checked.Select(section => $"{section.Section}|{section.Status}"));
            Assert.Equal(
                [$"1|{ClaimChecker.Rejected}", $"2|{ClaimChecker.Accepted}"],
                Query(store, $"SELECT version, status FROM research_section WHERE ticker = 'KEYS' AND section = '{ClaimRules.ComputedSection}' ORDER BY version;"));
        }
    }

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
    public async Task APassOverTheReleaseWritesTheCauseOfTheOneMoveTheReleaseFallsInsideAndASecondTheSameDayWritesOnlyWhatTheCheckerRefused()
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
            var writer = new ProseWriter(feed, LocalSettings(), ReleaseLane, ProseClock, store.DatabaseFile);
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

            var outcome = await new ProseWriter(feed, LocalSettings(context), ReleaseLane, ProseClock, store.DatabaseFile)
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
            // taken on its word, each counted as the rule counts it, each digit and each byte
            // outside ASCII a token and the rest at the stated characters a token: the
            // release's text alone, which every refused prompt carries whole, exceeds the room,
            // and the asked prompt fits inside it.
            var room = context - OpenAiCompatibleModelFeed.AnswerTokens;

            static decimal Counted(string text)
            {
                var runes = text.EnumerateRunes().ToArray();

                return runes.Sum(rune => rune.IsAscii ? (Rune.IsDigit(rune) ? 1 : 0) : rune.Utf8SequenceLength)
                    + Math.Ceiling(runes.Count(rune => rune.IsAscii && !Rune.IsDigit(rune)) / SectionPrompt.CharactersPerToken);
            }

            var release = Counted(document.Body!);

            Assert.True(release > room, $"The release counts {release} tokens, which fit in {room}.");
            Assert.Equal(hold.GetProperty("releaseTokens").GetInt32(), release);

            var asked = feed.Asked.Single();
            var counted = Counted(asked.System + asked.Prompt);

            Assert.True(counted <= room, $"The facts-only prompt counts {counted} tokens against {room}.");
            Assert.Equal(hold.GetProperty("askedTokens").GetInt32(), counted);
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
                    ReleaseLane,
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
        // making the requests that recorded them: the replay's passes, the release's
        // two, and from 6.8 the research pass with the configured lanes and the lane
        // comparison's pass with every section local. Both directions, so a recording
        // nothing asks for is reported rather than kept, and a count is stated in advance.
        var asked = new List<ModelRequest>();

        foreach (var lane in new[] { (IReadOnlyList<string>)ProseWriter.DefaultLane, ClaimRules.Sections })
        {
            var researched = new RecordedLocalModelFeed(Folder());

            using (await FixtureReplay.ResearchedAsync(lane: lane, local: researched))
            {
                asked.AddRange(researched.Asked);
            }
        }

        var replayed = new RecordedLocalModelFeed(Folder());

        using (await FixtureReplay.ReplayedAsync(replayed))
        {
            asked.AddRange(replayed.Asked);
        }

        // From 6.10, the overnight queue over a whole fixture night.
        var queued = new RecordedLocalModelFeed(Folder());
        var night = await FixtureReplay.NightAsync(NightQueue.FromFixture(Folder(), new RecordingAwake()) with { LocalModel = queued });

        using (night.Store)
        {
            Assert.True(night.Code == 0, night.Error);
            asked.AddRange(queued.Asked);
        }

        // And the queue on the three later nights the nightly run's tests make, the session
        // after the fixture's night, the night after one that did not run, and that night
        // where the caught-up file carried nothing for one member, each of which writes the
        // key under each figure for every name for the night's own facts file.
        // see: The key under each figure is dated by the night whose figures it explains, written for every name each night, and drawn only beside that night's figures
        var shortOf = FixtureExpectation.CurrentMembers.Order(StringComparer.Ordinal).First();

        foreach (var (at, runId, holed) in new[]
        {
            (new DateTimeOffset(2026, 9, 9, 21, 10, 0, TimeSpan.Zero), "token-night-two", false),
            (new DateTimeOffset(2026, 9, 10, 21, 10, 0, TimeSpan.Zero), "token-night-after-a-miss", false),
            (new DateTimeOffset(2026, 9, 10, 21, 10, 0, TimeSpan.Zero), "token-night-after-a-short-catch-up", true),
        })
        {
            using var later = new TemporaryStore();

            var (first, _, firstError) = await NightlyRun.NightAsync(later, runId: "token-night-one");

            Assert.True(first == 0, firstError);

            var laterQueued = new RecordedLocalModelFeed(Folder());
            IBulkPriceFeed bulk = new NextSessionBulkFeed(RecordedBulkPriceFeed.FromFolder(Folder()), new DateOnly(2026, 9, 8));
            var feeds = NightFeeds.FromFixture(Folder()) with { Bulk = holed ? new HoledBulkFeed(bulk, new DateOnly(2026, 9, 9), [shortOf]) : bulk };
            var (code, _, error) = await NightlyRun.NightAsync(later, feeds, runId, FixedClock.At(at, SessionZones.UnitedStates), NightQueue.FromFixture(Folder()) with { LocalModel = laterQueued });

            Assert.True(code == 0, error);
            asked.AddRange(laterQueued.Asked);
        }

        // And the two nights of the rebalance the nightly run's tests make, one member leaving the
        // index on the second and another joining it there, so on each night one Technology member
        // is out of the index and the other two each read a group of one.
        var members = FixtureExpectation.CurrentMembers.Order(StringComparer.Ordinal).ToArray();

        using (var rebalanced = new TemporaryStore())
        {
            NightFeeds Rebalanced(IBulkPriceFeed? bulk)
            {
                var feeds = NightFeeds.FromFixture(Folder());

                return feeds with
                {
                    Membership = new RebalancedMembershipFeed(feeds.Membership, members[0], members[1], new DateOnly(2026, 9, 9)),
                    Bulk = bulk ?? feeds.Bulk,
                };
            }

            foreach (var (at, runId, bulk) in new (DateTimeOffset, string, IBulkPriceFeed?)[]
            {
                (new DateTimeOffset(2026, 9, 8, 21, 10, 0, TimeSpan.Zero), "token-night-announced", null),
                (new DateTimeOffset(2026, 9, 9, 21, 10, 0, TimeSpan.Zero), "token-night-effective", new NextSessionBulkFeed(RecordedBulkPriceFeed.FromFolder(Folder()), new DateOnly(2026, 9, 8))),
            })
            {
                var rebalancedQueued = new RecordedLocalModelFeed(Folder());
                var (code, _, error) = await NightlyRun.NightAsync(rebalanced, Rebalanced(bulk), runId, FixedClock.At(at, SessionZones.UnitedStates), NightQueue.FromFixture(Folder()) with { LocalModel = rebalancedQueued });

                Assert.True(code == 0, error);
                asked.AddRange(rebalancedQueued.Asked);
            }
        }

        var (store, document) = await WithRelease();

        using (store)
        {
            var feed = new RecordedLocalModelFeed(Folder());
            var writer = new ProseWriter(feed, LocalSettings(), ReleaseLane, ProseClock, store.DatabaseFile);

            await writer.WriteAsync("KEYS", Handed(document), "prose-measured-1");
            await new ClaimChecker(ProseClock, store.DatabaseFile).RunAsync("claims-measured-1");
            await writer.WriteAsync("KEYS", Handed(document), "prose-measured-2");

            asked.AddRange(feed.Asked);
        }

        var recorded = Directory.GetFiles(Folder(), RecordedLocalModelFeed.FilePrefix + "*.json").Select(Path.GetFileName).Order(StringComparer.Ordinal).ToArray();

        // Two from the replay, MSFT's key under each figure and NFLX's; four from the release's
        // passes, what the company sells, the segment commentary and the cause, and the cause
        // again after the checker refused its first draft, the key being the research pass's own
        // request; three from the research pass with the configured lanes; seven from the
        // comparison with every section in the local lane, two of them drafts written after the
        // checker refused the first, the two cases and the risks each asked once in the parts the
        // name page draws, the first answer running to its budget and the second accepted; two
        // from the queue over the fixture's night, AAPL's key and
        // KEYS's, MSFT's and NFLX's being the replay's requests asked again; nine from the two
        // later nights, every member's key on each and NFLX's again on the night after a miss,
        // the checker having refused its first draft there; and one from the night after a short
        // catch-up, the key of the member the caught-up file left out, over the facts file its
        // missing session left it, the other three being the missed night's requests asked
        // again; and four from the rebalance's two nights, whose Technology names each read a
        // group of one: AAPL's key and MSFT's on the fixture's night, before KEYS joins, and
        // KEYS's and MSFT's on the next session, AAPL having left, NFLX's on each being a request
        // the other nights asked, since its group holds nobody either way.
        Assert.Equal(32, recorded.Length);
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

            var quarters = segments.GetProperty("periods").EnumerateArray()
                .Where(period => period.GetProperty("months").GetInt32() == 3)
                .Select(period => period.GetProperty("ended").GetString()!)
                .ToArray();
            var latest = quarters.Max(StringComparer.Ordinal)!;

            // The same quarter a year before, which the table states beside it.
            var yearBefore = quarters.Single(ended => Math.Abs(
                DateOnly.ParseExact(ended, "yyyy-MM-dd", CultureInfo.InvariantCulture).DayNumber
                - DateOnly.ParseExact(latest, "yyyy-MM-dd", CultureInfo.InvariantCulture).AddYears(-1).DayNumber) <= FundamentalsFetcher.PeriodEndDays);

            var facts = FactsAssembler.Segments(document.RootElement);

            // Every segment fact is the latest quarter's or the same quarter a year before, and
            // every quarter line the table carries for those two periods is a fact, counted off
            // the table rather than stated.
            int Lines(string ended) => segments.GetProperty("consolidated").EnumerateArray()
                .Concat(segments.GetProperty("groups").EnumerateArray().SelectMany(group => group.GetProperty("figures").EnumerateArray()))
                .Count(line => line.GetProperty("months").GetInt32() == 3
                    && line.GetProperty("ended").GetString() == ended
                    && line.GetProperty("value").ValueKind == JsonValueKind.String);

            var lines = Lines(latest);

            Assert.True(lines >= 2, $"The table carries {lines} lines for {latest}.");
            Assert.Equal(lines + Lines(yearBefore), facts.Count);
            Assert.Equal(lines, facts.Count(fact => fact.Name.EndsWith(" " + latest, StringComparison.Ordinal)));
            Assert.All(facts, fact => Assert.True(
                fact.Name.EndsWith(" " + latest, StringComparison.Ordinal) || fact.Name.EndsWith(" " + yearBefore, StringComparison.Ordinal),
                fact.Name));
            Assert.All(facts, fact => Assert.Equal(FactsAssembler.FromFundamentals, fact.Source));
            Assert.Equal(facts.Count, facts.Select(fact => fact.Name).Distinct(StringComparer.Ordinal).Count());

            // And the file the writer reads carries them, which is what the segment
            // commentary needed and did not have before 6.6.
            var file = FactsFile.Read(Query(store, $"SELECT payload FROM facts WHERE ticker = 'KEYS' AND session_date = '{Iso(ProseClock.SessionDateAt(ProseNight))}';").Single());

            Assert.All(facts, fact => Assert.Contains(file, held => held.Name == fact.Name && held.Value == fact.Value));
        }
    }

    [Fact]
    public async Task AnOpenedNamesFileCarriesItsGrowthItsEarningsAgainstTheEstimateItsTablesGrowthAndItsGuidedFigures()
    {
        // KEYS's, each worked by hand from the provider's payload, the captured segment table and
        // the release's outlook, as the expectation says.
        // see: The facts file carries a quarter's growth, its earnings against the estimate and the filing's own tables with their year-earlier columns
        // see: Guidance is stored as management's own prose, and the facts file carries each figure the passage states as the claim checker reads it
        var expected = Expected("facts").GetProperty("openedName");
        var ticker = expected.GetProperty("ticker").GetString()!;
        var (store, _) = await WithRelease();

        using (store)
        {
            var file = FactsFile.Read(Query(store, $"SELECT payload FROM facts WHERE ticker = '{ticker}' AND session_date = '{Iso(ProseClock.SessionDateAt(ProseNight))}';").Single());

            foreach (var fact in expected.GetProperty("facts").EnumerateObject())
            {
                Assert.Contains(file, held => held.Name == fact.Name && held.Value == fact.Value.GetString() && held.Source == FactsAssembler.FromFundamentals);
            }

            // A group's growth is found by the words its name carries, since its label is the
            // archive's own, and exactly one fact carries them.
            foreach (var grown in expected.GetProperty("tableGrowth").EnumerateArray())
            {
                var words = grown.GetProperty("nameCarries").EnumerateArray().Select(word => word.GetString()!).ToArray();
                var named = file.Where(held => words.All(word => held.Name.Contains(word, StringComparison.Ordinal))).ToArray();

                Assert.Equal(grown.GetProperty("value").GetString(), Assert.Single(named).Value);
            }

            // The guided figures are numbered in the order the passage states them, and no other
            // fact is named for guidance.
            Assert.Equal(
                expected.GetProperty("facts").EnumerateObject().Count(fact => fact.Name.StartsWith("guidance figure ", StringComparison.Ordinal)),
                file.Count(held => held.Name.StartsWith("guidance figure ", StringComparison.Ordinal)));

            // And the company's name, which the membership row holds.
            Assert.Contains(file, held => held.Name == FactsAssembler.CompanyName
                && held.Value == Expected("membership").GetProperty("names").GetProperty(ticker).GetString());
        }
    }

    [Fact]
    public void AFilingsOtherTablesAndEachGroupsGrowthAreNamedForWhatTheTableGroupsBy()
    {
        // NVDA's table by market platform in the shape the fetcher stores it, its data center
        // revenue as the captured R67 states it, and the growth the fetcher computed for it, beside
        // a growth from the segment table, which no other table names.
        // see: The facts file carries a quarter's growth, its earnings against the estimate and the filing's own tables with their year-earlier columns
        const string Payload = """
        {
          "revenueTables": [
            {
              "report": "R67.htm",
              "title": "Segment Information - Schedule of Revenue by Market Platform (Details)",
              "periods": [
                { "months": 3, "ended": "2026-07-26" },
                { "months": 3, "ended": "2025-07-27" },
                { "months": 6, "ended": "2026-07-26" }
              ],
              "consolidated": [
                { "lineItem": "Revenue", "months": 3, "ended": "2026-07-26", "value": "96221000000" },
                { "lineItem": "Revenue", "months": 3, "ended": "2025-07-27", "value": "46743000000" }
              ],
              "groups": [
                {
                  "label": "Data Center",
                  "figures": [
                    { "lineItem": "Revenue", "months": 3, "ended": "2026-07-26", "value": "89023000000" },
                    { "lineItem": "Revenue", "months": 3, "ended": "2025-07-27", "value": "41096000000" },
                    { "lineItem": "Revenue", "months": 6, "ended": "2026-07-26", "value": "164269000000" }
                  ]
                }
              ]
            }
          ],
          "tableGrowth": [
            { "report": "R67.htm", "group": 1, "label": "Data Center", "lineItem": "Revenue", "months": 3, "ended": "2026-07-26", "yearEarlier": "2025-07-27", "value": "1.166221" },
            { "report": "R63.htm", "group": 0, "label": "total", "lineItem": "Revenue", "months": 3, "ended": "2026-07-26", "yearEarlier": "2025-07-27", "value": "1.058533" }
          ]
        }
        """;

        using var document = JsonDocument.Parse(Payload);

        // The latest quarter and the same quarter a year before, named for what the title says
        // the table groups by, so a figure by market platform is never read as a segment's.
        Assert.Equal(
            [
                "segment revenue by market platform Data Center Revenue 2025-07-27",
                "segment revenue by market platform Data Center Revenue 2026-07-26",
                "segment revenue by market platform total Revenue 2025-07-27",
                "segment revenue by market platform total Revenue 2026-07-26",
            ],
            FactsAssembler.RevenueTables(document.RootElement).Select(fact => fact.Name).Order(StringComparer.Ordinal));
        Assert.Contains(FactsAssembler.RevenueTables(document.RootElement), fact => fact.Name == "segment revenue by market platform Data Center Revenue 2026-07-26" && fact.Value == "89023000000");

        var grown = FactsAssembler.TableGrowth(document.RootElement);

        Assert.Equal(2, grown.Count);
        Assert.Contains(grown, fact => fact.Name == "segment revenue by market platform Data Center Revenue growth on a year earlier 2026-07-26" && fact.Value == "1.166221");
        Assert.Contains(grown, fact => fact.Name == "segment total Revenue growth on a year earlier 2026-07-26" && fact.Value == "1.058533");
        Assert.All(grown, fact => Assert.Equal(FactsAssembler.FromFundamentals, fact.Source));

        // And the segment table's reader reads the segment table alone.
        Assert.Empty(FactsAssembler.Segments(document.RootElement));
    }

    [Fact]
    public void ATableOfTwelveMonthPeriodsAloneIsCarriedAndNamedForItsMonths()
    {
        // 8.0's ruling, over a table shaped as an annual report files one: three columns of
        // twelve months and nothing shorter, which is what the archive served for MSFT's
        // report for the year to 2026-06-30 in 6.11's run. The assembler took quarters alone
        // until then, so such a name carried no segment figure and both sections that quote
        // one fell back for a quarter.
        // see: A facts file carries the latest period of the segment table, and says which period it is
        const string Annual = """
        {
          "segments": {
            "periods": [
              { "months": 12, "ended": "2024-06-30" },
              { "months": 12, "ended": "2025-06-30" },
              { "months": 12, "ended": "2026-06-30" }
            ],
            "consolidated": [
              { "lineItem": "Revenue", "months": 12, "ended": "2026-06-30", "value": "270000000000" },
              { "lineItem": "Revenue", "months": 12, "ended": "2025-06-30", "value": "245000000000" }
            ],
            "groups": [
              {
                "label": "Productivity",
                "figures": [
                  { "lineItem": "Revenue", "months": 12, "ended": "2026-06-30", "value": "80000000000" },
                  { "lineItem": "Operating income", "months": 12, "ended": "2026-06-30", "value": "37000000000" }
                ]
              }
            ]
          }
        }
        """;

        using var annual = JsonDocument.Parse(Annual);

        var carried = FactsAssembler.Segments(annual.RootElement);

        // The newest period with the months in every name, so a sentence quoting a year cannot
        // be read as a quarter, and the same months a year before, which the table states
        // beside it. The column two years back is not carried.
        // see: The facts file carries a quarter's growth, its earnings against the estimate and the filing's own tables with their year-earlier columns
        Assert.Equal(4, carried.Count);
        Assert.Equal(3, carried.Count(fact => fact.Name.EndsWith(" 12 months to 2026-06-30", StringComparison.Ordinal)));
        Assert.Contains(carried, fact => fact.Name == "segment total Revenue 12 months to 2026-06-30" && fact.Value == "270000000000");
        Assert.Contains(carried, fact => fact.Name == "segment Productivity Operating income 12 months to 2026-06-30");
        Assert.Contains(carried, fact => fact.Name == "segment total Revenue 12 months to 2025-06-30" && fact.Value == "245000000000");
        Assert.DoesNotContain(carried, fact => fact.Name.EndsWith("2024-06-30", StringComparison.Ordinal));

        // And a table that files a quarter keeps the quarter, whose name has no months in
        // it, because every stored facts file and every section written from one carries it.
        var withQuarter = Annual
            .Replace("{ \"months\": 12, \"ended\": \"2026-06-30\" }", "{ \"months\": 3, \"ended\": \"2026-06-30\" }", StringComparison.Ordinal)
            .Replace("\"months\": 12, \"ended\": \"2026-06-30\", \"value\": \"270000000000\"", "\"months\": 3, \"ended\": \"2026-06-30\", \"value\": \"70000000000\"", StringComparison.Ordinal);

        using var quarterly = JsonDocument.Parse(withQuarter);

        var quarter = FactsAssembler.Segments(quarterly.RootElement);

        Assert.Contains(quarter, fact => fact.Name == "segment total Revenue 2026-06-30" && fact.Value == "70000000000");
        Assert.DoesNotContain(quarter, fact => fact.Name.Contains("months to", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ATableFilingNoQuarterIsAskedForByItsPeriodAndRefusedUnderAnotherPeriodsName()
    {
        // KEYS's captured table with its three-month columns taken out, which is the shape a
        // table takes where the filing files no quarter. The period, the ask and every verdict
        // are the expectation's; the figure quoted is read back from the stored table.
        // see: A segment figure held for a period longer than a quarter is asked for by that period and refused where its sentence names a period of another length
        var expected = Expected("prose").GetProperty("withoutAQuarter");
        var ticker = expected.GetProperty("ticker").GetString()!;
        var period = expected.GetProperty("period").GetString()!;
        var yearBefore = expected.GetProperty("yearBefore").GetString()!;
        var (store, release) = await WithRelease();

        using (store)
        {
            var payload = Query(store, $"SELECT payload FROM fundamentals WHERE ticker = '{ticker}' ORDER BY filing_date DESC LIMIT 1;").Single();
            var stored = FactsFile.Read(Query(store, $"SELECT payload FROM facts WHERE ticker = '{ticker}' AND session_date = '{Iso(ProseClock.SessionDateAt(ProseNight))}';").Single());

            var table = JsonNode.Parse(payload)!;
            var segments = table["segments"]!;

            bool AQuarter(JsonNode? line) => line!["months"]!.GetValue<int>() == SegmentPeriods.QuarterMonths;

            void TakeQuarters(JsonNode lines)
            {
                foreach (var line in lines.AsArray().Where(AQuarter).ToArray())
                {
                    lines.AsArray().Remove(line);
                }
            }

            TakeQuarters(segments["periods"]!);
            TakeQuarters(segments["consolidated"]!);

            foreach (var group in segments["groups"]!.AsArray())
            {
                TakeQuarters(group!["figures"]!);
            }

            using var withoutAQuarter = JsonDocument.Parse(table.ToJsonString());

            var carried = FactsAssembler.Segments(withoutAQuarter.RootElement);

            var latest = carried.Where(fact => fact.Name.EndsWith(" " + period, StringComparison.Ordinal)).ToArray();

            Assert.True(latest.Length >= 2, $"Carried {latest.Length} segment figure(s) for {period}.");
            Assert.All(carried, fact => Assert.True(
                fact.Name.EndsWith(" " + period, StringComparison.Ordinal) || fact.Name.EndsWith(" " + yearBefore, StringComparison.Ordinal),
                fact.Name));
            Assert.Contains(carried, fact => fact.Name.EndsWith(" " + yearBefore, StringComparison.Ordinal));

            Fact[] facts = [.. stored.Where(fact => !fact.Name.StartsWith(SegmentPeriods.Prefix, StringComparison.Ordinal)), .. carried];

            // Asked for the period, and the quarter's ask kept word for word over the file as
            // stored, which every recording of the section is keyed on.
            var asked = SectionPrompt.Prompt(ticker, Evidence.Segments, facts, []);

            Assert.Contains(expected.GetProperty("asked").GetString()!, asked, StringComparison.Ordinal);
            Assert.DoesNotContain(SectionPrompt.Asks[Evidence.Segments], asked, StringComparison.Ordinal);
            Assert.Contains(SectionPrompt.Asks[Evidence.Segments], SectionPrompt.Prompt(ticker, Evidence.Segments, stored, []), StringComparison.Ordinal);

            // The table's largest total for the period, its revenue, rounded to millions, under
            // each period's name the expectation lists.
            var total = latest
                .Where(fact => fact.Name.StartsWith(SegmentPeriods.Prefix + "total ", StringComparison.Ordinal))
                .MaxBy(fact => decimal.Parse(fact.Value, CultureInfo.InvariantCulture))!;
            var millions = Math.Round(decimal.Parse(total.Value, CultureInfo.InvariantCulture) / 1_000_000m, 0).ToString(CultureInfo.InvariantCulture);

            var verdicts = expected.GetProperty("verdicts").EnumerateArray().ToArray();

            Assert.True(verdicts.Length >= 4, $"Read {verdicts.Length} verdict(s), expected at least 4.");

            foreach (var verdict in verdicts)
            {
                var wording = verdict.GetProperty("wording").GetString()!;
                string[] reasons = verdict.GetProperty("reason").GetString() is { } reason ? [reason] : [];

                Assert.Equal(
                    reasons,
                    ClaimRules.Check(Evidence.Segments, $"The company reported revenue of ${millions} million {wording}. [D1]", facts, [release]).Findings.Select(finding => finding.Reason));
            }
        }
    }
}
