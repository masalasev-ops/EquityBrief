using System.Globalization;
using EquityBrief.Core.Candidates;
using EquityBrief.Core.Components;
using EquityBrief.Core.Configuration;
using EquityBrief.Core.History;
using EquityBrief.Core.Judge;
using EquityBrief.Core.Loop;
using EquityBrief.Core.Paper;
using EquityBrief.Core.Prices;
using EquityBrief.Core.Time;
using EquityBrief.Data;
using EquityBrief.Worker.Paper;
using Microsoft.Data.Sqlite;

namespace EquityBrief.Worker.Loop;

// The buy range's widths test, run by hand: on each index, each price setup whose live rule buys at the next open is walked
// over its setups from 2005 under that rule's own plan, once buying at the open and once in a range of each candidate width,
// through the plan test's own walk; each width's figures over the early years and from 2019 are written beside the open's,
// for each setup and for every setup of the index together, and the width the early years of every setup together chose is
// marked. A setup whose live rule buys at a level or on its first pullback carries no range and is not walked. With the
// picker's flag every stock session the four price families flagged is walked instead under each plan of the AI stock
// picker's menu, as stand-ins for its picks, the width it chooses provisional; and with the every-member flag every member
// of the index on every session the every-session table holds, the width it chooses replacing the provisional one. It moves
// no pick, plan, rule or version, and no plan uses a width before the operator approves one.
// see: Every buy at the next open carries a range from its last close, its widths chosen by test on the early years for each index
public sealed class RangeWidthTest(IClock clock, string databaseFile, string dataRoot, TextWriter output)
{
    public const string Verb = "buy-range-widths";

    public static ComponentAccess Access => new(
        Stores:
        [
            new StoreTouch(Store.LongSetup, Touch.Read),
            new StoreTouch(Store.Setup, Touch.Read),
            new StoreTouch(Store.SessionReading, Touch.Read),
            new StoreTouch(Store.SetupNight, Touch.Read),
            new StoreTouch(Store.CandidateRegister, Touch.Read),
            new StoreTouch(Store.BuyRangeWidth, Touch.Insert),
            new StoreTouch(Store.RunLog, Touch.Insert),
        ],
        Feeds: []);

    // A setup's flagged rows its live setting passed, from the long ledger to its standing start and the standing ledger after.
    const string Setups = @"
        SELECT family, ticker, session_date, entry, stop, target, trail, cap, company_value FROM long_setup
        WHERE index_code = $index AND family = $family AND live_pass = 1
        UNION ALL
        SELECT family, ticker, session_date, entry, stop, target, trail, cap, company_value FROM setup
        WHERE index_code = $index AND family = $family AND live_pass = 1 AND session_date >= $standing
        ORDER BY session_date, ticker;";

    // Every row the four price families flagged, whether or not its live setting passed it, from the long ledger to its standing
    // start and the standing ledger after, as the picker's stand-ins.
    const string PickerSetups = @"
        SELECT family, ticker, session_date, entry, stop, target, trail, cap, company_value FROM long_setup
        WHERE index_code = $index AND family IN ('pullback', 'breakout', 'drift', 'dip')
        UNION ALL
        SELECT family, ticker, session_date, entry, stop, target, trail, cap, company_value FROM setup
        WHERE index_code = $index AND family IN ('pullback', 'breakout', 'drift', 'dip') AND session_date >= $standing
        ORDER BY session_date, ticker;";

    // The flag walking the picker's menu over the price families' setups in place of each live rule's plan.
    public const string PickerFlag = "--picker";

    // From the operator's answer of 2026-11-23 the flag walking the picker's menu over every member of the index on every
    // session the every-session table holds, each with its company value as the table read it, which its costs are read by.
    // see: The picker's buy range width is chosen first over the price families' setups under its menu and then over every member on every session
    public const string EveryMemberFlag = "--every-member";

    const string EveryMember = "SELECT ticker, session_date, json_extract(readings, $place) FROM session_reading WHERE index_code = $index ORDER BY session_date, ticker;";

    // The company value's place in a row's readings, found by the catalogue's own words for it, since no engine's file names
    // a catalogue column but the learned score's.
    static readonly int CompanyValuePlace = Core.Ledger.LedgerReadings.All
        .Select((reading, at) => (reading.Definition, at))
        .Single(one => one.Definition == Core.Families.RuleWording.LedgerReadingsWords.CompanyValue).at;

