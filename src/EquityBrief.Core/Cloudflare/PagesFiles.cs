using System.Text;

namespace EquityBrief.Core.Cloudflare;

// One file of the copy as the host takes it: its address under the site, its hash, its type and its bytes.
public sealed record PagesFile(string Address, string Hash, string ContentType, byte[] Bytes);

// The copy's files as Cloudflare Pages' direct upload takes them: each keyed by the hash of its bytes in base64 followed
// by its extension, cut to its first 32 hexadecimal digits, as the host's own command-line tool keys a file, and sent in
// buckets of at most 40 MB and a thousand files, the largest first, as that tool fills them.
// see: A read-only copy of the site is published to Cloudflare Pages from the read surface's own screens and holds no control
public static class PagesFiles
{
    public const long BucketBytes = 40L * 1024 * 1024;

    public const int BucketFiles = 1000;

    public const int HashLength = 32;

    public static string HashOf(byte[] bytes, string address) =>
        Convert.ToHexStringLower(Blake3.Hash(Encoding.ASCII.GetBytes(Convert.ToBase64String(bytes) + Path.GetExtension(address).TrimStart('.'))))[..HashLength];

    public static PagesFile Of(string folder, string file)
    {
        var bytes = File.ReadAllBytes(file);
        var address = "/" + Path.GetRelativePath(folder, file).Replace('\\', '/');

        return new PagesFile(address, HashOf(bytes, address), ContentTypeOf(address), bytes);
    }

    static string ContentTypeOf(string address) => Path.GetExtension(address).ToLowerInvariant() switch
    {
        ".html" => "text/html",
        ".css" => "text/css",
        ".js" => "application/javascript",
        ".json" => "application/json",
        ".svg" => "image/svg+xml",
        ".txt" => "text/plain",
        _ => "application/octet-stream",
    };

    public static IReadOnlyList<IReadOnlyList<PagesFile>> Buckets(IReadOnlyList<PagesFile> files)
    {
        var buckets = new List<(List<PagesFile> Files, long Bytes)>();

        foreach (var file in files)
        {
            var at = buckets.FindIndex(bucket => bucket.Bytes + file.Bytes.Length <= BucketBytes && bucket.Files.Count < BucketFiles);

            if (at < 0)
            {
                buckets.Add(([file], file.Bytes.Length));
            }
            else
            {
                buckets[at].Files.Add(file);
                buckets[at] = (buckets[at].Files, buckets[at].Bytes + file.Bytes.Length);
            }
        }

        return [.. buckets.Select(bucket => (IReadOnlyList<PagesFile>)bucket.Files)];
    }
}
