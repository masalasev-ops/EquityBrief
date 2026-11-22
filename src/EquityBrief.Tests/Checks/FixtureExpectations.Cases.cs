using EquityBrief.Core.Loop;
using EquityBrief.Core.Providers;
using EquityBrief.Core.Releases;
using EquityBrief.Core.Time;
using EquityBrief.Tests.Harness;
using EquityBrief.Web.App;
using EquityBrief.Worker.Local;
using EquityBrief.Worker.Loop;

namespace EquityBrief.Tests.Checks;

// fixture-expectations: the case for promotion's stored cases and their words. A case's answer is read into the case for and
// the case against, or refused for a number, a ticker of the members or a company's name, each worked over a constructed
// answer; over a constructed store the case words asks each stored case's figures once under the pin, keeps the answer read,
// asks a refused one once more and keeps the second's cause, asks nothing again on a run again and asks again only where a
// later month stored the figures changed, the earlier words kept; the monthly run's cases step stores a month's case once,
// with the figures the case for promotion reads, and none where no variant meets the rule; and the page draws the stored
// words with their day and model, the same on every read, and code's sentences where none is kept.
// see: The monthly run stores each case standing from Paper trading's accounts in Core, and the local run writes its words
public partial class FixtureExpectations
{
    internal static readonly string[] CaseClaims =
    [
        CheckReach.Key(Scope.FailureTable, "A case's words the local model answers holding a number, a ticker or a company's name"),
    ];

    static readonly HashSet<string> CaseTickers = new(["ABCD", "EFGH"], StringComparer.Ordinal);

    static readonly string[] CaseNames = ["Abcdefg Holdings Inc.", "The Efgh Company"];

    static string CaseAnswer(string given, string against) =>
        System.Text.Json.JsonSerializer.Serialize(new Dictionary<string, string> { ["for"] = given, ["against"] = against });

    [Fact]
    public void ACasesAnswerIsReadIntoItsTwoArgumentsOrRefusedForANumberATickerOrAName()
    {
        var clean = CasePrompt.Read(CaseAnswer("It did better over the same nights.", "Its lead could still be luck."), null, CaseTickers, CaseNames);

        Assert.Equal(("It did better over the same nights.", "Its lead could still be luck.", (string?)null), (clean.For, clean.Against, clean.Cause));

        // Each refusal by the cause it is read for, the words around it the same.
        Assert.Equal(CasePrompt.HoldsDigit, CasePrompt.Read(CaseAnswer("It made 21 cents more.", "Luck."), null, CaseTickers, CaseNames).Cause);
        Assert.Equal(CasePrompt.HoldsTicker, CasePrompt.Read(CaseAnswer("It kept ABCD.", "Luck."), null, CaseTickers, CaseNames).Cause);
        Assert.Equal(CasePrompt.HoldsName, CasePrompt.Read(CaseAnswer("It kept Abcdefg in its picks.", "Luck."), null, CaseTickers, CaseNames).Cause);
        Assert.Equal(CasePrompt.HoldsName, CasePrompt.Read(CaseAnswer("Fine.", "Efgh fell."), null, CaseTickers, CaseNames).Cause);

        // Capitals that are no member's ticker, and a name's word written in a sentence's own lower case, refuse nothing.
        Assert.Null(CasePrompt.Read(CaseAnswer("Its lead held in the S and P index.", "A run of bad weeks would close it."), null, CaseTickers, CaseNames).Cause);
        Assert.Null(CasePrompt.Read(CaseAnswer("It did better.", "The abcdefg of it."), null, CaseTickers, CaseNames).Cause);

        // An answer that is not the object, or lacks one argument, or was cut at the budget.
        Assert.Equal(ReleaseAnswers.NotOneObject, CasePrompt.Read("It did better.", null, CaseTickers, CaseNames).Cause);
        Assert.Equal(CasePrompt.NoWords, CasePrompt.Read("{\"for\": \"It did better.\"}", null, CaseTickers, CaseNames).Cause);
        Assert.Equal(ReleaseAnswers.CutOff, CasePrompt.Read("{", ReleaseAnswers.BudgetReached, CaseTickers, CaseNames).Cause);
        Assert.Equal(ReleaseAnswers.Empty, CasePrompt.Read(" ", null, CaseTickers, CaseNames).Cause);

        // A company's name shorn of the words every name ends in.
        Assert.Equal(("Abcdefg", "Efgh"), (CasePrompt.Shorn(CaseNames[0]), CasePrompt.Shorn(CaseNames[1])));

        // The prompt names no ticker of the case's picks and the schema holds every argument to no digit.
        var prompt = CasePrompt.For(Reading.ReadSurface.ConstructedCase(), "the pullback");

        Assert.DoesNotContain("ABCD", prompt, StringComparison.Ordinal);
        Assert.Contains("Over the same nights, per $1 risked: the variant +0.21 over 54 closed trades; the running revision +0.06 over 61 closed trades", prompt, StringComparison.Ordinal);
        Assert.Equal("^[^0-9]*$", (string?)CasePrompt.Schema()["properties"]!["for"]!["pattern"]);
    }

