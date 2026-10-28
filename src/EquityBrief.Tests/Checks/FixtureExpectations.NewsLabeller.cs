using System.Globalization;
using System.Text.Json;
using EquityBrief.Core.News;
using EquityBrief.Core.Providers;
using EquityBrief.Core.Releases;
using EquityBrief.Core.Research;
using EquityBrief.Core.Time;
using EquityBrief.Worker.Filter;
using EquityBrief.Worker.Local;
using EquityBrief.Worker.News;

namespace EquityBrief.Tests.Checks;

// fixture-expectations, 12.6 and 18.4: the news labeller on the local model. The instruction's keys and the reading of
// an answer worked by hand; the labeller over a constructed store labelling tonight's list in its order and the names a
// report is written for under its own pin, the answer asked for in the instruction's schema, an empty or cut-off answer
// asked once more and an unreadable one kept with its cause, sending no article admissibility refused or outside the
// window, never overwriting a label and leaving a paid label standing beside its own; labelling nothing with no pin or
// one that differs; waiting for the local model's lock inside its time limit and stopping where the lock outlasts it;
// stopping at its time limit and before the local run's next slot; leaving a story the runtime does not answer for the
// next run and stopping where the runtime is gone; its month limit reading nothing spent; every stored story labelled
// once in batches that stop at their instant; and the counter keeping each member's articles judged as they are stored.
// see: The news labeller is a process of its own the off-peak run starts, and labels on the local model under a pin of its own
// see: The news labeller waits for the local model's lock and stops before the local run's next slot, its time limit counted from its start
// see: A label's reason is one sentence holding no digit, and an answer code cannot read is asked once more and then kept as unreadable with its cause
// see: A news article is stored once per member with its admissibility judged, and a label is never overwritten
public partial class FixtureExpectations
{
    const string Good = "{\"kind\":\"results\",\"direction\":\"positive\",\"reason\":\"Quarterly sales came in ahead of what the company had guided.\"}";

    const string LabellerModel = "google/gemma-4-26b-a4b-qat";

    static string LabelAnswer(string kind, string direction, string reason) =>
        JsonSerializer.Serialize(new { kind, direction, reason });

    [Fact]
    public void TheInstructionReadsALabelAndNamesWhyAnAnswerCannotBeRead()
    {
        var (label, cause) = NewsInstruction.Read(Good);

        Assert.Null(cause);
        Assert.Equal(("results", "positive", "Quarterly sales came in ahead of what the company had guided."), (label!.Kind, label.Direction, label.Reason));

        // Words around the object are read past, and the kind and direction are read whatever their case.
        Assert.Equal("deal", NewsInstruction.Read("Here is the label:\n" + LabelAnswer("Deal", "NEUTRAL", "The company agreed to buy a smaller rival.") + "\nDone.").Label!.Kind);

        // A line break written inside the reason, which no JSON reader takes, is read as a space and the reason as one
        // line, while two sentences either side of one are still two.
        var broken = NewsInstruction.Read("{\n  \"kind\": \"other\",\n  \"direction\": \"neutral\",\n  \"reason\": \"The article reports board appointments at another bank and holds no news \n  of the company.\"\n}");

        Assert.Equal(("other", "neutral", "The article reports board appointments at another bank and holds no news of the company."), (broken.Label!.Kind, broken.Label.Direction, broken.Label.Reason));
        Assert.Equal(NewsInstruction.NotOneSentence, NewsInstruction.Read("{\"kind\":\"results\",\"direction\":\"positive\",\"reason\":\"Sales rose.\nMargins widened.\"}").Cause);

        // Each cause, by the first check it fails; a readable name is no key, so an answer giving one is outside the set.
        Assert.Equal(NewsInstruction.NotJson, NewsInstruction.Read("positive, because sales rose").Cause);
        Assert.Equal(NewsInstruction.KindOutsideTheSet, NewsInstruction.Read(LabelAnswer("rumour", "positive", "A rival said so.")).Cause);
        Assert.Equal(NewsInstruction.KindOutsideTheSet, NewsInstruction.Read(LabelAnswer("legal or regulatory", "negative", "A regulator opened an inquiry.")).Cause);
        Assert.Equal(NewsInstruction.DirectionOutsideTheSet, NewsInstruction.Read(LabelAnswer("results", "mixed", "Sales rose and margins fell.")).Cause);
        Assert.Equal(NewsInstruction.DigitInTheReason, NewsInstruction.Read(LabelAnswer("results", "positive", "Sales rose by 5 percent on the year.")).Cause);
        Assert.Equal(NewsInstruction.NotOneSentence, NewsInstruction.Read(LabelAnswer("results", "positive", "Sales rose. Margins widened too.")).Cause);
        Assert.Equal(NewsInstruction.NotOneSentence, NewsInstruction.Read(LabelAnswer("results", "positive", "")).Cause);

        // From version 2 the nine kinds are one-word keys, the brief's words kept as their readable names, each version 1
        // label's kind read as its key one to one; the instruction gives each key its line, and the schema holds the
        // answer to the keys and the directions.
        Assert.Equal(["results", "guidance", "product", "legal", "deal", "analyst", "management", "opinion", "other"], NewsInstruction.Kinds);
        Assert.Equal(
            ["results", "guidance", "product", "legal or regulatory", "deal", "analyst change", "management", "opinion or promotional", "other"],
            NewsInstruction.Kinds.Select(NewsInstruction.NameOf));
        Assert.Equal(NewsInstruction.Kinds, NewsInstruction.Kinds.Select(kind => NewsInstruction.KeyOf(NewsInstruction.NameOf(kind))));
        Assert.Equal(("legal", "analyst", "opinion", "results"), (NewsInstruction.KeyOf("legal or regulatory"), NewsInstruction.KeyOf("analyst change"), NewsInstruction.KeyOf("opinion or promotional"), NewsInstruction.KeyOf("results")));
        Assert.True(NewsInstruction.IsOpinion("opinion or promotional") && NewsInstruction.IsOpinion("opinion") && !NewsInstruction.IsOpinion("other") && !NewsInstruction.IsOpinion(null));
        Assert.All(NewsInstruction.KindsWritten, kind => Assert.Contains(kind.Key + ": " + kind.Covers + ".", NewsInstruction.System, StringComparison.Ordinal));
        Assert.Contains("opinion: commentary, promotion or a recommendation, not a report of something that happened.", NewsInstruction.System, StringComparison.Ordinal);

        var schema = NewsInstruction.Schema();

        Assert.Equal(NewsInstruction.Kinds, schema["properties"]!["kind"]!["enum"]!.AsArray().Select(key => key!.GetValue<string>()));
        Assert.Equal(NewsInstruction.Directions, schema["properties"]!["direction"]!["enum"]!.AsArray().Select(direction => direction!.GetValue<string>()));

        // The reason's pattern refuses a digit and nothing else: a stop, a comma, a question and a line break inside an
        // answer pass it, and code reads the one sentence.
        var reasonPattern = schema["properties"]!["reason"]!["pattern"]!.GetValue<string>();

        Assert.All(
            ["Sales rose, and the company said why.", "Did sales rise? The company said so.", "Sales rose\nagain.", "Sales rose", ""],
            reason => Assert.Matches(reasonPattern, reason));
        Assert.All(["Sales rose 5 percent.", "Sales rose in 2026.", "Q3 sales rose."], reason => Assert.DoesNotMatch(reasonPattern, reason));
        Assert.Equal(9, NewsInstruction.Kinds.Length);
        Assert.Equal(["positive", "negative", "neutral"], NewsInstruction.Directions);
        Assert.Equal(7, NewsInstruction.Causes.Length);
        Assert.Equal((2, 1), (NewsInstruction.Version, NewsInstruction.PaidVersion));
        Assert.Contains("no digit", NewsInstruction.System, StringComparison.Ordinal);
    }

