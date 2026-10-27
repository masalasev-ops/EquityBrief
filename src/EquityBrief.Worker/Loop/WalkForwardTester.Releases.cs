using EquityBrief.Core.Time;
using EquityBrief.Data;
using EquityBrief.Worker.Sweep;
using Microsoft.Data.Sqlite;

namespace EquityBrief.Worker.Loop;

// The results releases a history test reads its flags from: the newest reading version whose history read is complete,
// its model's cutoff, and each filer's releases read under it, by its CIK padded to ten digits.
public sealed record HistoryReleases(long? Version, DateOnly? Cutoff, IReadOnlyDictionary<string, IReadOnlyList<ImprovingRelease>> ByFiler);

public sealed partial class WalkForwardTester
{
    // Each filer's releases read under the newest version whose history read is complete, through the one reader of the
    // release readings, newest first, each with the day in New York it was accepted on; a release whose reading was
    // refused or is not held is left out, as the night's flag leaves it out. None where no version's read is complete.
    // see: A Fundamentals flag stands only for a release reporting a quarter newer than the member's newest stored filing, accepted within the last 63 sessions
    public static async Task<HistoryReleases> ReleasesAsync(string databaseFile, IClock clock, CancellationToken cancellation = default)
    {
        await using var connection = new SqliteConnection(SweepHistory.ConnectionString(databaseFile));
        await connection.OpenAsync(cancellation);

        var version = (await ReleaseReadings.CompleteVersionsAsync(connection, null, cancellation)).FirstOrDefault();

        if (version <= 0)
        {
            return new HistoryReleases(null, null, new Dictionary<string, IReadOnlyList<ImprovingRelease>>(StringComparer.Ordinal));
        }

        var byFiler = (await ReleaseReadings.ForLedgerAsync(connection, version, cancellation))
            .ToDictionary(
                pair => pair.Key,
                pair => (IReadOnlyList<ImprovingRelease>)[.. pair.Value
                    .Where(release => release.Answer is not null)
                    .OrderByDescending(release => release.Accepted)
                    .Select(release => new ImprovingRelease(release.Accepted, clock.SessionDateAt(release.Accepted), release.Answer!))],
                StringComparer.Ordinal);

        return new HistoryReleases(version, await ReleaseReadings.CutoffAsync(connection, version, cancellation), byFiler);
    }
}
