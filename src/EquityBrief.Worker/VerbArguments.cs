using System.Globalization;

namespace EquityBrief.Worker;

// What a verb reads off its command line, in one place for every verb that takes
// the same shape of argument.
public static class VerbArguments
{
    // The value after a flag, or null where the flag is absent or has nothing
    // after it.
    public static string? Value(string[] args, string name)
    {
        var at = Array.IndexOf(args, name);

        return at >= 0 && at + 1 < args.Length ? args[at + 1] : null;
    }

    public static bool Has(string[] args, string name) => Array.IndexOf(args, name) >= 0;

    // `name=value,name=value`, parsed against the invariant culture for the reason
    // every date on this path is: a decimal comma read against the machine's locale
    // would register a different condition, or open a different version, here and
    // on the runner.
    public static IReadOnlyDictionary<string, double> Parameters(string? given)
    {
        var read = new Dictionary<string, double>(StringComparer.Ordinal);

        foreach (var pair in (given ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries))
        {
            var at = pair.IndexOf('=', StringComparison.Ordinal);

            if (at < 0 || !double.TryParse(pair[(at + 1)..], NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
            {
                throw new FormatException(
                    $"'{pair.Trim()}' is not a name and a number. Parameters are the values a candidate's " +
                    "evaluator or a rule's version is run with, so one nobody can read back is a row nothing can run.");
            }

            read[pair[..at].Trim()] = value;
        }

        return read;
    }
}