    // The family's constructed store with the filter run on its night, so tonight's list is ZZB, ZZC, ZZA in
    // that order and ZZD passes no gate, and the articles below stored for it.
    static async Task<TemporaryStore> NewsStore()
    {
        var store = await FamilyStore(new DateTimeOffset(2026, 9, 6, 22, 0, 0, TimeSpan.Zero));

        await new SwingFilter(FixedClock.At(FilterEvening, SessionZones.UnitedStates), store.DatabaseFile).RunAsync("GSPC", "filter-news");

        void Article(string ticker, string id, string published, string admissibility, string title) =>
            store.Execute(
                "INSERT INTO news_article (ticker, article_id, link, title, source, published_at, text, length, admissibility, session_date) VALUES " +
                $"('{ticker}', '{id}', 'https://example.invalid/{id}', '{title}', 'wire', '{published}', 'The company said so in a statement.', 36, '{admissibility}', '{published[..10]}');");

        Article("ZZB", "b1", "2026-09-07T12:00:00Z", Admissibility.Accepted, "ZZB beats");
        Article("ZZB", "b2", "2026-09-06T12:00:00Z", Admissibility.Accepted, "ZZB guides");
        Article("ZZB", "b3", "2026-09-05T12:00:00Z", Admissibility.PriceForecast, "ZZB to double, says a forecaster");
        Article("ZZB", "b4", "2026-08-01T12:00:00Z", Admissibility.Accepted, "ZZB from before the window");
        Article("ZZC", "c1", "2026-09-08T10:00:00Z", Admissibility.Accepted, "ZZC names a chief");
        Article("ZZD", "d1", "2026-09-08T10:00:00Z", Admissibility.Accepted, "ZZD is not on the list");

        return store;
    }

    // The local profile the labeller asks, its model file a stand-in of a few bytes in a models folder under the store's
    // root, and the pin written for it as the pinning run writes one; the part a test names written as it differs.
    static LocalModelSettings LabellerSettings(TemporaryStore store, int context = 50176)
    {
        var folder = Path.Combine(store.Root, "models");
        var file = Path.Combine(folder, "lmstudio-community", "gemma", "model.gguf");

        if (!File.Exists(file))
        {
            Directory.CreateDirectory(Path.GetDirectoryName(file)!);
            File.WriteAllText(file, "a stand-in for the model's weights");
        }

        return new LocalModelSettings("gemma-4", "http://127.0.0.1:1234/v1/", LabellerModel, 30, context, 30)
        {
            ModelFile = "lmstudio-community/gemma/model.gguf",
            ModelsFolder = folder,
        };
    }

    static void PinFor(TemporaryStore store, LocalModelSettings settings, string? model = null, int? version = null, int? context = null, long? size = null)
    {
        var folder = LocalModelFile.FolderOf(settings);
        var facts = LocalModelFile.Facts(folder, settings.ModelFile!)!;
        var sha = LocalModelFile.HashAsync(folder, settings.ModelFile!, CancellationToken.None).GetAwaiter().GetResult();

        store.Execute(
            "INSERT INTO label_pin (model, model_file, sha256, size, modified, runtime_name, runtime_version, instruction_version, temperature, seed, answer_tokens, context, written_at, run_id) VALUES " +
            FormattableString.Invariant($"('{model ?? settings.Model}', '{settings.ModelFile}', '{(size is null ? sha : new string('0', 64))}', {size ?? facts.Size}, '{facts.Modified.UtcDateTime:yyyy-MM-ddTHH:mm:ss.fffffffZ}', 'llama.cpp', '2.62.1', {version ?? NewsInstruction.Version}, 0, {ReadingSettings.Seed}, {NewsLabelling.AnswerTokens}, {context ?? settings.ContextTokens}, '2026-09-08T22:00:00Z', 'label-news-pin');"));
    }

