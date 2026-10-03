using System.Globalization;
using System.Text.Json;
using EquityBrief.Core.News;
using EquityBrief.Core.Providers;
using EquityBrief.Core.Research;
using EquityBrief.Core.Time;
using EquityBrief.Worker.Filter;
using EquityBrief.Worker.News;
using EquityBrief.Worker.Research;

namespace EquityBrief.Tests.Checks;

// fixture-expectations, 12.6: the news labeller. The instruction's reading of an answer worked by hand; the
// labeller over a constructed store labelling tonight's list in its order, asking once more and keeping an
// unreadable answer with its cause, sending no article admissibility refused or outside the window, never
// overwriting a label and writing rows of its own after a switch; its stops before the call that would pass
// the month limit, at the time limit and inside a peak window, a retry judged by each as a first call is; and
// the counter keeping each member's articles judged as they are stored and dropping them past thirty-one days.
// see: The news labeller is a process of its own the night starts after the close, and its calls and its spend are its own
// see: A label's reason is one sentence holding no digit, and an answer code cannot read is asked once more and then kept as unreadable with its cause
// see: A news article is stored once per member with its admissibility judged, and a label is never overwritten
public partial class FixtureExpectations
{
    const string Good = "{\"kind\":\"results\",\"direction\":\"positive\",\"reason\":\"Quarterly sales came in ahead of what the company had guided.\"}";

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

        // Each cause, by the first check it fails.
        Assert.Equal(NewsInstruction.NotJson, NewsInstruction.Read("positive, because sales rose").Cause);
        Assert.Equal(NewsInstruction.KindOutsideTheSet, NewsInstruction.Read(LabelAnswer("rumour", "positive", "A rival said so.")).Cause);
        Assert.Equal(NewsInstruction.DirectionOutsideTheSet, NewsInstruction.Read(LabelAnswer("results", "mixed", "Sales rose and margins fell.")).Cause);
        Assert.Equal(NewsInstruction.DigitInTheReason, NewsInstruction.Read(LabelAnswer("results", "positive", "Sales rose by 5 percent on the year.")).Cause);
        Assert.Equal(NewsInstruction.NotOneSentence, NewsInstruction.Read(LabelAnswer("results", "positive", "Sales rose. Margins widened too.")).Cause);
        Assert.Equal(NewsInstruction.NotOneSentence, NewsInstruction.Read(LabelAnswer("results", "positive", "")).Cause);

        // The nine kinds and the three directions are the brief's words, and the seven causes are correction 2's.
        Assert.Equal(9, NewsInstruction.Kinds.Length);
        Assert.Equal(["positive", "negative", "neutral"], NewsInstruction.Directions);
        Assert.Equal(7, NewsInstruction.Causes.Length);
        Assert.Equal(1, NewsInstruction.Version);
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

    static NewsLabeller Labeller(TemporaryStore store, IResearchModelFeed model, ResearchModelSettings? settings = null, NewsLimits? limits = null, IClock? clock = null)
    {
        var at = clock ?? FixedClock.At(new DateTimeOffset(2026, 9, 8, 23, 0, 0, TimeSpan.Zero), SessionZones.UnitedStates);

        return new NewsLabeller(
            new SpendCap(model, Core.Spending.SpendCaps.Default, at, store.DatabaseFile),
            settings ?? Providers.ResearchModelFeedTests.Pinned(),
            limits ?? NewsLimits.Default,
            at,
            store.DatabaseFile);
    }

    static JsonElement LabellerRow(TemporaryStore store, string runId) =>
        JsonDocument.Parse(Text(store, $"SELECT detail FROM run_log WHERE run_id = '{runId}' AND stage = '{NewsLabeller.Stage}';")).RootElement;

