using System.Globalization;
using System.Text.RegularExpressions;
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
    internal static readonly string[] CredentialMarkers =
        ["api_token", "api_key", "apikey", "access_token", "token=", "secret", "password", "EquityBrief/"];

    // The last of those is the archive's own user agent rather than a credential,
    // and it is in this list because of what the agent carries. The filings archive
    // is sent a header naming the tool, its version and a configured contact, and a
    // captured response echoing that header back would put the contact into the
    // repository. The contact itself cannot be scanned for, since it is
    // configuration this check does not read, so what is scanned for is the shape it
    // travels in: the contact reaches a request only inside this header.
    // see: The archive declares a contact in its user agent, and a blank one refuses at startup

    // Whether a marker appears in a body as a marker rather than inside a longer
    // word, which is the difference between a credential and the provider's prose.
    //
    // 6.1 captured four company payloads and two of them were refused for carrying
    // `secret`: the word is inside "Secretary", which is an officer's job title in
    // the company description. A substring scan over a payload that contains prose
    // reads the prose, and the same shape had already been found that morning in
    // the fundamentals parser, where the word "segment" appears in two
    // descriptions and would have read as a segment table.
    //
    // A letter on either side of the marker means it is part of a longer word. A
    // digit, an underscore, a quote, a colon or an equals sign does not, so
    // `client_secret`, `"secret":` and `secret=` are all still hits, which is what
    // the proof beside this asserts in both directions.
    //
    // The boundary is tested only at an end where the marker's own edge is a
    // letter. `token=` ends in its own separator and the value follows it with no
    // gap, so testing the character after it would refuse `?token=abc`, which is
    // the one form that marker exists for. That was the first form of this repair
    // and the proof caught it.
    internal static bool Carries(string body, string marker)
    {
        var boundedBefore = char.IsAsciiLetter(marker[0]);
        var boundedAfter = char.IsAsciiLetter(marker[^1]);

        for (var at = body.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
             at >= 0;
             at = body.IndexOf(marker, at + 1, StringComparison.OrdinalIgnoreCase))
        {
            var before = at == 0 ? ' ' : body[at - 1];
            var after = at + marker.Length >= body.Length ? ' ' : body[at + marker.Length];

            if ((!boundedBefore || !char.IsAsciiLetter(before)) && (!boundedAfter || !char.IsAsciiLetter(after)))
            {
                return true;
            }
        }

        return false;
    }

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

    internal static IReadOnlyList<ManifestFault> Faults(string manifest, string schema, string? folder = null)
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
                        if (Carries(text, marker))
                        {
                            faults.Add(new ManifestFault($"inputs[{index}].query", $"carries {marker}"));
                        }
                    }
                }

                if (input.TryGetProperty("file", out var file))
                {
                    var named = file.GetString() ?? string.Empty;

                    if (AbsolutePaths.LooksAbsolute(named))
                    {
                        faults.Add(new ManifestFault($"inputs[{index}].file", "absolute, so the fixture stops being portable"));
                    }
                    else if (folder is not null)
                    {
                        // The obligation carried out of 0.7: the checker scanned
                        // the query and never opened the response, while both
                        // manifest.schema.json and fixtures/README.md say no
                        // credential appears in a captured response and that the
                        // check scans as well. A manifest naming a file nobody
                        // opened is an assertion about a document the check has
                        // not read.
                        var path = Path.Combine(folder, named.Replace('/', Path.DirectorySeparatorChar));

                        if (!File.Exists(path))
                        {
                            faults.Add(new ManifestFault($"inputs[{index}].file", $"names '{named}', which is not in the fixture folder"));
                        }
                        else
                        {
                            var body = File.ReadAllText(path);

                            faults.AddRange(CredentialMarkers
                                .Where(marker => Carries(body, marker))
                                .Select(marker => new ManifestFault($"inputs[{index}].file", $"the captured response carries {marker}")));
                        }
                    }
                }

                index++;
            }
        }

        return faults;
    }

    // A zoneless instant is refused rather than resolved.
    //
    // The obligation carried out of 0.7: RoundtripKind gives a string with no
    // offset the machine's own zone, so the same manifest passed on a UTC runner
    // and failed on the operator's machine. It is also an implicit read of the
    // machine clock, which is the thing clock-usage exists to ban, and one no
    // grep for DateTime.Now would ever find.
    static bool IsUtcInstant(string? value)
    {
        var text = value ?? string.Empty;

        var zoned = text.EndsWith('Z')
            || Regex.IsMatch(text, @"[+-][0-9]{2}:?[0-9]{2}$");

        return zoned
            && DateTimeOffset.TryParse(
                text,
                CultureInfo.InvariantCulture,
                DateTimeStyles.RoundtripKind,
                out var instant)
            && instant.Offset == TimeSpan.Zero;
    }
}