    [Fact]
    public async Task TheCaseWordsAskEachStoredCasesFiguresOnceKeepTheirAnswerAndAskAgainOnlyWhereTheFiguresChanged()
    {
        using var store = new TemporaryStore().Migrated();
        using var models = new ModelsFolder();
        var settings = ReadingProfile(models.Root);
        var clock = FixedClock.At(new DateTimeOffset(2026, 12, 7, 12, 30, 0, TimeSpan.Zero), SessionZones.UnitedStates);

        await new LocalRun(ReadingFeedOver(AnsweringRuntime(loaded: true, chat: _ => Runtime("local-reading-probe-answered.json")), settings), settings, clock, store.DatabaseFile, store.Root, NoWait).PinAsync(TextWriter.Null);

        var one = Reading.ReadSurface.ConstructedCase();
        var other = one with { Family = "breakout", Variant = "breakout-variant-volume" };

        StoreCase(store, "2026-12", one);
        StoreCase(store, "2026-12", other);
        CaseExecute(store,"INSERT INTO membership (index_code, ticker, joined, observed_at, name) VALUES ('SML', 'ABCD', '2020-01-02', '2026-12-01T00:00:00Z', 'Abcdefg Holdings Inc.');");

        // The pullback's first answer holds a figure and its second is clean; the breakout's both name a member's ticker.
        var times = new Dictionary<string, int>(StringComparer.Ordinal);
        var feed = new IdeaAnsweringFeed(ReadingFeedOver(AnsweringRuntime(loaded: true, chat: _ => throw new InvalidOperationException("the answers are the test's own")), settings), request =>
        {
            var asked = times[request.Text] = times.GetValueOrDefault(request.Text) + 1;

            return request.Text.StartsWith("Setup: the pullback", StringComparison.Ordinal)
                ? Reply(asked == 1 ? CaseAnswer("It beat it by 15 cents.", "Luck.") : CaseAnswer("It did better over the same nights.", "Its lead could still be luck."))
                : Reply(CaseAnswer("It kept ABCD.", "Luck."));
        });
        var said = new StringWriter();
        var outcome = await new CaseWords(feed, settings, clock, store.DatabaseFile, store.Root, NoWait).RunAsync(said);

        Assert.Equal((CaseWords.Kind, LocalRun.Finished, 1), (outcome.Kind, outcome.Outcome, outcome.Read));
        Assert.Equal(1, outcome.Refused[CasePrompt.HoldsTicker]);
        Assert.Equal(4, feed.Requests);
        Assert.All(feed.Asked, request => Assert.Equal((Gemma, CasePrompt.Instruction, CasePrompt.AnswerTokens), (request.Model, request.Instruction, request.AnswerTokens)));
        Assert.All(feed.Asked, request => Assert.NotNull(request.Schema));
        Assert.Contains("case-words: case words came to finished; 1 case(s) answered and 1 refused", said.ToString(), StringComparison.Ordinal);
        Assert.Equal(
            [
                "breakout|refused|" + CasePrompt.HoldsTicker + "||",
                "pullback|answered||It did better over the same nights.|Its lead could still be luck.",
            ],
            Query(store, "SELECT family || '|' || outcome || '|' || COALESCE(cause, '') || '|' || COALESCE(for_words, '') || '|' || COALESCE(against_words, '') FROM promotion_case_words ORDER BY family;"));
        Assert.Equal([CasePrompt.Version + "|" + Gemma], Query(store, "SELECT DISTINCT version || '|' || model FROM promotion_case_words;"));

        // A run again asks nothing, the refused case's figures among those kept.
        var again = new IdeaAnsweringFeed(ReadingFeedOver(AnsweringRuntime(loaded: true, chat: _ => throw new InvalidOperationException("nothing is asked")), settings), _ => throw new InvalidOperationException("nothing is asked"));

        Assert.Equal(LocalRun.NothingToRead, (await new CaseWords(again, settings, clock, store.DatabaseFile, store.Root, NoWait).RunAsync(TextWriter.Null)).Outcome);
        Assert.Equal(0, again.Requests);

        // A later month storing the pullback's figures changed asks for its words again and keeps the earlier; the breakout's
        // figures the same ask nothing.
        StoreCase(store, "2027-01", one with { Same = one.Same with { Closed = 60 } });
        StoreCase(store, "2027-01", other);

        var later = new IdeaAnsweringFeed(ReadingFeedOver(AnsweringRuntime(loaded: true, chat: _ => throw new InvalidOperationException("the answers are the test's own")), settings), _ => Reply(CaseAnswer("It stayed ahead.", "It could still be luck.")));
        var third = await new CaseWords(later, settings, clock, store.DatabaseFile, store.Root, NoWait).RunAsync(TextWriter.Null);

        Assert.Equal((LocalRun.Finished, 1, 1), (third.Outcome, third.Read, later.Requests));
        Assert.Equal(2L, Scalar(store, "SELECT COUNT(*) FROM promotion_case_words WHERE family = 'pullback';"));
    }