    [Fact]
    public async Task TheLabellerLabelsTonightsListInItsOrderAsksOnceMoreKeepsAnUnreadableAnswerWithItsCauseAndNeverOverwrites()
    {
        using var store = await NewsStore();

        // Worked by hand: ZZB's two admitted articles of the window newest first, then ZZC's one; b3 is refused by
        // admissibility and b4 is outside the window, so neither is sent, and ZZD is not on the list. b1 is read
        // first time, b2 after one retry, and c1 fails twice on a kind outside the set.
        var model = new ScriptedModel(
            Good,
            "no json here",
            LabelAnswer("guidance", "negative", "The company lowered what it expects for the year."),
            LabelAnswer("rumour", "positive", "A rival said so."),
            LabelAnswer("rumour", "positive", "A rival said so again."));

        var outcome = await Labeller(store, model).RunAsync(new DateOnly(2026, 9, 8), "label-news-one");

        Assert.Equal((3, 3, 2, 1, 1, 0, NewsLabeller.Finished), (outcome.Names, outcome.NamesReached, outcome.Labelled, outcome.UnreadableCount, outcome.RefusedByAdmissibility, outcome.AlreadyLabelled, outcome.Stop));
        Assert.Equal(5, model.Requests);
        Assert.Equal(["b1", "b2", "b2", "c1", "c1"], model.Asked.Select(request => request.DocumentIds.Single()));
        Assert.All(model.Asked, request => Assert.Equal((NewsInstruction.Lane, NewsInstruction.Section, NewsInstruction.System), (request.Lane, request.Section, request.System)));
        Assert.Contains("Company: ZZB (ZZB)", model.Asked[0].Prompt, StringComparison.Ordinal);
        Assert.Contains("Title: ZZB beats", model.Asked[0].Prompt, StringComparison.Ordinal);

        var profile = Providers.ResearchModelFeedTests.PinnedProfile;

        Assert.Equal(
            ["b1|labelled|results|positive", "b2|labelled|guidance|negative", "c1|unreadable||"],
            TextRows(store, $"SELECT article_id || '|' || outcome || '|' || COALESCE(kind, '') || '|' || COALESCE(direction, '') FROM news_label WHERE profile = '{profile}' ORDER BY ticker, article_id;"));
        Assert.Equal(NewsInstruction.KindOutsideTheSet, Text(store, "SELECT cause FROM news_label WHERE article_id = 'c1';"));
        Assert.Equal(1, Scalar(store, "SELECT instruction_version FROM news_label WHERE article_id = 'b1';"));
        Assert.Equal("label-news-one", Text(store, "SELECT run_id FROM news_label WHERE article_id = 'b1';"));

        // Each call is a row of its own under the run, and the run's own row counts the night.
        Assert.Equal(5, Scalar(store, "SELECT COUNT(*) FROM run_log WHERE run_id = 'label-news-one' AND stage LIKE 'research call: news label%';"));

        var row = LabellerRow(store, "label-news-one");

        Assert.Equal((3, 3, 2, 1, 1, NewsLabeller.Finished), (row.GetProperty("names").GetInt32(), row.GetProperty("reached").GetInt32(), row.GetProperty("labelled").GetInt32(), row.GetProperty("unreadable").GetProperty(NewsInstruction.KindOutsideTheSet).GetInt32(), row.GetProperty("refusedByAdmissibility").GetInt32(), row.GetProperty("stop").GetString()));
        Assert.Equal(5, Scalar(store, "SELECT model_calls FROM run_log WHERE run_id = 'label-news-one' AND stage = 'news-labels';"));

        // Run again under the same profile, nothing is sent: every article of the window is labelled or
        // unreadable under it, and the unreadable one is not paid for again.
        var again = new ScriptedModel(Good, Good, Good);
        var second = await Labeller(store, again).RunAsync(new DateOnly(2026, 9, 8), "label-news-two");

        Assert.Equal((0, 0, 3), (again.Requests, second.Labelled, second.AlreadyLabelled));

        // After a switch the new profile writes rows of its own and the first profile's rows stand as written.
        var switched = new ScriptedModel(LabelAnswer("results", "neutral", "The quarter was as expected."), LabelAnswer("guidance", "neutral", "The outlook was kept."), LabelAnswer("management", "neutral", "A chief was named."));
        var third = await Labeller(store, switched, Providers.ResearchModelFeedTests.PinnedClaude()).RunAsync(new DateOnly(2026, 9, 8), "label-news-three");

        Assert.Equal((3, 3, 0), (switched.Requests, third.Labelled, third.UnreadableCount));
        Assert.Equal(3, Scalar(store, $"SELECT COUNT(*) FROM news_label WHERE profile = '{Providers.ResearchModelFeedTests.ClaudeProfile}';"));
        Assert.Equal("Quarterly sales came in ahead of what the company had guided.", Text(store, $"SELECT reason FROM news_label WHERE article_id = 'b1' AND profile = '{profile}';"));
        Assert.Equal("The quarter was as expected.", Text(store, $"SELECT reason FROM news_label WHERE article_id = 'b1' AND profile = '{Providers.ResearchModelFeedTests.ClaudeProfile}';"));
        Assert.Equal(6, Scalar(store, "SELECT COUNT(*) FROM news_label;"));
    }

