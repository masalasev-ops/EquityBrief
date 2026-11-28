using EquityBrief.Tests.Harness;
using EquityBrief.Worker.Ledger;
using Xunit;

namespace EquityBrief.Tests.Checks;

// From the 24.19 correction of 2026-11-28: a ledger build finds a setup's history by its code and its session, never by its
// code alone, since the long history reads one code as several parts; and every full build runs its smoke month before it
// writes, refusing where two parts of a code share a session or the month stops.
// see: A ledger build finds a setup's history by its code and its session, and runs one month over every code before it rebuilds
public class LedgerPartsTests
{
    // XXX read as two parts, its code passed on after the first: the first holding sessions 0 to 9, the second 20 to 29; YYY
    // one part holding 0 to 29.
    static readonly string[] Codes = ["XXX", "YYY", "XXX"];

    static readonly int[][] Sessions = [[.. Enumerable.Range(0, 10)], [.. Enumerable.Range(0, 30)], [.. Enumerable.Range(20, 10)]];

    [Fact]
    public void ACodeReadInPartsIsFoundByTheCodeAndTheSessionTogether()
    {
        var parts = LedgerParts.Of(Codes);

        Assert.Equal([0, 2], parts["XXX"]);
        Assert.Equal(0, LedgerParts.PlaceAt(parts, place => Sessions[place], "XXX", 5));
        Assert.Equal(2, LedgerParts.PlaceAt(parts, place => Sessions[place], "XXX", 25));

        // A session neither part holds, and a code the history does not read, name no history.
        Assert.Equal(-1, LedgerParts.PlaceAt(parts, place => Sessions[place], "XXX", 15));
        Assert.Equal(-1, LedgerParts.PlaceAt(parts, place => Sessions[place], "ZZZ", 5));
        Assert.Equal(1, LedgerParts.PlaceAt(parts, place => Sessions[place], "YYY", 25));
    }

    [Fact]
    public void TwoPartsOfACodeSharingASessionAreFound()
    {
        Assert.Empty(LedgerParts.SharingASession(LedgerParts.Of(Codes), place => Sessions[place]));

        // The second part moved to start at session 5, sharing 5 to 9 with the first.
        int[][] overlapping = [Sessions[0], Sessions[1], [.. Enumerable.Range(5, 25)]];

        Assert.Equal([("XXX", 5)], LedgerParts.SharingASession(LedgerParts.Of(Codes), place => overlapping[place]));
    }

    [Fact]
    public void EveryFullBuildRunsItsSmokeMonthBeforeItWritesAndNoLookupReadsACodeAlone()
    {
        var folder = Path.Combine(Repository.Root, "src", "EquityBrief.Worker", "Ledger");
        var ledger = File.ReadAllText(Path.Combine(folder, "SetupLedger.cs"));
        var longLedger = File.ReadAllText(Path.Combine(folder, "SetupLedger.Long.cs"));

        static void Before(string source, string first, string then)
        {
            var at = source.IndexOf(first, StringComparison.Ordinal);
            var after = source.IndexOf(then, StringComparison.Ordinal);

            Assert.True(at >= 0 && after > at, $"Expected \"{first}\" before \"{then}\".");
        }

        // The standing build, the monthly rebuild and the long build.
        Before(ledger, "return SmokeRefused;", "await WriteAsync(dataRoot, connection, prepared, first, last, sessionsToWrite, prepared.Compute");
        Before(ledger[ledger.IndexOf("var sessions = SessionsToWrite(prepared.Calendar, first, last, new HashSet<DateOnly>(), again: true", StringComparison.Ordinal)..], "if (!SmokeHolds(prepared, output))", "var computed = Computed(sessions.Count, item => prepared.Compute(sessions[item]));");
        Before(longLedger, "!SmokeHolds(prepared, output)", "for (var taken = 0; taken < sessionsToWrite.Length; taken += ChunkSessions)");

        // No source of the ledger finds a series by its code alone.
        foreach (var source in Directory.GetFiles(folder, "*.cs"))
        {
            var text = File.ReadAllText(source);

            Assert.DoesNotContain("prepared.Places", text, StringComparison.Ordinal);
            Assert.DoesNotContain("NameOf(series", text, StringComparison.Ordinal);
            Assert.DoesNotContain("NameOf(inputs.Series", text, StringComparison.Ordinal);
        }
    }
}
