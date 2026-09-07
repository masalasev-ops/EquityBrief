using System.Reflection;

namespace EquityBrief.Tests.Harness;

// What a check can reach: the corpus files it opens, and the claim subjects it
// can reach a verdict on.
//
// Declared by the check itself and never in a list beside it. A list beside a
// check is a second statement of one fact and nothing keeps the two together,
// which is how a placement came to name an instrument that never opened the
// file it was placed against.
//
// Reads is context and carries no floor, because its size is a fact about how
// the check happens to be written. Subjects is the scope that carries the
// property, and it is what the reconciliation counts.
internal sealed record CheckReach(
    string Check,
    IReadOnlyList<string> Reads,
    IReadOnlyList<string> Subjects)
{
    // A subject entry is either a table heading, meaning the whole table, or a
    // heading and one row's subject joined by this, meaning one claim.
    internal const string Joiner = " :: ";

    internal static string Key(string table, string subject) => table + Joiner + subject;

    internal bool Covers(string table, string subject) =>
        Subjects.Contains(table, StringComparer.Ordinal)
        || Subjects.Contains(Key(table, subject), StringComparer.Ordinal);
}

// Every declaration in the suite, collected from the checks themselves rather
// than from a register the checks do not read.
internal static class CheckReaches
{
    internal static IReadOnlyList<CheckReach> All() =>
        Assembly.GetExecutingAssembly().GetTypes()
            .Select(type => type.GetProperty(
                "Reach",
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static))
            .Where(property => property?.PropertyType == typeof(CheckReach))
            .Select(property => (CheckReach)property!.GetValue(null)!)
            .OrderBy(reach => reach.Check, StringComparer.Ordinal)
            .ToArray();

    internal static CheckReach? Of(string check) =>
        All().FirstOrDefault(reach => reach.Check == check);
}
