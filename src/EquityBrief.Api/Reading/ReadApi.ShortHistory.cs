using System.Globalization;
using System.Text.Json;
using EquityBrief.Core.Families;
using EquityBrief.Core.Indicators;
using EquityBrief.Core.Ladders;
using EquityBrief.Core.Levels;
using EquityBrief.Core.Prices;
using EquityBrief.Data;

namespace EquityBrief.Api.Reading;

// The plan a page draws: the night's stored one, or for a stock with too few sessions for its averages the one its levels
// give, worked here in the stored plan's own shape so every page reads it as it reads a stored one. Nothing is written.
// see: A stock with too few sessions for its averages draws its plan from its levels when a page is read and no rule reads it
public sealed partial class ReadApi
{
    const string SessionsOfName = "SELECT COUNT(*), MIN(session_date) FROM bar WHERE ticker = $ticker AND session_date <= $on;";

    const string FirstSessionOfTheStore = "SELECT MIN(session_date) FROM bar;";

    const string RecentOfName = @"
        SELECT session_date, high, low, close
        FROM (SELECT session_date, high, low, close FROM bar WHERE ticker = $ticker AND session_date <= $on
              ORDER BY session_date DESC LIMIT $window)
        ORDER BY session_date;
    ";

    const string TypicalMoveOfName = "SELECT value FROM indicator WHERE ticker = $ticker AND session_date = $session AND name = $name AND value IS NOT NULL;";

