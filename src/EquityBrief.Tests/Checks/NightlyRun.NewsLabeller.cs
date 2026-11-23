using EquityBrief.Core.Configuration;
using EquityBrief.Worker.Backup;
using EquityBrief.Worker.News;

namespace EquityBrief.Tests.Checks;

// nightly-run, 12.6 and the 12.6 correction that moves the labeller to the off-peak run: the night starts no news
// labeller, its own row after the request saying the off-peak run labels tonight's list, with no model call on it,
// and a night run again for an earlier session says its list is not tonight's; and, from the operator's word of
// 2026-11-20, no copy of the store started by the night, its row saying its own run copies it the morning after.
// see: The store is copied by a scheduled run of its own holding the drain's lock for the copy alone, and the oldest beyond two is removed once the new copy is opened and read
// see: The news labeller is a process of its own the off-peak run starts, and labels on the local model under a pin of its own
// see: The night queues its report requests and starts neither the drain nor the labeller, and the off-peak run starts both five minutes after each peak window ends
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

        // The step comes after the report's, the store's copy's row after it, starting no copy, then the queued survivors' row
        // with no model call or request on it, and the AI stock picker last, started once as a process of its own with no model
        // call on the night's row.
        Assert.Equal(
            ["report", NewsLabeller.NightStage, StoreBackup.NightStage, EquityBrief.Worker.Loop.SurvivorDesk.Stage, EquityBrief.Worker.Judge.JudgePicker.NightStage],
            Texts(store, "SELECT stage FROM run_log WHERE run_id LIKE 'night-%' AND instr(run_id, '-queue-') = 0 ORDER BY rowid DESC LIMIT 5;").Reverse());
        Assert.Equal("no survivor queued", Texts(store, $"SELECT detail FROM run_log WHERE stage = '{EquityBrief.Worker.Loop.SurvivorDesk.Stage}';").Single());
        Assert.Equal(0, Scalar(store, $"SELECT model_calls + network_requests FROM run_log WHERE stage = '{EquityBrief.Worker.Loop.SurvivorDesk.Stage}';"));
        Assert.Equal(1, launcher.JudgeStarts);
        Assert.Equal(NightLauncherForTheSuite.JudgeLine, Texts(store, $"SELECT detail FROM run_log WHERE stage = '{EquityBrief.Worker.Judge.JudgePicker.NightStage}';").Single());
        Assert.Equal(0, Scalar(store, $"SELECT model_calls FROM run_log WHERE stage = '{EquityBrief.Worker.Judge.JudgePicker.NightStage}';"));
        Assert.Equal(EquityBrief.Worker.Nightly.CopiedByItsOwnRun, Texts(store, $"SELECT detail FROM run_log WHERE stage = '{StoreBackup.NightStage}';").Single());
        Assert.Equal(0, Scalar(store, $"SELECT model_calls FROM run_log WHERE stage = '{StoreBackup.NightStage}';"));
        Assert.Empty(StoreCopies.In(Path.Combine(store.Root, StoreCopies.DefaultFolder)));

        // A night run again for an earlier session starts no labeller and says so, and starts no copy.
        using var earlier = new TemporaryStore();

        var still = new NightLauncherForTheSuite();
        var (again, _, said) = await NightAsync(earlier, launcher: still, askForTheFirstName: false);

        Assert.True(again == 0, said);
        Assert.Equal(0, still.LabellerStarts);
        Assert.Equal("no labeller was started, since this night was run again for an earlier session", Texts(earlier, $"SELECT detail FROM run_log WHERE stage = '{NewsLabeller.NightStage}';").Single());
        Assert.Equal(EquityBrief.Worker.Nightly.CopiedByItsOwnRun, Texts(earlier, $"SELECT detail FROM run_log WHERE stage = '{StoreBackup.NightStage}';").Single());
        Assert.Equal(0, still.JudgeStarts);
        Assert.Equal("no picker was started, since this night was run again for an earlier session", Texts(earlier, $"SELECT detail FROM run_log WHERE stage = '{EquityBrief.Worker.Judge.JudgePicker.NightStage}';").Single());

        // The night's own source asks its launcher for neither, so a night whose list asks for reports leaves them
        // queued as the fixture's night, which asks for none, shows.
        var source = File.ReadAllText(Path.Combine(Repository.Root, "src", "EquityBrief.Worker", "Nightly.cs"));

        Assert.DoesNotContain(".StartTheLabeller(", source, StringComparison.Ordinal);
        Assert.DoesNotContain(".StartTheBackup(", source, StringComparison.Ordinal);
        Assert.DoesNotContain("launcher?.Start()", source, StringComparison.Ordinal);
        Assert.DoesNotContain("launcher.Start()", source, StringComparison.Ordinal);
        Assert.Contains("ask.Line + \". \" + QueuedForTheOffPeakRun", source, StringComparison.Ordinal);

        // And a night handed nothing to start one with writes the same two rows.
        using var bare = new TemporaryStore();

        Assert.Equal(0, (await NightAsync(bare)).Code);
        Assert.Equal(EquityBrief.Worker.Nightly.LabelledByTheOffPeakRun, Texts(bare, $"SELECT detail FROM run_log WHERE stage = '{NewsLabeller.NightStage}';").Single());
        Assert.Equal(EquityBrief.Worker.Nightly.CopiedByItsOwnRun, Texts(bare, $"SELECT detail FROM run_log WHERE stage = '{StoreBackup.NightStage}';").Single());
    }
}