    [Fact]
    public async Task TheMonthlyRunsCasesStepStoresAMonthsCaseOnceAndNoneWhereNoVariantMeetsTheRule()
    {
        using var store = await Reading.ReadSurface.PaperVariantsStore();
        var clock = FixedClock.At(new DateTimeOffset(2026, 12, 5, 13, 0, 0, TimeSpan.Zero), SessionZones.UnitedStates);
        var said = new StringWriter();

        // Over a store whose variants close too few trades to meet the rule, the step stores none and says so for each index.
        var (held, words) = await new PromotionCaseStore(clock, store.DatabaseFile, store.Root).StoreAsync("2026-12", "loop-month-2026-12", said);

        Assert.True(held);
        Assert.Contains("0 case(s) standing", words, StringComparison.Ordinal);
        Assert.Contains("0 stored for 2026-12", words, StringComparison.Ordinal);
        Assert.Equal(0L, Scalar(store, "SELECT COUNT(*) FROM promotion_case;"));
        Assert.Equal(["promotion-cases|ok|0"], Query(store, "SELECT stage || '|' || outcome || '|' || rows_written FROM run_log WHERE run_id = 'loop-month-2026-12-cases';"));

        // A case's figures stored are the object the case for promotion reads, read back whole, and their key its hash.
        var one = Reading.ReadSurface.ConstructedCase();
        var figures = PromotionCases.Figures(one);
        var back = PromotionCases.FromFigures(figures)!;

        Assert.Equal((one.Same, one.RunningSame, one.Long, one.Conditions, one.Variant), (back.Same, back.RunningSame, back.Long, back.Conditions, back.Variant));
        Assert.Equal(one.Changes, back.Changes);
        Assert.Equal(PromotionCases.FiguresKey(figures), PromotionCases.FiguresKey(PromotionCases.Figures(back)));
        Assert.NotEqual(PromotionCases.FiguresKey(figures), PromotionCases.FiguresKey(PromotionCases.Figures(one with { Same = one.Same with { Closed = 55 } })));

        // Only a variant meeting the rule is a case standing.
        var short22 = one with { Conditions = one.Conditions with { Enough = false } };
        PromotionRowView[] rows = [new("SML", "S&P 600", "pullback", false, 7, new DateOnly(2026, 11, 16), one.RunningSame, [one, short22 with { Variant = "short" }])];

        Assert.Equal([one.Variant], PromotionCases.Standing(rows).Select(standing => standing.Variant));
    }

