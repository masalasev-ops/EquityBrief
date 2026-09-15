using System.Text.Json;
using EquityBrief.Core.Providers;
using EquityBrief.Core.Research;
using EquityBrief.Core.Time;
using EquityBrief.Tests.Harness;
using EquityBrief.Worker;
using EquityBrief.Worker.Nights;
using EquityBrief.Worker.Research;
using Microsoft.Extensions.Configuration;

namespace EquityBrief.Tests.Checks;

// fixture-expectations, 6.10: the overnight queue over whole fixture nights. The names it
// queues and in what order, what each pass asks for and is handed, the limit at the
// boundary either side, the local model not answering, the machine held while it works,
// and the arithmetic's own figures unmoved by whether it ran.
public partial class FixtureExpectations
{
    static readonly DateTimeOffset QueueNight = new(2026, 9, 8, 21, 10, 0, TimeSpan.Zero);

    // A local model that answers from the recordings and writes each call it takes, and the
    // name the call is for, into a list a hold on the machine writes to as well.
    sealed class NotedLocal(ILocalModelFeed inner, List<string> events, Action? onCall = null) : ILocalModelFeed
    {
        public int Requests => inner.Requests;

        public async Task<ModelAnswer> CompleteAsync(ModelRequest request, CancellationToken cancellation = default)
        {
            events.Add("call: " + request.Prompt.Split('\n')[0]);
            onCall?.Invoke();

            return await inner.CompleteAsync(request, cancellation);
        }
    }

    // A clock that stands still until a local model call moves it on, so how long a pass
    // takes is a number the test chose and the limit's boundary falls where the test put it.
    sealed class CallClock(DateTimeOffset start) : IClock
    {
        public DateTimeOffset UtcNow { get; private set; } = start;

        public TimeZoneInfo SessionZone { get; } = SessionZones.ResolveSessionZone(SessionZones.UnitedStates);

        public void Advance(TimeSpan by) => UtcNow += by;
    }

    static JsonElement QueueRow(TemporaryStore store, string runId = FixtureReplay.NightRunId) =>
        JsonDocument.Parse(Query(store, $"SELECT detail FROM run_log WHERE run_id = '{runId}' AND stage = '{OvernightQueue.Stage}';").Single()).RootElement;

    // ---- what the queue writes ----

