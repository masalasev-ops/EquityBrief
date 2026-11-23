using System.Text.RegularExpressions;
using EquityBrief.Core.Loop;
using EquityBrief.Core.Providers;
using EquityBrief.Core.Releases;
using EquityBrief.Core.Time;
using EquityBrief.Tests.Harness;
using EquityBrief.Worker.Local;
using EquityBrief.Worker.Loop;
using Microsoft.Data.Sqlite;

namespace EquityBrief.Tests.Checks;

// fixture-expectations: a failure list worked by hand over constructed picks; the researcher's prompt holding no ticker, no
// date and no figure of the later years over a constructed store, the later years one word; an answer outside the grammar
// refused with its cause and one inside it read into the line code writes, by hand; the week's lines chosen by hand, those
// that waited first, the strongest of every setup before any second, at most ten and the rest waiting, a repeat of an idea
// the notebook holds passed over as held; and a week the model answers nothing asked once more whole and counted as failed.
// see: The researcher reads each rule's failure list over the early years and the nights since the restart, and the later years as one word
public partial class FixtureExpectations
{
    internal static readonly string[] ResearchClaims =
    [
        CheckReach.Key(Scope.LimitsTable, "The researcher's week"),
        CheckReach.Key(Scope.FailureTable, "A week the researcher's model answers nothing"),
    ];

    internal static string[] ResearchRows =>
        [.. ComponentAccess.ResearcherRows, .. SchemaColumns.ResearchStoreRows, .. ResearchClaims, .. Reading.ReadSurface.WeeklyResearchRows];

    static IReadOnlyDictionary<string, double> Held(params (string Reading, double Value)[] values) =>
        values.ToDictionary(one => one.Reading, one => one.Value, StringComparer.Ordinal);

    [Fact]
    public void AFailureListIsWorkedByHandOverConstructedPicks()
    {
        double[] Tens(double from) => [.. Enumerable.Range(1, 9).Select(at => from * at)];

        var cuts = new Dictionary<string, double[]>(StringComparer.Ordinal)
        {
            ["rsi"] = Tens(10),
            ["strength"] = Tens(10),
            ["volume_ratio"] = Tens(1),
            ["depth"] = Tens(1),
        };

        // A hundred picks, the losers 0 to 29 and 40 to 59. rsi stands in its first tenth for picks 0 to 39; strength in its
        // tenth tenth for 0 to 26 and 60 to 62; volume_ratio in its tenth tenth for every pick; depth in its first for 0 to 19.
        var picks = Enumerable.Range(0, 100).Select(at => new FailurePick(
            at < 30 || at is >= 40 and < 60 ? -0.5 : 0.5,
            Held(
                ("rsi", at < 40 ? 5 : 50),
                ("strength", at < 27 || at is >= 60 and < 63 ? 95 : 50),
                ("volume_ratio", 9.5),
                ("depth", at < 20 ? 0.5 : 5)))).ToArray();

        var list = FailureList.Of(picks, cuts);

        // By hand: strength's highest three tenths hold 30 picks, 27 of the 50 losers, 54% of the losers against 30% of the
        // picks; rsi's lowest hold 40, 30 losers, 60% against 40%. volume_ratio's highest hold every pick and every loser alike,
        // and depth's lowest hold 20, fewer than the least.
        Assert.Equal([new FailureRow("strength", true, 100, 50, 30, 27), new FailureRow("rsi", false, 100, 50, 40, 30)], list);
        Assert.Equal(0.24, list[0].Gap, 10);
        Assert.Equal(0.2, list[1].Gap, 10);
        Assert.Equal("its highest three tenths", list[0].Band);
        Assert.Empty(FailureList.Of([.. picks.Select(pick => pick with { Edge = 1 })], cuts));
    }

    const string Answered1 =
        """{"has_idea": true, "condition_1_reading": "rsi", "condition_1_band": "tenths 4 to 10", "condition_2_reading": "none", "condition_2_band": "tenths 1 to 2", "condition_3_reading": "none", "condition_3_band": "tenths 1 to 2", "buy": "next open", "window": "5", "stop": "1.5", "take_profit": "whole", "multiple": "2", "order": "strength", "order_highest": true, "reason": "Its losers gather where the momentum is weakest."}""";