    [Fact]
    public async Task TheLabellerStopsBeforeTheCallThatWouldPassItsMonthLimitAtItsTimeLimitAndInsideAPeakWindow()
    {
        using var store = await NewsStore();

        // The month's limit already reached by the labeller's own earlier runs: nothing is asked.
        store.Execute("INSERT INTO run_log (run_id, stage, started_at, ended_at, outcome, rows_written, model_calls, network_requests, spend, detail) VALUES ('label-news-earlier', 'research call: news label, ZZB x', '2026-09-02T23:00:00Z', '2026-09-02T23:00:01Z', 'ok', 0, 1, 1, '5', '{}');");

        var reached = new ScriptedModel(Good, Good, Good);
        var atTheLimit = await Labeller(store, reached).RunAsync(new DateOnly(2026, 9, 8), "label-news-limit");

        Assert.Equal((0, NewsLabeller.MonthLimitReached, 5m), (reached.Requests, atTheLimit.Stop, atTheLimit.MonthCost));

        store.Execute("DELETE FROM run_log WHERE run_id = 'label-news-earlier';");

        // A limit the first call's ceiling would pass: nothing is asked, and the stop names the limit.
        var tiny = new ScriptedModel(Good, Good, Good);
        var wouldPass = await Labeller(store, tiny, limits: new NewsLimits(0.0000001m, TimeSpan.FromMinutes(20))).RunAsync(new DateOnly(2026, 9, 8), "label-news-tiny");

        Assert.Equal((0, NewsLabeller.MonthLimitReached, 0), (tiny.Requests, wouldPass.Stop, wouldPass.Labelled));
        Assert.Equal(NewsLabeller.MonthLimitReached, LabellerRow(store, "label-news-tiny").GetProperty("stop").GetString());

        // The time limit, passed before the first article: every name is left for the next night.
        var late = new ScriptedModel(Good, Good, Good);
        var outOfTime = await Labeller(store, late, limits: new NewsLimits(5m, TimeSpan.Zero)).RunAsync(new DateOnly(2026, 9, 8), "label-news-time");

        Assert.Equal((0, NewsLabeller.TimeLimitPassed, 1), (late.Requests, outOfTime.Stop, outOfTime.NamesReached));

        // A peak window of the profile open, the recorded profile's being weekdays from 01:00 to 04:00 UTC.
        var peak = new ScriptedModel(Good, Good, Good);
        var inPeak = await Labeller(store, peak, clock: FixedClock.At(new DateTimeOffset(2026, 9, 8, 2, 0, 0, TimeSpan.Zero), SessionZones.UnitedStates)).RunAsync(new DateOnly(2026, 9, 8), "label-news-peak");

        Assert.Equal((0, NewsLabeller.PeakWindowOpened), (peak.Requests, inPeak.Stop));
        Assert.Equal(0, Scalar(store, "SELECT COUNT(*) FROM news_label;"));
    }

