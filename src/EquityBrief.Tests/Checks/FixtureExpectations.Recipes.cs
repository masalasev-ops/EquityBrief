using System.Globalization;
using EquityBrief.Core.Ledger;
using EquityBrief.Core.Loop;
using EquityBrief.Core.Time;
using EquityBrief.Tests.Harness;
using EquityBrief.Worker.Loop;
using Microsoft.Data.Sqlite;

namespace EquityBrief.Tests.Checks;

// fixture-expectations, 24.30a: a reading recipe is one line of a closed set of operations over the catalogue's fields, at
// most two deep and each span from a closed list; each operation is worked by hand at a constructed session, the panel's
// whole working agreeing with each figure worked alone; a run writes a recipe's figures only where evenly spaced sessions
// worked again with every later session removed agree, a recipe reading a later session refused with nothing written; and a
// field or data the store lacks is listed once for the operator and blocks nothing.
// see: A reading recipe is a closed set of operations over stored fields and is kept apart from the pinned catalogue
public partial class FixtureExpectations
{
    internal static string[] RecipeClaims =>
    [
        CheckReach.Key(Scope.LimitsTable, "Reading recipes"),
    ];

    internal static string[] RecipeRows =>
        [.. ComponentAccess.RecipeDeskRows, .. SchemaColumns.RecipeStoreRows, .. RecipeClaims, .. Reading.ReadSurface.RecipeRows];

    static readonly int RecipeRsi = LedgerReadings.IndexOf("rsi");

    static readonly int RecipeVolume = LedgerReadings.IndexOf("volume_ratio");

    // Three members over 30 sessions: A's rsi the session's place plus 10 and its volume ratio 2, B's rsi 50 throughout and
    // its volume ratio the place plus 1, C's rsi 40 and its volume ratio 4, C no member before the 20th session; A and B
    // in industry "x", C in "y".
    static RecipePanel RecipeConstructed()
    {
        var calendar = Enumerable.Range(0, 30).Select(at => new DateOnly(2010, 1, 4).AddDays(at)).ToArray();

        double?[]? Row(double? rsi, double? volume)
        {
            var readings = new double?[LedgerReadings.Count];

            readings[RecipeRsi] = rsi;
            readings[RecipeVolume] = volume;

            return readings;
        }

        var rows = new Dictionary<string, IReadOnlyList<double?[]?>>(StringComparer.Ordinal)
        {
            ["A"] = [.. Enumerable.Range(0, 30).Select(at => Row(at + 10, 2))],
            ["B"] = [.. Enumerable.Range(0, 30).Select(at => Row(50, at + 1))],
            ["C"] = [.. Enumerable.Range(0, 30).Select(at => at < 20 ? null : Row(40, 4))],
        };

        return RecipePanel.Of(calendar, rows, (ticker, _) => ticker == "C" ? "y" : "x");
    }

    [Fact]
    public void EachRecipeOperationIsWorkedByHandAtAConstructedSession()
    {
        var panel = RecipeConstructed();

        double? At(string line, string ticker, int session) => Recipes.Evaluate(Recipes.Parse(line).Term!, panel, ticker, session);

        // A change over 5 sessions at the 25th: A's rsi 35 less its 30 five sessions before, 5; none before 5 sessions stand.
        Assert.Equal(5, At("change(rsi, 5)", "A", 25));
        Assert.Null(At("change(rsi, 5)", "A", 4));

        // A ratio: B's rsi 50 over its volume ratio of 26 at the 25th; none where the bottom is nothing.
        Assert.Equal(50.0 / 26, At("ratio(rsi, volume_ratio)", "B", 25));

        // An average over 5 sessions: A's rsi 31 to 35 at the 21st to the 25th, 33; none where a session of the span holds
        // none, C before it was a member.
        Assert.Equal(33, At("average(rsi, 5)", "A", 25));
        Assert.Null(At("average(rsi, 5)", "C", 22));

        // A difference from its own history over 5 sessions: A's rsi 35 less the mean of the 5 before it, 30 to 34, 32: 3.
        Assert.Equal(3, At("history(rsi, 5)", "A", 25));

        // A rank within the index at the 25th: rsi 35, 50 and 40; A at the bottom 0, B at the top 1, C between them 0.5;
        // within its industry A against B alone 0, and C alone in its own none.
        Assert.Equal(0, At("rank(rsi, index)", "A", 25));
        Assert.Equal(1, At("rank(rsi, index)", "B", 25));
        Assert.Equal(0.5, At("rank(rsi, index)", "C", 25));
        Assert.Equal(0, At("rank(rsi, industry)", "A", 25));
        Assert.Null(At("rank(rsi, industry)", "C", 25));

        // Two deep: the rank of each member's change over 5 sessions, A's 5 against B's and C's nothing, a tie counting half:
        // C's change reads none, its 15th session standing in no index, so A against B alone, B's 0 below A's 5: A 1.
        Assert.Equal(1, At("rank(change(rsi, 5), index)", "A", 22));

        // A member that stood in no index on the session reads none of any recipe.
        Assert.Null(At("ratio(rsi, volume_ratio)", "C", 10));

        // The panel's whole working gives every member's figure on every session as each worked alone gives it.
        foreach (var line in new[] { "change(rsi,5)", "ratio(rsi,volume_ratio)", "average(volume_ratio,10)", "history(rsi,5)", "rank(rsi,index)", "rank(rsi,industry)", "rank(change(rsi,5),index)", "average(ratio(rsi,volume_ratio),5)" })
        {
            var term = Recipes.Parse(line).Term!;
            var all = Recipes.All(term, panel);

            foreach (var ticker in panel.Members)
            {
                for (var at = 0; at < panel.Calendar.Count; at++)
                {
                    Assert.Equal(Recipes.Evaluate(term, panel, ticker, at), all[ticker][at]);
                }
            }
        }
    }

