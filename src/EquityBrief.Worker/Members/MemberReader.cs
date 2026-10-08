using System.Globalization;
using EquityBrief.Core.Components;
using EquityBrief.Core.Families;
using EquityBrief.Core.Indicators;
using EquityBrief.Core.Prices;
using EquityBrief.Core.Readings;
using EquityBrief.Core.Sweep;
using EquityBrief.Core.Time;
using EquityBrief.Data;
using EquityBrief.Worker.Sweep;
using Microsoft.Data.Sqlite;

namespace EquityBrief.Worker.Members;

public sealed record MemberReadOutcome(DateOnly Night, int Members, int WithABar, int Industries);

// The member readings. Writes, for every member of the S&P 500, 400 and 600 on the night, each reading its rules and
// their sweeps read, as it stood on the session: its dollar volume, the round trip a trade at its close pays at the
// published table and at double, the profit gate, the coverage and the state its quarters give it, its nearness to its
// year's high and the sessions since, its volume over its average, its industry's S&P 500 members' return over a month
// and a quarter and their mean surprise before the night; and the market switches an S&P 400's or 600's rule may read.
// Each reading is the one function the sweeps read it with. It makes no request and calls no model, and a night run
// again replaces its own session's rows and no other's.
// see: A 400 or 600 trade pays the published effective spread for its size and price, and its pass tests read the edge after it
// see: The 400 and 600 rules start provisional with liquidity floors and a profit gate before any testing
// see: The nightly run is arithmetic only
public sealed class MemberReader(IClock clock, string databaseFile) : IComponent
{
    public static ComponentAccess Access => new(
        Stores:
        [
            new StoreTouch(Store.Membership, Touch.Read),
            new StoreTouch(Store.Bar, Touch.Read),
            new StoreTouch(Store.MarketBar, Touch.Read),
            new StoreTouch(Store.Calendar, Touch.Read),
            new StoreTouch(Store.Indicator, Touch.Read),
            new StoreTouch(Store.ReportedQuarter, Touch.Read),
            new StoreTouch(Store.Company, Touch.Read),
            new StoreTouch(Store.FundamentalReading, Touch.Read),
            new StoreTouch(Store.MemberReading, Touch.Insert | Touch.Delete),
            new StoreTouch(Store.SwitchReading, Touch.Insert | Touch.Delete),
            new StoreTouch(Store.RunLog, Touch.Insert),
        ],
        Feeds: []);

    public const string Stage = "member-readings";

    // The funds the switches read, SPY the one each index fund is read against.
    public const string LargeFund = "SPY";

    public const string MidFund = "IJH";

    public const string SmallFund = "IJR";

    public const string CreditFund = "HYG";

    const string NewestSession = "SELECT MAX(session_date) FROM bar;";

    const string MembersOn = @"
        SELECT DISTINCT index_code, ticker
        FROM membership
        WHERE " + IndexScope.Condition + @"
          AND (joined IS NULL OR joined <= $session)
          AND (""left"" IS NULL OR ""left"" > $session)
        ORDER BY index_code, ticker;
    ";

    // The night's own index's spans, which say whether a member reporting before the night was one when it reported.
    const string SpansOf = @"SELECT ticker, joined, ""left"" FROM membership WHERE index_code = $index;";

    const string BarsTo = "SELECT ticker, session_date, high, low, close, volume, raw_close FROM bar WHERE session_date <= $session ORDER BY ticker, session_date;";

    const string VolumeAverages = "SELECT ticker, value FROM indicator WHERE session_date = $session AND name = $name;";

    // Each member's newest fetch made on a night before this one, its quarters and the counts and basis they carry.
    const string QuartersBefore = @"
        SELECT r.ticker, r.period_end, r.filing_date, r.net_income, r.operating_income, r.interest_expense, r.interest_read,
               r.shares, r.basis_session, r.basis_close
        FROM reported_quarter r
        WHERE r.fetched_at = (
            SELECT MAX(fetched_at) FROM reported_quarter
            WHERE ticker = r.ticker AND session_date < $session);
    ";

    // The company that same fetch answered for: its sector and its industry.
    const string CompaniesBefore = @"
        SELECT c.ticker, c.sector, c.industry
        FROM company c
        WHERE c.fetched_at = (
            SELECT MAX(fetched_at) FROM reported_quarter
            WHERE ticker = c.ticker AND session_date < $session);
    ";