    // A model priced at its own ceiling, every call the same, that answers each call with the next of its texts and
    // moves the clock by a step as it answers, so the spend so far and the instant a retry is judged at are the
    // test's own.
    sealed class PricedModel(CallClock clock, TimeSpan step, decimal each, params string[] texts) : IResearchModelFeed
    {
        readonly ResearchModelSettings settings = Providers.ResearchModelFeedTests.Pinned();

        public int Requests { get; private set; }

        public int Probes => 0;

        public string Identity => settings.Identity;

        public Task<string?> UnreachableAsync(CancellationToken cancellation = default) => Task.FromResult<string?>(null);

        public Task<ResearchAnswer> CompleteAsync(ModelRequest request, CancellationToken cancellation = default)
        {
            var text = texts[Requests++];

            clock.Advance(step);

            return Task.FromResult(new ResearchAnswer(settings.Model, text, 100, 0, 100, 50, 0, "stop", clock.UtcNow));
        }

        public decimal Price(ResearchAnswer answer) => each;

        public decimal Ceiling(ModelRequest request) => each;
    }

    static readonly DateTimeOffset RetryEvening = new(2026, 9, 8, 23, 0, 0, TimeSpan.Zero);

    // The news store's list labelled from an instant, each call costing a cent and moving the clock by a step, the
    // first article's first answer unreadable and every answer after it a label.
    static async Task<(NewsLabelOutcome Outcome, int Calls)> LabelledWithARetryAsync(TemporaryStore store, DateTimeOffset start, TimeSpan step, NewsLimits limits, string runId)
    {
        var clock = new CallClock(start);
        var model = new PricedModel(clock, step, 0.01m, "no json here", Good, Good, Good, Good);
        var outcome = await Labeller(store, model, limits: limits, clock: clock).RunAsync(new DateOnly(2026, 9, 8), runId);

        return (outcome, model.Requests);
    }

    [Fact]
    public async Task ARetryThatWouldPassTheMonthLimitIsNotAskedAndItsArticleIsLeftUnlabelled()
    {
        // Worked by hand, a call and its ceiling a cent each. At a limit of a cent and a half the first call is asked,
        // nothing having been spent, and its retry is not, a cent spent and a cent more passing it: one call, the
        // month at a cent, and no row for the article.
        using (var store = await NewsStore())
        {
            var (outcome, calls) = await LabelledWithARetryAsync(store, RetryEvening, TimeSpan.Zero, new NewsLimits(0.015m, TimeSpan.FromMinutes(20)), "label-news-retry-month");

            Assert.Equal((1, NewsLabeller.MonthLimitReached, 0.01m, 0, 0), (calls, outcome.Stop, outcome.MonthCost, outcome.Labelled, outcome.UnreadableCount));
            Assert.Equal(0, Scalar(store, "SELECT COUNT(*) FROM news_label;"));
            Assert.Equal(NewsLabeller.MonthLimitReached, LabellerRow(store, "label-news-retry-month").GetProperty("stop").GetString());
        }

        // At two cents the retry reaches the limit and does not pass it, so it is asked and labels the article, and
        // the next article's first call is the one refused.
        using (var store = await NewsStore())
        {
            var (outcome, calls) = await LabelledWithARetryAsync(store, RetryEvening, TimeSpan.Zero, new NewsLimits(0.02m, TimeSpan.FromMinutes(20)), "label-news-retry-month-reached");

            Assert.Equal((2, NewsLabeller.MonthLimitReached, 0.02m, 1), (calls, outcome.Stop, outcome.MonthCost, outcome.Labelled));
            Assert.Equal(["b1"], TextRows(store, "SELECT article_id FROM news_label;"));
        }
    }

