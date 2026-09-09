using System.Text.Json;

namespace EquityBrief.Tests.Harness;

// The fixture's populations, read from the membership expectation.
//
// They were literals in each test until 4.0, and 3.6 measured what that costs:
// widening the fixture from three names to four turned 28 tests red and the
// repair touched twenty-five assertion sites across six test classes. One of
// the twenty-five was a named constant covering six assertions, which is the
// shape the other twenty-four should have had.
//
// A literal is not wrong in a test. What is wrong is the same fact written
// twenty-five times, because widening the fixture is then a change to
// twenty-five places that agree with each other by hand, and a test that was
// missed reads as a test that was right.
//
// Read from the expectation rather than from the folder, which is where
// `Fixtures.Populations` reads. The two derivations are reconciled against each
// other once, in the check named for it, rather than at every site: one counts
// files on disk and the other states what the rules produce over them, and a
// disagreement between them is a finding rather than a detail.
internal static class FixtureExpectation
{
    internal const string Folder = "membership-2026-09-05";

    internal static JsonElement Of(string stage) =>
        JsonDocument.Parse(File.ReadAllText(Path.Combine(
            Repository.Root, "fixtures", Folder, "expectations", stage + ".json"))).RootElement;

    static JsonElement Membership => Of("membership");

    static string[] Strings(JsonElement array) =>
        [.. array.EnumerateArray().Select(item => item.GetString()!)];

    // Every membership row the captured payload carries, current and departed.
    internal static int Constituents => Membership.GetProperty("constituents").GetInt32();

    // The tickers with no leave date. This is the population a night computes
    // over and the one a per-name count is asserted against.
    internal static string[] CurrentMembers => Strings(Membership.GetProperty("currentMembers"));

    // The tickers that left the index. They keep their row and are owed no
    // series, which is why they are not names.
    internal static string[] Departed => Strings(Membership.GetProperty("departed"));

    // The names with a captured price series, which is what a document means by
    // a fixture widening to four names. Equal to the current members here and
    // not equal in general: a departed name could still carry a series.
    internal static string[] Names => Strings(Of("bars").GetProperty("namesBackfilled"));
}
