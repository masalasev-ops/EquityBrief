using System.Globalization;
using EquityBrief.Core.Levels;
using EquityBrief.Core.Time;
using EquityBrief.Tests.Checks;

namespace EquityBrief.Tests.Time;

// InvariantGlobalization stays false, because it is the setting that silently
// breaks IANA timezone lookup. Asserted twice: once over the running process,
// where the fault would actually appear, and once over the project files, where
// it would be introduced.
public class GlobalizationTests
{
    [Fact]
    public void TheRunningProcessCarriesTheTimezoneDatabase()
    {
        var invariant = AppContext.TryGetSwitch("System.Globalization.Invariant", out var value) && value;

        Assert.False(invariant);
        Assert.NotNull(SessionZones.ResolveSessionZone(SessionZones.UnitedStates));
    }

    [Fact]
    public void NoProjectFileTurnsItOn()
    {
        var files = Repository.ProjectFiles().Append(Repository.DirectoryBuildProps).ToArray();

        Assert.True(files.Length >= 7, $"Read {files.Length} build files, expected at least 7.");

        var turnedOn = files
            .Where(file => string.Equals(
                ProjectFile.ValueOf(File.ReadAllText(file), "InvariantGlobalization"),
                "true",
                StringComparison.OrdinalIgnoreCase))
            .ToArray();

        Assert.Empty(turnedOn);
    }

    [Fact]
    public void TheCheckReportsItWhereItIsTurnedOn()
    {
        // The permanent proof that the assertion can fail.
        Assert.Equal("true", ProjectFile.ValueOf(
            "<Project><PropertyGroup><InvariantGlobalization>true</InvariantGlobalization>" +
            "</PropertyGroup></Project>",
            "InvariantGlobalization"));

        Assert.Null(ProjectFile.ValueOf("<Project />", "InvariantGlobalization"));
    }

    [Fact]
    public void TheSentencesStoredInTheLevelAndLadderRowsCarryADotWhateverTheMachinesCultureIs()
    {
        // The phase 5 sign-off found numbers formatted in the machine's culture
        // into stored text, in `LevelSeries` and `LadderSeries`. Both render a
        // price or a count into a sentence that lands in `level.members` and
        // `ladder.plan`, so the same store written on a machine whose decimal
        // separator is a comma differs from one written here, and no column type
        // refuses it: it is text either way.
        //
        // `clock-usage` never saw it, because that reader is keyed on date
        // formats. This is the number half and it has no scan of its own, so the
        // property is asserted by running the renderers under a culture that
        // would show the fault.
        var was = CultureInfo.CurrentCulture;

        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("de-DE");

            var members = LevelSeries.RetracementsBetween(
                (200m, new DateOnly(2026, 3, 6)),
                (100m, new DateOnly(2026, 3, 2)));

            var kinds = string.Join(" ", members.Select(member => member.Kind));

            Assert.Contains("38.2", kinds, StringComparison.Ordinal);
            Assert.DoesNotContain("38,2", kinds, StringComparison.Ordinal);
        }
        finally
        {
            CultureInfo.CurrentCulture = was;
        }
    }
}
