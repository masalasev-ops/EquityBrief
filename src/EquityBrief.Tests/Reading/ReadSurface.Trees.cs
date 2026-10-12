using EquityBrief.Core.Ledger;
using EquityBrief.Core.Loop;
using EquityBrief.Core.Time;
using EquityBrief.Tests.Checks;

namespace EquityBrief.Tests.Reading;

// 17.15: a rule hooked on a stored tree model orders a night by it. The S&P 500's pullback standing at an approved change
// naming the learned trees by their key is kept and ordered by the trees the tester stored, walked by the core from their
// text; a key no stored model carries orders nothing and lists none.
// see: The learned score's challenger is a tree ensemble fitted by FastTree and scored by code from its stored trees, put only where it passes and earns more than the ridge score
public partial class ReadSurface
{
    // One tree splitting on the RSI at 50: a member at or under it reaches a leaf of 1 and one over it a leaf of 2.
    static TreeModel RsiTree() =>
        new(
            [LedgerReadings.IndexOf("rsi")],
            0,
            [new ScoreTree([0], [50f], [~0], [~1], [1.0, 2.0], 1)],
            400,
            -1,
            1,
            "constructed",
            new Dictionary<string, IReadOnlyList<double>>(StringComparer.Ordinal));

    static async Task<IReadOnlyList<string>> PullbackPicksUnder(long key, bool stored)
    {
        using var store = UniversesStore();
        var model = RsiTree();

        // The S&P 500's night: AAA and BBB both passed by the filter, AAA ranked first, listed by the filter.
        GateRow(store, IndexNight, new Member("AAA", Passed: true, Rank: 1));
        GateRow(store, IndexNight, new Member("BBB", Passed: true, Rank: 2));
        store.Execute($"INSERT INTO list_rule (session_date, rule) VALUES ('{IndexNight}', 'filter');");

        if (stored)
        {
            store.Execute(
                "INSERT INTO loop_model (run_id, index_code, family, kind, year, learned_from, learned_before, setups, readings, parameters, hash, pin, words) " +
                $"VALUES ('{LargeOctober}', 'GSPC', 'pullback', 'trees', 2027, '2019-01-02', '2026-10-10', 400, 'rsi', '{model.Canonical()}', '{model.Hash}', 'pin', 'words');");
        }

        var change = LoopChange.OfHooks(new Dictionary<string, double>(StringComparer.Ordinal) { [RuleHooks.TreesParameter] = key });

        store.Execute($"INSERT INTO provisional_setting (index_code, family, change, words, set_at, run_id, proposal) VALUES ('GSPC', 'pullback', '{change.Json}', '{change.Words()}', '2026-10-01T23:35:00Z', '{LargeOctober}', 'the learned trees, ordering the list');");

        var rsi = LedgerReadings.IndexOf("rsi");

        Task<Func<string, IReadOnlyList<double?>?>> Readings(CancellationToken _) =>
            Task.FromResult<Func<string, IReadOnlyList<double?>?>>(ticker =>
            {
                var held = new double?[LedgerReadings.Count];

                held[rsi] = ticker == "AAA" ? 40 : 60;

                return held;
            });

        await new EquityBrief.Worker.Families.FamilyLister(FixedClock.At(new DateTimeOffset(2026, 10, 2, 23, 40, 0, TimeSpan.Zero), SessionZones.UnitedStates), store.DatabaseFile)
            .RunAsync("night-list", CancellationToken.None, Readings);

        using var connection = store.Open();
        using var command = connection.CreateCommand();

        command.CommandText = $"SELECT ticker || '|' || place FROM family_pick WHERE session_date = '{IndexNight}' AND family = 'pullback' AND state = 'listed' ORDER BY place;";

        using var reader = command.ExecuteReader();
        var rows = new List<string>();

        while (reader.Read())
        {
            rows.Add(reader.GetString(0));
        }

        return rows;
    }

    [Fact]
    public async Task ARuleHookedOnAStoredTreeModelOrdersANightByIt()
    {
        var model = RsiTree();

        // Worked by hand: AAA at an RSI of 40 scores 1 and BBB at 60 scores 2, so the trees put BBB first where the
        // filter ranked AAA first.
        Assert.Equal((1.0, 2.0), (model.Score(Readings(40))!.Value, model.Score(Readings(60))!.Value));
        Assert.Equal(["BBB|1", "AAA|2"], await PullbackPicksUnder(model.Key, stored: true));

        // A key no stored model carries scores no member, so the pullback lists none.
        Assert.Empty(await PullbackPicksUnder(model.Key, stored: false));

        static IReadOnlyList<double?> Readings(double rsi)
        {
            var held = new double?[LedgerReadings.Count];

            held[LedgerReadings.IndexOf("rsi")] = rsi;

            return held;
        }
    }
}