    const string StatesOn = "SELECT ticker, state FROM fundamental_reading WHERE session_date = $session;";

    const string ReportsBetween = "SELECT ticker, event_date, detail FROM calendar WHERE kind = 'earnings' AND event_date >= $from AND event_date <= $to;";

    const string FundClosesTo = "SELECT series, session_date, close FROM market_bar WHERE series IN ($large, $mid, $small, $credit) AND session_date <= $session;";

    const string ClearMembers = "DELETE FROM member_reading WHERE session_date = $session;";

    const string ClearSwitches = "DELETE FROM switch_reading WHERE session_date = $session;";

    const string InsertMember = @"
        INSERT INTO member_reading (
            index_code, session_date, ticker, close, dollar_volume, company_value, cost, cost_double, profit, coverage,
            state, year_high, nearness, since_high, volume_ratio, industry, industry_month, industry_quarter, peer_surprise)
        VALUES (
            $index_code, $session_date, $ticker, $close, $dollar_volume, $company_value, $cost, $cost_double, $profit,
            $coverage, $state, $year_high, $nearness, $since_high, $volume_ratio, $industry, $industry_month,
            $industry_quarter, $peer_surprise);
    ";

    const string InsertSwitches = @"
        INSERT INTO switch_reading (session_date, ijh_half_year, ijh_year, ijr_half_year, ijr_year, hyg_average, hyg_change)
        VALUES ($session_date, $ijh_half_year, $ijh_year, $ijr_half_year, $ijr_year, $hyg_average, $hyg_change);
    ";

    const string AppendRun = @"
        INSERT INTO run_log (
            run_id, stage, started_at, ended_at, outcome,
            rows_written, model_calls, network_requests, spend, detail)
        VALUES (
            $run_id, $stage, $started_at, $ended_at, 'ok',
            $rows_written, 0, 0, '0', $detail);
    ";

    // One member's quarters as its newest fetch filed them, which of them carried the interest expense the fetch read,
    // and its counts with the close they are stated against.
    sealed record Quarters(IReadOnlyList<FiledIncome> Income, IReadOnlySet<DateOnly> InterestRead, IReadOnlyList<FiledCount> Counts, decimal? BasisClose, DateOnly? BasisSession);

