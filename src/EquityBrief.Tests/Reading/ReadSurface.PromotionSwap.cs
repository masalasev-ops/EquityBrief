using EquityBrief.Core.Candidates;
using EquityBrief.Core.Loop;
using EquityBrief.Core.Paper;
using EquityBrief.Core.Time;
using EquityBrief.Data;
using EquityBrief.Tests.Harness;
using EquityBrief.Web.App;
using EquityBrief.Worker.Candidates;
using EquityBrief.Worker.Loop;

namespace EquityBrief.Tests.Reading;

// From 24.23 the swap a promotion makes, over the S&P 500's breakout with one variant and its trades: inside the rule freeze a
// press is refused and a press the night finds is answered refused, nothing written; after it the variant's case, worked
// through the case code from its own records with only its figures set to stand, is pressed, waits with the night it runs
// from and can be undone; the next night's own build retires the variant's row and the running revision's and registers the
// variant's settings as revision 2 and revision 1's as a variant, the variant's trades counted to revision 2 and revision
// 1's to the variant it became, worked by hand; and an Undo swaps them back the next night, each record back where it was.
// see: A promotion swaps a running revision and its variant at the start of the next night that runs, each carrying its own record
public partial class ReadSurface
{
    static readonly DateOnly CaseNight = new(2026, 10, 2);

    static readonly DateOnly SwapNight = new(2027, 2, 8);

    static readonly DateOnly UndoNight = new(2027, 2, 9);

    // The variant's case on the S&P 500 worked by the case code from its own records on the store, with only its three
    // conditions set to stand, and stored at the monthly run of 2027-02.
    static async Task<PromotionCaseView> StandingCaseAsync(TemporaryStore store)
    {
        var reads = new PaperReads(store.DatabaseFile);
        var worked = await PaperWork.WorkAsync(reads, "GSPC", "S&P 500", true, CaseNight, null);
        var rows = PromotionCases.Rows("GSPC", "S&P 500", worked.Sources, worked.Views, worked.Sessions, [], CaseNight);
        var one = rows.SelectMany(row => row.Variants).Single(view => view.Variant == RuleRegister.Slug(ProvisionalBreakout));
        var standing = one with { Conditions = new PromotionConditions(true, true, true) };
        var figures = PromotionCases.Figures(standing);

        store.Execute(
            "INSERT INTO promotion_case (month, index_code, family, variant, night, figures, figures_key, stored_at, run_id) VALUES " +
            $"('2027-02', 'GSPC', 'breakout', '{standing.Variant}', '2026-10-02', '{figures.Replace("'", "''", StringComparison.Ordinal)}', '{PromotionCases.FiguresKey(figures)}', '2027-02-06T13:00:00Z', 'loop-month-2027-02');");

        return standing;
    }

    static IReadOnlyList<(string Candidate, string Parameters)> StandingBreakouts(TemporaryStore store, DateTimeOffset at) =>
        [
            .. CandidateFamily.Standing(new CandidateRegistrar(FixedClock.At(at, SessionZones.UnitedStates), store.DatabaseFile).RowsAsync().GetAwaiter().GetResult(), at)
                .Where(row => row.Candidate == LiveBreakout || row.Candidate == ProvisionalBreakout || row.Candidate == PromotionSwaps.ReplacedName(LiveBreakout, 1))
                .OrderBy(row => FamilyRecords.IsLive(row.Candidate) ? 0 : 1)
                .ThenBy(row => row.Candidate, StringComparer.Ordinal)
                .Select(row => (row.Candidate, row.Parameters)),
        ];

    static long SwapCount(TemporaryStore store, string sql)
    {
        using var connection = store.Open();
        using var command = connection.CreateCommand();

        command.CommandText = sql;

        return Convert.ToInt64(command.ExecuteScalar() ?? 0L, System.Globalization.CultureInfo.InvariantCulture);
    }

    // An account by the name its trades are kept under, a head by its live rule's name in its words.
    static bool KeptUnder(PaperAccountSource account, string name) =>
        account.Listed == name || (account.Standing == PaperAccountView.Live && account.Words == name);

    static async Task<PaperAccountSource[]> AccountsOn(TemporaryStore store, DateOnly night) =>
        [.. (await PaperWork.SourcesAsync(new PaperReads(store.DatabaseFile), "GSPC", "S&P 500", true, night, yours: false)).Accounts];

