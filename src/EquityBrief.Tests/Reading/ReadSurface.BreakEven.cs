using System.Text.RegularExpressions;
using EquityBrief.Api.Reading;
using EquityBrief.Core.Returns;
using EquityBrief.Core.Shortlist;
using EquityBrief.Tests.Checks;
using EquityBrief.Web.Marks;

namespace EquityBrief.Tests.Reading;

// read-surface, 8.2: the bar each plan set for itself travels from the store to
// the projection unchanged, the share of setups that cleared it and the mean of
// those bars are computed from the minimum upward and withheld below it, and
// neither reaches the page, which draws at 8.2 exactly what it drew at 8.1.
//
// Over constructed rows rather than over whatever the operator's store holds,
// because the minimum is 250 resolved setups and the store holds 93.
// see: A stored break-even is measured from the close the setup was entered at, as a percentage beside the figures it is compared with
public partial class ReadSurface
{
    // One reason's setups, constructed. The outcome and the bar its own plan set,
    // which is the pair the record is computed over.
    // Spread across sessions from 8.5, because a verdict needs both floors: 250
    // resolved setups over at least 60 distinct listing sessions, each
    // contributing at least one. A constructor that put every setup on one night
    // would build a population that can never earn a verdict, and every
    // assertion about what is drawn above the minimum would be an assertion about
    // the night floor instead.
    // see: A verdict tests a reason's wins against each of its setups' own break-even at a corrected threshold
    static IReadOnlyList<ResolvedSetup> Setups(
        DateOnly night,
        int wins,
        int losses,
        double breakEven,
        int withoutABar = 0,
        int sessions = ReasonVerdict.MinimumSessions)
    {
        var at = 0;

        // Round robin over the sessions, so each of them carries at least one
        // setup and the spread is a property of the construction rather than of
        // how the counts happen to divide.
        ResolvedSetup One(string outcome, double? bar) =>
            new($"N{++at:0000}", night.AddDays(-(at % Math.Max(sessions, 1))), outcome, bar);

        return
        [
            .. Enumerable.Range(0, wins).Select(_ => One(ForwardReturnSeries.Win, breakEven)),
            .. Enumerable.Range(0, losses).Select(_ => One(ForwardReturnSeries.Loss, breakEven)),

            // The resolved setups that entered and stopped on one session, which
            // have no entry close and so set no bar.
            .. Enumerable.Range(0, withoutABar).Select(_ => One(ForwardReturnSeries.Loss, null)),
        ];
    }

    static IReadOnlyList<ListingRow> Listings(IReadOnlyList<ResolvedSetup> setups, string reason) =>
    [
        .. setups.Select(setup => new ListingRow(setup.Ticker, setup.SessionDate, FiredNamed(reason), 1, "{}")),
    ];

    [Fact]
    public void AVerdictIsWithheldBelowTheMinimumWithItsCountAndIsComputedFromTheMinimumUpward()
    {
        // 8.2's own done condition, at the boundary rather than near it. A bound
        // is asserted at the value it names and one either side of it, because a
        // gate tested only well below its threshold is a gate nobody has shown
        // opens.
        // see: The record column stays empty until it has earned a number
        var night = new DateOnly(2026, 9, 4);

        ReasonRecord Record(int wins, int losses, int withoutABar = 0)
        {
            var setups = Setups(night, wins, losses, breakEven: 40d, withoutABar);

            return RunScreen.Records(Listings(setups, ShortlistSeries.AtEntryZone), setups)
                .Single(one => one.Reason == ShortlistSeries.AtEntryZone);
        }

        var below = Record(124, 125);
        var at = Record(125, 125);

        Assert.Equal((249, 250), (below.Resolved, at.Resolved));

        // Below the minimum there is no share and no bar at all, so no surface can
        // draw one by forgetting to ask. What stands in their place is the count
        // against the minimum, which the record has carried since 5.6.
        Assert.False(below.HasEarnedAVerdict);
        Assert.Null(below.Share);
        Assert.Null(below.BreakEven);
        Assert.Equal((249, RunScreen.MinimumResolvedSetups), (below.Scored, below.Minimum));

        // At the minimum both are computed. 125 of 250 is half, against a bar of
        // 40 per cent, which is a reason clearing what its own plans demanded.
        Assert.True(at.HasEarnedAVerdict);
        Assert.Equal(50d, at.Share!.Value, 6);
        Assert.Equal(40d, at.BreakEven!.Value, 6);

        // The share's population is the setups that set a bar and not every
        // resolved row, because a share tested against a bar has to be the share
        // of the rows that bar was averaged over. Five of the 93 setups the
        // operator's store had resolved on 2026-09-16 set none.
        var mixed = Record(125, 125, withoutABar: 6);

        Assert.Equal((256, 250), (mixed.Resolved, mixed.Scored));
        Assert.Equal(50d, mixed.Share!.Value, 6);
        Assert.Equal(40d, mixed.BreakEven!.Value, 6);

        // And the mean is a mean rather than the first bar it met.
        var varied = RunScreen.Records(
            Listings(
                [
                    new ResolvedSetup("AAAA", night, ForwardReturnSeries.Win, 20d),
                    new ResolvedSetup("BBBB", night, ForwardReturnSeries.Loss, 60d),
                ],
                ShortlistSeries.CrossedALevel),
            [
                new ResolvedSetup("AAAA", night, ForwardReturnSeries.Win, 20d),
                new ResolvedSetup("BBBB", night, ForwardReturnSeries.Loss, 60d),
            ]);

        Assert.Equal(
            (0, 2),
            (varied.Single(one => one.Reason == ShortlistSeries.CrossedALevel).Won - 1,
                varied.Single(one => one.Reason == ShortlistSeries.CrossedALevel).Scored));

        Assert.Equal(
            40d,
            ForwardReturnSeries.Record([(ForwardReturnSeries.Win, 20d), (ForwardReturnSeries.Loss, 60d)]).BreakEven!.Value,
            6);
    }

