using System.Net;
using EquityBrief.Core.Loop;
using EquityBrief.Core.Time;
using EquityBrief.Tests.Harness;
using EquityBrief.Web.App;
using EquityBrief.Web.Marks;

namespace EquityBrief.Tests.Reading;

// read-surface, 24.2: inside the rule freeze the Monthly check-up offers no approval and no restore and says why, a press
// of either is refused with nothing written, and a decline is still written; on the day the freeze ends both are offered
// and an approval is written, read off the rendered pages over a constructed store on each side of the freeze's last day.
// see: An approval inside the rule freeze is refused and replaces no live rule
public partial class ReadSurface
{
    [Fact]
    public async Task InsideTheRuleFreezeTheCheckUpOffersNoApprovalOrRestoreAndRefusesEitherPressAndOnTheDayItEndsBothAreOffered()
    {
        using var store = new TemporaryStore().Migrated();

        // October's S&P 600 run: a breakout exit and a pullback condition passing and undecided, and a drift exit approved
        // and applied as change 1, flagged by the live alarm so its restore is put.
        Run(store, SmallOctober, "SML", "2026-10");
        Proposed(store, SmallOctober, "SML", "breakout", "the autopsy's exit, ranked 1", true, Exit(7));
        Proposed(store, SmallOctober, "SML", "pullback", "winners against losers, ranked 1", true, Condition("rsi", 60));
        Proposed(store, SmallOctober, "SML", "drift", "the autopsy's exit, ranked 1", true, Exit(8));
        Decided(store, SmallOctober, "SML", "drift", "the autopsy's exit, ranked 1", LoopDecisions.Approved);
        store.Execute($"INSERT INTO loop_applied (run_id, index_code, family, proposal, applied_at, outcome, words) VALUES ('{SmallOctober}', 'SML', 'drift', 'the autopsy''s exit, ranked 1', '2026-10-09T23:35:00Z', 'applied', 'the family stands from the next night at exit: the eighth');");
        store.Execute($"INSERT INTO provisional_setting (id, index_code, family, change, words, set_at, run_id, proposal) VALUES (1, 'SML', 'drift', '{Exit(8)}', 'exit: the eighth', '2026-10-09T23:35:00Z', '{SmallOctober}', 'the autopsy''s exit, ranked 1');");
        store.Execute(
            "INSERT INTO loop_alarm (index_code, family, period, trades, edge, edge_floor, counted, under, streak, flagged, reference, run_id) VALUES " +
            $"('SML', 'drift', '2026-07-01', 6, -0.4, -0.1, 1, 1, 1, 0, '{SmallOctober}', 'night-1'), ('SML', 'drift', '2026-08-01', 5, -0.3, -0.1, 1, 1, 2, 1, '{SmallOctober}', 'night-2');");

        const string Frozen = "the rules are frozen until the monthly run of 6 Feb 2027, so an approval changes no rule before it";
        string Decisions()
        {
            using var connection = store.Open();
            using var command = connection.CreateCommand();

            command.CommandText = "SELECT family || '|' || decision FROM loop_decision ORDER BY family, decided_at;";

            using var reader = command.ExecuteReader();
            var rows = new List<string>();

            while (reader.Read())
            {
                rows.Add(reader.GetString(0));
            }

            return string.Join(";", rows);
        }

        // The freeze's last day, Friday 5 Feb 2027 in the evening.
        using (var host = new Host(store.Root, FixedClock.At(new DateTimeOffset(2027, 2, 5, 23, 0, 0, TimeSpan.Zero), SessionZones.UnitedStates)))
        using (var client = host.CreateClient())
        {
            var page = await LoopWhole(client, "universe=600");
            var breakout = Assert.Single(Blocks(page, "<div class=\"loop-decision\" data-loop-key=\"breakout\\|the autopsy's exit, ranked 1\">.*?</div>"));

            Assert.Contains($"<p class=\"degraded\" data-decision=\"frozen\">Approve is not offered: {ReaderText.Words(Frozen)}. Decline to record why not; it changes nothing.</p>", breakout, StringComparison.Ordinal);
            Assert.DoesNotContain("value=\"approve\"", breakout, StringComparison.Ordinal);
            Assert.Contains("<input type=\"hidden\" name=\"decision\" value=\"decline\"><label>Reason <input name=\"reason\" required></label> <button type=\"submit\">Decline</button>", breakout, StringComparison.Ordinal);
            Assert.Contains($"<p class=\"degraded\" data-restore=\"frozen\">No restore is offered: {ReaderText.Words(Frozen)}.</p>", page, StringComparison.Ordinal);
            Assert.DoesNotContain("Approve the restore", page, StringComparison.Ordinal);

            // An approval and a restore pressed are refused with why, writing nothing; a decline is written.
            var approved = await CardPress(client, Decide("SML", SmallOctober, "breakout"), [("proposal", "the autopsy's exit, ranked 1"), ("decision", "approve")]);

            Assert.Contains($"Nothing was approved: {Frozen}. Decline still records your reason.", approved.Said, StringComparison.Ordinal);
            Assert.Contains($"Nothing was restored: {Frozen}.", (await CardPress(client, $"{SinglePageApp.LoopRestorePostRoute}SML/drift/1", [])).Said, StringComparison.Ordinal);
            Assert.Equal("drift|approved", Decisions());
            Assert.Equal(HttpStatusCode.OK, (await CardPress(client, Decide("SML", SmallOctober, "pullback"), [("proposal", "winners against losers, ranked 1"), ("decision", "decline"), ("reason", "not inside the freeze")])).Status);
            Assert.Equal("drift|approved;pullback|declined", Decisions());
        }

        // The day the freeze ends, Saturday 6 Feb 2027 at the monthly run: Approve and the restore are offered again, and an
        // approval and a restore pressed are written.
        using (var host = new Host(store.Root, FixedClock.At(new DateTimeOffset(2027, 2, 6, 12, 0, 0, TimeSpan.Zero), SessionZones.UnitedStates)))
        using (var client = host.CreateClient())
        {
            var page = await LoopWhole(client, "universe=600");

            Assert.DoesNotContain("data-decision=\"frozen\"", page, StringComparison.Ordinal);
            Assert.Contains($"action=\"{Decide("SML", SmallOctober, "breakout")}\" data-loop-key=\"breakout|the autopsy's exit, ranked 1\"><input type=\"hidden\" name=\"proposal\" value=\"the autopsy's exit, ranked 1\"><input type=\"hidden\" name=\"decision\" value=\"approve\"><button type=\"submit\">Approve</button></form>", page, StringComparison.Ordinal);
            Assert.Contains("<button type=\"submit\">Approve the restore</button>", page, StringComparison.Ordinal);
            Assert.Equal(HttpStatusCode.OK, (await CardPress(client, Decide("SML", SmallOctober, "breakout"), [("proposal", "the autopsy's exit, ranked 1"), ("decision", "approve")])).Status);
            Assert.Contains("restored before the next night", (await CardPress(client, $"{SinglePageApp.LoopRestorePostRoute}SML/drift/1", [])).Said, StringComparison.Ordinal);
            Assert.Equal("breakout|approved;drift|approved;drift|approved;pullback|declined", Decisions());
        }
    }
}
