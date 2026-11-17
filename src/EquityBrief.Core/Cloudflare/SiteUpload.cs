using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace EquityBrief.Core.Cloudflare;

// The read-only copy's newest upload and the newest attempt at one, kept in a file of its own beside the copy's folder in the
// data root and written by the worker's publish-site verb alone, on each run asked to upload: the instant the copy last reached
// Cloudflare, the night it carried and its address, and the instant of the newest attempt with why it failed in plain words,
// none where it went up. A run that uploads nothing by design leaves it as it stands. Every instant is UTC.
// see: The read-only copy states no multiple of the risk and retries an upload only the network failed, and Home says when it was last uploaded
public sealed record SiteUpload(DateTimeOffset? Uploaded, DateOnly? Night, string? Address, DateTimeOffset Attempted, string? Failed)
{
    public const string FileName = "site-upload.json";

    public static string In(string dataRoot) => Path.Combine(dataRoot, FileName);

    // The record as the file holds it, or none where no run has written one or it cannot be read.
    public static SiteUpload? Read(string dataRoot)
    {
        try
        {
            var root = JsonNode.Parse(File.ReadAllText(In(dataRoot), Encoding.UTF8));

            if (root?["attempted"]?.GetValue<string>() is not { } attempted)
            {
                return null;
            }

            return new SiteUpload(
                Instant(root["uploaded"]?.GetValue<string>()),
                root["night"]?.GetValue<string>() is { } night ? DateOnly.ParseExact(night, "yyyy-MM-dd", CultureInfo.InvariantCulture) : null,
                root["address"]?.GetValue<string>(),
                Instant(attempted)!.Value,
                root["failed"]?.GetValue<string>());
        }
        catch (Exception unread) when (unread is IOException or UnauthorizedAccessException or JsonException or FormatException or InvalidOperationException)
        {
            return null;
        }
    }

    public void Write(string dataRoot)
    {
        var node = new JsonObject
        {
            ["uploaded"] = Uploaded?.UtcDateTime.ToString("O", CultureInfo.InvariantCulture),
            ["night"] = Night?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            ["address"] = Address,
            ["attempted"] = Attempted.UtcDateTime.ToString("O", CultureInfo.InvariantCulture),
            ["failed"] = Failed,
        };

        File.WriteAllText(In(dataRoot), node.ToJsonString(new JsonSerializerOptions { WriteIndented = true }) + "\n", new UTF8Encoding(false));
    }

    // The record after an upload that went up, and after an attempt that failed, the last upload kept as it stood.
    public static SiteUpload Succeeded(DateTimeOffset at, DateOnly? night, string address) => new(at, night, address, at, null);

    public static SiteUpload FailedAfter(SiteUpload? before, DateTimeOffset at, string why) => new(before?.Uploaded, before?.Night, before?.Address, at, why);

    static DateTimeOffset? Instant(string? stored) =>
        stored is null ? null : DateTimeOffset.Parse(stored, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal);
}