    // The family an every-member row is read under, which names no setup.
    public const string EveryMemberFamily = "every member";

    const string NewestNight = "SELECT MAX(session_date) FROM setup_night;";

    const string Insert = @"
        INSERT INTO buy_range_width (run_id, index_code, family, width, ceiling, floor, years, offered, bought, above, below, edge, missed, saved, chosen, written_at)
        VALUES ($run_id, $index_code, $family, $width, $ceiling, $floor, $years, $offered, $bought, $above, $below, $edge, $missed, $saved, $chosen, $written_at);";

    const string InsertRun = @"
        INSERT INTO run_log (run_id, stage, started_at, ended_at, outcome, rows_written, model_calls, network_requests, spend, detail)
        VALUES ($run_id, $stage, $started_at, $ended_at, 'ok', $rows, 0, 0, '0', $detail);";

    // The plan a live rule trades by: the one its registration states, or the next open at the rule's own stop and sale.
    public static PlanCombination PlanOf(CandidateRow live) =>
        PlanHook.Of(CandidateEvaluator.Read(live.Parameters)) ?? new PlanCombination(PlanPoints.NextOpen, 0, PlanPoints.RuleStop, PlanPoints.RuleSale);

    // Every index, or the one named; with the picker's flag the picker's menu over every setup the price families flagged, and
    // with the every-member flag over every member on every session.
    public async Task<int> RunAsync(string? index = null, bool picker = false, bool everyMember = false, CancellationToken cancellation = default)
    {
        picker |= everyMember;

        if (index is not null && !PaperFiguresRun.Indices.Contains(index, StringComparer.Ordinal))
        {
            output.WriteLine($"{Verb}: name '--index' as one of {string.Join(", ", PaperFiguresRun.Indices)}");

            return 2;
        }

        if (NightLock.Holder(dataRoot) is { } holder)
        {
            output.WriteLine($"{Verb}: the night holds the store ({holder}); run it once the night has finished");

            return 2;
        }

        var started = clock.UtcNow;
        var runId = FormattableString.Invariant($"{Verb}-{started.UtcDateTime:yyyyMMddTHHmmssZ}");
        var register = await new PaperReads(databaseFile).RegisteredCandidatesAsync();
        var night = await NewestNightAsync(cancellation) ?? DateOnly.FromDateTime(started.UtcDateTime);
        var rows = new List<RangeWidthRow>();

        foreach (var code in index is null ? PaperFiguresRun.Indices : [index])
        {
            var tallies = new List<(string Family, RangeWidths? Widths, RangeTally Early, RangeTally Late)>();
            PlanPointTest.Walker? walker = null;

            if (picker)
            {
                var flagged = everyMember ? await EveryMemberAsync(databaseFile, code, cancellation) : await SetupsAsync(code, null, cancellation);

                walker = (await PlanPointTest.WalkerAsync(databaseFile, code, output.WriteLine, cancellation)).Walker;
                tallies.AddRange(PickerTally(walker, flagged, cancellation));
                output.WriteLine(FormattableString.Invariant($"{Verb}: {code}: {flagged.Count} stock session(s) {(everyMember ? "of every member" : "the price families flagged")} walked under the picker's {JudgeAccounts.Plans.Count} plans"));
                rows.AddRange(BuyRanges.Rows(code, tallies));

                continue;
            }

            foreach (var family in PlanPoints.Families)
            {
                var standing = RuleRegister.Standing(family, code == PaperFiguresRun.Indices[0], OwnStepCandidate.NameOf(code), register, night);

                if (RuleRegister.LiveAndVariants(standing).Live is not { } live)
                {
                    output.WriteLine($"{Verb}: {code} {family}: no live rule stands, not walked");

                    continue;
                }

                var plan = PlanOf(live);

                if (plan.Entry != PlanPoints.NextOpen)
                {
                    output.WriteLine($"{Verb}: {code} {family}: its live rule buys at {plan.Entry}, so it carries no range and is not walked");

                    continue;
                }

                walker ??= (await PlanPointTest.WalkerAsync(databaseFile, code, output.WriteLine, cancellation)).Walker;

                var setups = await SetupsAsync(code, family, cancellation);

                tallies.AddRange(Tally(walker, family, plan, setups, cancellation));
                output.WriteLine(FormattableString.Invariant($"{Verb}: {code} {family}: {setups.Count} setup(s) walked under {plan.Words}"));
            }

            rows.AddRange(BuyRanges.Rows(code, tallies));
        }

        await WriteAsync(runId, started, rows, everyMember ? BuyRanges.EveryMemberRun : picker ? BuyRanges.PickerRun : null, cancellation);

        foreach (var row in rows.Where(one => one.Family == BuyRanges.AllSetups))
        {
            output.WriteLine(FormattableString.Invariant(
                $"{Verb}: {row.Index} {row.Width,-10} {row.Years,-5} offered {row.Tally.Offered,7} bought {row.Tally.Bought,7} above {row.Tally.Above,6} below {row.Tally.Below,6} edge a pick {row.Tally.EdgePerPick,8:+0.000;-0.000} missed {row.Tally.MissedPerPick,7:0.000} kept out {row.Tally.SavedPerPick,7:0.000}{(row.Chosen ? "  chosen" : string.Empty)}"));
        }

        return 0;
    }

