using EquityBrief.Api.Reading;
using EquityBrief.Tests.Checks;
using EquityBrief.Tests.Harness;
using EquityBrief.Worker.Candidates;
using EquityBrief.Worker.Cards;

namespace EquityBrief.Tests.Reading;

// The Tonight pages the plain-words check reads, from 22.6: each drawn over a night the read-surface tests construct, so
// every kind of card the page draws is read in the reader's words: picks and stocks one step short on the S&P 500, a
// version chosen on its card, the sector heavyweights holding, the pick cards the night stored, an S&P 400 night with
// its pick cards, a fixed breakout on the S&P 400 and an S&P 400 night whose market check closed.
// see: A redesigned page speaks in the reader's words: every card opens on a sentence from its numbers, and no internal term reaches the screen outside the glossary
public partial class ReadSurface
{
    // The rows 22.6 adds that this check reaches: Tonight in one minute and the page's words at its foot.
    internal static string[] TonightRows =>
    [
        CheckReach.Key("15.7 Tonight", "Tonight in one minute"),
        CheckReach.Key("15.7 Tonight", "Words used on this page"),
    ];

    internal static async Task<IReadOnlyList<(string Night, string Page)>> TonightPagesForPlainWords()
    {
        var pages = new List<(string, string)>();

        async Task Read(string night, TemporaryStore store, params string[] routes)
        {
            using var host = new Host(store.Root);
            using var client = host.CreateClient();

            foreach (var route in routes)
            {
                pages.Add((night + " " + route, await client.GetStringAsync(route)));
            }
        }

        using (var picks = await FamilyPagesStore())
        {
            await Read("the S&P 500's picks and stocks one step short", picks, $"/screens/tonight/{TheSwitch}");

            await new DecisionCards(new WaitedClock(new DateTimeOffset(2026, 10, 2, 23, 50, 0, TimeSpan.Zero)), picks.DatabaseFile).RunAsync("cards");
            await Read("the S&P 500's pick cards", picks, $"/screens/tonight/{TheSwitch}");
        }

        using (var versions = await FamilyPagesStore())
        {
            RegisteredRule(versions, 101, TheSetupFamilies.Breakouts[0], "2026-01-02T12:00:00Z");
            RegisteredRule(versions, 103, TheSetupFamilies.Breakouts[1], "2026-01-02T12:00:00Z");
            Kept(versions, NeighbourBreakout, "breakout", "K3", TheSwitch, null, null, null);
            RuleNightRow(versions, "GSPC", TheSwitch, "breakout", LiveBreakout, 2, EquityBrief.Core.Cards.RuleRows.GatesJson([("market", 10), ("new high", 4), ("volume", 3), ("tightened", 2), ("trade", 2)]), 0, 7, false, 31, 600);
            RuleNightRow(versions, "GSPC", TheSwitch, "breakout", NeighbourBreakout, 1, EquityBrief.Core.Cards.RuleRows.GatesJson([("market", 10), ("new high", 3), ("volume", 2), ("tightened", 1), ("trade", 1)]), 0, null, false, 2, 4);

            await Read("a version chosen on the S&P 500", versions, $"/screens/tonight/{TheSwitch}?breakout={RunScreen.Slug(NeighbourBreakout)}", $"/screens/tonight/{TheSwitch}");
        }

        using (var book = await FamilyNightStore())
        {
            HeavyweightBookOn(book);
            await Read("the sector heavyweights holding", book, $"/screens/tonight/{TheSwitch}");
        }

        using (var taken = TakenStore())
        {
            await new DecisionCards(new WaitedClock(new DateTimeOffset(2026, 10, 2, 23, 50, 0, TimeSpan.Zero)), taken.DatabaseFile).RunAsync("cards");
            await Read("the S&P 400's and 600's pick cards", taken, $"/screens/tonight/{IndexNight}?universe=400", $"/screens/tonight/{IndexNight}?universe=600");
        }

        using (var frozen = UniversesStore())
        {
            RecordFigured(frozen, "20261001T120000Z", "breakout", null, "none passed", "2026-10-01T12:00:00Z", MidBreakout);
            ReadByTheLiveBreakout(frozen, FreezeTheBreakout(frozen, "2026-10-02T12:00:00Z"));
            await Read("a fixed breakout on the S&P 400", frozen, $"/screens/tonight/{IndexNight}?universe=400");
        }

        using (var closed = FixtureExpectations.ClosedIndexStore())
        {
            var night = new DateTimeOffset(2026, 10, 2, 23, 40, 0, TimeSpan.Zero);

            await new EquityBrief.Worker.Indices.IndexFamilies(EquityBrief.Core.Time.FixedClock.At(night, EquityBrief.Core.Time.SessionZones.UnitedStates), closed.DatabaseFile).RunAsync("night-words", default, [], night);
            closed.Execute($"INSERT INTO listing (ticker, session_date, reasons, fired_count, plan_at_listing, shadow_reasons) VALUES ('ZZZ', '{IndexNight}', '[]', 0, '{{}}', '[]');");
            await Read("an S&P 400 night whose market check closed", closed, $"/screens/tonight/{IndexNight}?universe=400");
        }

        return pages;
    }
}
