using EquityBrief.Worker.Fills;
using EquityBrief.Worker.Returns;

namespace EquityBrief.Tests.Checks;

// nightly-run, 19.10's move: the night's forward returns step runs the fill step after the forward return filler, so the
// page list's plans scored tonight are priced at their next open with every other trade, under a stage of its own that
// makes no request and calls no model.
// see: A trade's entry is priced at the next session's open by a fill step of its own, and no rule's version moves because of it
public partial class NightlyRun
{
    [Fact]
    public async Task TheForwardReturnsStepRunsTheFillStepAfterTheFillerUnderAStageOfItsOwn()
    {
        var (store, code, _, _) = await FixtureReplay.NightAsync();

        using (store)
        {
            Assert.Equal(0, code);

            var stages = Texts(store, $"SELECT stage FROM run_log WHERE stage IN ('{ForwardReturnFiller.Stage}', '{TradeFiller.Stage}') ORDER BY rowid;");

            Assert.Equal([ForwardReturnFiller.Stage, TradeFiller.Stage], stages);
            Assert.Equal(["0|0|0"], Texts(store, $"SELECT model_calls || '|' || network_requests || '|' || spend FROM run_log WHERE stage = '{TradeFiller.Stage}';"));
            Assert.StartsWith("0 trade(s) priced at the next session's open", Texts(store, $"SELECT detail FROM run_log WHERE stage = '{TradeFiller.Stage}';").Single(), StringComparison.Ordinal);
        }
    }
}