    [Fact]
    public void AnAnswerOutsideTheGrammarIsRefusedAndOneInsideItIsReadIntoTheLineCodeWrites()
    {
        // By hand: the pullback on the S&P 600 keeping rsi's fourth tenth and up, sold after its own 63 sessions and 5 a night.
        var read = ResearchPrompt.Read(Answered1, "stop", "pullback", "SML");
        var idea = read.Idea!;
        const string Line = "event pullback(600) · if rsi above tenth 3 · buy next open · stop 1.5 typical days · take profit whole at 2 risks · sell after 63 sessions · cap 5 a night by strength highest";

        Assert.Equal(Line, idea.Line.Canonical);
        Assert.Equal("Its losers gather where the momentum is weakest.", idea.Reason);
        Assert.Equal((null, true, null), (ResearchPrompt.Read("""{"has_idea": false}""", "stop", "dip", "GSPC").Idea, ResearchPrompt.Read("""{"has_idea": false}""", "stop", "dip", "GSPC").None, ResearchPrompt.Read("""{"has_idea": false}""", "stop", "dip", "GSPC").Cause));

        string Changed(string from, string to) => Answered1.Replace(from, to, StringComparison.Ordinal);

        // A second condition on a reading already named is passed over, the line the same; a third on another reading joins it.
        Assert.Equal(Line, ResearchPrompt.Read(Changed("\"condition_2_reading\": \"none\"", "\"condition_2_reading\": \"rsi\""), "stop", "pullback", "SML").Idea!.Line.Canonical);
        Assert.Equal(
            "event pullback(600) · if depth between tenth 2 and 5 · if rsi above tenth 3 · buy next open · stop 1.5 typical days · take profit whole at 2 risks · sell after 63 sessions · cap 5 a night by strength highest",
            ResearchPrompt.Read(Changed("\"condition_3_reading\": \"none\", \"condition_3_band\": \"tenths 1 to 2\"", "\"condition_3_reading\": \"depth\", \"condition_3_band\": \"tenths 2 to 5\""), "stop", "pullback", "SML").Idea!.Line.Canonical);

        foreach (var (answer, cause) in new[]
        {
            (Changed("\"rsi\"", "\"made_up\""), ResearchPrompt.NotInGrammar),
            (Changed("tenths 4 to 10", "tenths 1 to 10"), ResearchPrompt.NotInGrammar),
            (Changed("tenths 4 to 10", "tenths 7 to 3"), ResearchPrompt.NotInGrammar),
            (Changed("tenths 4 to 10", "tenths 4 to 4"), ResearchPrompt.NotInGrammar),
            (Changed("\"stop\": \"1.5\"", "\"stop\": \"9\""), ResearchPrompt.NotInGrammar),
            (Changed("\"buy\": \"next open\"", "\"buy\": \"at the bell\""), ResearchPrompt.NotInGrammar),
            (Changed("\"condition_1_reading\": \"rsi\"", "\"condition_1_reading\": \"none\""), ResearchPrompt.NotInGrammar),
            (Changed("weakest", "weakest in 2021"), ResearchPrompt.HoldsDigit),
            (Changed("\"reason\": \"Its losers gather where the momentum is weakest.\"", "\"reason\": \" \""), ResearchPrompt.NoReason),
            ("not one object", ReleaseAnswers.NotOneObject),
            (string.Empty, ReleaseAnswers.Empty),
        })
        {
            var refused = ResearchPrompt.Read(answer, "stop", "pullback", "SML");

            Assert.Null(refused.Idea);
            Assert.False(refused.None);
            Assert.Equal(cause, refused.Cause);
        }

        Assert.Equal(ReleaseAnswers.CutOff, ResearchPrompt.Read(Answered1, ReleaseAnswers.BudgetReached, "pullback", "SML").Cause);

        // The schema holds the sentence to no digit and every choice to the grammar's own lists, a band to the 44 the
        // grammar reads: two tenths or more and never all ten.
        var properties = ResearchPrompt.Schema()["properties"]!;

        Assert.Equal("^[^0-9]*$", (string?)properties["reason"]!["pattern"]);
        Assert.Equal(IdeaGrammar.Readings.Count, properties["order"]!["enum"]!.AsArray().Count);
        Assert.Equal(IdeaGrammar.Readings.Count + 1, properties["condition_2_reading"]!["enum"]!.AsArray().Count);
        Assert.Equal(IdeaGrammar.StopDays.Count, properties["stop"]!["enum"]!.AsArray().Count);
        Assert.Equal(44, ResearchPrompt.Bands.Count);
        Assert.All(ResearchPrompt.Bands, band => Assert.NotNull(IdeaGrammar.Parse($"event dip(500) · if rsi {BandLine(band)} · buy next open · stop 1 typical days · take profit none · sell after 10 sessions").Line));

        static string BandLine(string band)
        {
            var at = band.Split(' ');
            var (lowest, highest) = (int.Parse(at[1], System.Globalization.CultureInfo.InvariantCulture), int.Parse(at[3], System.Globalization.CultureInfo.InvariantCulture));

            return new IdeaCondition("rsi", lowest, highest).Line["if rsi ".Length..];
        }
    }