    [Fact]
    public async Task APromotionSwapsTheRunningRevisionAndItsVariantAtTheNextNightEachCarryingItsOwnRecordAndAnUndoSwapsThemBack()
    {
        using var store = await PaperVariantsStore();
        var standing = await StandingCaseAsync(store);
        var liveParameters = CandidateEvaluator.Write(TheSetupFamilies.Breakouts[0].Parameters);
        var variantParameters = CandidateEvaluator.Write(TheSetupFamilies.Breakouts[^2].Parameters);
        var variantTrades = (await AccountsOn(store, CaseNight)).Single(account => KeptUnder(account, ProvisionalBreakout)).Trades.Select(one => (one.Trade.Ticker, one.Trade.ListedOn)).ToHashSet();
        var liveTrades = (await AccountsOn(store, CaseNight)).Single(account => KeptUnder(account, LiveBreakout));
        long Presses() => SwapCount(store, "SELECT COUNT(*) FROM promotion_press;");

        // Inside the freeze, on its last day, a press is refused and writes nothing, and a press the night finds is refused.
        using (var host = new Host(store.Root, FixedClock.At(new DateTimeOffset(2027, 2, 5, 23, 0, 0, TimeSpan.Zero), SessionZones.UnitedStates)))
        using (var client = host.CreateClient())
        {
            Assert.Contains("Nothing was promoted: the rules are frozen", (await CardPress(client, SinglePageApp.CasePostRoute + "GSPC/breakout", [("variant", standing.Variant), ("press", "promote")])).Said, StringComparison.Ordinal);
            Assert.Equal(0L, Presses());
        }

        store.Execute($"INSERT INTO promotion_press (index_code, family, variant, press, month, pressed_at) VALUES ('GSPC', 'breakout', '{standing.Variant}', 'promote', '2027-02', '2027-02-05T22:00:00Z');");
        var frozen = await new PromotionSwap(FixedClock.At(new DateTimeOffset(2027, 2, 5, 23, 0, 0, TimeSpan.Zero), SessionZones.UnitedStates), store.DatabaseFile).NightAsync("night-frozen", new DateOnly(2027, 2, 5));

        Assert.StartsWith("refused: the rules are frozen", Assert.Single(frozen.Answers).Words, StringComparison.Ordinal);
        Assert.Null(frozen.WroteFrom);
        Assert.Equal([(LiveBreakout, liveParameters), (ProvisionalBreakout, variantParameters)], StandingBreakouts(store, new DateTimeOffset(2027, 2, 6, 0, 0, 0, TimeSpan.Zero)));

        // After the freeze the press waits for the next night, which the page names, and can be undone there.
        using (var host = new Host(store.Root, FixedClock.At(new DateTimeOffset(2027, 2, 6, 14, 0, 0, TimeSpan.Zero), SessionZones.UnitedStates)))
        using (var client = host.CreateClient())
        {
            var said = (await CardPress(client, SinglePageApp.CasePostRoute + "GSPC/breakout", [("variant", standing.Variant), ("press", "promote")])).Said;

            Assert.Contains(">Waiting: becomes revision 2 from the night of 5 Oct 2026.<", said, StringComparison.Ordinal);
            Assert.Contains($"Nothing was promoted: {EquityBrief.Api.Reading.ReadApi.AlreadyWaiting}.", (await CardPress(client, SinglePageApp.CasePostRoute + "GSPC/breakout", [("variant", standing.Variant), ("press", "promote")])).Said, StringComparison.Ordinal);

            var page = await client.GetStringAsync("/screens/loop/case");

            Assert.Contains("data-card=\"case-presses\"", page, StringComparison.Ordinal);
            Assert.Contains("<h2>Your presses</h2>", page, StringComparison.Ordinal);
            Assert.Contains("Waiting: becomes revision 2 from the night of ", page, StringComparison.Ordinal);
            Assert.Contains("<input type=\"hidden\" name=\"press\" value=\"undo\">", page, StringComparison.Ordinal);
        }

        // The next night swaps them at its start: the variant's settings under the running revision's name as revision 2, and
        // revision 1's as the variant it became.
        var swap = await new PromotionSwap(FixedClock.At(new DateTimeOffset(2027, 2, 8, 23, 0, 0, TimeSpan.Zero), SessionZones.UnitedStates), store.DatabaseFile).NightAsync("night-swap", SwapNight);
        var applied = Assert.Single(swap.Answers);

        Assert.True(applied.Applied);
        Assert.Equal(2, applied.Revision);
        Assert.Equal("Promoted: revision 2 from the night of 8 Feb 2027, revision 1 running on as a variant, each carrying its own record.", applied.Words);
        Assert.NotNull(swap.WroteFrom);
        Assert.Equal([(LiveBreakout, variantParameters), (PromotionSwaps.ReplacedName(LiveBreakout, 1), liveParameters)], StandingBreakouts(store, new DateTimeOffset(2027, 2, 9, 0, 0, 0, TimeSpan.Zero)));

        // The variant's trades count to revision 2 and revision 1's to the variant it became, worked by hand from the trades
        // each listed before the swap.
        var after = await AccountsOn(store, SwapNight);
        var revision = after.Single(account => KeptUnder(account, LiveBreakout));
        var replaced = after.Single(account => KeptUnder(account, PromotionSwaps.ReplacedName(LiveBreakout, 1)));

        Assert.Equal(variantTrades, revision.Same.Select(one => (one.Trade.Ticker, one.Trade.ListedOn)).ToHashSet());
        Assert.Equal(2, revision.Revisions[^1].Number);
        Assert.Equal(SwapNight, revision.Revisions[^1].From);
        Assert.Equal(
            liveTrades.Trades.Concat(liveTrades.Earlier?.Select(one => (one.Trade, one.Result)) ?? []).Select(one => (one.Trade.Ticker, one.Trade.ListedOn)).ToHashSet(),
            replaced.Same.Select(one => (one.Trade.Ticker, one.Trade.ListedOn)).ToHashSet());
        Assert.DoesNotContain(after, account => KeptUnder(account, ProvisionalBreakout));

        // An Undo swaps them back from the next night, each record back under the name it was kept under.
        using (var host = new Host(store.Root, FixedClock.At(new DateTimeOffset(2027, 2, 9, 14, 0, 0, TimeSpan.Zero), SessionZones.UnitedStates)))
        using (var client = host.CreateClient())
        {
            var press = SwapCount(store, "SELECT MAX(id) FROM promotion_press WHERE press = 'promote';");

            Assert.Contains(">Waiting: the promotion is undone from the night of ", (await CardPress(client, SinglePageApp.CasePostRoute + "GSPC/breakout", [("variant", standing.Variant), ("press", "undo"), ("undoes", press.ToString(System.Globalization.CultureInfo.InvariantCulture))])).Said, StringComparison.Ordinal);
        }

        var undo = await new PromotionSwap(FixedClock.At(new DateTimeOffset(2027, 2, 9, 23, 0, 0, TimeSpan.Zero), SessionZones.UnitedStates), store.DatabaseFile).NightAsync("night-undo", UndoNight);

        Assert.True(Assert.Single(undo.Answers).Applied);
        Assert.Equal([(LiveBreakout, liveParameters), (ProvisionalBreakout, variantParameters)], StandingBreakouts(store, new DateTimeOffset(2027, 2, 10, 0, 0, 0, TimeSpan.Zero)));

        var back = await AccountsOn(store, UndoNight);

        Assert.Equal(variantTrades, back.Single(account => KeptUnder(account, ProvisionalBreakout)).Same.Select(one => (one.Trade.Ticker, one.Trade.ListedOn)).ToHashSet());
        Assert.DoesNotContain(back, account => KeptUnder(account, PromotionSwaps.ReplacedName(LiveBreakout, 1)));
        Assert.DoesNotContain(back.Single(account => KeptUnder(account, LiveBreakout)).Same, one => variantTrades.Contains((one.Trade.Ticker, one.Trade.ListedOn)) && !liveTrades.Trades.Any(live => live.Trade.Ticker == one.Trade.Ticker && live.Trade.ListedOn == one.Trade.ListedOn));
    }

