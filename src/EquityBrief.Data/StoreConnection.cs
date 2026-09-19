using Microsoft.Data.Sqlite;

namespace EquityBrief.Data;

// How the worker opens the store. A statement another writer's transaction holds up waits
// for it up to ten minutes, where the driver's own wait is thirty seconds: a night's stage
// holds its transaction for minutes on a spinning disk, and a pass started beside it would
// otherwise fail on its first write. Past the wait the statement fails, naming the lock.
//
// A source a ladder rule's code version or a candidate evaluator's version pins opens its
// own connection and keeps the driver's wait, since an edit there moves the version a window
// or a registration is measured under. Those are the night's own stages, and what they wait
// on beside a pass is one write.
// see: A writer waits up to ten minutes for another, and a pass stores what it fetched in one write
public static class StoreConnection
{
    public const int WaitSeconds = 600;

    public static SqliteConnectionStringBuilder Builder(string databaseFile) =>
        new() { DataSource = databaseFile, DefaultTimeout = WaitSeconds };

    public static string For(string databaseFile) => Builder(databaseFile).ConnectionString;
}
