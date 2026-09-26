using System.Globalization;
using EquityBrief.Core.Candidates;
using EquityBrief.Core.Indicators;
using EquityBrief.Core.Levels;
using EquityBrief.Core.Prices;
using EquityBrief.Core.Time;
using EquityBrief.Tests.Harness;
using EquityBrief.Worker.Candidates;

namespace EquityBrief.Tests.Checks;

// fixture-expectations, the 3.4 correction: a session counts once in a band, a touch is a visit, and a
// band is no wider than two typical days' moves. Each over constructed input worked by hand, and the
// command that registers again every standing candidate whose evaluator a code change moved.
public partial class FixtureExpectations
{
    static LevelBar Session(DateOnly day, decimal low, decimal high) => new(day, high, low, (low + high) / 2);

    // Two swing lows two points apart and one visit between them: the operator's reading of a band that
    // scored five on three sessions, because the sessions of the two swings were counted again as touches.
    // A merge distance of 3 on a typical move of 6, so the swings are one band from 353.69 to 355.81.
    // see: A touch is one visit to a band, counted only where the band holds no swing or retracement from that visit, and it never creates a band
    [Fact]
    public void ASessionTheBandHoldsAsASwingIsNotCountedAgainAsATouch()
    {
        LevelMember[] swings =
        [
            new(MemberSource.Swing, "swing low", 353.69m, new DateOnly(2026, 7, 29)),
            new(MemberSource.Swing, "swing low", 355.81m, new DateOnly(2026, 8, 5)),
        ];

        LevelBar[] window =
        [
            Session(new DateOnly(2026, 7, 27), 357m, 360m),
            Session(new DateOnly(2026, 7, 28), 356.5m, 359m),
            Session(new DateOnly(2026, 7, 29), 353.69m, 358m),
            Session(new DateOnly(2026, 7, 30), 356m, 359m),
            Session(new DateOnly(2026, 7, 31), 357m, 361m),
            Session(new DateOnly(2026, 8, 3), 354.5m, 358m),
            Session(new DateOnly(2026, 8, 4), 356.2m, 360m),
            Session(new DateOnly(2026, 8, 5), 355.81m, 359m),
            Session(new DateOnly(2026, 8, 6), 357m, 362m),
        ];

        var band = Assert.Single(LevelSeries.For(window, swings, close: 360m, mergeDistance: 3m, typicalMove: 6m, new DateOnly(2026, 8, 6)));

        // The lows of 07-29 and 08-05 are the swings themselves, each already in the band, and 08-03 is the
        // one visit that is not: three members, two swing lows and one touch.
        Assert.Equal(
            ["swing low 2026-07-29", "touch 2026-08-03", "swing low 2026-08-05"],
            band.Members.Select(member => FormattableString.Invariant($"{member.Kind} {member.Date:yyyy-MM-dd}")));
        Assert.Equal(354.5m, band.Members.Single(member => member.Source == MemberSource.Touch).Price);

        // Strength: one a member, three; one more for evidence inside the last twenty sessions, which the
        // whole nine-session window is; no retracement and no shelf. Four, where counting each session again
        // gave five members and six.
        Assert.Equal(4, band.Strength);
    }

    // A band the price entered, stayed in for three weeks and left has one touch, dated the session it
    // arrived, and a second visit later is a second touch. The band runs 100 to 101 on a swing low and the
    // 50-day average, and the swing's own session opens the window.
    [Fact]
    public void AThreeWeekStayInsideABandIsOneVisit()
    {
        var first = new DateOnly(2026, 6, 1);
        var sessions = Enumerable.Range(0, 30).Select(at => first.AddDays(at)).ToArray();

        LevelMember[] candidates =
        [
            new(MemberSource.Swing, "swing low", 100m, sessions[0]),
            new(MemberSource.Average, "sma50", 101m, sessions[^1]),
        ];

        var window = sessions.Select((day, at) => at switch
        {
            0 => Session(day, 100m, 102.5m),
            >= 5 and < 20 => Session(day, 100.5m, 102.5m),
            25 or 26 => Session(day, 100.8m, 102.5m),
            _ => Session(day, 103m, 105m),
        }).ToArray();

        var band = Assert.Single(LevelSeries.For(window, candidates, close: 104m, mergeDistance: 1.5m, typicalMove: 3m, sessions[^1]));
        var touches = band.Members.Where(member => member.Source == MemberSource.Touch).ToArray();

        // Session 0 is the swing's own and adds nothing; sessions 5 to 19 are fifteen sessions and one visit,
        // dated 5; sessions 25 and 26 are the second, dated 25.
        Assert.Equal([sessions[5], sessions[25]], touches.Select(touch => touch.Date));
        Assert.Equal([100.5m, 100.8m], touches.Select(touch => touch.Price));
    }