    [Fact]
    public void APromotionsCarriesReadBackAsWrittenAndAnUndoneSwapsCarriesStopAtTheUndo()
    {
        PromotionCarry[] carries = [new("the live a", "the a, b", new DateOnly(2026, 11, 16), SwapNight), new("the a, as revision 1 ran", "the live a", new DateOnly(2026, 11, 16), SwapNight)];

        Assert.Equal(carries, PromotionSwaps.ReadCarries(PromotionSwaps.WriteCarries(carries)));

        PromotionPressRow[] presses = [new(1, "GSPC", "breakout", "v", "promote", null, "2027-02", DateTimeOffset.UnixEpoch), new(2, "GSPC", "breakout", "v", "undo", 1, "2027-02", DateTimeOffset.UnixEpoch)];
        PromotionSwapRow[] answers = [new(1, "GSPC", "breakout", true, "w", 2, "the a, b", "the live a", "the a, as revision 1 ran", carries, SwapNight, DateTimeOffset.UnixEpoch), new(2, "GSPC", "breakout", true, "w", 3, null, null, null, [], UndoNight, DateTimeOffset.UnixEpoch.AddSeconds(1))];

        Assert.Equal(carries, PromotionSwaps.CarriesOf(presses, answers, "GSPC", SwapNight));
        Assert.Empty(PromotionSwaps.CarriesOf(presses, answers, "GSPC", UndoNight));
        Assert.Empty(PromotionSwaps.CarriesOf(presses, answers, "MID", SwapNight));
        Assert.Equal("the breakout, as revision 7 ran", PromotionSwaps.ReplacedName("the live breakout", 7));
        Assert.Equal(new DateOnly(2027, 2, 8), PromotionSwaps.NextNight(new DateOnly(2027, 2, 5)));
    }
}
