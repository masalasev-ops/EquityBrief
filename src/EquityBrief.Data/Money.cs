using System.Globalization;
using Microsoft.Data.Sqlite;

namespace EquityBrief.Data;

// The one way a price or a money value reaches a store.
//
// SCHEMA says prices and money are TEXT in storage and decimal in code, and
// price-storage-form asserts the storage half by reading every migration. What
// nothing asserted until now is the code half at runtime, and STRICT does not
// help: a STRICT table refuses a value it cannot losslessly convert, and SQLite
// converts a double to text by rendering it, so a double reaching a money column
// is stored as its rendering rather than refused. 0.2 recorded that after a test
// written to prove the opposite failed.
//
// So the guard is here, and it is a guard rather than a convention: a caller
// that hands this anything but a decimal is refused, by type at compile time and
// by a named refusal at runtime for the boxed case a parameter collection makes
// easy to reach.
public static class Money
{
    // The invariant round-trip form. A money value formatted against the
    // machine's locale would store "1,5" on a comma-decimal machine and read
    // back as something else on another, which is the same class of fault as a
    // date parsed against the machine's locale.
    public static string ToStorage(decimal value) =>
        value.ToString(CultureInfo.InvariantCulture);

    // A sign and a decimal point, and nothing else. Not NumberStyles.Number,
    // which allows group separators: under the invariant culture the group
    // separator is a comma, so "12,34" written by a comma-decimal machine parses
    // as 1234 rather than failing. That is a hundredfold error on a price, read
    // back with nothing refusing it, and it is the reason this is spelled out
    // rather than left at the convenient default.
    const NumberStyles Form = NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint;

    public static decimal FromStorage(string stored) =>
        decimal.TryParse(stored, Form, CultureInfo.InvariantCulture, out var value)
            ? value
            : throw new FormatException(
                $"'{stored}' is not a money value this store wrote. Money is decimal in code and " +
                "TEXT in storage, in the invariant form, and a value that will not round-trip is " +
                "a value something else put there.");

    // Binds a money parameter, refusing anything that is not a decimal.
    //
    // The refusal is by name rather than by a cast exception, because the caller
    // that reaches this with a double has usually written a literal without the
    // m suffix, and the message has to say that rather than say InvalidCast.
    public static SqliteParameter Bind(SqliteCommand command, string name, object value)
    {
        if (value is not decimal money)
        {
            throw new ArgumentException(
                $"{name} is a money column and was given a {value.GetType().Name}. Money is decimal " +
                "in code and TEXT in storage. STRICT will not catch this: SQLite renders a double " +
                "as text and stores it, because that conversion is lossless, so the wrong number " +
                "would be stored and read back without anything failing.",
                nameof(value));
        }

        return command.Parameters.AddWithValue(name, ToStorage(money));
    }
}