    static NewsLabeller Labeller(TemporaryStore store, IReadingModelFeed model, LocalModelSettings? settings = null, NewsLimits? limits = null, IClock? clock = null, Func<TimeSpan, CancellationToken, Task>? wait = null) =>
        new(
            model,
            settings ?? LabellerSettings(store),
            limits ?? NewsLimits.Default,
            clock ?? FixedClock.At(new DateTimeOffset(2026, 9, 8, 23, 0, 0, TimeSpan.Zero), SessionZones.UnitedStates),
            store.DatabaseFile,
            store.Root,
            wait ?? ((_, _) => Task.CompletedTask));

    static ReadingReply Answered(string content, string finish = "stop") =>
        new(LabellerModel, content, finish, 900, 40, 0, "llama.cpp", "2.62.1", 1.1);

    // The local model as a script: each call answers with the next reply, a null one as the runtime refusing the call,
    // and moves the clock by a step where the test gives one; the model made ready and released as the runtime is.
    sealed class LocalAnswers(CallClock? clock, TimeSpan step, params ReadingReply?[] replies) : IReadingModelFeed
    {
        readonly Queue<ReadingReply?> script = new(replies);

        public List<ReadingRequest> Asked { get; } = [];

        public int Requests => Asked.Count;

        public bool Prepared { get; private set; }

        public bool? ReleasedLoaded { get; private set; }

        public Exception? Gone { get; init; }

        public Task<bool> PrepareAsync(CancellationToken cancellation = default)
        {
            if (Gone is { } gone)
            {
                throw gone;
            }

            Prepared = true;

            return Task.FromResult(true);
        }

        public Task ReleaseAsync(bool loadedHere, CancellationToken cancellation = default)
        {
            ReleasedLoaded = loadedHere;

            return Task.CompletedTask;
        }

        public Task<ReadingReply> AskAsync(ReadingRequest request, CancellationToken cancellation = default)
        {
            Asked.Add(request);
            clock?.Advance(step);

            return script.Count == 0
                ? throw new InvalidOperationException("the labeller asked past the test's script")
                : script.Dequeue() is { } reply ? Task.FromResult(reply) : throw new ReadingRefused(ReleaseAnswers.RuntimeRefused, "the runtime refused the call");
        }
    }

    // A clock that stands still until a call or a wait moves it on, so how long a run takes is a number the test
    // chose and a limit's boundary falls where the test put it.
    sealed class CallClock(DateTimeOffset start) : IClock
    {
        public DateTimeOffset UtcNow { get; private set; } = start;

        public TimeZoneInfo SessionZone { get; } = SessionZones.ResolveSessionZone(SessionZones.UnitedStates);

        public void Advance(TimeSpan by) => UtcNow += by;
    }

    // A wait that moves the clock by what it is asked to wait, as a process's own wait moves the machine's.
    static Func<TimeSpan, CancellationToken, Task> Moving(CallClock clock) => (span, _) =>
    {
        clock.Advance(span);

        return Task.CompletedTask;
    };

    static JsonElement LabellerRow(TemporaryStore store, string runId) =>
        JsonDocument.Parse(Text(store, $"SELECT detail FROM run_log WHERE run_id = '{runId}' AND stage = '{NewsLabeller.Stage}';")).RootElement;

