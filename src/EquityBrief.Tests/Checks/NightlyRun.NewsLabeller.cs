using EquityBrief.Worker.Backup;
using EquityBrief.Worker.News;

namespace EquityBrief.Tests.Checks;

// nightly-run, 12.6 and the 12.6 correction that moves the labeller to the off-peak run: the night starts no news
// labeller, its own row after the request saying the off-peak run labels tonight's list, with no model call on it,
// and a night run again for an earlier session says its list is not tonight's; and, from the operator's ruling of
// 2026-10-02, the store's copy last, which waits for no labeller since the night starts none.
// see: The store is copied once the night and every process it started have finished and the newest three copies are kept after each is opened and read, and the copy writes a row as it starts and one as it ends
// see: The news labeller is a process of its own the off-peak run starts, and its calls and its spend are its own
// see: The night queues its report requests and starts neither the drain nor the labeller, and the off-peak run starts both at 16:30Z from main's own commit
public partial class NightlyRun
{
    [Fact]
    public async Task TheNightStartsNoLabellerAndItsRowSaysTheOffPeakRunLabelsTonightsList()
    {
        using var store = new TemporaryStore();

        var launcher = new NightLauncherForTheSuite();
        var (code, _, error) = await NightAsync(store, launcher: launcher);

        Assert.True(code == 0, error);
        Assert.Equal("label-news", NewsLabeller.NightStage);
        Assert.Equal((0, 0), (launcher.LabellerStarts, launcher.Started));
        Assert.Equal(EquityBrief.Worker.Nightly.LabelledByTheOffPeakRun, Texts(store, $"SELECT detail FROM run_log WHERE stage = '{NewsLabeller.NightStage}';").Single());
        Assert.Equal(0, Scalar(store, $"SELECT model_calls FROM run_log WHERE stage = '{NewsLabeller.NightStage}';"));

        // The step comes after the report's, and the store's copy last, waiting for no labeller.
        Assert.Equal(
            ["report", NewsLabeller.NightStage, StoreBackup.NightStage],
            Texts(store, "SELECT stage FROM run_log WHERE run_id LIKE 'night-%' AND instr(run_id, '-queue-') = 0 ORDER BY rowid DESC LIMIT 3;").Reverse());
        Assert.Equal([false], launcher.BackupStarts);
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

        // The night's own source asks its launcher for neither, so a night whose list asks for reports leaves them
        // queued as the fixture's night, which asks for none, shows.
        var source = File.ReadAllText(Path.Combine(Repository.Root, "src", "EquityBrief.Worker", "Nightly.cs"));

        Assert.DoesNotContain(".StartTheLabeller(", source, StringComparison.Ordinal);
        Assert.DoesNotContain("launcher?.Start()", source, StringComparison.Ordinal);
        Assert.DoesNotContain("launcher.Start()", source, StringComparison.Ordinal);
        Assert.Contains("ask.Line + \". \" + QueuedForTheOffPeakRun", source, StringComparison.Ordinal);

        // And a night handed nothing to start one with writes the same labeller row and starts no copy, saying so.
        using var bare = new TemporaryStore();

        Assert.Equal(0, (await NightAsync(bare)).Code);
        Assert.Equal(EquityBrief.Worker.Nightly.LabelledByTheOffPeakRun, Texts(bare, $"SELECT detail FROM run_log WHERE stage = '{NewsLabeller.NightStage}';").Single());
        Assert.StartsWith("No copy of the store was started", Texts(bare, $"SELECT detail FROM run_log WHERE stage = '{StoreBackup.NightStage}';").Single(), StringComparison.Ordinal);
    }
}