    [Fact]
    public async Task TheResearchersPromptHoldsNoTickerNoDateAndNoFigureOfTheLaterYears()
    {
        using var store = new TemporaryStore().Migrated();

        // A long setup of 2010 its rule picked and lost on, a standing pick since the restart, and an idea the notebook holds
        // whose later edge, +0.4321, the researcher may not see.
        store.Execute("INSERT INTO long_setup (index_code, family, ticker, session_date, rule, live_pass, picked, entry, stop, cap, result, edge, end, settled, source, pin, last_end, last_settled, run_id, rsi) VALUES ('SML', 'pullback', 'ZQXW', '2010-03-04', 'r', 1, 1, '100', '95', 63, -1, -0.9, 'stop', 1, 'long', 'p', 'stop', 1, 'r', 41.5);");
        store.Execute("INSERT INTO setup (index_code, family, ticker, session_date, rule, live_pass, picked, entry, stop, target, trail, cap, result, end, settled, source, pin, edge, rsi) VALUES ('SML', 'pullback', 'QWZX', '2026-11-17', 'r', 1, 1, '100', '95', NULL, NULL, 63, -1, 'stop', 1, 'night', 'p', -0.7, 38.25);");
        store.Execute("INSERT INTO notebook (entered_at, source, index_code, family, words, line, early_picks, early_edge, late_picks, late_edge, verdict, why, tested_on, run_id) VALUES ('2026-11-22T00:00:00Z', 'the operator', 'SML', 'pullback', 'w', 'event pullback(600) · buy next open · stop 1 typical days · take profit none · sell after 63 sessions', 300, 0.0712, 200, 0.4321, 'failed', 'Reached 1 in 7, the bar 1 in 1040, from 2019.', '2026-11-20', 'r');");

        var clock = FixedClock.At(new DateTimeOffset(2026, 11, 29, 13, 0, 0, TimeSpan.Zero), SessionZones.UnitedStates);
        using var models = new ModelsFolder();
        var settings = ReadingProfile(models.Root);
        var input = await new Researcher(new IdeaAnsweringFeed(ReadingFeedOver(AnsweringRuntime(loaded: true, chat: _ => "{}"), settings), _ => Reply("{}")), settings, clock, store.DatabaseFile, store.Root, NoWait).InputAsync("SML", "pullback");
        var prompt = ResearchPrompt.For(input);

        Assert.Equal((1, 1, 1, 1), (input.EarlyPicks, input.EarlyLosers, input.LivePicks, input.LiveLosers));
        Assert.Contains("event pullback(600) · buy next open · stop 1 typical days · take profit none · sell after 63 sessions | the early years, per $1 risked: +0.07 | the later years: failed", prompt, StringComparison.Ordinal);
        Assert.DoesNotContain("ZQXW", prompt, StringComparison.Ordinal);
        Assert.DoesNotContain("QWZX", prompt, StringComparison.Ordinal);
        Assert.DoesNotContain("0.43", prompt, StringComparison.Ordinal);
        Assert.DoesNotContain("1 in 7", prompt, StringComparison.Ordinal);
        Assert.Empty(Regex.Matches(prompt, @"(?<![0-9])(19|20)[0-9]{2}(?![0-9])"));
        Assert.StartsWith("Rule: the pullbacks to support on the S&P 600. It buys at most 5 a night and sells after 63 sessions.", prompt, StringComparison.Ordinal);

        // A constructed week's prompts, the most rows each, hold none either.
        Assert.All(new[] { "GSPC", "MID", "SML" }.SelectMany(index => IdeaGrammar.Families.Select(family => ResearchPrompt.For(Researcher.Constructed(index, family)))), one => Assert.Empty(Regex.Matches(one, @"(?<![0-9])(19|20)[0-9]{2}(?![0-9])")));
    }