    // Each width's tallies and the open's over one setup's flagged rows, a row the open walks to no result offered under none.
    public static IReadOnlyList<(string Family, RangeWidths? Widths, RangeTally Early, RangeTally Late)> Tally(
        PlanPointTest.Walker walker, string family, PlanCombination plan, IReadOnlyList<PlanPointTest.FlaggedSetup> setups, CancellationToken cancellation = default)
    {
        var widths = BuyRanges.Widths;
        var read = new (bool Early, double Open, (double? Edge, string? NotBought)[] Ranged)?[setups.Count];

        Parallel.For(0, setups.Count, new ParallelOptions { CancellationToken = cancellation }, at =>
        {
            var setup = setups[at];

            if (walker.PicksUnder(setup, [plan]) is not [{ Edge: { } open }])
            {
                return;
            }

            var ranged = new (double? Edge, string? NotBought)[widths.Count];

            for (var one = 0; one < widths.Count; one++)
            {
                var worked = walker.WorkedInRange(setup, [plan], widths[one]) is [var walked] ? walked : null;

                ranged[one] = (worked?.Pick is { Filled: true, Edge: { } edge } ? edge : null, worked?.NotBought);
            }

            read[at] = (setup.Session.Year <= IdeaTester.EarlyThrough, open, ranged);
        });

        var tallies = new List<(string, RangeWidths?, RangeTally, RangeTally)>();
        var (openEarly, openLate) = (RangeTally.Empty, RangeTally.Empty);

        foreach (var one in read)
        {
            if (one is { } held)
            {
                (openEarly, openLate) = held.Early ? (openEarly.Add(held.Open, held.Open, null), openLate) : (openEarly, openLate.Add(held.Open, held.Open, null));
            }
        }

        tallies.Add((family, null, openEarly, openLate));

        for (var width = 0; width < widths.Count; width++)
        {
            var (early, late) = (RangeTally.Empty, RangeTally.Empty);

            foreach (var one in read)
            {
                if (one is { } held)
                {
                    var (edge, side) = held.Ranged[width];

                    (early, late) = held.Early ? (early.Add(held.Open, edge, side), late) : (early, late.Add(held.Open, edge, side));
                }
            }

            tallies.Add((family, widths[width], early, late));
        }

        return tallies;
    }