    [Fact]
    public async Task TheQueueWritesTheListedNamesDraftsInOrderOfReasonsFiredAndSpendsNothing()
    {
        var expected = Expected("overnight-queue");
        var events = new List<string>();
        var local = new NotedLocal(new RecordedLocalModelFeed(Folder()), events);

        var night = await FixtureReplay.NightAsync(NightQueue.FromFixture(Folder(), new RecordingAwake(events)) with { LocalModel = local });

        using var store = night.Store;

        Assert.True(night.Code == 0, night.Error);

        // The names on the night's list, in order of reasons fired and then of their tickers,
        // read off the listing rows the arithmetic wrote, and the order the file states.
        var listed = Listed(expected.GetProperty("listed"));

        Assert.Equal(
            listed,
            Query(store, $"SELECT ticker FROM listing WHERE session_date = '{expected.GetProperty("night").GetString()}' AND fired_count > 0 ORDER BY fired_count DESC, ticker;"));

        var row = QueueRow(store);

        Assert.Equal(expected.GetProperty("night").GetString(), row.GetProperty("night").GetString());
        Assert.Equal(listed, Listed(row.GetProperty("listed")));
        Assert.Equal(Listed(expected.GetProperty("queued")), Listed(row.GetProperty("queued")));
        Assert.Equal(Listed(expected.GetProperty("left")), Listed(row.GetProperty("left")));
        Assert.Equal(expected.GetProperty("outcome").GetString(), row.GetProperty("outcome").GetString());
        Assert.Equal(expected.GetProperty("limitHours").GetDouble(), row.GetProperty("limitHours").GetDouble());
        Assert.Equal(JsonValueKind.Null, row.GetProperty("stopped").ValueKind);

        // Each pass asks for the lane's sections that rest on no document, which the lane and
        // the checker's own rule say, and the file states.
        var asked = Listed(expected.GetProperty("asked"));

        Assert.Equal(asked, ProseWriter.DefaultLane.Where(section => !ClaimRules.IsResearched(section)).ToList());

        var passes = expected.GetProperty("passes").EnumerateArray().ToArray();
        var completed = row.GetProperty("completed").EnumerateArray().ToArray();

        Assert.Equal(passes.Select(pass => pass.GetProperty("ticker").GetString()), completed.Select(pass => pass.GetProperty("ticker").GetString()));

        foreach (var (pass, ran) in passes.Zip(completed))
        {
            var ticker = pass.GetProperty("ticker").GetString()!;
            var runId = OvernightQueue.PassRunId(FixtureReplay.NightRunId, ticker);

            Assert.Equal(runId, ran.GetProperty("runId").GetString());
            Assert.Equal(asked, Listed(ran.GetProperty("asked")));
            Assert.Equal(pass.GetProperty("modelCalls").GetInt32(), ran.GetProperty("modelCalls").GetInt32());

            // The versions the name's key holds, each written by the local model the lane
            // names and handed no document, with the checker's verdict on each.
            Assert.Equal(
                Listed(pass.GetProperty("versions")),
                Query(store, $"SELECT status FROM research_section WHERE ticker = '{ticker}' AND section = '{asked[0]}' ORDER BY version;"));
            Assert.Equal(["[]"], Query(store, $"SELECT DISTINCT source_ids FROM research_section WHERE ticker = '{ticker}';"));
            Assert.Equal([LocalModelSettings.DefaultModel], Query(store, $"SELECT DISTINCT model FROM research_section WHERE ticker = '{ticker}';"));

            // And the pass's own rows under its own run: the judge's, the writer's and the
            // checker's.
            Assert.Contains(StalenessJudge.Stage, Query(store, $"SELECT stage FROM run_log WHERE run_id = '{runId}';"));
            Assert.Contains(ProseWriter.Stage, Query(store, $"SELECT stage FROM run_log WHERE run_id = '{runId}';"));
            Assert.Contains(ClaimChecker.Stage, Query(store, $"SELECT stage FROM run_log WHERE run_id = '{runId}';"));
        }

        Assert.Equal("nothing", expected.GetProperty("handed").GetString());
        Assert.Equal(["0"], Query(store, "SELECT COUNT(*) FROM source_document;"));

        // Zero spend and no request, read off the run log rather than off what the queue
        // returned, on the queue's row and on every row the night wrote.
        Assert.Equal(
            [$"{expected.GetProperty("modelCalls").GetInt32()}|0|0|{expected.GetProperty("spend").GetString()}"],
            Query(store, $"SELECT model_calls, network_requests, rows_written, spend FROM run_log WHERE run_id = '{FixtureReplay.NightRunId}' AND stage = '{OvernightQueue.Stage}';"));
        Assert.Equal(["0"], Query(store, "SELECT DISTINCT spend FROM run_log;"));
        Assert.Empty(Query(store, "SELECT stage FROM run_log WHERE stage LIKE 'research call%';"));
        Assert.Empty(NightlyCost.CallsTheCarveDoesNotAllow(NightlyCost.CostRows(store.DatabaseFile), FixtureReplay.NightRunId));

        // The machine held before the first call and released after the last.
        Assert.Equal("held: " + OvernightQueue.AwakeReason, events[0]);
        Assert.Equal("released", events[^1]);
        Assert.Equal(expected.GetProperty("modelCalls").GetInt32(), events.Count(one => one.StartsWith("call: ", StringComparison.Ordinal)));
        Assert.Equal(MachineAwake.HeldLine, row.GetProperty("awake").GetString());
    }