    [Fact]
    public async Task TheLabellerLabelsTonightsListOnTheLocalModelUnderItsPinAsksOnceMoreOnAnEmptyOrCutOffAnswerAndNeverOverwrites()
    {
        using var store = await NewsStore();

        var settings = LabellerSettings(store);

        PinFor(store, settings);

        // A paid label written before the switch, which stands beside the local one.
        store.Execute(
            "INSERT INTO news_label (ticker, article_id, profile, instruction_version, model, outcome, cause, kind, direction, reason, labelled_at, run_id) VALUES " +
            "('ZZB', 'b1', 'deepseek', 1, 'deepseek-flash', 'labelled', NULL, 'results', 'positive', 'A paid label.', '2026-09-08T04:00:00Z', 'label-news-paid');");

        // Worked by hand: ZZB's two admitted articles of the window newest first, then ZZC's one; b3 is refused by
        // admissibility and b4 is outside the window, so neither is sent, and ZZD is not on the list. b1 is read first
        // time, b2 after an empty answer, and c1 is cut off at the budget twice.
        var model = new LocalAnswers(
            null,
            TimeSpan.Zero,
            Answered(Good),
            Answered(string.Empty),
            Answered(LabelAnswer("guidance", "negative", "The company lowered what it expects for the year.")),
            Answered("{\"kind\":\"other\",\"direction\":\"neutral\",\"reason\":\"The company", ReleaseAnswers.BudgetReached),
            Answered("{\"kind\":\"other\",\"direction\":\"neutral\",\"reason\":\"The company", ReleaseAnswers.BudgetReached));

        var outcome = await Labeller(store, model, settings).RunAsync(new DateOnly(2026, 9, 8), "label-news-one");

        Assert.Equal((3, 3, 2, 1, 1, 0, 0, NewsLabeller.Finished), (outcome.Names, outcome.NamesReached, outcome.Labelled, outcome.UnreadableCount, outcome.RefusedByAdmissibility, outcome.AlreadyLabelled, outcome.NotAnswered, outcome.Stop));
        Assert.Equal(["b1", "b2", "b2", "c1", "c1"], model.Asked.Select(request => request.Text.Split('\n')[1]["Title: ".Length..]).Select(title => title switch { "ZZB beats" => "b1", "ZZB guides" => "b2", _ => "c1" }));
        Assert.True(model.Prepared && model.ReleasedLoaded == true);

        // Each request asks the profile's model under the pin's seed and budget, the instruction's own words, and the
        // schema holding the answer to the keys.
        Assert.All(model.Asked, request => Assert.Equal((LabellerModel, NewsInstruction.System, ReadingSettings.Seed, NewsLabelling.AnswerTokens), (request.Model, request.Instruction, request.Seed, request.AnswerTokens)));
        Assert.All(model.Asked, request => Assert.Equal(NewsInstruction.Kinds, request.Schema!["properties"]!["kind"]!["enum"]!.AsArray().Select(key => key!.GetValue<string>())));
        Assert.StartsWith("Company: ZZB (ZZB)\nTitle: ZZB beats", model.Asked[0].Text, StringComparison.Ordinal);

        Assert.Equal(
            ["b1|labelled|results|positive|google/gemma-4-26b-a4b-qat", "b2|labelled|guidance|negative|google/gemma-4-26b-a4b-qat", "c1|unreadable|||google/gemma-4-26b-a4b-qat"],
            TextRows(store, "SELECT article_id || '|' || outcome || '|' || COALESCE(kind, '') || '|' || COALESCE(direction, '') || '|' || model FROM news_label WHERE profile = 'gemma-4' AND instruction_version = 2 ORDER BY ticker, article_id;"));
        Assert.Equal(NewsInstruction.CutOff, Text(store, "SELECT cause FROM news_label WHERE article_id = 'c1' AND profile = 'gemma-4';"));
        Assert.Equal("A paid label.", Text(store, "SELECT reason FROM news_label WHERE article_id = 'b1' AND profile = 'deepseek';"));

        // The run's own row names the local profile and its model, spends nothing and names the pin it labelled under.
        var row = LabellerRow(store, "label-news-one");

        Assert.Equal(("gemma-4", LabellerModel, true, "0", NewsLabeller.Finished), (row.GetProperty("profile").GetString(), row.GetProperty("model").GetString(), row.GetProperty("local").GetBoolean(), row.GetProperty("cost").GetString(), row.GetProperty("stop").GetString()));
        Assert.Equal(1, row.GetProperty("pin").GetInt64());
        Assert.Equal("0", Text(store, "SELECT spend FROM run_log WHERE run_id = 'label-news-one' AND stage = 'news-labels';"));
        Assert.Equal(5, Scalar(store, "SELECT model_calls FROM run_log WHERE run_id = 'label-news-one' AND stage = 'news-labels';"));

        // Run again, nothing is asked: every article of the window is labelled or unreadable under the profile.
        var again = new LocalAnswers(null, TimeSpan.Zero);
        var second = await Labeller(store, again, settings).RunAsync(new DateOnly(2026, 9, 8), "label-news-two");

        Assert.Equal((0, 0, 3), (again.Requests, second.Labelled, second.AlreadyLabelled));
    }

    // An answer that arrives whole and fails a check is kept as unreadable with its cause after the one ask, since the
    // same story under the same seed answers the same way again: b1's reason holds a digit, and b2 and c1 are read.
    [Fact]
    public async Task AnAnswerArrivingWholeThatCodeCannotReadIsKeptUnreadableWithoutASecondAsk()
    {
        using var store = await NewsStore();

        var settings = LabellerSettings(store);

        PinFor(store, settings);

        var model = new LocalAnswers(null, TimeSpan.Zero, Answered(LabelAnswer("results", "positive", "Sales rose by 5 percent.")), Answered(Good), Answered(Good));
        var outcome = await Labeller(store, model, settings).RunAsync(new DateOnly(2026, 9, 8), "label-news-whole");

        Assert.Equal((3, 2, 1), (model.Requests, outcome.Labelled, outcome.UnreadableCount));
        Assert.Equal(NewsInstruction.DigitInTheReason, Text(store, "SELECT cause FROM news_label WHERE article_id = 'b1' AND profile = 'gemma-4';"));
        Assert.Equal(1, LabellerRow(store, "label-news-whole").GetProperty("unreadable").GetProperty(NewsInstruction.DigitInTheReason).GetInt32());
    }