    // Each width's tallies and the open's under each plan of the picker's menu, over every stock session given; a plan the open
    // walks to no result on a stock session is not offered there.
    // see: The picker's buy range width is chosen first over the price families' setups under its menu and then over every member on every session
    // Each stock session is walked and added to its thread's own tallies at once, the threads' tallies summed at the end, so
    // millions of stock sessions hold no walk of theirs in memory.
    public static IReadOnlyList<(string Family, RangeWidths? Widths, RangeTally Early, RangeTally Late)> PickerTally(
        PlanPointTest.Walker walker, IReadOnlyList<PlanPointTest.FlaggedSetup> setups, CancellationToken cancellation = default)
    {
        var widths = BuyRanges.Widths;
        var plans = JudgeAccounts.Plans;

        // Each plan's tallies: the open's and then each width's, each over the early years and from 2019.
        RangeTally[][][] Empty() => [.. plans.Select(plan => Enumerable.Range(0, widths.Count + 1).Select(entry => new[] { RangeTally.Empty, RangeTally.Empty }).ToArray())];

        var total = Empty();
        var gate = new object();

        Parallel.For(0, setups.Count, new ParallelOptions { CancellationToken = cancellation }, Empty, (at, _, local) =>
        {
            var worked = walker.PickerWorked(setups[at], plans, widths);
            var period = setups[at].Session.Year <= IdeaTester.EarlyThrough ? 0 : 1;

            for (var plan = 0; plan < plans.Count; plan++)
            {
                if (worked[plan] is not { } held)
                {
                    continue;
                }

                local[plan][0][period] = local[plan][0][period].Add(held.Open, held.Open, null);

                for (var width = 0; width < widths.Count; width++)
                {
                    var (edge, side) = held.Ranged[width];

                    local[plan][width + 1][period] = local[plan][width + 1][period].Add(held.Open, edge, side);
                }
            }

            return local;
        },
        local =>
        {
            lock (gate)
            {
                for (var plan = 0; plan < plans.Count; plan++)
                {
                    for (var entry = 0; entry <= widths.Count; entry++)
                    {
                        for (var period = 0; period < 2; period++)
                        {
                            total[plan][entry][period] = total[plan][entry][period].Plus(local[plan][entry][period]);
                        }
                    }
                }
            }
        });

        var tallies = new List<(string, RangeWidths?, RangeTally, RangeTally)>();

        for (var plan = 0; plan < plans.Count; plan++)
        {
            var family = BuyRanges.PickerFamily(plans[plan].Hold, plans[plan].Shape);

            tallies.Add((family, null, total[plan][0][0], total[plan][0][1]));
            tallies.AddRange(widths.Select((width, one) => (family, (RangeWidths?)width, total[plan][one + 1][0], total[plan][one + 1][1])));
        }

        return tallies;
    }

    // Every member of the index on every session the every-session table holds, each with the company value the table read
    // for it, a stock session once.
    public static async Task<List<PlanPointTest.FlaggedSetup>> EveryMemberAsync(string databaseFile, string index, CancellationToken cancellation = default)
    {
        await using var connection = new SqliteConnection(StoreConnection.For(databaseFile));
        await connection.OpenAsync(cancellation);
        await using var command = connection.CreateCommand();

        command.CommandText = EveryMember;
        command.Parameters.AddWithValue("$index", index);
        command.Parameters.AddWithValue("$place", FormattableString.Invariant($"$[{CompanyValuePlace}]"));

        var rows = new List<PlanPointTest.FlaggedSetup>();

        await using var reader = await command.ExecuteReaderAsync(cancellation);

        while (await reader.ReadAsync(cancellation))
        {
            rows.Add(new PlanPointTest.FlaggedSetup(
                EveryMemberFamily,
                reader.GetString(0),
                DateOnly.ParseExact(reader.GetString(1), "yyyy-MM-dd", CultureInfo.InvariantCulture),
                0,
                0,
                null,
                null,
                0,
                reader.IsDBNull(2) ? null : reader.GetDouble(2)));
        }

        return rows;
    }

    async Task<DateOnly?> NewestNightAsync(CancellationToken cancellation)
    {
        await using var connection = new SqliteConnection(StoreConnection.For(databaseFile));
        await connection.OpenAsync(cancellation);
        await using var command = connection.CreateCommand();

        command.CommandText = NewestNight;

        return await command.ExecuteScalarAsync(cancellation) is string day ? DateOnly.ParseExact(day, "yyyy-MM-dd", CultureInfo.InvariantCulture) : null;
    }

    // One family's rows its live setting passed, or with no family every row the four flagged, a stock's session once.
    async Task<List<PlanPointTest.FlaggedSetup>> SetupsAsync(string index, string? family, CancellationToken cancellation)
    {
        await using var connection = new SqliteConnection(StoreConnection.For(databaseFile));
        await connection.OpenAsync(cancellation);
        await using var command = connection.CreateCommand();

        command.CommandText = family is null ? PickerSetups : Setups;
        command.Parameters.AddWithValue("$index", index);
        command.Parameters.AddWithValue("$standing", LongLedger.StandingFrom.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));

