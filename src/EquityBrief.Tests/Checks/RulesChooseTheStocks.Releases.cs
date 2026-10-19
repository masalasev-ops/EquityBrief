using EquityBrief.Core.Components;
using EquityBrief.Tests.Harness;
using DataStore = EquityBrief.Core.Components.Store;
using Declared = EquityBrief.Core.Components.ComponentAccess;

namespace EquityBrief.Tests.Checks;

// rules-choose-the-stocks, 20.4: the release readings are the one thing a model wrote that a component deciding a pick may
// read. It reads them through the one reader of the release readings alone, which hands a rule a release only under a
// registered version whose history read is complete, so no source under the deciding folders names their table; the
// research sections, the theme sections and the news labels stay barred to every deciding component. From 20.8 the setup
// ledger and the walk-forward tester read them as well, through the same reader.
// see: Rules choose the stocks from stored data and a pinned local model's readings of documents with their names removed, and no model judges a stock
// see: The drift's tone variant lists only where its print's release reads better than expected and its outlook was not lowered or withdrawn
// see: The release readings join the ledger's catalogue once their history read is complete, and their use is judged on the years after the model's cutoff
public partial class RulesChooseTheStocks
{
    // The table a pinned local model's readings of the releases are stored in, and the one reader every component deciding
    // a pick reads them through.
    internal const string ReleaseReadingTable = "release_reading";

    internal const string TheOneReader = "src/EquityBrief.Data/ReleaseReadings.cs";

    internal static IReadOnlyList<string> NamesTheReleaseReadings(string file, string text) =>
        file == TheOneReader || !text.Contains(ReleaseReadingTable, StringComparison.Ordinal) ? [] : [$"{file} names {ReleaseReadingTable}"];

    [Fact]
    public void ADecidingComponentReadsTheReleaseReadingsThroughTheOneReaderAlone()
    {
        var components = ShippedComponents.All();
        var reading = components.Where(component => component.Access.On(DataStore.ReleaseReading) != Touch.None).Select(component => component.Name).Order(StringComparer.Ordinal).ToArray();

        // The scope, by name: the local run writes them, its accuracy report and the read surface read them, and the night's
        // family evaluator and the S&P 400's and 600's families hand them to the drift, as the replay and the cards reading
        // the evaluator's inputs do; and from 20.8 the setup ledger reads them for its four release readings and the
        // walk-forward tester for the tone variant's walk and what each answer came to, through the one reader as every
        // deciding read is.
        Assert.Equal(["FamilyEvaluator", "FamilyReplay", "IndexFamilies", "LocalRun", "ReadApi", "ReleaseAccuracy", "RuleCards", "SetupLedger", "WalkForwardTester"], reading);
        Assert.Equal(["FamilyEvaluator"], Deciding.Where(one => one.Access.On(DataStore.ReleaseReading) != Touch.None).Select(one => one.Name));
        Assert.Equal(
            ["FamilyEvaluator", "IndexFamilies", "SetupLedger", "WalkForwardTester"],
            PickMaking.Where(one => one.Access.On(DataStore.ReleaseReading) != Touch.None).Select(one => one.Name).Order(StringComparer.Ordinal));

        var sources = SourcesUnderTheFolders();
        var naming = sources.SelectMany(file => NamesTheReleaseReadings(file, File.ReadAllText(Path.Combine(Repository.Root, file)))).ToArray();

        Assert.True(sources.Length >= 100, $"Read {sources.Length} sources under the deciding folders, expected at least 100.");
        Assert.True(
            naming.Length == 0,
            $"Of the {sources.Length} sources under the deciding folders, these name the release readings' table rather than reading it through {TheOneReader}: {string.Join("; ", naming)}.");
        Assert.Contains(ReleaseReadingTable, File.ReadAllText(Path.Combine(Repository.Root, TheOneReader)), StringComparison.Ordinal);
    }

    [Fact]
    public void AReadOfEachBarredTableAndOfTheReleaseReadingsOutsideTheOneReaderIsFound()
    {
        // Each barred table read in a declaration is found, and the release readings read in one are not, since a deciding
        // component may read them.
        foreach (var (store, table) in ModelOutput)
        {
            Assert.Equal([$"Planted reads {table}"], ReadsAModel("Planted", new Declared([new StoreTouch(store, Touch.Read)], [])));
        }

        Assert.Empty(ReadsAModel("Clean", new Declared([new StoreTouch(DataStore.ReleaseReading, Touch.Read)], [])));

        // A source under a deciding folder naming the release readings' table is found, and the one reader naming it, or a
        // source calling the one reader, is not.
        Assert.Equal(
            ["src/EquityBrief.Worker/Families/Planted.cs names release_reading"],
            NamesTheReleaseReadings("src/EquityBrief.Worker/Families/Planted.cs", "const string Sql = \"SELECT results FROM release_reading WHERE pin = $pin;\";"));
        Assert.Empty(NamesTheReleaseReadings(TheOneReader, "const string Sql = \"SELECT results FROM release_reading WHERE pin = $pin;\";"));
        Assert.Empty(NamesTheReleaseReadings("src/EquityBrief.Worker/Families/Clean.cs", "var release = await ReleaseReadings.ForRuleAsync(connection, cik, report, night, version, complete, calendar, zone);"));
    }
}