    // Worked by hand over the constructed store: ZZB holds 24 stories of its own in the window, b1 and b2 and 22 more from
    // 2026-08-14, all sent in the one run where a run sent a name its newest twenty; ZZD, on no list, holds the night's
    // own request asked inside its session and is labelled after the list; ZZC's request names a name the list holds,
    // labelled once in the list's place; ZZE's request was asked the day before the session and ZZF's was refused, so
    // neither names a report and neither's story is sent.
    // see: The news labeller labels the S&P 500's list and every name a report is written for, each name's own stories in one run
    [Fact]
    public async Task TheLabellerLabelsEachNameAReportIsWrittenForAfterTheListAndEveryOneOfANamesOwnStoriesInOneRun()
    {
        using var store = await NewsStore();

        var settings = LabellerSettings(store);

        PinFor(store, settings);

        void Article(string ticker, string id, DateOnly published) =>
            store.Execute(
                "INSERT INTO news_article (ticker, article_id, link, title, source, published_at, text, length, admissibility, session_date) VALUES " +
                FormattableString.Invariant($"('{ticker}', '{id}', 'https://example.invalid/{id}', '{ticker} story {id}', 'wire', '{published:yyyy-MM-dd}T09:00:00Z', 'The company said so in a statement.', 36, '{Admissibility.Accepted}', '{published:yyyy-MM-dd}');"));

        void Request(string ticker, string askedAt, string state) =>
            store.Execute($"INSERT INTO research_request (ticker, asked_at, asked_from, lane, state) VALUES ('{ticker}', '{askedAt}', 'night', 'paid', '{state}');");

        for (var day = 0; day < 22; day++)
        {
            Article("ZZB", FormattableString.Invariant($"b{10 + day}"), new DateOnly(2026, 8, 14).AddDays(day));
        }

        Article("ZZE", "e1", new DateOnly(2026, 9, 7));
        Article("ZZF", "f1", new DateOnly(2026, 9, 7));

        Request("ZZD", "2026-09-08T23:05:00Z", "outstanding");
        Request("ZZC", "2026-09-08T23:05:01Z", "written");
        Request("ZZE", "2026-09-07T23:05:00Z", "written");
        Request("ZZF", "2026-09-08T23:06:00Z", "refused");

        var model = new LocalAnswers(null, TimeSpan.Zero, [.. Enumerable.Repeat<ReadingReply?>(Answered(Good), 26)]);
        var outcome = await Labeller(store, model, settings).RunAsync(new DateOnly(2026, 9, 8), "label-news-reports");

        Assert.Equal((4, 4, 26, 0, 1, NewsLabeller.Finished), (outcome.Names, outcome.NamesReached, outcome.Labelled, outcome.UnreadableCount, outcome.RefusedByAdmissibility, outcome.Stop));
        Assert.Equal(26, model.Requests);

        var titles = model.Asked.Select(request => request.Text.Split('\n')[1]["Title: ".Length..]).ToArray();

        Assert.Equal(["ZZB beats", "ZZB guides", "ZZB story b31", "ZZB story b30"], titles[..4]);
        Assert.Equal(24, titles.Count(title => title.StartsWith("ZZB", StringComparison.Ordinal)));
        Assert.Equal("ZZB story b10", titles[23]);
        Assert.Equal(["ZZC names a chief", "ZZD is not on the list"], titles[24..]);
        Assert.Equal(0, Scalar(store, "SELECT COUNT(*) FROM news_label WHERE ticker IN ('ZZE', 'ZZF');"));
        Assert.Equal(24, Scalar(store, "SELECT COUNT(*) FROM news_label WHERE ticker = 'ZZB' AND outcome = 'labelled';"));
    }

    // No pin labels nothing and names how one is written; a pin the run differs from in the model, its file, the
    // instruction's version or the window labels nothing and names the part; the run's row says why either way, and no
    // call is made.
    [Fact]
    public async Task TheLabellerLabelsNothingWhereNoPinIsWrittenOrThePinDiffers()
    {
        using var store = await NewsStore();

        var settings = LabellerSettings(store);
        var none = new LocalAnswers(null, TimeSpan.Zero);
        var unpinned = await Labeller(store, none, settings).RunAsync(new DateOnly(2026, 9, 8), "label-news-unpinned");

        Assert.Equal((NewsLabeller.NoPin, 0, 0, false), (unpinned.Stop, unpinned.Labelled, none.Requests, none.Prepared));
        Assert.Contains("label-news --pin", LabellerRow(store, "label-news-unpinned").GetProperty("why").GetString(), StringComparison.Ordinal);

        foreach (var (pin, part) in (IEnumerable<(Action Write, string Part)>)[
            (() => PinFor(store, settings, version: 1), ReadingPins.PromptPart),
            (() => PinFor(store, settings, model: "qwen/qwen3.5-9b"), ReadingPins.ModelPart),
            (() => PinFor(store, settings, context: 60160), ReadingPins.WindowPart),
            (() => PinFor(store, settings, size: 7), ReadingPins.FilePart)])
        {
            pin();

            var asked = new LocalAnswers(null, TimeSpan.Zero);
            var differs = await Labeller(store, asked, settings).RunAsync(new DateOnly(2026, 9, 8), "label-news-" + part.Replace(' ', '-').Replace("'", string.Empty, StringComparison.Ordinal));

            Assert.Equal((NewsLabeller.PinDiffers, 0, 0), (differs.Stop, differs.Labelled, asked.Requests));
            Assert.StartsWith(part + " differs from label pin ", LabellerRow(store, "label-news-" + part.Replace(' ', '-').Replace("'", string.Empty, StringComparison.Ordinal)).GetProperty("why").GetString(), StringComparison.Ordinal);
        }

        Assert.Equal(0, Scalar(store, "SELECT COUNT(*) FROM news_label;"));
    }