    public async Task<MemberReadOutcome> RunAsync(string indexCode, string runId, CancellationToken cancellation = default, IReadOnlyList<string>? wider = null)
    {
        var startedAt = clock.UtcNow;

        await using var connection = new SqliteConnection(StoreConnection.For(databaseFile));
        await connection.OpenAsync(cancellation);

        // The night is the newest session any name holds, read off the store as the fundamental readings read it.
        var night = await NewestAsync(connection, cancellation) ?? clock.SessionDateAt(clock.UtcNow);
        var members = await MembersAsync(connection, indexCode, night, wider, cancellation);
        var bars = await BarsAsync(connection, night, cancellation);
        var calendar = bars.Values.SelectMany(series => series.Select(bar => bar.Session)).Where(day => day <= night).Distinct().Order().ToArray();
        var at = Array.IndexOf(calendar, night);
        var averages = await ValuesAsync(connection, VolumeAverages, night, ("$name", IndicatorSeries.VolAvg50), cancellation);
        var quarters = await QuartersAsync(connection, night, cancellation);
        var companies = await CompaniesAsync(connection, night, cancellation);
        var states = await StatesAsync(connection, night, cancellation);
        var byDay = bars.ToDictionary(pair => pair.Key, pair => pair.Value.ToDictionary(bar => bar.Session), StringComparer.Ordinal);

        // A name whose stored series holds a hole is read over nothing its bars give, in its own row or in its industry's.
        // see: Bars are never interpolated
        var gaps = new Bars.SeriesGapStop();
        var stopped = members
            .Select(member => member.Ticker)
            .Where(ticker => bars.TryGetValue(ticker, out var series) && gaps.Stops(ticker, [.. series.Select(bar => bar.Session)]))
            .ToHashSet(StringComparer.Ordinal);

        // A member's company value on a session of the calendar, from the count filed before it on its newest fetch's
        // basis, none where it holds no bar on the session or no count was filed before it.
        decimal? ValueOn(string ticker, DateOnly session)
        {
            if (!byDay.TryGetValue(ticker, out var held) || !held.TryGetValue(session, out var bar) || quarters.GetValueOrDefault(ticker) is not { BasisClose: { } basisClose, BasisSession: { } basis } fetch)
            {
                return null;
            }

            return CompanyValue.Tonight(session, bar.Close, fetch.Counts, basisClose, held.TryGetValue(basis, out var then) ? then.Close : null);
        }

        // The night's own index's members, whose industries the wider indices' members are read against.
        var large = members.Where(member => member.Index == indexCode).Select(member => member.Ticker).ToArray();
        var industryOf = companies.Where(pair => pair.Value.Industry is not null).ToDictionary(pair => pair.Key, pair => pair.Value.Industry!, StringComparer.Ordinal);
        var returns = new Dictionary<int, IReadOnlyDictionary<string, double>>();

        foreach (var window in MemberReadings.IndustryWindows)
        {
            var groups = new Dictionary<string, List<(double Value, double Figure)>>(StringComparer.Ordinal);

            if (at - window >= 0)
            {
                var (start, day) = (calendar[at - window], calendar[at]);

                foreach (var ticker in large)
                {
                    if (stopped.Contains(ticker) || !industryOf.TryGetValue(ticker, out var industry) || !byDay.TryGetValue(ticker, out var held)
                        || !held.TryGetValue(day, out var now) || !held.TryGetValue(start, out var then) || then.Close <= 0m
                        || ValueOn(ticker, start) is not { } value || value <= 0m)
                    {
                        continue;
                    }

                    Group(groups, industry).Add((Statistic.FromPrice(value), Statistic.FromRatio(now.Close / then.Close) - 1.0));
                }
            }

            returns[window] = Weighted(groups);
        }

        // The industry's members reporting in the 20 sessions before the night, each weighted by its value on the session
        // before it and read only where it was a member when it reported.
        var peers = new Dictionary<string, List<(double Value, double Figure)>>(StringComparer.Ordinal);

        if (at - MemberReadings.PeerSessions >= 0)
        {
            var (from, to) = (calendar[at - MemberReadings.PeerSessions], calendar[at - 1]);
            var spans = await SpansAsync(connection, indexCode, cancellation);
            var inLarge = large.ToHashSet(StringComparer.Ordinal);

            foreach (var (ticker, reported, surprise) in await ReportsAsync(connection, from, to, cancellation))
            {
                if (!inLarge.Contains(ticker) || stopped.Contains(ticker) || !industryOf.TryGetValue(ticker, out var industry)
                    || !(spans.GetValueOrDefault(ticker) ?? []).Any(span => (span.Joined is not { } joined || joined <= reported) && (span.Left is not { } left || left > reported))
                    || ValueOn(ticker, to) is not { } value || value <= 0m)
                {
                    continue;
                }

                Group(peers, industry).Add((Statistic.FromPrice(value), surprise));
            }
        }

        var peerSurprise = Weighted(peers);
        var switches = await SwitchesAsync(connection, night, cancellation);
        var withABar = 0;

        await using var transaction = await connection.BeginTransactionAsync(cancellation);

        foreach (var clear in new[] { ClearMembers, ClearSwitches })
        {
            await using var command = connection.CreateCommand();

            command.Transaction = (SqliteTransaction)transaction;
            command.CommandText = clear;
            command.Parameters.AddWithValue("$session", Stamp(night));

            await command.ExecuteNonQueryAsync(cancellation);
        }

        foreach (var (index, ticker) in members)
        {
            var series = bars.GetValueOrDefault(ticker) ?? [];
            var last = series.Count - 1;
            var held = last >= 0 && series[last].Session == night && !stopped.Contains(ticker);
            var bar = held ? series[last] : default;
            decimal? traded = held ? (bar.RawClose > 0m ? bar.RawClose : bar.Close) : null;
            var value = held ? ValueOn(ticker, night) : null;
            var fetch = quarters.GetValueOrDefault(ticker);
            var income = fetch?.Income ?? [];
            var four = MemberReadings.FiledBefore(income, night);
            var company = companies.GetValueOrDefault(ticker);

            // see: A member's coverage is read on the night only over quarters whose fetch read their interest expense
            bool? coverage = four.All(quarter => fetch!.InterestRead.Contains(quarter.PeriodEnd))
                ? MemberReadings.Coverage(income, night, company.Sector)
                : null;
            var high = held ? MemberReadings.YearHigh(series, last) : null;
            var industry = company.Industry;

            withABar += held ? 1 : 0;

            await using var command = connection.CreateCommand();

            command.Transaction = (SqliteTransaction)transaction;
            command.CommandText = InsertMember;
            command.Parameters.AddWithValue("$index_code", index);
            command.Parameters.AddWithValue("$session_date", Stamp(night));
            command.Parameters.AddWithValue("$ticker", ticker);
            command.Parameters.AddWithValue("$close", Money(traded));
            command.Parameters.AddWithValue("$dollar_volume", Money(held ? MemberReadings.DollarVolume([.. series.Select(one => (one.Close, one.Volume))]) : null));
            command.Parameters.AddWithValue("$company_value", Money(value));
            command.Parameters.AddWithValue("$cost", Number(traded is { } price ? TradeCost.InPercent(value, price, price) : null));
            command.Parameters.AddWithValue("$cost_double", Number(traded is { } doubled ? TradeCost.InPercent(value, doubled, doubled, TradeCost.Doubled) : null));
            command.Parameters.AddWithValue("$profit", MemberReadings.Profit(income, night) ? 1 : 0);
            command.Parameters.AddWithValue("$coverage", coverage is { } covered ? (covered ? 1 : 0) : DBNull.Value);
            command.Parameters.AddWithValue("$state", (object?)states.GetValueOrDefault(ticker) ?? DBNull.Value);
            command.Parameters.AddWithValue("$year_high", Money(high?.High));
            command.Parameters.AddWithValue("$nearness", Number(high is { High: > 0m } year ? Statistic.FromRatio(bar.Close / year.High) : null));
            command.Parameters.AddWithValue("$since_high", high is { } since ? since.Since : DBNull.Value);
            command.Parameters.AddWithValue("$volume_ratio", Number(held ? MemberReadings.VolumeRatio(bar.Volume, averages.GetValueOrDefault(ticker)) : null));
            command.Parameters.AddWithValue("$industry", (object?)industry ?? DBNull.Value);
            command.Parameters.AddWithValue("$industry_month", Number(industry is not null && returns[MemberReadings.IndustryWindows[0]].TryGetValue(industry, out var month) ? month : null));
            command.Parameters.AddWithValue("$industry_quarter", Number(industry is not null && returns[MemberReadings.IndustryWindows[1]].TryGetValue(industry, out var quarter) ? quarter : null));
            command.Parameters.AddWithValue("$peer_surprise", Number(industry is not null && peerSurprise.TryGetValue(industry, out var surprised) ? surprised : null));

            await command.ExecuteNonQueryAsync(cancellation);
        }

        await using (var command = connection.CreateCommand())
        {
            command.Transaction = (SqliteTransaction)transaction;
            command.CommandText = InsertSwitches;
            command.Parameters.AddWithValue("$session_date", Stamp(night));
            command.Parameters.AddWithValue("$ijh_half_year", Number(switches?.MidHalfYear));
            command.Parameters.AddWithValue("$ijh_year", Number(switches?.MidYear));
            command.Parameters.AddWithValue("$ijr_half_year", Number(switches?.SmallHalfYear));
            command.Parameters.AddWithValue("$ijr_year", Number(switches?.SmallYear));
            command.Parameters.AddWithValue("$hyg_average", Number(switches?.CreditAverage));
            command.Parameters.AddWithValue("$hyg_change", Number(switches?.CreditChange));

            await command.ExecuteNonQueryAsync(cancellation);
        }

        var industries = returns.Values.SelectMany(read => read.Keys).Distinct(StringComparer.Ordinal).Count();

        await using (var log = connection.CreateCommand())
        {
            log.Transaction = (SqliteTransaction)transaction;
            log.CommandText = AppendRun;
            log.Parameters.AddWithValue("$run_id", runId);
            log.Parameters.AddWithValue("$stage", Stage);
            log.Parameters.AddWithValue("$started_at", startedAt.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture));
            log.Parameters.AddWithValue("$ended_at", clock.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture));
            log.Parameters.AddWithValue("$rows_written", members.Count + 1);
            log.Parameters.AddWithValue(
                "$detail",
                FormattableString.Invariant($"{members.Count} member(s) read for {Stamp(night)}, {withABar} holding a bar on it, {industries} industr(ies) of the S&P 500 read; the switches read") + gaps.Report());

