using EquityBrief.Core.Components;
using EquityBrief.Worker.Candidates;
using EquityBrief.Worker.Families;
using EquityBrief.Worker.Filter;
using EquityBrief.Worker.Indicators;
using EquityBrief.Worker.Ladders;
using EquityBrief.Worker.Levels;
using EquityBrief.Worker.Quarters;
using EquityBrief.Worker.Returns;
using EquityBrief.Worker.Rules;
using EquityBrief.Worker.Shortlist;
using EquityBrief.Worker.Sweep;
using EquityBrief.Worker.Swings;
using EquityBrief.Worker.Volume;
using DataStore = EquityBrief.Core.Components.Store;
using Declared = EquityBrief.Core.Components.ComponentAccess;

namespace EquityBrief.Tests.Checks;

// rules-choose-the-stocks. No model's output decides, filters, ranks or orders a pick, a family, a variant, a gate
// or a record: every component that does declares no read of what a model wrote and no model or search among its
// feeds, and no source under the folders their code runs through names a table a model's output is stored in.
// see: Rules choose the stocks, and the AI writes the reports
public partial class RulesChooseTheStocks
{
    // The components that decide a pick, a list, a family, a variant, a gate or a record, and the arithmetic their
    // gates and plans read. A component added that decides joins this list.
    internal static readonly (string Name, Declared Access)[] Deciding =
    [
        (nameof(IndicatorEngine), IndicatorEngine.Access),
        (nameof(SwingFinder), SwingFinder.Access),
        (nameof(VolumeProfileBuilder), VolumeProfileBuilder.Access),
        (nameof(LevelBuilder), LevelBuilder.Access),
        (nameof(TrendClassifier), TrendClassifier.Access),
        (nameof(LadderBuilder), LadderBuilder.Access),
        (nameof(ShortlistBuilder), ShortlistBuilder.Access),
        (nameof(FundamentalReader), FundamentalReader.Access),
        (nameof(SwingReader), SwingReader.Access),
        (nameof(SwingFilter), SwingFilter.Access),
        (nameof(ShapeProposer), ShapeProposer.Access),
        (nameof(ShapeCommand), ShapeCommand.Access),
        (nameof(FamilyEvaluator), FamilyEvaluator.Access),
        (nameof(FamilyLister), FamilyLister.Access),
        (nameof(FamilyRecorder), FamilyRecorder.Access),
        (nameof(HeavyweightBook), HeavyweightBook.Access),
        (nameof(ForwardReturnFiller), ForwardReturnFiller.Access),
        (nameof(RuleVersionScorer), RuleVersionScorer.Access),
        (nameof(CandidateRegistrar), CandidateRegistrar.Access),
        (nameof(SweepHistory), SweepHistory.Access),
        (nameof(SweepPointInTime), SweepPointInTime.Access),
    ];

    // The folders the deciding components' code runs through, in the core and in the worker.
    internal static readonly string[] Folders =
    [
        .. new[] { "Candidates", "Families", "Filter", "Indicators", "Ladders", "Levels", "Quarters", "Returns", "Rules", "Shortlist", "Sweep", "Swings", "Volume" }
            .SelectMany(folder => new[] { $"src/EquityBrief.Core/{folder}", $"src/EquityBrief.Worker/{folder}" }),
    ];

    // Where a model's output is stored, by the store an access declaration names and by the table a query names.
    internal static readonly (DataStore Store, string Table)[] ModelOutput =
    [
        (DataStore.ResearchSection, "research_section"),
        (DataStore.ThemeSection, "theme_section"),
        (DataStore.NewsLabel, "news_label"),
    ];

    // The feeds that are a model or a search a model is handed.
    internal static readonly Feed[] Models = [Feed.ResearchModel, Feed.LocalModel, Feed.SearchTool];

    internal static IReadOnlyList<string> ReadsAModel(string name, Declared access) =>
    [
        .. ModelOutput.Where(output => access.On(output.Store) != Touch.None).Select(output => $"{name} reads {output.Table}"),
        .. Models.Where(feed => access.Feeds.Contains(feed)).Select(feed => $"{name} asks {feed}"),
    ];