    // Worked by hand over a limit of twenty minutes, each call three: another local process holds the local model's lock
    // for a quarter of an hour, as the local run or its history read does, and the labeller waits for it, then asks b1
    // at fifteen minutes and b2 at eighteen, and c1, at twenty-one, is past the limit counted from the start. A run whose
    // lock is held past its limit asks nothing, so the drain after it starts when it always did, and c1 waits again.
    [Fact]
    public async Task TheLabellerWaitsForTheLocalModelsLockInsideItsTimeLimitAndStopsWhereTheLockOutlastsIt()
    {
        using var store = await NewsStore();

        var settings = LabellerSettings(store);

        PinFor(store, settings);

        var clock = new CallClock(new DateTimeOffset(2026, 9, 9, 4, 5, 0, TimeSpan.Zero));
        var held = await LocalLocks.TakeAsync(store.Root, null, clock, (_, _) => Task.CompletedTask, CancellationToken.None);
        var waits = 0;

        Task Wait(TimeSpan span, CancellationToken cancellation)
        {
            waits++;
            clock.Advance(TimeSpan.FromMinutes(15));
            held?.Dispose();
            held = null;

            return Task.CompletedTask;
        }

        var model = new LocalAnswers(clock, TimeSpan.FromMinutes(3), Answered(Good), Answered(Good));
        var outcome = await Labeller(store, model, settings, NewsLimits.Default, clock, Wait).RunAsync(new DateOnly(2026, 9, 8), "label-news-waited");

        Assert.Equal((1, TimeSpan.FromMinutes(15), 2, 2, NewsLabeller.TimeLimitPassed), (waits, outcome.Waited, model.Requests, outcome.Labelled, outcome.Stop));
        Assert.Equal(15, LabellerRow(store, "label-news-waited").GetProperty("waitedMinutes").GetInt32());

        using (await LocalLocks.TakeAsync(store.Root, null, clock, (_, _) => Task.CompletedTask, CancellationToken.None))
        {
            var none = new LocalAnswers(clock, TimeSpan.FromMinutes(3));
            var blocked = await Labeller(store, none, settings, NewsLimits.Default, clock, Moving(clock)).RunAsync(new DateOnly(2026, 9, 8), "label-news-lock-held");

            Assert.Equal((NewsLabeller.LockHeld, TimeSpan.FromMinutes(20), 0, false), (blocked.Stop, blocked.Waited, none.Requests, none.Prepared));
            Assert.Contains("the next run takes these stories up", LabellerRow(store, "label-news-lock-held").GetProperty("why").GetString(), StringComparison.Ordinal);
        }

        Assert.Equal(0, Scalar(store, "SELECT COUNT(*) FROM news_label WHERE article_id = 'c1';"));
    }

    // Worked by hand from ten to midday, each call eight minutes: b1 is asked then and b2 at two minutes to, and c1, at a
    // minute past the local run's early slot, waits for the next run, inside the labeller's twenty minutes.
    [Fact]
    public async Task TheLabellerStopsBeforeTheLocalRunsNextSlot()
    {
        DateTimeOffset At(int day, int hour, int minute) => new(2026, 9, day, hour, minute, 0, TimeSpan.Zero);

        Assert.Equal(
            [At(9, 12, 5), At(9, 12, 5), At(9, 21, 40), At(10, 12, 5)],
            new[] { At(9, 4, 5), At(9, 12, 5), At(9, 12, 6), At(9, 21, 41) }.Select(NewsLabelling.NextLocalSlotStart));

        using var store = await NewsStore();

        var settings = LabellerSettings(store);

        PinFor(store, settings);

        var clock = new CallClock(At(9, 11, 50));
        var model = new LocalAnswers(clock, TimeSpan.FromMinutes(8), Answered(Good), Answered(Good));
        var outcome = await Labeller(store, model, settings, NewsLimits.Default, clock).RunAsync(new DateOnly(2026, 9, 8), "label-news-slot");

        Assert.Equal((2, 2, NewsLabeller.NextLocalSlot), (model.Requests, outcome.Labelled, outcome.Stop));
        Assert.Equal(0, Scalar(store, "SELECT COUNT(*) FROM news_label WHERE article_id = 'c1';"));
    }

    // Worked by hand over a limit of twenty minutes, each call fifteen: b1 asked at the start and b2 at fifteen minutes are
    // labelled, and c1, at thirty, is past the limit and waits for the next run.
    [Fact]
    public async Task TheLabellerStopsAtItsTimeLimitAndLeavesTheRestForTheNextRun()
    {
        using var store = await NewsStore();

        var settings = LabellerSettings(store);

        PinFor(store, settings);

        var clock = new CallClock(new DateTimeOffset(2026, 9, 9, 4, 5, 0, TimeSpan.Zero));
        var model = new LocalAnswers(clock, TimeSpan.FromMinutes(15), Answered(Good), Answered(Good));
        var outcome = await Labeller(store, model, settings, NewsLimits.Default, clock).RunAsync(new DateOnly(2026, 9, 8), "label-news-limit");

        Assert.Equal((2, 2, NewsLabeller.TimeLimitPassed), (model.Requests, outcome.Labelled, outcome.Stop));
        Assert.Equal(0, Scalar(store, "SELECT COUNT(*) FROM news_label WHERE article_id = 'c1';"));
    }

    // A story the runtime refuses twice is left with no row and asked again by the next run; a runtime gone stops the run
    // where it is, its row naming the model unreachable and why; and nothing of the night's moves either way.
    [Fact]
    public async Task AStoryTheRuntimeDoesNotAnswerIsTakenUpByTheNextRunAndARuntimeGoneStopsTheRun()
    {
        using var store = await NewsStore();

        var settings = LabellerSettings(store);

        PinFor(store, settings);

        var nightRows = Scalar(store, "SELECT COUNT(*) FROM run_log;");
        var refusing = new LocalAnswers(null, TimeSpan.Zero, null, null, Answered(Good), Answered(Good));
        var first = await Labeller(store, refusing, settings).RunAsync(new DateOnly(2026, 9, 8), "label-news-refusing");

        Assert.Equal((4, 2, 1, NewsLabeller.Finished), (refusing.Requests, first.Labelled, first.NotAnswered, first.Stop));
        Assert.Equal(["b2", "c1"], TextRows(store, "SELECT article_id FROM news_label ORDER BY article_id;"));
        Assert.Equal(1, LabellerRow(store, "label-news-refusing").GetProperty("notAnswered").GetInt32());

        var next = new LocalAnswers(null, TimeSpan.Zero, Answered(Good));
        var second = await Labeller(store, next, settings).RunAsync(new DateOnly(2026, 9, 8), "label-news-taken-up");

        Assert.Equal((1, 1), (next.Requests, second.Labelled));
        Assert.Equal(["b1", "b2", "c1"], TextRows(store, "SELECT article_id FROM news_label ORDER BY article_id;"));

        var gone = new LocalAnswers(null, TimeSpan.Zero) { Gone = new ReadingRuntimeUnavailable(ReadingRuntimeUnavailable.NotRunning, "nothing answered at the runtime's address") };
        var stopped = await Labeller(store, gone, settings).RunAsync(new DateOnly(2026, 9, 9), "label-news-gone");

        Assert.Equal((NewsLabeller.ModelUnreachable, 0), (stopped.Stop, stopped.Labelled));
        Assert.Equal("nothing answered at the runtime's address", LabellerRow(store, "label-news-gone").GetProperty("why").GetString());
        Assert.Equal(nightRows, Scalar(store, "SELECT COUNT(*) FROM run_log WHERE run_id NOT LIKE 'label-news-%';"));
    }

