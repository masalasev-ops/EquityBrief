using EquityBrief.Core.Components;
using EquityBrief.Worker.Judge;
using DataStore = EquityBrief.Core.Components.Store;
using Declared = EquityBrief.Core.Components.ComponentAccess;

namespace EquityBrief.Tests.Checks;

// rules-choose-the-stocks, from 24.3: the AI judge is the one place a model judges a stock. Its components may read what a
// model wrote and ask the local model, and are named here as the check's one exception; they write the judge's own tables
// and the run log alone, sit under folders no deciding component's code runs through, and no deciding component declares
// a touch of the judge's tables nor any source under the deciding folders names one.
// see: Rules choose the stocks from stored data and a pinned local model's readings of documents with their names removed, and a model judges a stock only for the AI judge's own account
public partial class RulesChooseTheStocks
{
    // The judge's components, the check's one exception.
    internal static readonly (string Name, Declared Access)[] Judge =
    [
        (nameof(JudgeFactWriter), JudgeFactWriter.Access),
        (nameof(JudgeNamingTest), JudgeNamingTest.Access),
        (nameof(JudgePicker), JudgePicker.Access),
    ];

    // The judge's tables, by the store a declaration names and by the table a query names.
    internal static readonly (DataStore Store, string Table)[] JudgeTables =
    [
        (DataStore.JudgeFact, "judge_fact"),
        (DataStore.JudgeNaming, "judge_naming"),
        (DataStore.JudgePin, "judge_pin"),
        (DataStore.JudgeRun, "judge_run"),
        (DataStore.JudgeAsk, "judge_ask"),
        (DataStore.JudgePick, "judge_pick"),
    ];

    // The folders the judge's code sits under.
    internal static readonly string[] JudgeFolders = ["src/EquityBrief.Core/Judge", "src/EquityBrief.Worker/Judge"];

    internal static IReadOnlyList<string> TouchesTheJudge(string name, Declared access) =>
        [.. JudgeTables.Where(table => access.On(table.Store) != Touch.None).Select(table => $"{name} touches {table.Table}")];

    internal static IReadOnlyList<string> NamesTheJudgesTables(string file, string text) =>
    [
        .. JudgeTables
            .SelectMany(table => new[] { table.Table, table.Store.ToString() })
            .Where(word => text.Contains(word, StringComparison.Ordinal))
            .Select(word => $"{file} names {word}"),
    ];

    // What a judge's component writes beyond the judge's own tables and the run log.
    static IReadOnlyList<string> WritesBeyondTheJudge(string name, Declared access) =>
    [
        .. access.Stores
            .Where(touch => (touch.Touch & (Touch.Insert | Touch.Update | Touch.Delete)) != Touch.None)
            .Where(touch => touch.Store != DataStore.RunLog && JudgeTables.All(table => table.Store != touch.Store))
            .Select(touch => $"{name} writes {touch.Store}"),
    ];

    [Fact]
    public void TheJudgesComponentsAreTheOneExceptionAndNoDecidingComponentOrSourceTouchesTheJudgesTables()
    {
        // The exception, by name: the judge's components are none of the deciding ones, and their folders none of the
        // deciding folders.
        Assert.Equal(3, Judge.Length);
        Assert.Empty(Judge.Select(one => one.Name).Intersect(Deciding.Select(one => one.Name), StringComparer.Ordinal));
        Assert.Empty(JudgeFolders.Intersect(Folders, StringComparer.Ordinal));
        Assert.All(JudgeFolders, folder => Assert.True(Directory.Exists(Path.Combine(Repository.Root, folder)), folder));

        // The judge's components write the judge's own tables and the run log alone.
        var writing = Judge.SelectMany(one => WritesBeyondTheJudge(one.Name, one.Access)).ToArray();

        Assert.True(writing.Length == 0, $"The judge's components write beyond its own tables: {string.Join("; ", writing)}.");

        // No deciding component declares a touch of the judge's tables.
        var touching = Deciding.SelectMany(one => TouchesTheJudge(one.Name, one.Access)).ToArray();

        Assert.True(touching.Length == 0, $"Of the {Deciding.Length} deciding components, these touch the judge's tables: {string.Join("; ", touching)}.");

        // And no source under the deciding folders names one.
        var sources = SourcesUnderTheFolders();
        var naming = sources.SelectMany(file => NamesTheJudgesTables(file, File.ReadAllText(Path.Combine(Repository.Root, file)))).ToArray();

        Assert.True(sources.Length >= 100, $"Read {sources.Length} sources under the deciding folders, expected at least 100.");
        Assert.True(naming.Length == 0, $"Of the {sources.Length} sources under the deciding folders, these name the judge's tables: {string.Join("; ", naming)}.");
    }

    [Fact]
    public void AReadOfTheJudgesTablesIsFoundWhereverItIsPlanted()
    {
        // A deciding component's declaration planted with a read of the judge's facts is found, and a clean one passes.
        var planted = new Declared([new StoreTouch(DataStore.Bar, Touch.Read), new StoreTouch(DataStore.JudgeFact, Touch.Read)], []);
        var clean = new Declared([new StoreTouch(DataStore.Bar, Touch.Read)], []);

        Assert.Equal(["a family touches judge_fact"], TouchesTheJudge("a family", planted));
        Assert.Empty(TouchesTheJudge("a family", clean));

        // A source naming the naming test's table, or the judge's facts by their store, is found, and a clean one passes.
        Assert.Equal(["Rule.cs names judge_naming"], NamesTheJudgesTables("Rule.cs", "SELECT named FROM judge_naming;"));
        Assert.Equal(["Rule.cs names JudgeFact"], NamesTheJudgesTables("Rule.cs", "new StoreTouch(Store.JudgeFact, Touch.Read)"));
        Assert.Empty(NamesTheJudgesTables("Rule.cs", "SELECT close FROM bar;"));

        // A judge's component planted with a write of a rule's table is found.
        Assert.Equal(["the judge writes CandidateRegister"], WritesBeyondTheJudge("the judge", new Declared([new StoreTouch(DataStore.CandidateRegister, Touch.Insert)], [])));
    }
}