    internal static IReadOnlyList<string> NamesAModelsOutput(string file, string text) =>
    [
        .. ModelOutput
            .SelectMany(output => new[] { output.Table, output.Store.ToString() })
            .Where(word => text.Contains(word, StringComparison.Ordinal))
            .Select(word => $"{file} names {word}"),
    ];

    static string[] SourcesUnderTheFolders() =>
    [
        .. Folders
            .Select(folder => Path.Combine(Repository.Root, folder))
            .Where(Directory.Exists)
            .SelectMany(folder => Directory.GetFiles(folder, "*.cs", SearchOption.AllDirectories))
            .Select(path => Path.GetRelativePath(Repository.Root, path).Replace('\\', '/'))
            .Order(StringComparer.Ordinal),
    ];

    [Fact]
    public void NoComponentThatDecidesReadsWhatAModelWroteOrAsksAModel()
    {
        // The scope, in numbers and by name.
        Assert.True(Deciding.Length >= 20, $"Read {Deciding.Length} deciding components, expected at least 20: {string.Join(", ", Deciding.Select(one => one.Name))}.");
        Assert.Equal(Deciding.Length, Deciding.Select(one => one.Name).Distinct(StringComparer.Ordinal).Count());

        var reading = Deciding.SelectMany(one => ReadsAModel(one.Name, one.Access)).ToArray();

        Assert.True(
            reading.Length == 0,
            $"Of the {Deciding.Length} deciding components ({string.Join(", ", Deciding.Select(one => one.Name))}), these read what a model wrote or ask a model: {string.Join("; ", reading)}.");
    }

    [Fact]
    public void NoSourceUnderTheDecidingFoldersNamesWhereAModelsOutputIsStored()
    {
        var folders = Folders.Where(folder => Directory.Exists(Path.Combine(Repository.Root, folder))).ToArray();
        var sources = SourcesUnderTheFolders();

        // The scope, in numbers: every folder the list names is there, so one renamed is a fault rather than a
        // narrower scan, and they hold over a hundred sources, so a reader finding a handful reads the wrong place.
        Assert.True(folders.Length == Folders.Length, $"Found {folders.Length} of the {Folders.Length} deciding folders: missing {string.Join(", ", Folders.Except(folders, StringComparer.Ordinal))}.");
        Assert.True(sources.Length >= 100, $"Read {sources.Length} sources under the deciding folders, expected at least 100.");

        var naming = sources
            .SelectMany(file => NamesAModelsOutput(file, File.ReadAllText(Path.Combine(Repository.Root, file))))
            .ToArray();

        Assert.True(
            naming.Length == 0,
            $"Of the {sources.Length} sources under {folders.Length} deciding folders, these name where a model's output is stored: {string.Join("; ", naming)}.");
    }

    [Fact]
    public void AReadOfWhatAModelWroteIsFoundWhereverItIsPlanted()
    {
        // A declaration reading the news labels, and one asking the local model, are each found, and a clean one is not.
        var labels = new Declared([new StoreTouch(DataStore.NewsLabel, Touch.Read)], []);
        var asking = new Declared([new StoreTouch(DataStore.Bar, Touch.Read)], [Feed.LocalModel]);

        Assert.Equal(["Planted reads news_label"], ReadsAModel("Planted", labels));
        Assert.Equal(["Planted asks LocalModel"], ReadsAModel("Planted", asking));
        Assert.Empty(ReadsAModel("Clean", new Declared([new StoreTouch(DataStore.Bar, Touch.Read)], [Feed.BulkPrice])));

        // A source naming a model's output table in a query, or its store in a declaration, is found, and a source
        // naming neither is not.
        Assert.Equal(
            ["Planted.cs names research_section"],
            NamesAModelsOutput("Planted.cs", "const string Sql = \"SELECT body FROM research_section WHERE ticker = $ticker;\";"));
        Assert.Equal(
            ["Planted.cs names ThemeSection"],
            NamesAModelsOutput("Planted.cs", "new StoreTouch(Store.ThemeSection, Touch.Read)"));
        Assert.Empty(NamesAModelsOutput("Clean.cs", "const string Sql = \"SELECT close FROM bar WHERE ticker = $ticker;\";"));
    }
}
