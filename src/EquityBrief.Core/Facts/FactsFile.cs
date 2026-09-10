using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace EquityBrief.Core.Facts;

// One declared fact: what it is, what it is worth, and where it came from.
//
// `Name` is the descriptive name a report cites rather than a position, because
// a number tells a reader nothing and forces a lookup. `Source` is the store the
// value was read from, so every figure in a written section traces to a computed
// value and says which stage computed it.
// see: Facts are declared once and cited by descriptive name
// see: Code owns every number
public sealed record Fact(string Name, string Value, string Source);

// The facts file: every number the computed sections may use, each with its
// source, and the hash of the whole.
//
// The serialised form is what a fixture is diffed on, so it is written here once
// and read by both the assembler and the comparison rather than assembled at
// each. Keys are in a stated order rather than in insertion order, because a
// file whose byte-for-byte comparison depends on the order a dictionary happened
// to yield is a comparison that reports a difference nobody made.
// see: The fixture is diffed on each stage's serialised output, of which the facts file is one
public static class FactsFile
{
    // Written with no indentation and with the facts in name order, so two runs
    // over the same store produce the same bytes and a diff is a difference in
    // the figures rather than in the writing.
    public static string Serialise(string ticker, DateOnly sessionDate, IReadOnlyList<Fact> facts) =>
        JsonSerializer.Serialize(new
        {
            ticker,
            sessionDate = sessionDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            facts = facts
                .OrderBy(fact => fact.Name, StringComparer.Ordinal)
                .Select(fact => new { name = fact.Name, value = fact.Value, source = fact.Source }),
        });

    // The hash of the payload, which is what the staleness judge reads to know
    // whether tonight's facts differ from the last night's without holding the
    // facts they differ from. That is also why the retention keeps it when it
    // empties the payload beside it.
    public static string Hash(string payload) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(payload)));

    // What changed between two facts files, by name.
    //
    // A material change is a fact that appeared, went, or took a different
    // value. The comparison is on the name rather than on the position, so a
    // fact added between two others is one change rather than every fact after
    // it changing.
    public static IReadOnlyList<string> Changed(string? before, string after)
    {
        if (before is null)
        {
            return [];
        }

        var was = ByName(before);
        var now = ByName(after);

        return
        [
            .. was.Keys.Concat(now.Keys)
                .Distinct(StringComparer.Ordinal)
                .Where(name => !was.TryGetValue(name, out var old)
                    || !now.TryGetValue(name, out var fresh)
                    || !string.Equals(old, fresh, StringComparison.Ordinal))
                .OrderBy(name => name, StringComparer.Ordinal),
        ];
    }

    static IReadOnlyDictionary<string, string> ByName(string payload)
    {
        using var document = JsonDocument.Parse(payload);

        return document.RootElement.GetProperty("facts").EnumerateArray()
            .ToDictionary(
                fact => fact.GetProperty("name").GetString()!,
                fact => fact.GetProperty("value").GetString()!,
                StringComparer.Ordinal);
    }
}
