namespace EquityBrief.Core.Providers;

// The Treasury's par yield table answered from recorded responses, one a year, read by the parser the live feed reads with.
// A year the capture holds no table for is answered as the Treasury answers a year it has published nothing for: empty.
public sealed class RecordedTreasuryYieldFeed(IReadOnlyDictionary<int, string> responses) : ITreasuryYieldFeed
{
    public const string Prefix = "treasury-";

    public int Requests { get; private set; }

    // Whether a folder holds a capture of the table, which a night built from it reads.
    public static bool Holds(string folder) => Directory.GetFiles(folder, Prefix + "*.csv").Length > 0;

    public static RecordedTreasuryYieldFeed FromFolder(string folder) =>
        new(Directory
            .GetFiles(folder, Prefix + "*.csv")
            .ToDictionary(
                path => int.Parse(Path.GetFileNameWithoutExtension(path)[Prefix.Length..], System.Globalization.CultureInfo.InvariantCulture),
                File.ReadAllText));

    public Task<IReadOnlyList<TreasuryYield>> YearAsync(int year, CancellationToken cancellation = default)
    {
        Requests++;

        return Task.FromResult(responses.TryGetValue(year, out var captured) ? TreasuryYields.Parse(captured) : (IReadOnlyList<TreasuryYield>)[]);
    }
}
