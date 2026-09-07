namespace EquityBrief.Core.Configuration;

// Where the store is. One configured data root, composed through the platform
// API, so nothing in the system writes a separator or a drive letter of its own
// and the whole installation stays a checkout and one file.
// see: The whole system is a checkout and one database file
public sealed class StoreLocation
{
    public const string DatabaseFileName = "equitybrief.db";

    // The configuration key, stated once so the message below and the host that
    // reads it cannot disagree.
    public const string DataRootKey = "EquityBrief:DataRoot";

    public StoreLocation(string dataRoot)
    {
        if (string.IsNullOrWhiteSpace(dataRoot))
        {
            throw new ArgumentException(
                $"No data root is configured. Set {DataRootKey} in appsettings.json, or " +
                "EquityBrief__DataRoot in the environment. Guessing a location would put the " +
                "store somewhere nobody backs up.",
                nameof(dataRoot));
        }

        // A relative root resolves against the working directory, which is why
        // tools/migrate changes to the repository root before it runs anything.
        // An operator wanting the store elsewhere configures an absolute path.
        DataRoot = Path.GetFullPath(dataRoot);
    }

    public string DataRoot { get; }

    public string DatabaseFile => Path.Combine(DataRoot, DatabaseFileName);
}