            await log.ExecuteNonQueryAsync(cancellation);
        }

        await transaction.CommitAsync(cancellation);

        return new MemberReadOutcome(night, members.Count, withABar, industries);
    }

    // The switches on the night: each index fund over SPY across half a year and a year of sessions, and HYG against its
    // average and its change, every close aligned on the funds' own stored sessions and not the members' bars, since
    // the bar store holds a year and a year's window reads the close 252 sessions before the night, which the members'
    // sessions never reach, while the night's fetch holds each fund's series over the 400 days before its first night
    // and every session since, so the window is read from the funds' own closes once they hold 253 sessions. None is
    // read where the night is no session of the funds'.
    // see: The night asks for the market series' daily closes once a series, and keeps them apart from the members' bars
    // see: The night's switches are aligned on the funds' own sessions, so a year's window is read from closes the members' year of bars never reaches
    sealed record Switches(double? MidHalfYear, double? MidYear, double? SmallHalfYear, double? SmallYear, double? CreditAverage, double? CreditChange);

    static async Task<Switches?> SwitchesAsync(SqliteConnection connection, DateOnly night, CancellationToken cancellation)
    {
        var rows = new List<(string Fund, DateOnly Session, double Close)>();

        await using (var command = connection.CreateCommand())
        {
            command.CommandText = FundClosesTo;
            command.Parameters.AddWithValue("$large", LargeFund);
            command.Parameters.AddWithValue("$mid", MidFund);
            command.Parameters.AddWithValue("$small", SmallFund);
            command.Parameters.AddWithValue("$credit", CreditFund);
            command.Parameters.AddWithValue("$session", Stamp(night));

            await using var reader = await command.ExecuteReaderAsync(cancellation);

            while (await reader.ReadAsync(cancellation))
            {
                rows.Add((reader.GetString(0), Date(reader.GetString(1)), Statistic.FromPrice(Money(reader.GetString(2)))));
            }
        }

        var calendar = rows.Select(row => row.Session).Distinct().Order().ToArray();
        var position = calendar.Select((day, index) => (day, index)).ToDictionary(pair => pair.day, pair => pair.index);

        if (!position.TryGetValue(night, out var at))
        {
            return null;
        }

        var closes = new Dictionary<string, double[]>(StringComparer.Ordinal);

        foreach (var fund in new[] { LargeFund, MidFund, SmallFund, CreditFund })
        {
            var aligned = new double[calendar.Length];

            Array.Fill(aligned, double.NaN);
            closes[fund] = aligned;
        }

        foreach (var (fund, session, close) in rows)
        {
            closes[fund][position[session]] = close;
        }

        double? Against(string fund, int window) =>
            at - window >= 0 ? IndexSwitches.Relative(closes[fund][at], closes[LargeFund][at], closes[fund][at - window], closes[LargeFund][at - window]) : null;

        return new Switches(
            Against(MidFund, IndexSwitches.SmallWindows[0]),
            Against(MidFund, IndexSwitches.SmallWindows[1]),
            Against(SmallFund, IndexSwitches.SmallWindows[0]),
            Against(SmallFund, IndexSwitches.SmallWindows[1]),
            IndexSwitches.OverAverage(closes[CreditFund], at, IndexSwitches.CreditAverageSessions),
            IndexSwitches.Change(closes[CreditFund], at, IndexSwitches.CreditChangeSessions));
    }

    static List<(double Value, double Figure)> Group(Dictionary<string, List<(double Value, double Figure)>> groups, string industry)
    {
        if (!groups.TryGetValue(industry, out var held))
        {
            groups[industry] = held = [];
        }

        return held;
    }

    // Each industry's value-weighted mean through the one function the sweeps read it with, an industry left with none
    // reading none.
    static IReadOnlyDictionary<string, double> Weighted(Dictionary<string, List<(double Value, double Figure)>> groups) =>
        groups
            .Select(pair => (Industry: pair.Key, Mean: MemberReadings.ValueWeighted(pair.Value)))
            .Where(pair => pair.Mean is not null)
            .ToDictionary(pair => pair.Industry, pair => pair.Mean!.Value, StringComparer.Ordinal);

    static async Task<DateOnly?> NewestAsync(SqliteConnection connection, CancellationToken cancellation)
    {
        await using var command = connection.CreateCommand();

        command.CommandText = NewestSession;

        return await command.ExecuteScalarAsync(cancellation) is string newest ? Date(newest) : null;
    }

    static async Task<IReadOnlyList<(string Index, string Ticker)>> MembersAsync(SqliteConnection connection, string indexCode, DateOnly session, IReadOnlyList<string>? wider, CancellationToken cancellation)
    {
        await using var command = connection.CreateCommand();

        command.CommandText = MembersOn;
        IndexScope.Bind(command, indexCode, wider);
        command.Parameters.AddWithValue("$session", Stamp(session));

        var members = new List<(string, string)>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        await using var reader = await command.ExecuteReaderAsync(cancellation);

        while (await reader.ReadAsync(cancellation))
        {
            // A name two indices hold on one session is read once, in the first.
            if (seen.Add(reader.GetString(1)))
            {
                members.Add((reader.GetString(0), reader.GetString(1)));
            }
        }

        return members;
    }

    static async Task<IReadOnlyDictionary<string, IReadOnlyList<(DateOnly? Joined, DateOnly? Left)>>> SpansAsync(SqliteConnection connection, string indexCode, CancellationToken cancellation)
    {
        await using var command = connection.CreateCommand();

        command.CommandText = SpansOf;
        command.Parameters.AddWithValue("$index", indexCode);

        var spans = new Dictionary<string, List<(DateOnly?, DateOnly?)>>(StringComparer.Ordinal);

        await using var reader = await command.ExecuteReaderAsync(cancellation);

        while (await reader.ReadAsync(cancellation))
        {
            if (!spans.TryGetValue(reader.GetString(0), out var held))
            {
                spans[reader.GetString(0)] = held = [];
            }

            held.Add((reader.IsDBNull(1) ? null : Date(reader.GetString(1)), reader.IsDBNull(2) ? null : Date(reader.GetString(2))));
        }

        return spans.ToDictionary(pair => pair.Key, pair => (IReadOnlyList<(DateOnly? Joined, DateOnly? Left)>)pair.Value, StringComparer.Ordinal);
    }

    static async Task<IReadOnlyDictionary<string, IReadOnlyList<SweepBar>>> BarsAsync(SqliteConnection connection, DateOnly night, CancellationToken cancellation)
    {
        await using var command = connection.CreateCommand();

        command.CommandText = BarsTo;
        command.Parameters.AddWithValue("$session", Stamp(night));

        var bars = new Dictionary<string, List<SweepBar>>(StringComparer.Ordinal);

        await using var reader = await command.ExecuteReaderAsync(cancellation);

        while (await reader.ReadAsync(cancellation))
        {
            if (!bars.TryGetValue(reader.GetString(0), out var held))
            {
                bars[reader.GetString(0)] = held = [];
            }

            held.Add(new SweepBar(
                Date(reader.GetString(1)),
                Money(reader.GetString(2)),
                Money(reader.GetString(3)),
                Money(reader.GetString(4)),
                reader.GetInt64(5),
                Money(reader.GetString(4)),
                reader.IsDBNull(6) ? 0m : Money(reader.GetString(6))));
        }

        return bars.ToDictionary(pair => pair.Key, pair => (IReadOnlyList<SweepBar>)pair.Value, StringComparer.Ordinal);
    }

    static async Task<IReadOnlyDictionary<string, double>> ValuesAsync(SqliteConnection connection, string sql, DateOnly night, (string Name, string Value) parameter, CancellationToken cancellation)
    {
        await using var command = connection.CreateCommand();

        command.CommandText = sql;
        command.Parameters.AddWithValue("$session", Stamp(night));
        command.Parameters.AddWithValue(parameter.Name, parameter.Value);

        var values = new Dictionary<string, double>(StringComparer.Ordinal);

        await using var reader = await command.ExecuteReaderAsync(cancellation);

        while (await reader.ReadAsync(cancellation))
        {
            if (!reader.IsDBNull(1))
            {
                values[reader.GetString(0)] = reader.GetDouble(1);
            }
        }

        return values;
    }

    static async Task<IReadOnlyDictionary<string, Quarters>> QuartersAsync(SqliteConnection connection, DateOnly night, CancellationToken cancellation)
    {
        await using var command = connection.CreateCommand();

        command.CommandText = QuartersBefore;
        command.Parameters.AddWithValue("$session", Stamp(night));

        var income = new Dictionary<string, List<FiledIncome>>(StringComparer.Ordinal);
        var read = new Dictionary<string, HashSet<DateOnly>>(StringComparer.Ordinal);
        var counts = new Dictionary<string, List<FiledCount>>(StringComparer.Ordinal);
        var basis = new Dictionary<string, (decimal? Close, DateOnly? Session)>(StringComparer.Ordinal);

        await using var reader = await command.ExecuteReaderAsync(cancellation);

        while (await reader.ReadAsync(cancellation))
        {
            var ticker = reader.GetString(0);
            var periodEnd = Date(reader.GetString(1));

            if (!income.ContainsKey(ticker))
            {
                (income[ticker], read[ticker], counts[ticker]) = ([], [], []);
            }

            basis[ticker] = (reader.IsDBNull(9) ? null : Money(reader.GetString(9)), reader.IsDBNull(8) ? null : Date(reader.GetString(8)));

            // A quarter carrying no filing date cannot be read as it stood, and is read as none filed.
            if (reader.IsDBNull(2))
            {
                continue;
            }

            var filed = Date(reader.GetString(2));

            income[ticker].Add(new FiledIncome(
                periodEnd,
                filed,
                reader.IsDBNull(3) ? null : Money(reader.GetString(3)),
                reader.IsDBNull(4) ? null : Money(reader.GetString(4)),
                reader.IsDBNull(5) ? null : Money(reader.GetString(5))));

            if (reader.GetInt64(6) == 1)
            {
                read[ticker].Add(periodEnd);
            }

            if (!reader.IsDBNull(7) && !reader.IsDBNull(8))
            {
                counts[ticker].Add(new FiledCount(periodEnd, filed, Money(reader.GetString(7)), Date(reader.GetString(8))));
            }
        }

        return income.ToDictionary(
            pair => pair.Key,
            pair => new Quarters(pair.Value, read[pair.Key], counts[pair.Key], basis[pair.Key].Close, basis[pair.Key].Session),
            StringComparer.Ordinal);
    }

    static async Task<IReadOnlyDictionary<string, (string? Sector, string? Industry)>> CompaniesAsync(SqliteConnection connection, DateOnly night, CancellationToken cancellation)
    {
        await using var command = connection.CreateCommand();

        command.CommandText = CompaniesBefore;
        command.Parameters.AddWithValue("$session", Stamp(night));

        var companies = new Dictionary<string, (string?, string?)>(StringComparer.Ordinal);

        await using var reader = await command.ExecuteReaderAsync(cancellation);

        while (await reader.ReadAsync(cancellation))
        {
            companies[reader.GetString(0)] = (reader.IsDBNull(1) ? null : reader.GetString(1), reader.IsDBNull(2) ? null : reader.GetString(2));
        }

        return companies;
    }

    static async Task<IReadOnlyDictionary<string, string>> StatesAsync(SqliteConnection connection, DateOnly night, CancellationToken cancellation)
    {
        await using var command = connection.CreateCommand();

        command.CommandText = StatesOn;
        command.Parameters.AddWithValue("$session", Stamp(night));

        var states = new Dictionary<string, string>(StringComparer.Ordinal);

        await using var reader = await command.ExecuteReaderAsync(cancellation);

        while (await reader.ReadAsync(cancellation))
        {
            states[reader.GetString(0)] = reader.GetString(1);
        }

        return states;
    }

    static async Task<IReadOnlyList<(string Ticker, DateOnly Reported, double Surprise)>> ReportsAsync(SqliteConnection connection, DateOnly from, DateOnly to, CancellationToken cancellation)
    {
        await using var command = connection.CreateCommand();

        command.CommandText = ReportsBetween;
        command.Parameters.AddWithValue("$from", Stamp(from));
        command.Parameters.AddWithValue("$to", Stamp(to));

        var reports = new List<(string, DateOnly, double)>();

        await using var reader = await command.ExecuteReaderAsync(cancellation);

        while (await reader.ReadAsync(cancellation))
        {
            if (!reader.IsDBNull(2) && SweepHistory.SurpriseIn(reader.GetString(2)) is { } surprise)
            {
                reports.Add((reader.GetString(0), Date(reader.GetString(1)), surprise));
            }
        }

        return reports;
    }

    static object Money(decimal? value) => value is { } held ? Data.Money.ToStorage(held) : DBNull.Value;

    static decimal Money(string stored) => Data.Money.FromStorage(stored);

    static object Number(double? value) => value is { } held && !double.IsNaN(held) && !double.IsInfinity(held) ? held : DBNull.Value;

    static string Stamp(DateOnly date) => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    static DateOnly Date(string text) => DateOnly.ParseExact(text, "yyyy-MM-dd", CultureInfo.InvariantCulture);
}