    // The grammar: two spellings read as one canonical line; an operation off the closed list, a span off it, a third
    // operation deep, a bare field and a rank within anything but the index or the industry each refused with why; a field
    // the store does not hold refuses the line and is named as lacking.
    [Fact]
    public void ARecipeIsOneLineOfTheClosedGrammarAndAFieldTheStoreLacksIsNamed()
    {
        Assert.Equal("rank(change(rsi,21),industry)", Recipes.Parse("RANK( change(rsi , 21) , industry )").Term!.Canonical);
        Assert.Equal(Recipes.Parse("rank(change(rsi,21),industry)").Term, Recipes.Parse("rank( change( rsi,21 ),industry)").Term);
        Assert.Contains("no operation", Recipes.Parse("median(rsi, 21)").Why, StringComparison.Ordinal);
        Assert.Contains("not one of the closed list", Recipes.Parse("change(rsi, 20)").Why, StringComparison.Ordinal);
        Assert.Contains("nests 3 operations", Recipes.Parse("rank(change(average(rsi, 5), 21), index)").Why, StringComparison.Ordinal);
        Assert.Contains("a field of the catalogue already", Recipes.Parse("rsi").Why, StringComparison.Ordinal);
        Assert.Contains("within the index or the industry", Recipes.Parse("rank(rsi, sector)").Why, StringComparison.Ordinal);

        var lacking = Recipes.Parse("change(days_to_cover, 21)");

        Assert.Null(lacking.Term);
        Assert.Equal(["days_to_cover"], lacking.Lacking);
        Assert.Equal([Recipes.IndustryAsItStood], Recipes.Needs(Recipes.Parse("rank(rsi, industry)").Term!, holdsIndustries: false));
        Assert.Empty(Recipes.Needs(Recipes.Parse("rank(rsi, index)").Term!, holdsIndustries: false));
    }

    // A figure reading the session after its own is refused by the check, and one reading those before it alone passes.
    [Fact]
    public void ARecipesFigureReadingALaterSessionFailsTheCheck()
    {
        var panel = RecipeConstructed();
        var term = Recipes.Parse("change(rsi, 5)").Term!;

        Assert.True(Recipes.ReadsNothingLater((one, ticker, at) => Recipes.Evaluate(term, one, ticker, at), panel, "A", 25));
        Assert.False(Recipes.ReadsNothingLater((one, ticker, at) => one.Value(ticker, RecipeRsi, at + 1) is { } next && one.Value(ticker, RecipeRsi, at) is { } now ? next - now : null, panel, "A", 25));
    }

    static void RecipeSessionRows(TemporaryStore store)
    {
        using var connection = store.Open();
        using var transaction = connection.BeginTransaction();
        using var insert = connection.CreateCommand();

        insert.Transaction = transaction;
        insert.CommandText = "INSERT INTO session_reading (index_code, session_date, ticker, readings, pin, source) VALUES ('SML', $session, $ticker, $readings, $pin, 'build');";
        insert.Parameters.AddWithValue("$session", string.Empty);
        insert.Parameters.AddWithValue("$ticker", string.Empty);
        insert.Parameters.AddWithValue("$readings", string.Empty);
        insert.Parameters.AddWithValue("$pin", LedgerReadings.Version);

        // A and B over 30 weekdays from 2010-01-04, A's rsi the session's place plus 10, B's 50.
        var calendar = Weekdays(new DateOnly(2010, 1, 4), 30);

        for (var at = 0; at < calendar.Length; at++)
        {
            foreach (var (ticker, rsi) in new[] { ("A", at + 10.0), ("B", 50.0) })
            {
                var readings = new double?[LedgerReadings.Count];

                readings[RecipeRsi] = rsi;
                insert.Parameters["$session"].Value = calendar[at].ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
                insert.Parameters["$ticker"].Value = ticker;
                insert.Parameters["$readings"].Value = Worker.Ledger.SetupLedger.ReadingsText(readings);
                insert.ExecuteNonQuery();
            }
        }

        transaction.Commit();
    }

    static long RecipeCount(TemporaryStore store, string sql)
    {
        using var connection = store.Open();
        using var command = connection.CreateCommand();

        command.CommandText = sql;

        return (long)command.ExecuteScalar()!;
    }

