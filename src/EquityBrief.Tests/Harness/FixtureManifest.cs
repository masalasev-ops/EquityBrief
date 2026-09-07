using System.Globalization;
using System.Text.Json;
using EquityBrief.Tests.Checks;

namespace EquityBrief.Tests.Harness;

internal sealed record ManifestFault(string Field, string Reason);

// Reads and checks a fixture manifest.
//
// The required field names are read out of fixtures/manifest.schema.json rather
// than written here, so the shape is declared once and a field added to the
// schema is a field this refuses without it.
internal static class FixtureManifest
{
    // Anything that looks like a key in a captured query. A fixture is
    // committed, so a credential in one is published.
    static readonly string[] CredentialMarkers =
        ["api_token", "api_key", "apikey", "access_token", "token=", "secret", "password"];

    internal static IReadOnlyList<string> RequiredFields(string schema, string scope)
    {
        using var document = JsonDocument.Parse(schema);
        var node = document.RootElement;

        if (scope == "input")
        {
            node = node.GetProperty("properties").GetProperty("inputs").GetProperty("items");
        }

        return node.GetProperty("required").EnumerateArray().Select(field => field.GetString()!).ToArray();
    }

    internal static IReadOnlyList<ManifestFault> Faults(string manifest, string schema)
    {
        var faults = new List<ManifestFault>();

        JsonDocument document;

        try
        {
            document = JsonDocument.Parse(manifest);
        }
        catch (JsonException broken)
        {
            return [new ManifestFault("(whole file)", $"not valid JSON: {broken.Message}")];
        }

        using (document)
        {
            var root = document.RootElement;

            foreach (var field in RequiredFields(schema, "manifest"))
            {
                if (!root.TryGetProperty(field, out _))
                {
                    faults.Add(new ManifestFault(field, "required and absent"));
                }
            }

            if (root.TryGetProperty("capturedFor", out var capturedFor)
                && !DateOnly.TryParseExact(
                    capturedFor.GetString() ?? string.Empty, "yyyy-MM-dd",
                    CultureInfo.InvariantCulture, DateTimeStyles.None, out _))
            {
                faults.Add(new ManifestFault("capturedFor", "not a date in YYYY-MM-DD"));
            }

            if (root.TryGetProperty("noCredentials", out var noCredentials)
                && noCredentials.ValueKind != JsonValueKind.True)
            {
                faults.Add(new ManifestFault("noCredentials", "must assert true"));
            }

            if (!root.TryGetProperty("inputs", out var inputs) || inputs.ValueKind != JsonValueKind.Array)
            {
                return faults;
            }

            if (inputs.GetArrayLength() == 0)
            {
                faults.Add(new ManifestFault("inputs", "a fixture with no captured inputs is not a fixture"));
            }

            var required = RequiredFields(schema, "input");
            var index = 0;

            foreach (var input in inputs.EnumerateArray())
            {
                foreach (var field in required)
                {
                    if (!input.TryGetProperty(field, out _))
                    {
                        faults.Add(new ManifestFault($"inputs[{index}].{field}", "required and absent"));
                    }
                }

                if (input.TryGetProperty("fetchedAt", out var fetchedAt)
                    && !IsUtcInstant(fetchedAt.GetString()))
                {
                    faults.Add(new ManifestFault($"inputs[{index}].fetchedAt", "not a UTC instant"));
                }

                if (input.TryGetProperty("query", out var query))
                {
                    var text = query.GetString() ?? string.Empty;

                    foreach (var marker in CredentialMarkers)
                    {
                        if (text.Contains(marker, StringComparison.OrdinalIgnoreCase))
                        {
                            faults.Add(new ManifestFault($"inputs[{index}].query", $"carries {marker}"));
                        }
                    }
                }

                if (input.TryGetProperty("file", out var file) && AbsolutePaths.LooksAbsolute(file.GetString() ?? string.Empty))
                {
                    faults.Add(new ManifestFault($"inputs[{index}].file", "absolute, so the fixture stops being portable"));
                }

                index++;
            }
        }

        return faults;
    }

    static bool IsUtcInstant(string? value) =>
        DateTimeOffset.TryParse(
            value ?? string.Empty,
            CultureInfo.InvariantCulture,
            DateTimeStyles.RoundtripKind,
            out var instant)
        && instant.Offset == TimeSpan.Zero;
}