    [Fact]
    public void TheWeeksLinesAreThoseThatWaitedThenEverySetupsStrongestAtMostTenAndARepeatIsHeld()
    {
        ResearchIdea Idea(string family, string index, int tenth) =>
            new(IdeaGrammar.Parse(ResearchPrompt.LineOf(family, index, [("rsi", tenth, 10)], "next open", 0, 1, IdeaGrammar.TimeOnly, 2, "strength", true)).Line!, "A sentence.");

        // Twelve setups each answering three ideas; one waited from an earlier week; the notebook holds the S&P 500
        // pullback's strongest.
        var answered = new[] { "GSPC", "MID", "SML" }.SelectMany(index => IdeaGrammar.Families.Select(family => (index, family, (IReadOnlyList<ResearchIdea>)[Idea(family, index, 2), Idea(family, index, 3), Idea(family, index, 4)]))).ToArray();
        var waited = Idea("drift", "SML", 9).Line.Canonical;
        var held = new HashSet<string>(StringComparer.Ordinal) { answered[0].Item3[0].Line.Canonical };
        var chosen = Researcher.Choose([(waited, "Waited.", "SML", "drift")], answered, held, Researcher.TestsAWeek);

        // By hand: the waited line first; then the strongest of the eleven setups not held, nine of them, filling ten; the
        // twelfth setup's strongest and every second and third waiting; the held one passed over.
        Assert.Equal((waited, Researcher.Tested, 0), (chosen[0].Line, chosen[0].Outcome, chosen[0].Rank));
        Assert.Equal(Researcher.Held, chosen.Single(one => one.Line == answered[0].Item3[0].Line.Canonical).Outcome);
        Assert.Equal(10, chosen.Count(one => one.Outcome == Researcher.Tested));
        Assert.All(chosen.Where(one => one.Outcome == Researcher.Tested).Skip(1), one => Assert.Equal(1, one.Rank));
        Assert.Equal(Researcher.Waiting, chosen.Last(one => one.Rank == 1).Outcome);
        Assert.Equal(1 + 36, chosen.Count);
        Assert.Equal(26, chosen.Count(one => one.Outcome == Researcher.Waiting));

        // A waited line the notebook has since tested is passed over, not written again.
        Assert.DoesNotContain(Researcher.Choose([(waited, "Waited.", "SML", "drift")], [], new HashSet<string>(StringComparer.Ordinal) { waited }, 10), one => one.Line == waited);
    }

