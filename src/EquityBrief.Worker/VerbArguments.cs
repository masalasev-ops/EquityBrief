using System.Globalization;

namespace EquityBrief.Worker;

// One form of a verb: the flag that names it, the flags taking a value it needs and may take, and its switches.
public sealed record VerbForm(string Flag, IReadOnlyList<string> Needs, IReadOnlyList<string> May, IReadOnlyList<string> Switches);

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

    // The form a command line is, or why it is none: a flag where a value goes, a
    // blank value, a flag no form takes, a stray word, a flag given twice, no form or
    // a second one, a flag its form does not take, or a flag it needs. A second form
    // is refused rather than one chosen, since choosing would be the command
    // deciding what was meant.
    public static (VerbForm? Form, string? Refusal) FormOf(string[] args, IReadOnlyList<VerbForm> forms)
    {
        var valued = forms.SelectMany(form => form.Needs.Concat(form.May)).ToHashSet(StringComparer.Ordinal);
        var switches = forms.SelectMany(form => form.Switches).ToHashSet(StringComparer.Ordinal);
        var given = new List<string>();

        for (var at = 1; at < args.Length; at++)
        {
            var token = args[at];

            if (!token.StartsWith("--", StringComparison.Ordinal))
            {
                return (null, $"'{token}' is neither a flag nor the value of one.");
            }

            if (given.Contains(token, StringComparer.Ordinal))
            {
                return (null, $"'{token}' is given twice.");
            }

            given.Add(token);

            if (valued.Contains(token))
            {
                if (at + 1 >= args.Length || args[at + 1].StartsWith("--", StringComparison.Ordinal))
                {
                    return (null, $"'{token}' is followed by {(at + 1 < args.Length ? $"'{args[at + 1]}'" : "nothing")}, where it takes a value.");
                }

                if (string.IsNullOrWhiteSpace(args[at + 1]))
                {
                    return (null, $"'{token}' was given a blank value.");
                }

                at++;
            }
            else if (!switches.Contains(token))
            {
                return (null, $"'{token}' is not a flag this verb takes.");
            }
        }

        var named = forms.Where(form => given.Contains(form.Flag, StringComparer.Ordinal)).ToArray();

        if (named.Length == 0)
        {
            return (null, $"no form was given. The forms are {string.Join(", ", forms.Select(form => $"'{form.Flag}'"))}.");
        }

        if (named.Length > 1)
        {
            return (null,
                $"{string.Join(" and ", named.Select(form => $"'{form.Flag}'"))} are {named.Length} forms given together, " +
                "and choosing one would be this command deciding what was meant.");
        }

        var chosen = named[0];
        var takes = new[] { chosen.Flag }.Concat(chosen.Needs).Concat(chosen.May).Concat(chosen.Switches).ToHashSet(StringComparer.Ordinal);

        if (given.FirstOrDefault(flag => !takes.Contains(flag)) is { } stray)
        {
            return (null, $"'{stray}' is not a flag the '{chosen.Flag}' form takes.");
        }

        var missing = chosen.Needs.Where(flag => !given.Contains(flag, StringComparer.Ordinal)).ToArray();

        return missing.Length > 0
            ? (null, $"the '{chosen.Flag}' form needs {string.Join(", ", missing.Select(flag => $"'{flag}'"))}.")
            : (chosen, null);
    }

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

            var name = pair[..at].Trim();

            if (read.ContainsKey(name))
            {
                throw new FormatException(
                    $"'{name}' is given twice. A candidate or a version runs with one value of each parameter, " +
                    "and keeping either would be choosing for the person who typed both.");
            }

            read[name] = value;
        }

        return read;
    }
}