    [Fact]
    public async Task ARetryAtTheTimeLimitIsNotAskedAndItsArticleIsLeftUnlabelled()
    {
        // Worked by hand over a limit of twenty minutes: a first answer taking twenty minutes cannot be read, and the
        // retry, judged as the limit is reached, is not asked.
        using (var store = await NewsStore())
        {
            var (outcome, calls) = await LabelledWithARetryAsync(store, RetryEvening, TimeSpan.FromMinutes(20), new NewsLimits(5m, TimeSpan.FromMinutes(20)), "label-news-retry-time");

            Assert.Equal((1, NewsLabeller.TimeLimitPassed, 0, 0), (calls, outcome.Stop, outcome.Labelled, outcome.UnreadableCount));
            Assert.Equal(0, Scalar(store, "SELECT COUNT(*) FROM news_label;"));
            Assert.Equal(NewsLabeller.TimeLimitPassed, LabellerRow(store, "label-news-retry-time").GetProperty("stop").GetString());
        }

        // One taking a second less leaves the retry its second: it is asked and labels the article, and the next
        // article is the one the limit stops.
        using (var store = await NewsStore())
        {
            var (outcome, calls) = await LabelledWithARetryAsync(store, RetryEvening, TimeSpan.FromMinutes(20) - TimeSpan.FromSeconds(1), new NewsLimits(5m, TimeSpan.FromMinutes(20)), "label-news-retry-time-inside");

            Assert.Equal((2, NewsLabeller.TimeLimitPassed, 1), (calls, outcome.Stop, outcome.Labelled));
            Assert.Equal(["b1"], TextRows(store, "SELECT article_id FROM news_label;"));
        }
    }

    [Fact]
    public async Task ARetryInsideAPeakWindowIsNotAskedAndItsArticleIsLeftUnlabelled()
    {
        // Worked by hand: the recorded profile's peak window opens at 01:00 UTC on a weekday. A run starting at 00:50
        // on Wednesday 2026-09-09 asks its first call off peak, the answer arrives at 01:00 and cannot be read, and the
        // retry, judged inside the window, is not asked.
        var tenToOne = new DateTimeOffset(2026, 9, 9, 0, 50, 0, TimeSpan.Zero);

        using (var store = await NewsStore())
        {
            var (outcome, calls) = await LabelledWithARetryAsync(store, tenToOne, TimeSpan.FromMinutes(10), NewsLimits.Default, "label-news-retry-peak");

            Assert.Equal((1, NewsLabeller.PeakWindowOpened, 0, 0), (calls, outcome.Stop, outcome.Labelled, outcome.UnreadableCount));
            Assert.Equal(0, Scalar(store, "SELECT COUNT(*) FROM news_label;"));
            Assert.Equal(NewsLabeller.PeakWindowOpened, LabellerRow(store, "label-news-retry-peak").GetProperty("stop").GetString());
        }

        // An answer arriving a second before the window leaves the retry off peak: it is asked and labels the article,
        // and the next article's first call, inside the window, is the one refused.
        using (var store = await NewsStore())
        {
            var (outcome, calls) = await LabelledWithARetryAsync(store, tenToOne, TimeSpan.FromMinutes(10) - TimeSpan.FromSeconds(1), NewsLimits.Default, "label-news-retry-peak-before");

            Assert.Equal((2, NewsLabeller.PeakWindowOpened, 1), (calls, outcome.Stop, outcome.Labelled));
            Assert.Equal(["b1"], TextRows(store, "SELECT article_id FROM news_label;"));
        }
    }