    // Entering: a line read is entered once, a second time said so; a field the store lacks listed for the operator and the
    // recipe not entered, blocking nothing; a rank within the industry entered and its industry as it stood listed, once
    // however often asked; a line the grammar refuses writes nothing. Running: without the word nothing is run; with it the
    // change's figures are written, 25 of A's and 25 of B's, the first 5 sessions holding none; a run whose working reads
    // the session after its own is refused with nothing of it written; and the tables refuse an update and a delete.
    [Fact]
    public async Task ARecipeIsEnteredOnceItsLackListedAndARunWritesItsFiguresOnlyWhereTheyReadNothingLater()
    {
        using var store = new TemporaryStore().Migrated();
        var clock = new SweepClock(new DateTimeOffset(2026, 12, 5, 13, 0, 0, TimeSpan.Zero));
        var output = new StringWriter();
        var desk = new RecipeDesk(clock, store.DatabaseFile, store.Root, output, (_, _) =>
        {
            clock.UtcNow = clock.UtcNow.AddMinutes(1);

            return Task.CompletedTask;
        });

        Task<int> Run(bool word, Func<RecipeTerm, RecipePanel, IReadOnlyDictionary<string, double?[]>>? compute = null)
        {
            clock.UtcNow = clock.UtcNow.AddMinutes(1);

            return desk.RunAsync("SML", word, compute: compute);
        }

        RecipeSessionRows(store);

        Assert.Equal(0, await desk.EnterAsync("change(rsi, 5)", "rsi's change over a week", RecipeDesk.OperatorSource));
        Assert.Equal(0, await desk.EnterAsync("change( rsi,5 )", "the same again", RecipeDesk.OperatorSource));
        Assert.Contains("entered already as recipe 1", output.ToString(), StringComparison.Ordinal);
        Assert.Equal(0, await desk.EnterAsync("change(days_to_cover, 21)", "short interest's change", "the researcher"));
        Assert.Equal(0, await desk.EnterAsync("rank(rsi, industry)", "rsi against its industry", RecipeDesk.OperatorSource));
        Assert.Equal(0, await desk.EnterAsync("rank(volume_ratio, industry)", "volume against its industry", RecipeDesk.OperatorSource));
        Assert.Equal(2, await desk.EnterAsync("median(rsi, 21)", "a median", RecipeDesk.OperatorSource));
        Assert.Equal(3, RecipeCount(store, "SELECT COUNT(*) FROM recipe;"));
        Assert.Equal(2, RecipeCount(store, "SELECT COUNT(*) FROM data_request;"));
        Assert.Equal(1, RecipeCount(store, "SELECT COUNT(*) FROM data_request WHERE need = 'the field days_to_cover' AND source = 'the researcher';"));
        Assert.Equal(1, RecipeCount(store, $"SELECT COUNT(*) FROM data_request WHERE need = '{Recipes.IndustryAsItStood.Replace("'", "''", StringComparison.Ordinal)}';"));

        Assert.Equal(0, await Run(word: false));
        Assert.Equal(0, RecipeCount(store, "SELECT COUNT(*) FROM recipe_reading;"));
        Assert.Contains("nothing run", output.ToString(), StringComparison.Ordinal);

        // A working that reads the session after its own is refused, and nothing of the recipe is written.
        IReadOnlyDictionary<string, double?[]> Peeking(RecipeTerm term, RecipePanel panel) =>
            panel.Members.ToDictionary(ticker => ticker, ticker => Enumerable.Range(0, panel.Calendar.Count).Select(at => panel.Value(ticker, RecipeRsi, at + 1)).ToArray(), StringComparer.Ordinal);

        Assert.Equal(0, await Run(word: true, Peeking));
        Assert.Contains("recipe 1 refused: its figures on", output.ToString(), StringComparison.Ordinal);
        Assert.Contains("recipe 2 waits for " + Recipes.IndustryAsItStood, output.ToString(), StringComparison.Ordinal);
        Assert.Equal(0, RecipeCount(store, "SELECT COUNT(*) FROM recipe_reading;"));
        Assert.Equal(0, RecipeCount(store, "SELECT COUNT(*) FROM recipe_run;"));

        Assert.Equal(0, await Run(word: true));
        Assert.Equal(50, RecipeCount(store, "SELECT COUNT(*) FROM recipe_reading WHERE recipe_id = 1;"));
        Assert.Equal(25, RecipeCount(store, "SELECT COUNT(*) FROM recipe_reading WHERE recipe_id = 1 AND ticker = 'A' AND value = 5;"));
        Assert.Equal(25, RecipeCount(store, "SELECT COUNT(*) FROM recipe_reading WHERE recipe_id = 1 AND ticker = 'B' AND value = 0;"));
        Assert.Equal(1, RecipeCount(store, "SELECT COUNT(*) FROM recipe_run WHERE recipe_id = 1 AND index_code = 'SML' AND figures = 50 AND members = 2 AND checked = 20;"));

        foreach (var table in new[] { "recipe", "recipe_run", "recipe_reading", "data_request" })
        {
            Assert.Throws<SqliteException>(() => store.Execute($"UPDATE {table} SET run_id = 'x';"));
            Assert.Throws<SqliteException>(() => store.Execute($"DELETE FROM {table};"));
        }
    }
}