    [Fact]
    public async Task AWeekWhoseModelAnswersNothingIsAskedOnceMoreWholeAndCountedAndARepeatOfAHeldIdeaIsNotTested()
    {
        using var store = new TemporaryStore().Migrated();
        using var models = new ModelsFolder();
        var settings = ReadingProfile(models.Root);
        var clock = FixedClock.At(new DateTimeOffset(2026, 11, 29, 13, 0, 0, TimeSpan.Zero), SessionZones.UnitedStates);

        await new LocalRun(ReadingFeedOver(AnsweringRuntime(loaded: true, chat: _ => Runtime("local-reading-probe-answered.json")), settings), settings, clock, store.DatabaseFile, store.Root, NoWait).PinAsync(TextWriter.Null);

        // Every ask refused: the whole week asked twice and counted as failed, every ask kept.
        var refusing = new IdeaAnsweringFeed(ReadingFeedOver(AnsweringRuntime(loaded: true, chat: _ => throw new InvalidOperationException("the answers are the test's own")), settings), _ => throw new ReadingRefused(ReleaseAnswers.RuntimeRefused, "the runtime refused the prompt"));
        var said = new StringWriter();

        await new Researcher(refusing, settings, clock, store.DatabaseFile, store.Root, NoWait).RunAsync(said);

        Assert.Equal(24, refusing.Requests);
        Assert.All(refusing.Asked, request => Assert.Equal((ResearchPrompt.Instruction, ResearchPrompt.AnswerTokens), (request.Instruction, request.AnswerTokens)));
        Assert.Equal(["1|12", "2|12"], Query(store, "SELECT round || '|' || COUNT(*) FROM research_ask WHERE outcome = 'refused' GROUP BY round ORDER BY round;"));
        Assert.Equal(["failed"], Query(store, $"SELECT outcome FROM run_log WHERE stage = '{Researcher.Stage}';"));
        Assert.Contains("research: no answer on the first asking", said.ToString(), StringComparison.Ordinal);
        Assert.Empty(Query(store, "SELECT line FROM research_idea;"));

        // A week whose one idea the notebook holds tests nothing and writes it held; an unreadable answer asked once more, the
        // setup's second ask shown the line it gave and giving it again, which ends its asks.
        var line = ResearchPrompt.Read(Answered1, "stop", "pullback", "SML").Idea!.Line.Canonical;

        store.Execute($"INSERT INTO notebook (entered_at, source, index_code, family, words, line, verdict, why, tested_on, run_id) VALUES ('2026-11-22T00:00:00Z', 'the researcher', 'SML', 'pullback', 'w', '{line}', 'failed', 'y', '2026-11-20', 'r');");

        var times = 0;
        var answering = new IdeaAnsweringFeed(ReadingFeedOver(AnsweringRuntime(loaded: true, chat: _ => throw new InvalidOperationException("the answers are the test's own")), settings), request =>
            !request.Text.StartsWith("Rule: the pullbacks to support on the S&P 600.", StringComparison.Ordinal) ? Reply("""{"has_idea": false}""")
            : ++times == 1 ? Reply("not one object") : Reply(Answered1));

        await new Researcher(answering, settings, clock, store.DatabaseFile, store.Root, NoWait).RunAsync(TextWriter.Null);

        Assert.Equal((14, 3), (answering.Requests, times));
        Assert.Single(answering.Asked, request => request.Text.Contains($"Ideas you have already given this week, which you must not give again:\n- {line}\n", StringComparison.Ordinal));
        Assert.Equal([$"SML|pullback|1|{line}|held"], Query(store, "SELECT index_code || '|' || family || '|' || rank || '|' || line || '|' || outcome FROM research_idea;"));
        Assert.Equal(1L, Scalar(store, "SELECT COUNT(*) FROM notebook;"));
        Assert.Contains("12 of 12 setups answered, 0 idea(s) tested, 0 waiting, 1 the notebook held", Query(store, $"SELECT detail FROM run_log WHERE stage = '{Researcher.Stage}' ORDER BY rowid DESC LIMIT 1;").Single(), StringComparison.Ordinal);

        // The researcher's tables take inserts alone.
        store.Execute("INSERT INTO research_failure (run_id, index_code, family, population, reading, highest, picks, losers, in_band, losers_in_band) VALUES ('r', 'SML', 'pullback', 'early', 'rsi', 0, 100, 50, 40, 30);");

        foreach (var write in new[] { "UPDATE research_ask SET outcome = 'answered';", "DELETE FROM research_ask;", "UPDATE research_idea SET outcome = 'tested';", "DELETE FROM research_idea;", "UPDATE research_failure SET picks = 1;", "DELETE FROM research_failure;" })
        {
            Assert.Throws<SqliteException>(() => store.Execute(write));
        }
    }
}