    // Candidates each closer than the merge distance to the one before can chain across prices anyone can
    // tell apart. On a typical move of 1 the merge distance is 0.5 and the widest a band may be is 2.
    // see: A band is no wider than two typical days' moves, and a chain that would be wider splits at its widest gap
    [Fact]
    public void AChainWiderThanTwoTypicalMovesSplitsAtItsWidestGap()
    {
        IReadOnlyList<(decimal Low, decimal High)> Bands(params decimal[] prices) =>
        [
            .. LevelSeries.For(
                    [Session(new DateOnly(2026, 6, 1), 300m, 310m)],
                    [.. prices.Select((price, at) => new LevelMember(MemberSource.Swing, "swing high", price, new DateOnly(2026, 5, 1).AddDays(at)))],
                    close: 305m,
                    mergeDistance: 0.5m,
                    typicalMove: 1m,
                    new DateOnly(2026, 6, 1))
                .Select(level => (level.LowEdge, level.HighEdge)),
        ];

        // Gaps of 0.3 but one of 0.45, across 2.25: one chain, wider than 2, split at the 0.45 gap.
        Assert.Equal(
            [(100m, 100.6m), (101.05m, 102.25m)],
            Bands(100m, 100.3m, 100.6m, 101.05m, 101.35m, 101.65m, 101.95m, 102.25m));

        // Exactly 2 wide is the widest a band may be, and stays one band.
        Assert.Equal([(100m, 102.0m)], Bands(100m, 100.4m, 100.8m, 101.2m, 101.6m, 102.0m));

        // Every gap 0.45 across 2.25: the lowest of the equal gaps splits first, and the rest, 1.8 wide, fits.
        Assert.Equal(
            [(100m, 100m), (100.45m, 102.25m)],
            Bands(100m, 100.45m, 100.9m, 101.35m, 101.8m, 102.25m));

        // A chain 4.5 wide splits again inside its first part until every part fits.
        Assert.All(
            Bands([.. Enumerable.Range(0, 11).Select(at => 100m + (at * 0.45m))]),
            band => Assert.True(band.High - band.Low <= 2m, FormattableString.Invariant($"{band.Low} to {band.High} is wider than 2")));
    }

    // Section 17's band width, read off the row against the multiple the builder splits at, and every band
    // the fixture's store holds no wider than that multiple of the typical move stored at its as-of session.
    [Fact]
    public async Task TheBandWidthIsTwoTypicalMovesAndNoStoredBandIsWiderThanIt()
    {
        var row = ArchitectureTables
            .In(File.ReadAllText(Repository.Architecture))
            .Single(table => table.Heading == Scope.LimitsTable)
            .Body.Single(cells => cells.Count > 1 && cells[0] == "Band width");

        Assert.StartsWith("at most two typical days' moves, marked proposed", row[1], StringComparison.Ordinal);
        Assert.Equal(2m, LevelSeries.MaximumWidthInTypicalMoves);

        using var store = await WithLevels();

        var bands = 0;

        foreach (var name in Expected("levels").GetProperty("asOf").EnumerateObject())
        {
            var session = name.Value.GetString()!;
            var typicalMove = Statistic.ToPrice(double.Parse(
                Query(store, $"SELECT value FROM indicator WHERE ticker = '{name.Name}' AND session_date = '{session}' AND name = '{IndicatorSeries.Atr14}';").Single(),
                CultureInfo.InvariantCulture));

            foreach (var edges in Query(store, $"SELECT low_edge, high_edge FROM level WHERE ticker = '{name.Name}' AND as_of = '{session}';"))
            {
                var parts = edges.Split('|');
                var width = decimal.Parse(parts[1], CultureInfo.InvariantCulture) - decimal.Parse(parts[0], CultureInfo.InvariantCulture);

                bands++;

                Assert.True(width <= LevelSeries.MaximumWidthInTypicalMoves * typicalMove, $"{name.Name}'s band from {parts[0]} is {width} wide over a typical move of {typicalMove}.");
            }
        }

        Assert.True(bands >= 15, $"Read {bands} bands, expected at least 15.");
    }

