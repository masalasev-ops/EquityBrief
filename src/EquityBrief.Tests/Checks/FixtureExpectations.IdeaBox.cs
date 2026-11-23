using EquityBrief.Core.Loop;
using EquityBrief.Core.Releases;
using EquityBrief.Core.Time;
using EquityBrief.Tests.Harness;
using EquityBrief.Worker.Local;
using EquityBrief.Worker.Loop;
using Microsoft.Data.Sqlite;

namespace EquityBrief.Tests.Checks;

// fixture-expectations: the idea box. An idea typed is read through a recorded answer into the line code writes from the
// answer's closed choices, the setup and the index among them, and an answer outside the grammar is refused with its cause;
// the box's worker writes each attempt, an unreadable answer asked once more; and a line joins the week's tests only once it is
// confirmed, first and under the box as its source.
// see: An idea typed in the box is read into one grammar line by the local model and joins the week's tests only on the operator's confirm
public partial class FixtureExpectations
{
    internal static string[] IdeaBoxClaims =>
    [
        CheckReach.Key(Scope.FailureTable, "An idea the box cannot read"),
    ];

    internal static string[] IdeaBoxRows =>
        [.. ComponentAccess.IdeaBoxRows, .. SchemaColumns.IdeaBoxStoreRows, .. IdeaBoxClaims, .. Reading.ReadSurface.IdeaBoxRows];

    const string Boxed =
        """{"setup": "pullback", "index": "600", "has_idea": true, "condition_1_reading": "rsi", "condition_1_band": "tenths 4 to 10", "condition_2_reading": "none", "condition_2_band": "tenths 1 to 2", "condition_3_reading": "none", "condition_3_band": "tenths 1 to 2", "buy": "next open", "window": "5", "stop": "1.5", "take_profit": "whole", "multiple": "2", "order": "strength", "order_highest": true, "reason": "Keep the small cap pullbacks whose momentum has not faded."}""";

    const string BoxedLine = "event pullback(600) · if rsi above tenth 3 · buy next open · stop 1.5 typical days · take profit whole at 2 risks · sell after 63 sessions · cap 5 a night by strength highest";

    [Fact]
    public void AnIdeaTypedIsReadIntoTheLineCodeWritesAndAnAnswerOutsideTheGrammarIsRefusedWithItsCause()
    {
        // By hand: the pullback on the S&P 600 keeping rsi's fourth tenth and up, sold after its own 63 sessions and 5 a night.
        var read = IdeaBoxPrompt.Read(Boxed, "stop");

        Assert.Equal((BoxedLine, "Keep the small cap pullbacks whose momentum has not faded.", false, null), (read.Line!.Canonical, read.Reason, read.None, read.Cause));
        Assert.Equal((null, true, null), (IdeaBoxPrompt.Read("""{"has_idea": false}""", "stop").Line, IdeaBoxPrompt.Read("""{"has_idea": false}""", "stop").None, IdeaBoxPrompt.Read("""{"has_idea": false}""", "stop").Cause));
        Assert.Equal(IdeaBoxPrompt.Unknown, IdeaBoxPrompt.Read(Boxed.Replace("\"pullback\"", "\"heavyweight\"", StringComparison.Ordinal), "stop").Cause);
        Assert.Equal(IdeaBoxPrompt.Unknown, IdeaBoxPrompt.Read(Boxed.Replace("\"600\"", "\"100\"", StringComparison.Ordinal), "stop").Cause);
        Assert.Equal(ResearchPrompt.HoldsDigit, IdeaBoxPrompt.Read(Boxed.Replace("has not faded", "has not faded in 3 weeks", StringComparison.Ordinal), "stop").Cause);
        Assert.Equal(ResearchPrompt.NotInGrammar, IdeaBoxPrompt.Read(Boxed.Replace("\"rsi\"", "\"no such reading\"", StringComparison.Ordinal), "stop").Cause);
        Assert.Equal(ReleaseAnswers.NotOneObject, IdeaBoxPrompt.Read("I would buy pullbacks", "stop").Cause);
        Assert.Equal(ReleaseAnswers.CutOff, IdeaBoxPrompt.Read(Boxed, ReleaseAnswers.BudgetReached).Cause);

        // The schema: the setup and the index first, every key required, nothing else admitted.
        var schema = IdeaBoxPrompt.Schema();
        var keys = schema["properties"]!.AsObject().Select(property => property.Key).ToArray();

        Assert.Equal(["setup", "index", "has_idea"], keys[..3]);
        Assert.Equal(keys, schema["required"]!.AsArray().Select(key => key!.GetValue<string>()));
        Assert.False(schema["additionalProperties"]!.GetValue<bool>());

        // The prompt holds the words as typed and the readings, and the words are cut at the longest the box takes.
        Assert.StartsWith("The operator's idea, as typed:\nbuy pullbacks\n", IdeaBoxPrompt.For("  buy pullbacks  ").Replace("\r\n", "\n", StringComparison.Ordinal), StringComparison.Ordinal);
        Assert.Equal(IdeaBoxPrompt.MostCharacters, IdeaBoxPrompt.Trimmed(new string('a', IdeaBoxPrompt.MostCharacters + 50)).Length);
    }