    [Fact]
    public void TheShareAndTheBarAreDrawnWithTheVerdictAndItsDivisorFromEightFive()
    {
        // 8.2's hard stop, lifted here. The two figures existed at 8.2 and the
        // page drew neither, because both were out of scope until the verdict
        // that reads them. This is that verdict, so the test that held the stop
        // becomes the test that the stop is over: the same figures, now drawn,
        // with the divisor that corrected the threshold beside them.
        // see: A verdict tests a reason's wins against each of its setups' own break-even at a corrected threshold
        var night = new DateOnly(2026, 9, 4);
        var setups = Setups(night, wins: 150, losses: 150, breakEven: 33.5d);

        var records = RunScreen.Records(Listings(setups, ShortlistSeries.AtEntryZone), setups);
        var earned = records.Single(one => one.Reason == ShortlistSeries.AtEntryZone);

        Assert.True(earned.HasEarnedAVerdict);
        Assert.NotNull(earned.Share);
        Assert.NotNull(earned.BreakEven);

        var drawn = new MarkRenderer().ReasonRecords(records, RunScreen.Tracks(records), Rates(1.2, 3.4), 60);

        // The three together, which is what 15.11 says at or above the minimum: a
        // share without its denominator hides how much was checked, and a share
        // without the break-even hides whether it was any good.
        Assert.Contains("50 per cent of 300 resolved", drawn, StringComparison.Ordinal);
        Assert.Contains("33.5 per cent those setups demanded", drawn, StringComparison.Ordinal);

        // The divisor, drawn beside the verdict, because a verdict without it
        // hides how hard the test actually was.
        // see: The significance threshold is divided by the family size, and the divisor is shown
        Assert.Contains("data-divisor=\"6\"", drawn, StringComparison.Ordinal);
        Assert.Contains("0.05 divided by a family of 6", drawn, StringComparison.Ordinal);
        Assert.Contains("data-threshold=\"0.00833\"", drawn, StringComparison.Ordinal);

        // Half of 300 against a bar of 33.5 per cent is a reason well clear of
        // what its own plans demanded, so the verdict is drawn as cleared and the
        // exact p is beside it.
        Assert.Contains("data-verdict=\"cleared\"", drawn, StringComparison.Ordinal);
        Assert.Contains("Clears at 0.00833", drawn, StringComparison.Ordinal);
        Assert.Contains("exact one-sided p of", drawn, StringComparison.Ordinal);

        // The row's own columns, unchanged from 8.1.
        var row = Assert.Single(Blocks(drawn, $"<tr data-reason=\"{ShortlistSeries.AtEntryZone}\".*?</tr>"));

        Assert.Equal(6, Regex.Matches(row, "<td").Count);
        Assert.Contains("data-resolved=\"300\"", row, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TheReadApiHandsBackTheBarEachPlanSetAsTheStoreHoldsIt()
    {
        // The column end to end: migration 24 creates it, the filler writes it,
        // and the read API hands it back unchanged, which is what the roster row
        // for this check claims of every stored value.
        using var store = await FixtureExpectations.WithReturns();

        var night = NightIn(store);

        // Two setup rows in the shape the filler writes: one carrying the bar its
        // entry close set, and one that entered and stopped on a single session
        // and so carries neither a bar nor a return.
        store.Execute(
            "INSERT INTO forward_return (ticker, session_date, horizon, outcome, resolved_on, return_pct, base_rate, break_even) " +
            $"VALUES ('ZZZA', '{night}', 'setup', 'win', '{night}', 12.5, NULL, 44.25), " +
            $"('ZZZB', '{night}', 'setup', 'loss', '{night}', NULL, NULL, NULL);");

        var returned = (await Api(store).ForwardReturnsAsync())
            .Where(row => row.Ticker.StartsWith("ZZZ", StringComparison.Ordinal))
            .OrderBy(row => row.Ticker, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(2, returned.Length);
        Assert.Equal(44.25, returned[0].BreakEven!.Value, 6);
        Assert.Equal(12.5, returned[0].ReturnPct!.Value, 6);

        // Present together and absent together, because both are measured from the
        // close the setup was entered at and this one has none.
        Assert.Null(returned[1].BreakEven);
        Assert.Null(returned[1].ReturnPct);

        // And the projection carries it onto the setup the record counts.
        var resolved = RunScreen.Resolved(returned).OrderBy(setup => setup.Ticker, StringComparer.Ordinal).ToArray();

        Assert.Equal(44.25, resolved[0].BreakEven!.Value, 6);
        Assert.Null(resolved[1].BreakEven);

        // Every row the fixture's own replay wrote carries none, because nothing in
        // it has matured and a bar is written with the outcome it belongs to.
        var replayed = (await Api(store).ForwardReturnsAsync())
            .Where(row => !row.Ticker.StartsWith("ZZZ", StringComparison.Ordinal))
            .ToArray();

        Assert.True(replayed.Length >= 12, $"the replay wrote {replayed.Length} forward return rows, expected at least 12.");
        Assert.All(replayed, row => Assert.Null(row.BreakEven));
    }
}