    public async Task<LadderRow?> PlanLadderAsync(string ticker, DateOnly? asOf = null)
    {
        var stored = await LadderAsync(ticker, asOf);

        await using var connection = Open();

        int sessions;
        DateOnly first;

        await using (var command = connection.CreateCommand())
        {
            command.CommandText = SessionsOfName;
            command.Parameters.AddWithValue("$ticker", ticker);
            command.Parameters.AddWithValue("$on", On(asOf));

            await using var reader = await command.ExecuteReaderAsync();

            if (!await reader.ReadAsync() || reader.GetInt32(0) is 0 or >= ShortHistoryPlan.SessionsTheAverageNeeds)
            {
                return stored;
            }

            sessions = reader.GetInt32(0);
            first = DateOnly.ParseExact(reader.GetString(1), "yyyy-MM-dd", CultureInfo.InvariantCulture);
        }

        // A stock whose bars open with the store's own did not start trading on that day, however few they are, and a plan the
        // night placed is never replaced.
        await using (var command = connection.CreateCommand())
        {
            command.CommandText = FirstSessionOfTheStore;

            if (first <= DateOnly.ParseExact((string)(await command.ExecuteScalarAsync())!, "yyyy-MM-dd", CultureInfo.InvariantCulture) || NameScreen.PlanRows(stored).Count > 0)
            {
                return stored;
            }
        }

        var recent = new List<LadderBar>();

        await using (var command = connection.CreateCommand())
        {
            command.CommandText = RecentOfName;
            command.Parameters.AddWithValue("$ticker", ticker);
            command.Parameters.AddWithValue("$on", On(asOf));
            command.Parameters.AddWithValue("$window", LadderSeries.ConditionLookback);

            await using var reader = await command.ExecuteReaderAsync();

            while (await reader.ReadAsync())
            {
                recent.Add(new LadderBar(
                    DateOnly.ParseExact(reader.GetString(0), "yyyy-MM-dd", CultureInfo.InvariantCulture),
                    Money.FromStorage(reader.GetString(1)),
                    Money.FromStorage(reader.GetString(2)),
                    Money.FromStorage(reader.GetString(3))));
            }
        }

        var session = recent[^1].SessionDate;
        decimal? typicalMove = null;

        await using (var command = connection.CreateCommand())
        {
            command.CommandText = TypicalMoveOfName;
            command.Parameters.AddWithValue("$ticker", ticker);
            command.Parameters.AddWithValue("$session", session.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
            command.Parameters.AddWithValue("$name", IndicatorSeries.Atr14);

            if (await command.ExecuteScalarAsync() is double move)
            {
                typicalMove = Statistic.ToPrice(move);
            }
        }

        var bands = (await LevelsAsync(ticker, session))
            .Select(row => new Level(row.LowEdge, row.HighEdge, row.Role, row.Immediate, row.Strength, row.HasNonAverageAnchor, []))
            .ToArray();

        if (bands.Length == 0 || typicalMove is not { } typical)
        {
            var why = ShortHistoryPlan.NoLevels(first, sessions);

            return new LadderRow(ticker, session, stored?.TrendState ?? TrendState.NotClassified, Shaped(new Ladder([], [], null, why, []), stored), ShortWhy: why);
        }

        var plan = ShortHistoryPlan.Worked(bands, recent[^1].Close, typical, recent);

        if (plan.Tranches.Count == 0)
        {
            var why = ShortHistoryPlan.NoZone(first, sessions, plan.Reason ?? RuleWording.Ladder.NoTranche);

            return new LadderRow(ticker, session, stored?.TrendState ?? TrendState.NotClassified, Shaped(plan with { Reason = why }, stored), ShortWhy: why);
        }

        return new LadderRow(ticker, session, stored?.TrendState ?? TrendState.NotClassified, Shaped(plan, stored), ShortNote: ShortHistoryPlan.Why(first, sessions));
    }

    // The stored plan's shape, the night's earnings rule carried over as it stands.
    static string Shaped(Ladder plan, LadderRow? stored)
    {
        using var kept = stored is null ? null : JsonDocument.Parse(stored.Plan);
        var arithmetic = LadderSeries.ArithmeticFor(plan);

        return JsonSerializer.Serialize(new
        {
            tranches = plan.Tranches.Select(tranche => new
            {
                lowEdge = Money.ToStorage(tranche.LowEdge),
                highEdge = Money.ToStorage(tranche.HighEdge),
                condition = tranche.Condition.ToString(),
                stop = tranche.Stop is { } stop ? Money.ToStorage(stop) : null,
            }),
            exits = plan.Exits.Select(exit => new
            {
                lowEdge = Money.ToStorage(exit.LowEdge),
                highEdge = Money.ToStorage(exit.HighEdge),
                traded = exit.Traded,
                trailing = exit.Trailing,
                fraction = exit.Fraction,
                reason = exit.Reason,
            }),
            invalidation = plan.Invalidation is { } price ? Money.ToStorage(price) : null,
            events = Array.Empty<object>(),
            arithmetic = new
            {
                absent = arithmetic.Absent,
                firstEntry = Money.ToStorage(arithmetic.FirstEntry),
                firstRisk = Money.ToStorage(arithmetic.FirstRisk),
                firstReward = Money.ToStorage(arithmetic.FirstReward),
                blendedEntry = arithmetic.BlendedEntry is { } entry ? Money.ToStorage(entry) : null,
                blendedRisk = arithmetic.BlendedRisk is { } risk ? Money.ToStorage(risk) : null,
                blendedReward = arithmetic.BlendedReward is { } reward ? Money.ToStorage(reward) : null,
                firstRewardToRisk = arithmetic.FirstRewardToRisk is { } first ? Money.ToStorage(Quoted(first)) : null,
                blendedRewardToRisk = arithmetic.BlendedRewardToRisk is { } blended ? Money.ToStorage(Quoted(blended)) : null,
                breakEven = arithmetic.BreakEven is { } even ? Money.ToStorage(Quoted(even)) : null,
            },
            earningsRule = kept is not null && kept.RootElement.TryGetProperty("earningsRule", out var prints) ? prints : JsonSerializer.SerializeToElement(Array.Empty<object>()),
            reason = plan.Reason ?? RuleWording.Ladder.NoReportDated,
        });
    }

    static decimal Quoted(decimal ratio) => Math.Round(ratio, PriceForm.Places, MidpointRounding.AwayFromZero);
}
