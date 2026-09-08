using System.Globalization;
using EquityBrief.Data;
using Microsoft.Data.Sqlite;

namespace EquityBrief.Tests.Store;

// The runtime half of the money rule, which nothing guarded until 1.2.
//
// price-storage-form reads every migration and asserts no money column is
// declared REAL. That is the storage half. The code half is that a double never
// reaches one, and 0.2 recorded that STRICT does not enforce it after a test
// written to prove it did failed instead.
public class MoneyTests
{
    [Fact]
    public void ADoubleIsRefusedRatherThanStoredAsItsRendering()
    {
        using var store = new TemporaryStore().Migrated();
        using var connection = store.Open();
        using var command = connection.CreateCommand();

        var refusal = Assert.Throws<ArgumentException>(() => Money.Bind(command, "$close", 12.34));

        Assert.Contains("money column", refusal.Message, StringComparison.Ordinal);
        Assert.Contains("Double", refusal.Message, StringComparison.Ordinal);

        // A decimal is bound, so this is not a guard that refuses everything.
        Assert.Equal("12.34", Money.Bind(command, "$open", 12.34m).Value);
    }

    [Fact]
    public void TheStoreItselfWouldHaveTakenTheDouble()
    {
        // The reason the guard exists, asserted rather than asserted about.
        // Without it the double reaches the column, is rendered to text, and is
        // read back as a value nothing refused.
        using var store = new TemporaryStore().Migrated();
        using var connection = store.Open();

        using var insert = connection.CreateCommand();
        insert.CommandText =
            "INSERT INTO bar (ticker, session_date, open, high, low, close, volume, source, observed_at) " +
            "VALUES ('AAPL', '2026-09-04', $open, '1', '1', '1', 1, 'test', '2026-09-04T20:00:00Z');";
        insert.Parameters.AddWithValue("$open", 12.34);
        insert.ExecuteNonQuery();

        using var read = connection.CreateCommand();
        read.CommandText = "SELECT typeof(open), open FROM bar WHERE ticker = 'AAPL';";
        using var reader = read.ExecuteReader();

        Assert.True(reader.Read());

        // Stored as text, silently, which is what STRICT does not stop.
        Assert.Equal("text", reader.GetString(0));
        Assert.Equal("12.34", reader.GetString(1));
    }

    [Fact]
    public void MoneyRoundTripsInTheInvariantForm()
    {
        Assert.Equal("1.5", Money.ToStorage(1.5m));
        Assert.Equal(1.5m, Money.FromStorage("1.5"));

        // Trailing zeros are a decimal's own scale and are kept, because a price
        // quoted to the cent and one quoted to the dollar are different facts.
        Assert.Equal("12.30", Money.ToStorage(12.30m));

        // A comma-decimal rendering is refused rather than read as 1234.
        //
        // This is not a hypothetical. NumberStyles.Number, the convenient
        // default, allows group separators, and under the invariant culture the
        // group separator is a comma, so "12,34" written by a comma-decimal
        // machine parses cleanly as one thousand two hundred and thirty four.
        // A hundredfold error on a price, read back with nothing refusing it.
        Assert.Throws<FormatException>(() => Money.FromStorage("12,34"));
        Assert.Throws<FormatException>(() => Money.FromStorage("1,234.56"));
        Assert.Throws<FormatException>(() => Money.FromStorage("not a number"));
    }

    [Fact]
    public void TheFormDoesNotDependOnTheMachinesLocale()
    {
        // Pinned against a comma-decimal culture, since the default on this
        // machine happens to be a dot-decimal one and would prove nothing.
        var was = Thread.CurrentThread.CurrentCulture;

        try
        {
            Thread.CurrentThread.CurrentCulture = new CultureInfo("de-DE");

            Assert.Equal("1.5", Money.ToStorage(1.5m));
            Assert.Equal(1.5m, Money.FromStorage("1.5"));
        }
        finally
        {
            Thread.CurrentThread.CurrentCulture = was;
        }
    }
}
