using EquityBrief.Worker.Indices;
using EquityBrief.Worker.Research;
using DataStore = EquityBrief.Core.Components.Store;
using Declared = EquityBrief.Core.Components.ComponentAccess;

namespace EquityBrief.Tests.Checks;

// rules-choose-the-stocks, 16.3: the operator's own trades and record decide nothing. No component that decides a pick, a
// list, a family, a variant, a gate or a record, nor the S&P 400's and 600's families, the overnight queue or the drain
// that writes the night's report requests, declares a read of the taken trades or the operator's record, and no source
// under the folders their code runs through names either table.
// see: A taken trade's fill is the next session's open once its bar is stored, and the plan's buy marked provisional until then
// see: The operator's own record states its average result once twenty of its trades in a family and index have ended
public partial class RulesChooseTheStocks
{
    // The tables the operator's own trades and record are stored in.
    internal static readonly (DataStore Store, string Table)[] OperatorsOwn =
    [
        (DataStore.TakenTrade, "taken_trade"),
        (DataStore.TakenRecord, "taken_record"),
    ];

    // Every component that makes a pick, keeps a list or a record, or asks for a report.
    static (string Name, Declared Access)[] PickMaking =>
    [
        .. Deciding,
        (nameof(IndexFamilies), IndexFamilies.Access),
        (nameof(OvernightQueue), OvernightQueue.Access),
        (nameof(RequestDrain), RequestDrain.Access),
        // From 17.3 the setup ledger, which every engine reads, so a taken trade reaching it would reach them all.
        // see: The operator's taken trades never feed a pick, a rule, an engine or a test
        (nameof(EquityBrief.Worker.Ledger.SetupLedger), EquityBrief.Worker.Ledger.SetupLedger.Access),
        // From 17.4 the walk-forward tester, which judges every proposal.
        (nameof(EquityBrief.Worker.Loop.WalkForwardTester), EquityBrief.Worker.Loop.WalkForwardTester.Access),
    ];

    internal static IReadOnlyList<string> ReadsTheOperatorsOwn(string name, Declared access) =>
        [.. OperatorsOwn.Where(own => access.On(own.Store) != EquityBrief.Core.Components.Touch.None).Select(own => $"{name} reads {own.Table}")];

    [Fact]
    public void NoComponentThatMakesAPickReadsTheOperatorsTradesOrRecord()
    {
        var components = PickMaking;

        // The scope, in numbers: the deciding list and the five named beside it.
        Assert.Equal(Deciding.Length + 5, components.Length);
        Assert.Empty(components.SelectMany(component => ReadsTheOperatorsOwn(component.Name, component.Access)));

        var sources = Folders
            .Select(folder => Path.Combine(Repository.Root, folder))
            .Where(Directory.Exists)
            .SelectMany(folder => Directory.GetFiles(folder, "*.cs", SearchOption.AllDirectories))
            .Concat(Directory.GetFiles(Path.Combine(Repository.Root, "src/EquityBrief.Worker/Indices"), "*.cs"))
            .Concat(Directory.GetFiles(Path.Combine(Repository.Root, "src/EquityBrief.Worker/Research"), "*.cs"))
            .ToArray();

        Assert.True(sources.Length >= 100, $"Read {sources.Length} sources under the folders the pick-making components run through, expected at least 100.");
        Assert.Empty(sources.SelectMany(file => OperatorsOwn
            .Where(own => File.ReadAllText(file).Contains(own.Table, StringComparison.Ordinal))
            .Select(own => $"{Path.GetFileName(file)} names {own.Table}")));
    }

    [Fact]
    public void TheReaderOfTheOperatorsOwnFindsAPlantedReadAndPassesAClearOne()
    {
        var planted = new Declared([new EquityBrief.Core.Components.StoreTouch(DataStore.TakenTrade, EquityBrief.Core.Components.Touch.Read)], []);
        var clear = new Declared([new EquityBrief.Core.Components.StoreTouch(DataStore.WatchList, EquityBrief.Core.Components.Touch.Read)], []);

        Assert.Equal(["Planted reads taken_trade"], ReadsTheOperatorsOwn("Planted", planted));
        Assert.Empty(ReadsTheOperatorsOwn("Clear", clear));
    }
}