    [Fact]
    public async Task TheQueueTakesTheNamesThatFiredInOrderOfReasonsFiredAndNotOfTheirTickers()
    {
        // Three members of the fixture's night fired one reason each and NFLX fired none, so
        // the order of reasons fired and the order of tickers are one order there and a queue
        // reading either passed, which 6.10's sweep showed. Over a copy of that night two
        // listings are changed: NFLX firing three reasons, which puts it first where its ticker
        // puts it last, and KEYS firing none, which takes it off tonight's list. Until the 5.4
        // correction AAPL fired two, earnings soon among them, and NFLX one, earnings soon
        // alone, each on a count of 0 read off stored bars the night did not hold.
        var night = await FixtureReplay.NightAsync(NightQueue.FromFixture(Folder(), new RecordingAwake()) with { LocalModel = new NothingAnsweringLocal() });

        using var store = night.Store;

        Assert.True(night.Code == 0, night.Error);

        // The coincidence, stated, so the change below is read against the night it changes.
        Assert.Equal(
            ["AAPL|1", "KEYS|1", "MSFT|1"],
            Query(store, "SELECT ticker || '|' || fired_count FROM listing WHERE session_date = '2026-09-08' AND fired_count > 0 ORDER BY fired_count DESC, ticker;"));

        store.Execute("UPDATE listing SET fired_count = 3 WHERE ticker = 'NFLX' AND session_date = '2026-09-08';");
        store.Execute("UPDATE listing SET fired_count = 0 WHERE ticker = 'KEYS' AND session_date = '2026-09-08';");

        var clock = FixedClock.At(QueueNight, SessionZones.UnitedStates);
        var local = new RecordedLocalModelFeed(Folder());

        var reordered = await new OvernightQueue(
            new StalenessJudge(clock, store.DatabaseFile),
            sections => new ProseWriter(local, new LocalModelSettings(null, null, null, null, null), sections, clock, store.DatabaseFile),
            new ClaimChecker(clock, store.DatabaseFile),
            ProseWriter.DefaultLane,
            TimeSpan.FromHours(OvernightQueue.DefaultHours),
            new RecordingAwake(),
            clock,
            store.DatabaseFile).RunAsync("reordered", new DateOnly(2026, 9, 8));

        Assert.Equal(["NFLX", "AAPL", "MSFT"], reordered.Listed);
        Assert.Equal(["NFLX", "AAPL", "MSFT"], reordered.Queued);
        Assert.Equal(["NFLX", "AAPL", "MSFT"], reordered.Completed.Select(pass => pass.Ticker));
        Assert.DoesNotContain("KEYS", Query(store, "SELECT DISTINCT ticker FROM research_section;"));
    }

    [Fact]
    public async Task APassWritesAgainOnlyWhatTheCheckerRefusedOfItsOwnName()
    {
        // The checker moves every draft waiting in the store rather than the pass's alone, and
        // another name's draft can be waiting when a pass checks: one a pass started on demand
        // wrote during the night, or one a pass stopped before its check left behind. Over a
        // copy of the fixture's night, ORCL's key waits with a figure no facts file holds. The
        // first pass's check refuses it, and that pass, whose own draft was accepted, writes no
        // second round, while MSFT's, whose own first draft was refused, still writes one.
        var night = await FixtureReplay.NightAsync(NightQueue.FromFixture(Folder(), new RecordingAwake()) with { LocalModel = new NothingAnsweringLocal() });

        using var store = night.Store;

        Assert.True(night.Code == 0, night.Error);

        store.Execute(
            "INSERT INTO research_section (ticker, section, version, as_of, model, status, prose, source_ids, reject_reason) " +
            $"VALUES ('ORCL', '{ClaimRules.ComputedSection}', 1, '2026-09-08', '{LocalModelSettings.DefaultModel}', 'pending', 'The close of 123.45 sits above the average.', '[]', NULL);");

        var clock = FixedClock.At(QueueNight, SessionZones.UnitedStates);
        var local = new RecordedLocalModelFeed(Folder());

        var outcome = await new OvernightQueue(
            new StalenessJudge(clock, store.DatabaseFile),
            sections => new ProseWriter(local, new LocalModelSettings(null, null, null, null, null), sections, clock, store.DatabaseFile),
            new ClaimChecker(clock, store.DatabaseFile),
            ProseWriter.DefaultLane,
            TimeSpan.FromHours(OvernightQueue.DefaultHours),
            new RecordingAwake(),
            clock,
            store.DatabaseFile).RunAsync("waiting", new DateOnly(2026, 9, 8));

        // The other name's draft was checked by the queue's first pass, and refused.
        Assert.Equal([ClaimChecker.Rejected], Query(store, "SELECT status FROM research_section WHERE ticker = 'ORCL';"));

        Assert.Equal(["AAPL", "KEYS", "MSFT"], outcome.Completed.Select(pass => pass.Ticker));
        Assert.Equal([1, 1, 2], outcome.Completed.Select(pass => pass.ModelCalls));

        var secondRound = ProseWriter.StageFor(ResearchRunner.SecondRound);

        Assert.DoesNotContain(secondRound, Query(store, $"SELECT stage FROM run_log WHERE run_id = '{OvernightQueue.PassRunId("waiting", "AAPL")}';"));
        Assert.Contains(secondRound, Query(store, $"SELECT stage FROM run_log WHERE run_id = '{OvernightQueue.PassRunId("waiting", "MSFT")}';"));
    }