    // A model that answers once and then nothing: the labeller stops where it is naming the model, the label
    // before it kept and nothing asked again. A run refused before it asked writes its own row, and nothing of
    // the night's moves either way.
    [Fact]
    public async Task AModelThatStopsAnsweringStopsTheLabellerWhereItIsAndARefusedRunWritesItsOwnRowAndNoneOfTheNights()
    {
        using var store = await NewsStore();

        var nightRows = Scalar(store, "SELECT COUNT(*) FROM run_log;");
        var gateRows = Scalar(store, "SELECT COUNT(*) FROM gate_result;");
        var listingRows = Scalar(store, "SELECT COUNT(*) FROM listing;");

        var model = new AnsweringOnce(Good);
        var outcome = await Labeller(store, model).RunAsync(new DateOnly(2026, 9, 8), "label-news-gone");

        Assert.Equal((2, 1, 1, NewsLabeller.ModelUnreachable), (model.Requests, outcome.Labelled, outcome.NamesReached, outcome.Stop));
        Assert.Equal(["b1"], TextRows(store, "SELECT article_id FROM news_label ORDER BY article_id;"));
        Assert.Equal(NewsLabeller.ModelUnreachable, LabellerRow(store, "label-news-gone").GetProperty("stop").GetString());

        await NewsLabeller.RefusedAsync(store.DatabaseFile, "label-news-refused", new DateTimeOffset(2026, 9, 8, 23, 0, 0, TimeSpan.Zero), "the news job's model, claude-haiku, did not answer: no route to the host");

        Assert.Equal("refused", Text(store, "SELECT outcome FROM run_log WHERE run_id = 'label-news-refused';"));
        Assert.Contains("did not answer", Text(store, "SELECT detail FROM run_log WHERE run_id = 'label-news-refused';"), StringComparison.Ordinal);

        Assert.Equal(
            (nightRows, gateRows, listingRows),
            (Scalar(store, "SELECT COUNT(*) FROM run_log WHERE run_id NOT LIKE 'label-news-%';"), Scalar(store, "SELECT COUNT(*) FROM gate_result;"), Scalar(store, "SELECT COUNT(*) FROM listing;")));
    }

    [Fact]
    public async Task ACapTheFirstCallWouldPassMakesNoCallAndTheLabellerNamesTheCapAsItsStop()
    {
        using var store = await NewsStore();

        var model = new ScriptedModel(Good, Good, Good);
        var at = FixedClock.At(new DateTimeOffset(2026, 9, 8, 23, 0, 0, TimeSpan.Zero), SessionZones.UnitedStates);
        var tiny = new SpendCap(model, new Core.Spending.SpendCaps(0.000001m, 0.000001m), at, store.DatabaseFile);

        var outcome = await new NewsLabeller(tiny, Providers.ResearchModelFeedTests.Pinned(), NewsLimits.Default, at, store.DatabaseFile).RunAsync(new DateOnly(2026, 9, 8), "label-news-cap");

        Assert.Equal((0, 0, NewsLabeller.CapPaused), (model.Requests, outcome.Labelled, outcome.Stop));
        Assert.Equal(0, Scalar(store, "SELECT COUNT(*) FROM news_label;"));
        Assert.Equal((NewsLabeller.CapPaused, 0), (LabellerRow(store, "label-news-cap").GetProperty("stop").GetString(), Scalar(store, "SELECT model_calls FROM run_log WHERE run_id = 'label-news-cap' AND stage = 'news-labels';")));
    }

    // A feed that answers its first request and is gone for every one after.
    sealed class AnsweringOnce(string first) : IResearchModelFeed
    {
        readonly ResearchModelSettings settings = Providers.ResearchModelFeedTests.Pinned();

        public int Requests { get; private set; }

        public int Probes => 0;

        public string Identity => settings.Identity;

        public Task<string?> UnreachableAsync(CancellationToken cancellation = default) => Task.FromResult<string?>(null);

        public Task<ResearchAnswer> CompleteAsync(ModelRequest request, CancellationToken cancellation = default)
        {
            Requests++;

            if (Requests > 1)
            {
                throw new ResearchModelUnavailable("the model went away after its first answer");
            }

            return Task.FromResult(new ResearchAnswer(settings.Model, first, 100, 0, 100, 50, 0, "stop", new DateTimeOffset(2026, 9, 8, 23, 0, 30, TimeSpan.Zero)));
        }

        public decimal Price(ResearchAnswer answer) => settings.Pricing.Price(answer);

        public decimal Ceiling(ModelRequest request) => settings.Pricing.Ceiling(request, settings.AnswerTokens);
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