    [Fact]
    public async Task TheCasePageDrawsTheStoredWordsWithTheirDayAndModelTheSameOnEveryReadAndCodesSentencesWhereNoneIsKept()
    {
        using var store = new TemporaryStore().Migrated();
        var one = Reading.ReadSurface.ConstructedCase();

        CaseExecute(store,$"INSERT INTO promotion_case_words (index_code, family, variant, figures_key, pin, version, outcome, for_words, against_words, model, asked_at, run_id) VALUES ('SML', 'pullback', '{one.Variant}', 'k1', 1, 'v', 'answered', 'It held its lead.', 'It may be luck.', '{Gemma}', '2026-12-07T12:31:00Z', 'r1');");
        CaseExecute(store,$"INSERT INTO promotion_case_words (index_code, family, variant, figures_key, pin, version, outcome, for_words, against_words, model, asked_at, run_id) VALUES ('SML', 'pullback', '{one.Variant}', 'k2', 1, 'v', 'answered', 'It stayed ahead.', 'A bad run would close it.', '{Gemma}', '2027-01-04T12:31:00Z', 'r2');");
        CaseExecute(store,$"INSERT INTO promotion_case_words (index_code, family, variant, figures_key, pin, version, outcome, cause, model, asked_at, run_id) VALUES ('SML', 'breakout', 'other', 'k3', 1, 'v', 'refused', '{CasePrompt.HoldsTicker}', '{Gemma}', '2027-01-04T12:31:00Z', 'r2');");

        var read = new EquityBrief.Api.Reading.ReadApi(store.DatabaseFile, FixedClock.At(new DateTimeOffset(2027, 1, 5, 12, 0, 0, TimeSpan.Zero), SessionZones.UnitedStates));
        var words = await read.CaseWordsAsync();
        var refused = one with { Family = "breakout", Variant = "other" };
        PromotionRowView[] rows = [new("SML", "S&P 600", "pullback", false, 7, new DateOnly(2026, 11, 16), one.RunningSame, [one, refused])];
        string Page() => new SinglePageApp().CaseRegion(new PromotionPageView(new DateOnly(2027, 1, 4), null, rows, 1214, 3, words));

        var page = Page();

        // The newest answer's words with their day and model, the counted part above the case against's words, and the earlier
        // answer kept below as its history.
        Assert.Contains($"<p class=\"ai-quote\" data-words=\"model\">It stayed ahead.<small>Written on 4 Jan 2027 by {Gemma}</small></p>", page, StringComparison.Ordinal);
        Assert.Contains($"<p class=\"ai-quote\" data-words=\"model\">A bad run would close it.<small>Written on 4 Jan 2027 by {Gemma}</small></p>", page, StringComparison.Ordinal);
        Assert.True(page.IndexOf("Counted by code:", StringComparison.Ordinal) < page.IndexOf("A bad run would close it.", StringComparison.Ordinal));
        Assert.Contains($"<li>Written on 7 Dec 2026 by {Gemma}: for, It held its lead. Against, It may be luck.</li>", page, StringComparison.Ordinal);

        // The refused case draws code's sentences, and two reads draw the same page.
        var decision = page.IndexOf("data-loop-key=\"case|SML|breakout\"", StringComparison.Ordinal);
        var breakout = page[page.LastIndexOf("<div class=\"lm-case\">", decision, StringComparison.Ordinal)..decision];

        Assert.Contains($"data-words=\"code\">{SinglePageApp.CodeFor}", breakout, StringComparison.Ordinal);
        Assert.DoesNotContain("data-words=\"model\"", breakout, StringComparison.Ordinal);
        Assert.Equal(page, Page());
        Assert.Equal(words.Keys.Order(StringComparer.Ordinal), [SinglePageApp.CaseKey("SML", "pullback", one.Variant)]);
    }

    static void CaseExecute(TemporaryStore store, string sql)
    {
        using var connection = store.Open();
        using var command = connection.CreateCommand();

        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    static void StoreCase(TemporaryStore store, string month, PromotionCaseView one)
    {
        var figures = PromotionCases.Figures(one);

        using var connection = store.Open();
        using var command = connection.CreateCommand();

        command.CommandText = "INSERT INTO promotion_case (month, index_code, family, variant, night, figures, figures_key, stored_at, run_id) VALUES ($month, $index, $family, $variant, '2026-12-04', $figures, $key, '2026-12-05T13:00:00Z', 'loop-month');";
        command.Parameters.AddWithValue("$month", month);
        command.Parameters.AddWithValue("$index", one.Index);
        command.Parameters.AddWithValue("$family", one.Family);
        command.Parameters.AddWithValue("$variant", one.Variant);
        command.Parameters.AddWithValue("$figures", figures);
        command.Parameters.AddWithValue("$key", PromotionCases.FiguresKey(figures));
        command.ExecuteNonQuery();
    }
}