    [Fact]
    public async Task ANameWhoseLaneStandsTodayIsNotQueuedAgain()
    {
        // The same night's queue run a second time, after the first wrote every key: each
        // name's only section resting on no document is accepted today, and what is still
        // missing, what the company sells and the segment commentary, rests on documents,
        // so no name is queued rather than every name counted as a pass that wrote nothing.
        var night = await FixtureReplay.NightAsync();

        using var store = night.Store;

        Assert.True(night.Code == 0, night.Error);

        var clock = FixedClock.At(QueueNight, SessionZones.UnitedStates);
        var local = new RecordedLocalModelFeed(Folder());

        var again = await new OvernightQueue(
            new StalenessJudge(clock, store.DatabaseFile),
            sections => new ProseWriter(local, new LocalModelSettings(null, null, null, null, null), sections, clock, store.DatabaseFile),
            new ClaimChecker(clock, store.DatabaseFile),
            ProseWriter.DefaultLane,
            TimeSpan.FromHours(OvernightQueue.DefaultHours),
            new RecordingAwake(),
            clock,
            store.DatabaseFile).RunAsync("second-queue", new DateOnly(2026, 9, 8));

        Assert.Equal(Listed(Expected("overnight-queue").GetProperty("listed")).Count(), again.Listed.Count);
        Assert.Empty(again.Queued);
        Assert.Empty(again.Completed);
        Assert.Equal(OvernightQueue.Ran, again.Outcome);
        Assert.Equal(0, local.Requests);
    }

    // ---- the limit ----

    [Fact]
    public async Task ABusyNightLeavesNamesForTheNextNightAtTheBoundaryEitherSide()
    {
        // Every call moves the clock on a minute, so AAPL's pass ends at one minute, KEYS's
        // at two and MSFT's two calls at four. A limit of one minute is reached as KEYS's
        // pass would start, so KEYS and MSFT are left; a limit a tick past one minute lets
        // KEYS's pass start inside it and run to its end, and MSFT, whose turn comes at two,
        // is left. A limit a tick past two minutes lets MSFT's pass start inside it and run
        // past it to its end, both of its calls.
        //
        // Three names since the 5.4 correction. NFLX fired earnings soon alone on the
        // fixture's night, on a count of 0 read off the stored bars after it, and its print
        // is thirty sessions out, so it fires nothing, is not listed and is not queued. The
        // boundaries this test named at MSFT and NFLX moved one name earlier with it.
        var step = TimeSpan.FromMinutes(1);

        async Task<QueueOutcome> WithLimit(TimeSpan limit)
        {
            var clock = new CallClock(QueueNight);
            var local = new NotedLocal(new RecordedLocalModelFeed(Folder()), [], () => clock.Advance(step));

            var night = await FixtureReplay.NightAsync(
                new NightQueue(local, new LocalModelSettings(null, null, null, null, null), ProseWriter.DefaultLane, limit, new RecordingAwake()),
                clock);

            using (night.Store)
            {
                Assert.True(night.Code == 0, night.Error);

                var row = QueueRow(night.Store);

                return new QueueOutcome(
                    new DateOnly(2026, 9, 8),
                    row.GetProperty("outcome").GetString()!,
                    limit,
                    Listed(row.GetProperty("listed")),
                    Listed(row.GetProperty("queued")),
                    [.. row.GetProperty("completed").EnumerateArray().Select(pass => new QueuePass(pass.GetProperty("ticker").GetString()!, pass.GetProperty("runId").GetString()!, [], [], [], pass.GetProperty("modelCalls").GetInt32(), pass.GetProperty("seconds").GetDouble()))],
                    Listed(row.GetProperty("left")),
                    row.GetProperty("awake").GetString()!,
                    null);
            }
        }

        var at = await WithLimit(step);

        Assert.Equal(OvernightQueue.StoppedAtItsLimit, at.Outcome);
        Assert.Equal(["AAPL"], at.Completed.Select(pass => pass.Ticker));
        Assert.Equal(["KEYS", "MSFT"], at.Left);

        var inside = await WithLimit(step + TimeSpan.FromTicks(1));

        Assert.Equal(OvernightQueue.StoppedAtItsLimit, inside.Outcome);
        Assert.Equal(["AAPL", "KEYS"], inside.Completed.Select(pass => pass.Ticker));
        Assert.Equal(["MSFT"], inside.Left);

        // A pass that started inside the limit runs past it to its end, both of its calls.
        var past = await WithLimit(step * 2 + TimeSpan.FromTicks(1));

        Assert.Equal(["AAPL", "KEYS", "MSFT"], past.Completed.Select(pass => pass.Ticker));
        Assert.Equal(2, past.Completed[^1].ModelCalls);
        Assert.Equal((step * 2).TotalSeconds, past.Completed[^1].Seconds);

        // And a limit that covers every pass leaves nothing.
        var covering = await WithLimit(step * 4 + TimeSpan.FromTicks(1));

        Assert.Equal(OvernightQueue.Ran, covering.Outcome);
        Assert.Empty(covering.Left);
    }