    // The month's limit read as it always was, off the labeller's own rows: on the local model they spend nothing, so a
    // month holding no paid labeller's spend runs, and a month the paid labeller spent its limit in labels nothing.
    [Fact]
    public async Task TheLabellersMonthLimitReadsNothingSpentOnTheLocalModel()
    {
        using var store = await NewsStore();

        var settings = LabellerSettings(store);

        PinFor(store, settings);

        var labels = new LocalAnswers(null, TimeSpan.Zero, Answered(Good), Answered(Good), Answered(Good));
        var ran = await Labeller(store, labels, settings).RunAsync(new DateOnly(2026, 9, 8), "label-news-month");

        Assert.Equal((3, 0m, 0m), (ran.Labelled, ran.Cost, ran.MonthCost));

        store.Execute(
            "INSERT INTO run_log (run_id, stage, started_at, ended_at, outcome, rows_written, model_calls, network_requests, spend, detail) VALUES " +
            "('label-news-20260901T010000Z', 'research call: news label, ZZA a1', '2026-09-01T01:00:00Z', '2026-09-01T01:00:01Z', 'ok', 0, 1, 1, '5', '{}');");

        var held = new LocalAnswers(null, TimeSpan.Zero);
        var stopped = await Labeller(store, held, settings).RunAsync(new DateOnly(2026, 9, 8), "label-news-month-spent");

        Assert.Equal((NewsLabeller.MonthLimitReached, 0, 5m), (stopped.Stop, held.Requests, stopped.MonthCost));
    }

    // Every admitted story the store holds, newest first and the member's own, whatever list or window: a first batch
    // ending two minutes in, each call a minute, labels c1 and d1; a second, each call a quarter of an hour, labels b1, b2
    // and b4 past the labeller's twenty minutes, since a batch stops at its instant and not at the time limit, and none
    // twice; b3, refused by admissibility, is sent by neither, and the paid label on b2 stands beside its own.
    [Fact]
    public async Task EveryStoredStoryIsLabelledOnceInBatchesThatStopAtTheirInstant()
    {
        using var store = await NewsStore();

        var settings = LabellerSettings(store);

        PinFor(store, settings);

        store.Execute(
            "INSERT INTO news_label (ticker, article_id, profile, instruction_version, model, outcome, cause, kind, direction, reason, labelled_at, run_id) VALUES " +
            "('ZZB', 'b2', 'deepseek', 1, 'deepseek-flash', 'labelled', NULL, 'guidance', 'negative', 'A paid label.', '2026-09-08T04:00:00Z', 'label-news-paid');");

        var clock = new CallClock(new DateTimeOffset(2026, 9, 9, 5, 0, 0, TimeSpan.Zero));
        var first = new LocalAnswers(clock, TimeSpan.FromMinutes(1), Answered(Good), Answered(Good));
        var batch = await Labeller(store, first, settings, NewsLimits.Default, clock).EveryStoryAsync(clock.UtcNow.AddMinutes(2), "label-news-all-one");

        Assert.Equal((2, NewsLabeller.BatchEnded), (batch.Labelled, batch.Stop));
        Assert.Equal(["c1", "d1"], TextRows(store, "SELECT article_id FROM news_label WHERE profile = 'gemma-4' ORDER BY article_id;"));
        Assert.Equal(NewsLabelling.EveryStory, LabellerRow(store, "label-news-all-one").GetProperty("session").GetString());

        var rest = new LocalAnswers(clock, TimeSpan.FromMinutes(15), Answered(Good), Answered(Good), Answered(Good));
        var second = await Labeller(store, rest, settings, NewsLimits.Default, clock).EveryStoryAsync(clock.UtcNow.AddHours(5), "label-news-all-two");

        Assert.Equal((3, 3, NewsLabeller.Finished), (rest.Requests, second.Labelled, second.Stop));
        Assert.Equal(["b1", "b2", "b4", "c1", "d1"], TextRows(store, "SELECT article_id FROM news_label WHERE profile = 'gemma-4' ORDER BY article_id;"));
        Assert.Equal(0, Scalar(store, "SELECT COUNT(*) FROM news_label WHERE article_id = 'b3';"));
        Assert.Equal("A paid label.", Text(store, "SELECT reason FROM news_label WHERE article_id = 'b2' AND profile = 'deepseek';"));
    }