        if (family is not null)
        {
            command.Parameters.AddWithValue("$family", family);
        }

        var rows = new List<PlanPointTest.FlaggedSetup>();
        var seen = new HashSet<(string, string)>();

        await using var reader = await command.ExecuteReaderAsync(cancellation);

        while (await reader.ReadAsync(cancellation))
        {
            double? Price(int column) => reader.IsDBNull(column) ? null : Statistic.FromPrice(Money.FromStorage(reader.GetString(column)));

            if (family is null && !seen.Add((reader.GetString(1), reader.GetString(2))))
            {
                continue;
            }

            rows.Add(new PlanPointTest.FlaggedSetup(
                reader.GetString(0),
                reader.GetString(1),
                DateOnly.ParseExact(reader.GetString(2), "yyyy-MM-dd", CultureInfo.InvariantCulture),
                Price(3) ?? 0,
                Price(4) ?? 0,
                Price(5),
                Price(6),
                reader.GetInt32(7),
                reader.IsDBNull(8) ? null : reader.GetDouble(8)));
        }

        return rows;
    }

    async Task WriteAsync(string runId, DateTimeOffset started, IReadOnlyList<RangeWidthRow> rows, string? kind, CancellationToken cancellation)
    {
        await using var connection = new SqliteConnection(StoreConnection.For(databaseFile));
        await connection.OpenAsync(cancellation);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellation);
        var written = Stamp(clock.UtcNow);

        foreach (var row in rows)
        {
            await using var command = connection.CreateCommand();

            command.Transaction = transaction;
            command.CommandText = Insert;
            command.Parameters.AddWithValue("$run_id", runId);
            command.Parameters.AddWithValue("$index_code", row.Index);
            command.Parameters.AddWithValue("$family", row.Family);
            command.Parameters.AddWithValue("$width", row.Width);
            command.Parameters.AddWithValue("$ceiling", (object?)row.Widths?.Ceiling ?? DBNull.Value);
            command.Parameters.AddWithValue("$floor", (object?)row.Widths?.Floor ?? DBNull.Value);
            command.Parameters.AddWithValue("$years", row.Years);
            command.Parameters.AddWithValue("$offered", row.Tally.Offered);
            command.Parameters.AddWithValue("$bought", row.Tally.Bought);
            command.Parameters.AddWithValue("$above", row.Tally.Above);
            command.Parameters.AddWithValue("$below", row.Tally.Below);
            command.Parameters.AddWithValue("$edge", (object?)row.Tally.EdgePerPick ?? DBNull.Value);
            command.Parameters.AddWithValue("$missed", (object?)row.Tally.MissedPerPick ?? DBNull.Value);
            command.Parameters.AddWithValue("$saved", (object?)row.Tally.SavedPerPick ?? DBNull.Value);
            command.Parameters.AddWithValue("$chosen", row.Chosen ? 1 : 0);
            command.Parameters.AddWithValue("$written_at", written);
            await command.ExecuteNonQueryAsync(cancellation);
        }

        var chosen = rows.Where(row => row.Chosen && row.Years == BuyRanges.Early).Select(row => $"{row.Index} {row.Width}").ToArray();

        await using (var command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText = InsertRun;
            command.Parameters.AddWithValue("$run_id", runId);
            command.Parameters.AddWithValue("$stage", Verb);
            command.Parameters.AddWithValue("$started_at", Stamp(started));
            command.Parameters.AddWithValue("$ended_at", Stamp(clock.UtcNow));
            command.Parameters.AddWithValue("$rows", rows.Count);
            command.Parameters.AddWithValue("$detail", (kind is null ? string.Empty : kind + "; ") + (chosen.Length == 0 ? "no width rests on enough fills on any index" : "chosen on 2005 to 2018: " + string.Join("; ", chosen)));
            await command.ExecuteNonQueryAsync(cancellation);
        }

        await transaction.CommitAsync(cancellation);
    }

    static string Stamp(DateTimeOffset at) => at.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);
}
