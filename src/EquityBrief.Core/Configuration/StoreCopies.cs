using System.Globalization;

namespace EquityBrief.Core.Configuration;

// The store's copies: the folder the setting names, absolute or under the data root, and `backups` under the
// data root where it names none; each copy named by the instant it was made, which orders them; and the folder
// as a store row carries it, relative to the data root with forward separators, since no absolute path is
// written into a store row, or by its name alone where it lies on a volume no relative path reaches.
// see: The store is copied once the night and every process it started have finished, and the newest three copies are kept after each is opened and read
public static class StoreCopies
{
    public const string FolderKey = "EquityBrief:Backup:Folder";

    public const string DefaultFolder = "backups";

    // The stage the copy's own row is written under.
    public const string Stage = "store-backup";

    // How many copies are kept, the newest.
    public const int Kept = 3;

    // How long a copy waits for the night, its drain and its labeller before it gives up, which ends it well
    // before the next night is built.
    public static readonly TimeSpan WaitsAtMost = TimeSpan.FromHours(20);

    // A copy written and not yet checked and named, which a copy that did not finish leaves behind.
    public const string Unfinished = ".partial";

    const string Prefix = "equitybrief-";
    const string Extension = ".db";
    const string Stamp = "yyyyMMdd'T'HHmmss'Z'";

    public static string Folder(string? configured, string dataRoot)
    {
        var root = Path.GetFullPath(dataRoot);

        return string.IsNullOrWhiteSpace(configured) ? Path.Combine(root, DefaultFolder) : Path.GetFullPath(configured.Trim(), root);
    }

    public static string NameAt(DateTimeOffset at) => Prefix + at.UtcDateTime.ToString(Stamp, CultureInfo.InvariantCulture) + Extension;

    // The instant a copy's name states, or none for a file that is not a copy.
    public static DateTimeOffset? MadeAt(string fileName) =>
        fileName.StartsWith(Prefix, StringComparison.Ordinal)
        && fileName.EndsWith(Extension, StringComparison.Ordinal)
        && DateTime.TryParseExact(fileName[Prefix.Length..^Extension.Length], Stamp, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var made)
            ? new DateTimeOffset(made, TimeSpan.Zero)
            : null;

    // The copies a folder holds, newest first.
    public static IReadOnlyList<(string File, DateTimeOffset MadeAt)> In(string folder) =>
        Directory.Exists(folder)
            ?
            [
                .. Directory.EnumerateFiles(folder)
                    .Select(file => (File: file, MadeAt: MadeAt(Path.GetFileName(file))))
                    .Where(copy => copy.MadeAt is not null)
                    .Select(copy => (copy.File, copy.MadeAt!.Value))
                    .OrderByDescending(copy => copy.Item2),
            ]
            : [];

    // The folder as a store row carries it, relative to the data root with forward separators, or none where
    // no relative path reaches it.
    public static string? AsStored(string folder, string dataRoot)
    {
        var relative = Path.GetRelativePath(Path.GetFullPath(dataRoot), Path.GetFullPath(folder));

        return Path.IsPathRooted(relative) ? null : relative.Replace(Path.DirectorySeparatorChar, '/');
    }

    // The folder a store row names, read against the data root of whoever reads it.
    public static string FromStored(string stored, string dataRoot) =>
        Path.GetFullPath(Path.Combine(Path.GetFullPath(dataRoot), stored.Replace('/', Path.DirectorySeparatorChar)));
}