    // A local model that answers from the recordings and notes whether the token each call
    // was handed can ever be cancelled.
    sealed class TokenNotingLocal(ILocalModelFeed inner, List<bool> cancellable) : ILocalModelFeed
    {
        public int Requests => inner.Requests;

        public Task<ModelAnswer> CompleteAsync(ModelRequest request, CancellationToken cancellation = default)
        {
            cancellable.Add(cancellation.CanBeCanceled);

            return inner.CompleteAsync(request, cancellation);
        }
    }

    [Fact]
    public async Task TheQueueIsHandedNothingFromTheNightsDeadline()
    {
        // The deadline is sized for the arithmetic, and a queue handed it would have its
        // passes cut at whatever the arithmetic left of fifteen minutes. So no call a pass
        // makes carries a token anything can cancel: the night's deadline cannot reach it,
        // and its own limit is read between passes rather than inside one.
        var cancellable = new List<bool>();

        var night = await FixtureReplay.NightAsync(NightQueue.FromFixture(Folder(), new RecordingAwake()) with { LocalModel = new TokenNotingLocal(new RecordedLocalModelFeed(Folder()), cancellable) });

        using (night.Store)
        {
            Assert.True(night.Code == 0, night.Error);
        }

        Assert.NotEmpty(cancellable);
        Assert.All(cancellable, can => Assert.False(can));
    }

