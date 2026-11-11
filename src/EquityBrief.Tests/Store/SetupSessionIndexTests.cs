using System.Reflection;
using System.Text.RegularExpressions;
using EquityBrief.Worker.Ledger;

namespace EquityBrief.Tests.Store;

public class SetupSessionIndexTests
{
    // The plan is read off a migrated store for the statements the ledger itself runs, read from the ledger rather than
    // copied here, so a statement or the index changing shape turns this red.
    [Theory]
    [InlineData("BusinessAgain", "session_date=? AND ticker=?")]
    [InlineData("InsidersAgain", "session_date=? AND ticker=?")]
    [InlineData("NightsSetupTickers", "session_date=?")]
    public void TheFilingsRefreshsSecondReadFindsTheNightsSetupsThroughTheSessionIndexAndReadsNoWholeTable(string statement, string searched)
    {
        var sql = (string)typeof(SetupLedger).GetField(statement, BindingFlags.NonPublic | BindingFlags.Static)!.GetRawConstantValue()!;

        using var store = new TemporaryStore().Migrated();
        using var connection = store.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "EXPLAIN QUERY PLAN " + sql;

        foreach (var name in Regex.Matches(sql, @"\$\w+").Select(match => match.Value).Distinct(StringComparer.Ordinal))
        {
            command.Parameters.AddWithValue(name, DBNull.Value);
        }

        var plan = new List<string>();

        using (var reader = command.ExecuteReader())
        {
            while (reader.Read())
            {
                plan.Add(reader.GetString(3));
            }
        }

        Assert.Contains(plan, step => Regex.IsMatch(step, $@"^SEARCH setup USING (COVERING )?INDEX setup_session \({Regex.Escape(searched)}\)$"));
        Assert.DoesNotContain(plan, step => step.StartsWith("SCAN setup", StringComparison.Ordinal));
    }
}
