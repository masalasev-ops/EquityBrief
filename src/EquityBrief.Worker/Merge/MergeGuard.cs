using System.Diagnostics;
using EquityBrief.Core.Components;
using EquityBrief.Data;
using Microsoft.Data.Sqlite;

namespace EquityBrief.Worker.Merge;

// From the 24.47 correction of 2026-11-28 what tools/merge asks before it merges anything: whether a writer is running, read
// off the machine's processes and the run log. Every worker process but its own is a writer, the night, the off-peak run,
// the local run, the monthly run, a ledger or folded build and the store's copy each running as one, named by the copy of a
// commit it runs from under the data root's nights folder, or by its own folder where it runs from elsewhere; the night's lock
// held names the night; and with any writer running the run log's newest rows are named beside them, the run and the stage
// each wrote, so the operator reads which run it is. It writes nothing.
// see: tools/merge refuses while any writer runs, read off the machine's processes and the run log, and names the writer
public sealed class MergeGuard(string dataRoot, string databaseFile) : IComponent
{
    public static ComponentAccess Access => new(
        Stores:
        [
            new StoreTouch(Store.RunLog, Touch.Read),
        ],
        Feeds: []);

    public const string Verb = "writers";

    // What the verb answers where a writer runs, which tools/merge refuses on.
    public const int Refused = 3;

    // The run log's rows named beside a running writer.
    public const int NewestRows = 3;

    // The name every worker process runs under, whatever copy or build it runs from.
    public const string WorkerProcess = "EquityBrief.Worker";

    const string Newest = "SELECT run_id, stage, COALESCE(ended_at, started_at) FROM run_log ORDER BY COALESCE(ended_at, started_at) DESC LIMIT $rows;";

    // The copy of a commit a worker's file runs from: the folder under the data root's nights folder holding it, none where it
    // runs from elsewhere or its file could not be read.
    public static string? CopyOf(string? executable, string nightsFolder)
    {
        if (executable is null)
        {
            return null;
        }

        var relative = Path.GetRelativePath(Path.GetFullPath(nightsFolder), Path.GetFullPath(executable));

        return relative.StartsWith("..", StringComparison.Ordinal) || Path.IsPathRooted(relative)
            ? null
            : relative.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)[0];
    }

    // One running worker in words: the copy it runs from, or its own folder, or that its file could not be read.
    public static string WriterWords(int process, string? executable, string nightsFolder) =>
        CopyOf(executable, nightsFolder) is { } copy
            ? FormattableString.Invariant($"a worker, process {process}, running from the night copy {copy}")
            : executable is { } file
                ? FormattableString.Invariant($"a worker, process {process}, running from {Path.GetDirectoryName(file)}")
                : FormattableString.Invariant($"a worker, process {process}, whose file could not be read");

    public async Task<int> RunAsync(TextWriter output, CancellationToken cancellation = default)
    {
        var nights = Path.Combine(dataRoot, "nights");
        var writers = new List<string>();

        foreach (var process in Process.GetProcessesByName(WorkerProcess))
        {
            using (process)
            {
                if (process.Id == Environment.ProcessId)
                {
                    continue;
                }

                string? file = null;

                try
                {
                    file = process.MainModule?.FileName;
                }
                catch (Exception fault) when (fault is System.ComponentModel.Win32Exception or InvalidOperationException or NotSupportedException)
                {
                }

                writers.Add(WriterWords(process.Id, file, nights));
            }
        }

        if (Core.Configuration.NightLock.Holder(dataRoot) is { } holder)
        {
            writers.Add($"the night, holding its lock ({holder})");
        }

        if (writers.Count == 0)
        {
            output.WriteLine("writers: none running");

            return 0;
        }

        output.WriteLine("writers: running now: " + string.Join("; ", writers));

        if (File.Exists(databaseFile))
        {
            var opened = StoreConnection.Builder(databaseFile);

            opened.Mode = SqliteOpenMode.ReadOnly;

            await using var connection = new SqliteConnection(opened.ConnectionString);
            await connection.OpenAsync(cancellation);
            await using var command = connection.CreateCommand();

            command.CommandText = Newest;
            command.Parameters.AddWithValue("$rows", NewestRows);

            await using var reader = await command.ExecuteReaderAsync(cancellation);

            while (await reader.ReadAsync(cancellation))
            {
                output.WriteLine($"writers: the run log's newest row: {reader.GetString(0)}, {reader.GetString(1)}, at {reader.GetString(2)}");
            }
        }

        return Refused;
    }
}
