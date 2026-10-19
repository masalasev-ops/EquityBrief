using EquityBrief.Core.Components;
using EquityBrief.Tests.Harness;
using EquityBrief.Worker.Loop;
using DataStore = EquityBrief.Core.Components.Store;

namespace EquityBrief.Tests.Checks;

// rules-choose-the-stocks, 20.9: the idea generator's stored answers are the other thing a model wrote that a component
// deciding may read. The walk-forward tester reads them through the one reader of the stored answers alone and asks no
// model; the generator writes them and asks the local model and decides nothing; no other component touches them; and no
// source under the deciding folders names their table, which the one reader alone names.
// see: Rules choose the stocks from stored data and a pinned local model's readings of documents with their names removed, and no model judges a stock
// see: The idea generator proposes conditions in the winners' grammar by closed labels code turns into levels, judged as a procedure inside each fold
public partial class RulesChooseTheStocks
{
    // The table the idea generator's answers are stored in, and the one reader the tester reads them through.
    internal const string LoopIdeaTable = "loop_idea";

    internal const string TheIdeasReader = "src/EquityBrief.Data/LoopIdeas.cs";

    internal static IReadOnlyList<string> NamesTheStoredIdeas(string file, string text) =>
        file == TheIdeasReader || !text.Contains(LoopIdeaTable, StringComparison.Ordinal) ? [] : [$"{file} names {LoopIdeaTable}"];

    [Fact]
    public void TheTesterReadsTheGeneratorsStoredAnswersThroughTheOneReaderAlone()
    {
        var components = ShippedComponents.All();
        var touching = components.Where(component => component.Access.On(DataStore.LoopIdea) != Touch.None).ToArray();

        // The scope, by name: the generator writes them and the tester reads them, and nothing else touches them.
        Assert.Equal(["IdeaGenerator", "WalkForwardTester"], touching.Select(component => component.Name).Order(StringComparer.Ordinal));
        Assert.Equal(["IdeaGenerator"], touching.Where(component => (component.Access.On(DataStore.LoopIdea) & Touch.Insert) != Touch.None).Select(component => component.Name));
        Assert.Equal(Touch.Read, WalkForwardTester.Access.On(DataStore.LoopIdea));

        // The tester asks no model and reads nothing else a model wrote, and the generator is no deciding component.
        Assert.Empty(ReadsAModel(nameof(WalkForwardTester), WalkForwardTester.Access));
        Assert.DoesNotContain(Deciding, one => one.Name == "IdeaGenerator");

        var sources = SourcesUnderTheFolders();
        var naming = sources.SelectMany(file => NamesTheStoredIdeas(file, File.ReadAllText(Path.Combine(Repository.Root, file)))).ToArray();

        Assert.True(sources.Length >= 100, $"Read {sources.Length} sources under the deciding folders, expected at least 100.");
        Assert.True(
            naming.Length == 0,
            $"Of the {sources.Length} sources under the deciding folders, these name the stored ideas' table rather than reading it through {TheIdeasReader}: {string.Join("; ", naming)}.");
        Assert.Contains(LoopIdeaTable, File.ReadAllText(Path.Combine(Repository.Root, TheIdeasReader)), StringComparison.Ordinal);
        Assert.Contains("LoopIdeas.NewestAsync", File.ReadAllText(Path.Combine(Repository.Root, "src", "EquityBrief.Worker", "Loop", "WalkForwardTester.cs")), StringComparison.Ordinal);
    }

    [Fact]
    public void AReadOfTheStoredIdeasOutsideTheOneReaderIsFound()
    {
        Assert.Equal(
            ["src/EquityBrief.Worker/Loop/Planted.cs names loop_idea"],
            NamesTheStoredIdeas("src/EquityBrief.Worker/Loop/Planted.cs", "const string Sql = \"SELECT answer FROM loop_idea WHERE prompt_key = $key;\";"));
        Assert.Empty(NamesTheStoredIdeas(TheIdeasReader, "const string Sql = \"SELECT answer FROM loop_idea WHERE prompt_key = $key;\";"));
        Assert.Empty(NamesTheStoredIdeas("src/EquityBrief.Worker/Loop/Clean.cs", "var stored = await LoopIdeas.NewestAsync(connection, cancellation);"));
    }
}
