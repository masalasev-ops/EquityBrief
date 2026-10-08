using EquityBrief.Worker.Backup;
using EquityBrief.Worker.News;

namespace EquityBrief.Tests.Checks;

// nightly-run, 12.6: the night starts the news labeller once after its own request, as a process of its own
// through the launcher it was handed, with no model call on its own row, and starts none for a night run again
// for an earlier session or a night handed nothing to start one with; and, from the operator's ruling of
// 2026-10-02, the store's copy last, waiting for the labeller where the night started one.
// see: The store is copied once the night and every process it started have finished and the newest three copies are kept after each is opened and read, and the copy writes a row as it starts and one as it ends
// see: The news labeller is a process of its own the night starts after the close, and its calls and its spend are its own
public partial class NightlyRun
{
    [Fact]
    public async Task TheNightStartsTheLabellerAfterTheReportAsAProcessOfItsOwnAndStartsNoneForAnEarlierSession()
    {
        using var store = new TemporaryStore();

        var launcher = new NightLauncherForTheSuite();
        var (code, _, error) = await NightAsync(store, launcher: launcher);

        Assert.True(code == 0, error);
        Assert.Equal("label-news", NewsLabeller.NightStage);
        Assert.Equal(1, launcher.LabellerStarts);
        Assert.Equal(NightLauncherForTheSuite.LabellerLine, Texts(store, $"SELECT detail FROM run_log WHERE stage = '{NewsLabeller.NightStage}';").Single());
        Assert.Equal(0, Scalar(store, $"SELECT model_calls FROM run_log WHERE stage = '{NewsLabeller.NightStage}';"));

        // The step comes after the report's, and the store's copy last, waiting for the labeller started.
        Assert.Equal(
            ["report", NewsLabeller.NightStage, StoreBackup.NightStage],
            Texts(store, "SELECT stage FROM run_log WHERE run_id LIKE 'night-%' AND instr(run_id, '-queue-') = 0 ORDER BY rowid DESC LIMIT 3;").Reverse());
        Assert.Equal([true], launcher.BackupStarts);
        Assert.Equal(NightLauncherForTheSuite.BackupLine, Texts(store, $"SELECT detail FROM run_log WHERE stage = '{StoreBackup.NightStage}';").Single());
        Assert.Equal(0, Scalar(store, $"SELECT model_calls FROM run_log WHERE stage = '{StoreBackup.NightStage}';"));

        // A night run again for an earlier session starts no labeller and says so, and starts a copy that waits for none.
        using var earlier = new TemporaryStore();

        var still = new NightLauncherForTheSuite();
        var (again, _, said) = await NightAsync(earlier, launcher: still, askForTheFirstName: false);

        Assert.True(again == 0, said);
        Assert.Equal(0, still.LabellerStarts);
        Assert.Equal("no labeller was started, since this night was run again for an earlier session", Texts(earlier, $"SELECT detail FROM run_log WHERE stage = '{NewsLabeller.NightStage}';").Single());
        Assert.Equal([false], still.BackupStarts);

        // And a night handed nothing to start one with says so and starts neither.
        using var bare = new TemporaryStore();

        Assert.Equal(0, (await NightAsync(bare)).Code);
        Assert.StartsWith("No labeller was started", Texts(bare, $"SELECT detail FROM run_log WHERE stage = '{NewsLabeller.NightStage}';").Single(), StringComparison.Ordinal);
        Assert.StartsWith("No copy of the store was started", Texts(bare, $"SELECT detail FROM run_log WHERE stage = '{StoreBackup.NightStage}';").Single(), StringComparison.Ordinal);
    }
}