    [Fact]
    public async Task TheBoxsWorkerWritesEachAttemptAndAConfirmedLineAloneJoinsTheWeeksTestsFirstUnderTheBox()
    {
        using var store = new TemporaryStore().Migrated();
        using var models = new ModelsFolder();
        var settings = ReadingProfile(models.Root);
        var clock = FixedClock.At(new DateTimeOffset(2026, 11, 29, 13, 0, 0, TimeSpan.Zero), SessionZones.UnitedStates);

        await new LocalRun(ReadingFeedOver(AnsweringRuntime(loaded: true, chat: _ => Runtime("local-reading-probe-answered.json")), settings), settings, clock, store.DatabaseFile, store.Root, NoWait).PinAsync(TextWriter.Null);
        store.Execute("INSERT INTO idea_box (words, pressed_at) VALUES ('buy small cap pullbacks whose momentum holds', '2026-11-29T12:00:00Z');");

        // An unreadable answer asked once more, then read: two attempts written, the second understood with its line.
        var times = 0;
        var answering = new IdeaAnsweringFeed(ReadingFeedOver(AnsweringRuntime(loaded: true, chat: _ => throw new InvalidOperationException("the answers are the test's own")), settings), _ => ++times == 1 ? Reply("not one object") : Reply(Boxed));
        var said = new StringWriter();

        Assert.Equal(0, await new IdeaBoxReader(answering, settings, clock, store.DatabaseFile, store.Root, NoWait).RunAsync(1, said));
        Assert.Equal((2, IdeaBoxPrompt.Instruction), (answering.Requests, answering.Asked[0].Instruction));
        Assert.Contains("buy small cap pullbacks whose momentum holds", answering.Asked[0].Text, StringComparison.Ordinal);
        Assert.Equal([$"1|refused|{ReleaseAnswers.NotOneObject}|", $"2|understood||{BoxedLine}"], Query(store, "SELECT attempt || '|' || outcome || '|' || COALESCE(cause, '') || '|' || COALESCE(line, '') FROM idea_box_answer ORDER BY attempt;"));
        Assert.Contains("idea-box: understood: Keep the small cap pullbacks", said.ToString(), StringComparison.Ordinal);
        Assert.Equal(["ok|2|press 1: 2 attempt(s), the last understood"], Query(store, $"SELECT outcome || '|' || rows_written || '|' || detail FROM run_log WHERE stage = '{IdeaBoxReader.Verb}';"));

        // A press read already is not asked again.
        Assert.Equal(0, await new IdeaBoxReader(answering, settings, clock, store.DatabaseFile, store.Root, NoWait).RunAsync(1, TextWriter.Null));
        Assert.Equal(2, answering.Requests);

        // Nothing joins the week's tests before the confirm; once confirmed the line comes first, under the box, before a line
        // that waited from an earlier week.
        var researcher = new Researcher(answering, settings, clock, store.DatabaseFile, store.Root, NoWait);

        Assert.Empty(await researcher.BoxedAsync());

        store.Execute($"INSERT INTO idea_box_confirm (press_id, line, confirmed_at) VALUES (1, '{BoxedLine}', '2026-11-29T12:30:00Z');");

        var boxed = await researcher.BoxedAsync();
        const string Waited = "event drift(600) · if rsi above tenth 3 · buy next open · stop 1 typical days · take profit none · sell after 60 sessions · cap 5 a night by rsi lowest";
        var chosen = Researcher.Choose([(Waited, "Waited.", "SML", "drift")], [], new HashSet<string>(StringComparer.Ordinal), 1, boxed);

        Assert.Equal([(BoxedLine, "SML", "pullback", "Keep the small cap pullbacks whose momentum has not faded.")], boxed.Select(one => (one.Line, one.Index, one.Family, one.Reason)));
        Assert.Equal([(BoxedLine, Researcher.Tested, true), (Waited, Researcher.Waiting, false)], chosen.Select(one => (one.Line, one.Outcome, one.Boxed)));

        // A confirmed line the notebook holds is not tested again.
        Assert.Empty(Researcher.Choose([], [], new HashSet<string>(StringComparer.Ordinal) { BoxedLine }, 10, boxed));

        // The box's tables take inserts alone.
        foreach (var write in new[] { "UPDATE idea_box SET words = 'x';", "DELETE FROM idea_box;", "UPDATE idea_box_answer SET outcome = 'none';", "DELETE FROM idea_box_answer;", "UPDATE idea_box_confirm SET line = 'x';", "DELETE FROM idea_box_confirm;" })
        {
            Assert.Throws<SqliteException>(() => store.Execute(write));
        }
    }
}
