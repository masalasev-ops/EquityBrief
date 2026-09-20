using System.Globalization;
using System.Net;
using System.Text.RegularExpressions;
using EquityBrief.Api.Passes;
using EquityBrief.Tests.Checks;
using EquityBrief.Tests.Harness;
using EquityBrief.Web.App;
using EquityBrief.Web.Marks;
using EquityBrief.Worker.Research;

namespace EquityBrief.Tests.Reading;

// read-surface, 9.3 and 9.4: the queue screen and the lane the head of the page states.
//
// The screen is read back off its own markup against the store in both directions, so
// neither a request the store lacks nor one it holds and the page omits passes. The lane
// is read off the surface a person reads rather than off the constant that draws it,
// because what the done condition is about is what the operator sees.
public partial class ReadSurface
{
    [Fact]
    public void ARequestIsSettledByWhatThePassCameToAndNeverByWhetherTheVerbRan()
    {
        // The defect the first drain had, kept as a case: it settled on the verb's exit
        // code, so two passes that reported unavailable were recorded as written. A verb
        // that exits without failing has run, and a pass that ran is not a pass that
        // wrote.
        var wrote = RequestDrain.SettlementFor(RequestDrain.Ok);

        Assert.Equal(ResearchRequests.Written, wrote.State);
        Assert.Null(wrote.Reason);

        // Every other outcome the runner writes settles the request as refused and carries
        // the run's own word for why, so the queue screen says what came of it.
        foreach (var outcome in new[] { "unavailable", "failed", "paused", "nothing to write" })
        {
            var refused = RequestDrain.SettlementFor(outcome);

            Assert.Equal(ResearchRequests.Refused, refused.State);
            Assert.Contains(outcome, refused.Reason!, StringComparison.Ordinal);
        }

        // A request whose pass left no run at all is refused and says that, rather than
        // being read as written because nothing contradicted it.
        var absent = RequestDrain.SettlementFor(null);

        Assert.Equal(ResearchRequests.Refused, absent.State);
        Assert.Contains("no run at all", absent.Reason!, StringComparison.Ordinal);

        // And the word the runner writes for a pass that ran to its end is the word this
        // reads, rather than a second spelling of it kept beside the check.
        Assert.Equal("ok", RequestDrain.Ok);
    }
}