    [Fact]
    public void ALabellersLatestEndCountsItsLimitOverTheHoursOutsideThePeakWindows()
    {
        // Worked by hand over the recorded profile's windows, 01:00 to 04:00 and 06:00 to 10:00 UTC on weekdays, and a
        // limit of twenty minutes: started at 01:35 on a Wednesday, inside the first window, it ends at 04:20; at 23:30 on
        // the Tuesday before, off peak with an hour and a half to the window, at 23:50; at 05:50, ten minutes before the
        // second window, at 10:10 after it; a second before 04:00, at 04:20; on a Saturday, which names no window, twenty
        // minutes after its start; and with no prices, which name no window, the same.
        var pricing = Providers.ResearchModelFeedTests.Pinned().Pricing;
        var limit = TimeSpan.FromMinutes(20);

        DateTimeOffset At(int year, int month, int day, int hour, int minute, int second = 0) => new(year, month, day, hour, minute, second, TimeSpan.Zero);

        Assert.Equal(At(2026, 10, 7, 4, 20), NewsLabelling.EndsBy(At(2026, 10, 7, 1, 35), limit, pricing));
        Assert.Equal(At(2026, 10, 6, 23, 50), NewsLabelling.EndsBy(At(2026, 10, 6, 23, 30), limit, pricing));
        Assert.Equal(At(2026, 10, 7, 10, 10), NewsLabelling.EndsBy(At(2026, 10, 7, 5, 50), limit, pricing));
        Assert.Equal(At(2026, 10, 7, 4, 20), NewsLabelling.EndsBy(At(2026, 10, 7, 3, 59, 59), limit, pricing));

        // A limit ending exactly as the second window opens, from 05:40, ends at 06:00 and does not wait the window out.
        Assert.Equal(At(2026, 10, 7, 6, 0), NewsLabelling.EndsBy(At(2026, 10, 7, 5, 40), limit, pricing));
        Assert.Equal(At(2026, 10, 3, 2, 20), NewsLabelling.EndsBy(At(2026, 10, 3, 2, 0), limit, pricing));
        Assert.Equal(At(2026, 10, 7, 1, 55), NewsLabelling.EndsBy(At(2026, 10, 7, 1, 35), limit, null));
    }

    [Fact]
    public async Task TheCounterKeepsEachMembersArticlesJudgedAsTheyAreStoredAndDropsThemPastThirtyOneDays()
    {
        var session = new DateOnly(2026, 9, 8);
        var feed = RecordedNewsFeed.FromFolder(Folder());
        var byName = NewsAttribution.ByName(await feed.ArticlesAsync(session, session));

        // Two of the symbols the capture names most made members, worked by hand off the capture through the
        // same attribution: the rows expected are their articles, keyed on a hash of each link.
        var chosen = byName.OrderByDescending(pair => pair.Value.Count).ThenBy(pair => pair.Key, StringComparer.Ordinal).Take(2).Select(pair => pair.Key).ToArray();

        using var store = new TemporaryStore().Migrated();

        foreach (var ticker in chosen)
        {
            store.Execute($"INSERT INTO membership (index_code, ticker, joined, \"left\", observed_at) VALUES ('GSPC', '{ticker}', NULL, NULL, '2026-09-05T21:00:00Z');");
        }

        store.Execute(
            "INSERT INTO news_article (ticker, article_id, link, title, source, published_at, text, length, admissibility, session_date) VALUES " +
            $"('{chosen[0]}', 'old', 'https://example.invalid/old', 'old', 'wire', '2026-08-01T12:00:00Z', 'old', 3, 'accepted', '2026-08-01');");

        var outcome = await new NewsPulseCounter(feed, FixedClock.At(FilterEvening, SessionZones.UnitedStates), store.DatabaseFile).RunAsync("GSPC", session, "pulse-articles");

        var expected = chosen.SelectMany(ticker => byName[ticker].Select(article => ticker + "|" + NewsPulseCounter.ArticleId(article.Url))).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();

        Assert.True(expected.Length > 0);
        Assert.Equal(expected, TextRows(store, "SELECT ticker || '|' || article_id FROM news_article ORDER BY ticker || '|' || article_id;"));
        Assert.Equal((expected.Length, 1), (outcome.ArticlesStored, outcome.ArticlesDropped));
        Assert.Equal(0, Scalar(store, "SELECT COUNT(*) FROM news_article WHERE article_id = 'old';"));

        // Each row judged as it was stored, its text cut at the instruction's length and its length as delivered.
        Assert.All(
            TextRows(store, "SELECT admissibility FROM news_article;"),
            verdict => Assert.Contains(verdict, Admissibility.Verdicts));
        Assert.Equal(0, Scalar(store, $"SELECT COUNT(*) FROM news_article WHERE length(text) > {NewsInstruction.TextCharacters} OR length < length(text);"));
        Assert.Contains($"{expected.Length} article(s) stored for members and 1 dropped past 31 days", Text(store, "SELECT detail FROM run_log WHERE run_id = 'pulse-articles' AND stage = 'news-pulse';"), StringComparison.Ordinal);

        // A second night leaves every row as it was, and a fill of the same day stores nothing new under its own row.
        await new NewsPulseCounter(feed, FixedClock.At(FilterEvening, SessionZones.UnitedStates), store.DatabaseFile).RunAsync("GSPC", session, "pulse-again");
        var fill = await new NewsPulseCounter(feed, FixedClock.At(FilterEvening, SessionZones.UnitedStates), store.DatabaseFile).FillAsync("GSPC", session, "news-fill-test");

        Assert.Equal(expected.Length, Scalar(store, "SELECT COUNT(*) FROM news_article;"));
        Assert.Equal(0, fill.ArticlesStored);
        Assert.Equal("ok", Text(store, "SELECT outcome FROM run_log WHERE run_id = 'news-fill-test' AND stage = 'news-fill 2026-09-08';"));
    }
}