    [Fact]
    public void TheNightReadsItsQueueFromItsSettingsAndRefusesOneItCannotRead()
    {
        static IConfiguration Settings(params (string Key, string Value)[] pairs) =>
            new ConfigurationBuilder().AddInMemoryCollection(pairs.ToDictionary(pair => pair.Key, pair => (string?)pair.Value)).Build();

        // A night over a capture reaches the recorded runtime, with the hours and the lane its
        // settings state.
        var stated = NightQueue.From(
            Settings((OvernightQueue.HoursKey, "2"), (LocalLane.SectionsKey + ":0", ClaimRules.ComputedSection)),
            FeedSource.Fixture,
            Folder(),
            new RecordingAwake());

        Assert.IsType<RecordedLocalModelFeed>(stated.LocalModel);
        Assert.Equal(TimeSpan.FromHours(2), stated.Limit);
        Assert.Equal([ClaimRules.ComputedSection], stated.Lane);

        // Settings stating nothing: this machine's lane and the default hours.
        var blank = NightQueue.From(Settings(), FeedSource.Fixture, Folder(), new RecordingAwake());

        Assert.Equal(TimeSpan.FromHours(OvernightQueue.DefaultHours), blank.Limit);
        Assert.Equal(ProseWriter.DefaultLane, blank.Lane);
        Assert.Equal(LocalModelSettings.DefaultModel, blank.Settings.Model);

        // A live night reaches the operator's runtime, and a night over a capture never does.
        Assert.IsType<OpenAiCompatibleModelFeed>(NightQueue.From(Settings(), FeedSource.Live, null, new RecordingAwake()).LocalModel);

        // Refused before the night starts, each naming what it could not read: hours that
        // are not whole hours, a lane naming a section figure 12.2 does not, a fixture folder
        // that is not there, and a key on the local lane.
        Assert.Contains(
            OvernightQueue.HoursKey,
            Assert.Throws<InvalidOperationException>(() => NightQueue.From(Settings((OvernightQueue.HoursKey, "half")), FeedSource.Fixture, Folder(), new RecordingAwake())).Message,
            StringComparison.Ordinal);
        Assert.Contains(
            "The cause of every move",
            Assert.Throws<InvalidOperationException>(() => NightQueue.From(Settings((LocalLane.SectionsKey + ":0", "The cause of every move")), FeedSource.Fixture, Folder(), new RecordingAwake())).Message,
            StringComparison.Ordinal);
        Assert.Throws<DirectoryNotFoundException>(() => NightQueue.From(Settings(), FeedSource.Fixture, Path.Combine(Folder(), "no-such-capture"), new RecordingAwake()));
        Assert.Throws<InvalidOperationException>(() => NightQueue.From(Settings((LocalModelSettings.ApiKeyKey, "a-key")), FeedSource.Fixture, Folder(), new RecordingAwake()));
    }

    [Fact]
    public void SectionSeventeenAndTheRunbookStateTheHoursTheQueueDefaultsToAndTheKeyItReads()
    {
        // The figure the row states is the constant the code uses, read off the row's own
        // description cell, and the runbook's settings row names the key and the default the
        // same way, so the hours cannot move in one place alone.
        var row = ArchitectureTables
            .In(File.ReadAllText(Repository.Architecture))
            .Single(table => table.Heading == Scope.LimitsTable)
            .Body.Single(cells => cells.Count > 1 && cells[0] == "Overnight queue");

        Assert.Contains($"starting no pass once a configured number of hours has passed, which is {OvernightQueue.DefaultHours}:", row[1], StringComparison.Ordinal);

        var runbook = Corpus.Read("docs/RUNBOOK.md");

        Assert.Contains($"| the hours after which the queue starts no pass | `{OvernightQueue.HoursKey}`, a whole number above zero | `{OvernightQueue.DefaultHours}` |", runbook, StringComparison.Ordinal);
    }

    [Fact]
    public void TheLimitIsReadAsWholeHoursAndAValueThatIsNotOneIsRefusedByName()
    {
        IConfiguration With(string? value) =>
            new ConfigurationBuilder().AddInMemoryCollection(value is null ? [] : new Dictionary<string, string?> { [OvernightQueue.HoursKey] = value }).Build();

        Assert.Equal(TimeSpan.FromHours(OvernightQueue.DefaultHours), OvernightQueue.Limit(With(null)));
        Assert.Equal(TimeSpan.FromHours(OvernightQueue.DefaultHours), OvernightQueue.Limit(With(" ")));
        Assert.Equal(TimeSpan.FromHours(3), OvernightQueue.Limit(With("3")));

        foreach (var wrong in new[] { "0", "-1", "1.5", "an hour", "1 " })
        {
            var refused = Assert.Throws<InvalidOperationException>(() => OvernightQueue.Limit(With(wrong)));

            Assert.Contains(OvernightQueue.HoursKey, refused.Message, StringComparison.Ordinal);
        }
    }

    // ---- the local model not answering ----

