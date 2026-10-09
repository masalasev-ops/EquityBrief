using EquityBrief.Core.Components;
using EquityBrief.Tests.Harness;
using DataStore = EquityBrief.Core.Components.Store;
using Declared = EquityBrief.Core.Components.ComponentAccess;

namespace EquityBrief.Tests.Checks;

// rules-choose-the-stocks, 17.7: the fitted model's store joins the check. A score fitted by code to the ledger is a rule,
// so what touches its store is held as a rule's code is: its one writer is the walk-forward tester, and no component
// touching it but the read surface, which draws it, declares a read of what a language model wrote, a model or the
// search among its feeds, or a read of the operator's trades or record.
// see: A fitted statistical model is a rule
// see: Rules choose the stocks, and a language model writes only the reports
public partial class RulesChooseTheStocks
{
    const string ReadSurface = "ReadApi";

    // What a component touching the fitted model's store declares that it may not.
    internal static IReadOnlyList<string> TouchesTheModelAndReadsWhatItMayNot(string name, Declared access) =>
        access.On(DataStore.LoopModel) == Touch.None || name == ReadSurface
            ? []
            : [.. ReadsAModel(name, access), .. ReadsTheOperatorsOwn(name, access)];

    [Fact]
    public void TheFittedModelsStoreIsWrittenByTheTesterAloneAndTouchedByNothingThatReadsAModelOrTheOperatorsTrades()
    {
        var components = ShippedComponents.All();
        var touching = components.Where(component => component.Access.On(DataStore.LoopModel) != Touch.None).ToArray();
        var writing = touching.Where(component => (component.Access.On(DataStore.LoopModel) & (Touch.Insert | Touch.Update | Touch.Delete)) != Touch.None).ToArray();

        // The scope, by name: the tester writes it, and the decision cards and the read surface read it.
        Assert.Equal(["DecisionCards", ReadSurface, "WalkForwardTester"], touching.Select(component => component.Name).Order(StringComparer.Ordinal));
        Assert.Equal(["WalkForwardTester"], writing.Select(component => component.Name));
        Assert.Empty(touching.SelectMany(component => TouchesTheModelAndReadsWhatItMayNot(component.Name, component.Access)));
    }

    [Fact]
    public void TheReaderOfWhatTouchesTheModelFindsAPlantedReadAndPassesAClearOne()
    {
        var takenToo = new Declared([new StoreTouch(DataStore.LoopModel, Touch.Insert), new StoreTouch(DataStore.TakenTrade, Touch.Read)], []);
        var asking = new Declared([new StoreTouch(DataStore.LoopModel, Touch.Read)], [Feed.ResearchModel]);
        var clear = new Declared([new StoreTouch(DataStore.LoopModel, Touch.Insert), new StoreTouch(DataStore.Setup, Touch.Read)], []);
        var elsewhere = new Declared([new StoreTouch(DataStore.TakenTrade, Touch.Read)], []);

        Assert.Equal(["Planted reads taken_trade"], TouchesTheModelAndReadsWhatItMayNot("Planted", takenToo));
        Assert.Equal(["Planted asks ResearchModel"], TouchesTheModelAndReadsWhatItMayNot("Planted", asking));
        Assert.Empty(TouchesTheModelAndReadsWhatItMayNot("Clear", clear));
        Assert.Empty(TouchesTheModelAndReadsWhatItMayNot("Elsewhere", elsewhere));
    }
}
