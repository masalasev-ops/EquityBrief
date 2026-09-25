using System.Reflection;
using EquityBrief.Core;
using EquityBrief.Core.Filter;
using EquityBrief.Tests.Harness;
using EquityBrief.Worker.Filter;

namespace EquityBrief.Tests.Checks;

// fixture-expectations, 12.2: the code every gate row names is the pin of every source the filter's
// answers and the readings beneath them run through, so a change to any of them is a row a later
// reader can tell apart rather than one written as though nothing moved.
public partial class FixtureExpectations
{
    static MethodBase[] WhereTheFilterRuns =>
    [
        typeof(SwingFilter).GetMethod(nameof(SwingFilter.RunAsync))!,
        typeof(SwingReader).GetMethod(nameof(SwingReader.RunAsync))!,
        typeof(SwingGates).GetMethod(nameof(SwingGates.Evaluate))!,
        typeof(SwingGates).GetMethod(nameof(SwingGates.Ranked))!,
        typeof(SwingFunnel).GetMethod(nameof(SwingFunnel.Of))!,
    ];

    [Fact]
    public void TheFiltersCodeVersionIsThePinOfEverySourceItsAnswersAndReadingsRunThrough()
    {
        // The list is held to what the compiled code reaches, so a rule moved into a file the list does
        // not name fails here rather than going unpinned.
        Assert.All(WhereTheFilterRuns, Assert.NotNull);

        var reached = SourcesReached.From(WhereTheFilterRuns);

        Assert.Equal(reached, [.. SwingFilter.CodeVersionSources.Order(StringComparer.Ordinal)]);

        var sources = SwingFilter.CodeVersionSources
            .Select(path => File.ReadAllText(Path.Combine(Repository.Root, path)))
            .ToArray();

        Assert.Equal(SwingFilter.CodeVersion, SourcePin.Of(sources, SwingFilter.CodeVersionDeclaration));

        // Every source moves it, and the declaring line moves nothing.
        for (var at = 0; at < sources.Length; at++)
        {
            var moved = sources.ToArray();
            moved[at] += "\n// a changed line";

            Assert.NotEqual(SwingFilter.CodeVersion, SourcePin.Of(moved, SwingFilter.CodeVersionDeclaration));
        }
    }
}