    [Fact]
    public async Task ALocalModelThatDoesNotAnswerStopsTheQueueAndItsRowSaysItCouldNotRun()
    {
        var night = await FixtureReplay.NightAsync(NightQueue.FromFixture(Folder(), new RecordingAwake()) with { LocalModel = new NothingAnsweringLocal() });

        using var store = night.Store;

        // The night itself is not failed by it: the arithmetic closed before step 17, and a
        // queue that could not run says so on its row rather than stopping the night.
        Assert.True(night.Code == 0, night.Error);

        var row = QueueRow(store);

        Assert.Equal(OvernightQueue.Unavailable, row.GetProperty("outcome").GetString());
        Assert.StartsWith(ProseWriter.Unavailable, row.GetProperty("reason").GetString()!, StringComparison.Ordinal);
        Assert.Empty(row.GetProperty("completed").EnumerateArray());

        // The first name's pass found out, and every name is left, the first among them.
        Assert.Equal("AAPL", row.GetProperty("stopped").GetProperty("ticker").GetString());
        Assert.Equal(Listed(Expected("overnight-queue").GetProperty("queued")), Listed(row.GetProperty("left")));

        // No section was written, and the one call attempted is on the pass the row names.
        Assert.Equal(["0"], Query(store, "SELECT COUNT(*) FROM research_section;"));
        Assert.Equal(["1"], Query(store, $"SELECT model_calls FROM run_log WHERE run_id = '{FixtureReplay.NightRunId}' AND stage = '{OvernightQueue.Stage}';"));
        Assert.Empty(NightlyCost.CallsTheCarveDoesNotAllow(NightlyCost.CostRows(store.DatabaseFile), FixtureReplay.NightRunId));
    }

    // ---- the arithmetic, unmoved ----

    [Fact]
    public async Task TheArithmeticsOwnFiguresAreTheSameWhetherTheQueueRanOrNot()
    {
        // One night whose queue wrote four names' keys, and one whose local model did not
        // answer, over the same fixture. Every table the arithmetic writes, and every row it
        // logged before step 17, are the same in both.
        var ran = await FixtureReplay.NightAsync();
        var stopped = await FixtureReplay.NightAsync(NightQueue.FromFixture(Folder(), new RecordingAwake()) with { LocalModel = new NothingAnsweringLocal() });

        using var withQueue = ran.Store;
        using var withoutQueue = stopped.Store;

        Assert.True(ran.Code == 0, ran.Error);
        Assert.True(stopped.Code == 0, stopped.Error);

        string[] arithmetic = ["bar", "indicator", "swing", "volume_profile", "level", "ladder", "move", "listing", "facts", "forward_return", "news_pulse", "membership", "calendar", "series_state"];

        foreach (var table in arithmetic)
        {
            var rows = Query(withQueue, $"SELECT * FROM {table} ORDER BY 1, 2;");

            Assert.True(rows.Count > 0 || table == "series_state", $"The night wrote no row into {table}, so comparing it says nothing.");
            Assert.Equal(rows, Query(withoutQueue, $"SELECT * FROM {table} ORDER BY 1, 2;"));
        }

        const string Logged = "SELECT stage, outcome, rows_written, model_calls, network_requests, spend, detail FROM run_log WHERE run_id = '" + FixtureReplay.NightRunId + "' AND stage != '" + OvernightQueue.Stage + "' ORDER BY stage;";

        Assert.Equal(Query(withQueue, Logged), Query(withoutQueue, Logged));

        // And the two queues did differ, so the comparison is between nights that were not
        // the same at step 17.
        Assert.NotEqual(Query(withQueue, "SELECT COUNT(*) FROM research_section;"), Query(withoutQueue, "SELECT COUNT(*) FROM research_section;"));
    }

    // ---- the machine held awake ----

    [Fact]
    public void TheMachineIsHeldOnWindowsAndMacOSAndAnyOtherMachineSaysWhyNot()
    {
        // The platform's own request, taken and released on the machine running the suite,
        // which is macOS on its hosted runner, Windows on the operator's machine and Linux in
        // the case-sensitivity job. A second hold is taken after the first is released, so a hold the machine gives
        // once is not read as one it gives every night. Whether the release closed the
        // platform's request is not read back here, since the platform lists its requests
        // through a tool of its own and this repository shells out to none.
        var awake = new MachineAwake();

        foreach (var attempt in new[] { 1, 2 })
        {
            using var hold = awake.Hold(OvernightQueue.AwakeReason);

            if (OperatingSystem.IsWindows() || OperatingSystem.IsMacOS())
            {
                Assert.True(hold.Held, $"attempt {attempt}: {hold.Line}");
                Assert.Equal(MachineAwake.HeldLine, hold.Line);
            }
            else
            {
                Assert.False(hold.Held);
                Assert.StartsWith("not held:", hold.Line, StringComparison.Ordinal);
            }
        }
    }
}
