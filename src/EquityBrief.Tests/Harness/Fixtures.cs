namespace EquityBrief.Tests.Harness;

internal sealed record FixtureStatus(int Folders, string State, string Note);

// What the harness can say about the committed fixture.
//
// A fixture that is not there is reported as absent. It is never reported as
// passing and never left out, because a stage nobody mentions reads as a stage
// that went fine.
internal static class Fixtures
{
    internal static FixtureStatus Of(string root)
    {
        var folder = Path.Combine(root, "fixtures");

        var captured = Directory.Exists(folder)
            ? Directory.GetDirectories(folder).Length
            : 0;

        return captured == 0
            ? new FixtureStatus(
                0,
                "ABSENT",
                "no fixture is captured. 0.6 builds the folder and its manifest schema and " +
                "nothing else; the first captured inputs and the first manifest arrive with the " +
                "gap fixture at 1.3, and phase 1's expectations at 1.8. Out of scope until then, " +
                "and never a pass.")
            : new FixtureStatus(
                captured,
                "PRESENT",
                "each folder's manifest is checked against fixtures/manifest.schema.json.");
    }
}
