using System.IO.Compression;
using System.Text.RegularExpressions;

namespace EquityBrief.Core.Providers;

// The SEC's quarterly insider data sets, answered from captured cuts. The listing page is the capture named for it, and a
// quarter's file is the folder named for its quarter, its three files zipped as the archive serves them, so the reader
// opens the zip it would be handed live. A quarter the history asks for that no folder holds refuses by name, since a
// replay runs over a set that should be complete and an absence there is a fault in the fixture.
public sealed class RecordedInsiderDataSetFeed : IInsiderDataSetFeed
{
    public const string ListingFile = "insider-data-sets.htm";

    public const string QuarterPrefix = "insider-data-set-";

    readonly string? listing;

    readonly IReadOnlyDictionary<string, string> folders;

    readonly List<string> asked = [];

    RecordedInsiderDataSetFeed(string? listing, IReadOnlyDictionary<string, string> folders)
    {
        this.listing = listing;
        this.folders = folders;
    }

    public int Documents => asked.Count;

    // Every document asked for, in order, as the listing's file name or the quarter.
    public IReadOnlyList<string> Asked => asked;

    // Whether the folder holds the listing at all, which is what lets a fixture without one be given no feed.
    public static bool Holds(string folder) => File.Exists(Path.Combine(folder, ListingFile));

    public static RecordedInsiderDataSetFeed FromFolder(string folder) =>
        new(
            File.Exists(Path.Combine(folder, ListingFile)) ? File.ReadAllText(Path.Combine(folder, ListingFile)) : null,
            Directory.GetDirectories(folder, QuarterPrefix + "*").ToDictionary(
                path => Path.GetFileName(path)[QuarterPrefix.Length..],
                path => path,
                StringComparer.Ordinal));

    public Task<string?> ListingAsync(CancellationToken cancellation = default)
    {
        asked.Add(ListingFile);

        return Task.FromResult(listing);
    }

    public Task<byte[]?> QuarterAsync(string path, CancellationToken cancellation = default)
    {
        var quarter = QuarterOf.Match(path) is { Success: true } match ? match.Groups["quarter"].Value : path;

        asked.Add(quarter);

        if (!folders.TryGetValue(quarter, out var folder))
        {
            throw new InvalidOperationException($"The history asked for the insider data set of {quarter} and the fixture holds no cut of it.");
        }

        using var buffer = new MemoryStream();

        using (var zip = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var name in SecEdgarInsiderDataSet.Files)
            {
                using var entry = zip.CreateEntry(name).Open();
                using var source = File.OpenRead(Path.Combine(folder, name));

                source.CopyTo(entry);
            }
        }

        return Task.FromResult<byte[]?>(buffer.ToArray());
    }

    static readonly Regex QuarterOf = new(@"(?<quarter>\d{4}q[1-4])_form345\.zip$", RegexOptions.CultureInvariant);
}