    // The command that registers again, unchanged and at one instant, every standing candidate whose
    // evaluator a code change moved: refused where none has moved, writing a retirement and a registration
    // for each one that has with its rule, test, evaluator and parameters as they stood, leaving the rest,
    // and refused whole where one's evaluator is no longer carried.
    // see: A candidate whose evaluator a code change moved is registered again unchanged, every one at one instant
    [Fact]
    public async Task TheCandidatesWhoseEvaluatorMovedAreRegisteredAgainUnchangedAtOneInstantOrNone()
    {
        var threeAt = new DateTimeOffset(2026, 9, 6, 22, 0, 0, TimeSpan.Zero);

        using var store = await FamilyStore(threeAt);

        var (none, noneSaid) = await RegisterVerbAt(store, threeAt.AddDays(1), RegisterVerb.Moved, "--evidence", "the level arithmetic changed");

        Assert.Equal(1, none);
        Assert.Contains("no standing candidate's evaluator has moved", noneSaid, StringComparison.Ordinal);
        Assert.Equal(3, Scalar(store, "SELECT COUNT(*) FROM candidate_register;"));

        // A fourth standing on the first one's evaluator and parameters at a version the code does not carry.
        store.Execute(
            "INSERT INTO candidate_register (id, candidate, rule, test, evaluator, parameters, evaluator_version, event, retires, registered_at, evidence) " +
            "SELECT 4, 'on a moved evaluator', rule, test, evaluator, parameters, 'moved000000', 'registered', NULL, '2026-09-06T23:00:00Z', NULL FROM candidate_register WHERE id = 1;");

        var movedAt = threeAt.AddDays(2);
        var (code, said) = await RegisterVerbAt(store, movedAt, RegisterVerb.Moved, "--evidence", "the level arithmetic changed");

        Assert.True(code == 0, said);
        Assert.Contains("'on a moved evaluator'", said, StringComparison.Ordinal);

        var rows = FamilyRows(store);

        Assert.Equal(6, rows.Count);
        Assert.Equal(
            ("on a moved evaluator", "retired", "on a moved evaluator", "moved000000", "the level arithmetic changed"),
            (rows[4].Candidate, rows[4].Event, rows[4].Retires, rows[4].EvaluatorVersion, rows[4].Evidence));
        Assert.Equal(
            ("on a moved evaluator", "registered", rows[0].Rule, rows[0].Test, rows[0].Evaluator, rows[0].Parameters, rows[0].EvaluatorVersion),
            (rows[5].Candidate, rows[5].Event, rows[5].Rule, rows[5].Test, rows[5].Evaluator, rows[5].Parameters, rows[5].EvaluatorVersion));
        Assert.Equal(rows[4].RegisteredAt, rows[5].RegisteredAt);
        Assert.Equal(movedAt, rows[5].RegisteredAt);
        Assert.Equal(4, CandidateFamily.Standing(rows, movedAt.AddSeconds(1)).Count);

        // Nothing has moved now, so a second run writes nothing.
        Assert.Equal(1, (await RegisterVerbAt(store, movedAt.AddDays(1), RegisterVerb.Moved, "--evidence", "again")).Code);
        Assert.Equal(6, Scalar(store, "SELECT COUNT(*) FROM candidate_register;"));

        // One standing with parameters its evaluator does not read refuses the command whole, since registering
        // it again unchanged would register a row that does not say what will run.
        store.Execute(
            "INSERT INTO candidate_register (id, candidate, rule, test, evaluator, parameters, evaluator_version, event, retires, registered_at, evidence) " +
            "SELECT 7, 'with parameters unread', rule, test, evaluator, '{}', 'moved000000', 'registered', NULL, '2026-09-09T22:00:00Z', NULL FROM candidate_register WHERE id = 1;");

        var (unread, unreadSaid) = await RegisterVerbAt(store, movedAt.AddDays(2), RegisterVerb.Moved, "--evidence", "the level arithmetic changed");

        Assert.Equal(1, unread);
        Assert.Contains("'with parameters unread' was refused, so none of the 1 was written", unreadSaid, StringComparison.Ordinal);
        Assert.Contains("is registered with [] and reads [", unreadSaid, StringComparison.Ordinal);
        Assert.Equal(7, Scalar(store, "SELECT COUNT(*) FROM candidate_register;"));

        // One standing on an evaluator the code no longer carries refuses the command whole.
        store.Execute(
            "INSERT INTO candidate_register (id, candidate, rule, test, evaluator, parameters, evaluator_version, event, retires, registered_at, evidence) " +
            "SELECT 8, 'on an evaluator gone', rule, test, 'an-evaluator-gone', parameters, 'moved000000', 'registered', NULL, '2026-09-09T23:00:00Z', NULL FROM candidate_register WHERE id = 1;");
        store.Execute(
            "INSERT INTO candidate_register (id, candidate, rule, test, evaluator, parameters, evaluator_version, event, retires, registered_at, evidence) " +
            "SELECT 9, 'moved again', rule, test, evaluator, parameters, 'moved000000', 'registered', NULL, '2026-09-09T23:00:00Z', NULL FROM candidate_register WHERE id = 1;");

        var (gone, goneSaid) = await RegisterVerbAt(store, movedAt.AddDays(3), RegisterVerb.Moved, "--evidence", "the level arithmetic changed");

        Assert.Equal(1, gone);
        Assert.Contains("'on an evaluator gone' runs on 'an-evaluator-gone', which the code no longer carries", goneSaid, StringComparison.Ordinal);
        Assert.Equal(9, Scalar(store, "SELECT COUNT(*) FROM candidate_register;"));
    }

    static IReadOnlyList<RegisterRow> FamilyRows(TemporaryStore store) =>
        [
            .. Query(store, "SELECT id || '|' || candidate || '|' || rule || '|' || test || '|' || evaluator || '|' || parameters || '|' || evaluator_version || '|' || event || '|' || IFNULL(retires, '') || '|' || registered_at || '|' || IFNULL(evidence, '') FROM candidate_register ORDER BY id;")
                .Select(line => line.Split('|'))
                .Select(part => new RegisterRow(
                    long.Parse(part[0], System.Globalization.CultureInfo.InvariantCulture),
                    part[1], part[2], part[3], part[4], part[5], part[6], part[7],
                    part[8].Length == 0 ? null : part[8],
                    DateTimeOffset.Parse(part[9], System.Globalization.CultureInfo.InvariantCulture),
                    part[10].Length == 0 ? null : part[10])),
        ];
}
