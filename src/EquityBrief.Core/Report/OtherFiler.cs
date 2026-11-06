using System.Text.Json;
using System.Text.Json.Nodes;

namespace EquityBrief.Core.Report;

// A stored fundamentals row whose archive parts were read under a filer other than the company's own, the filer its
// newest quarters fetch names from the SEC's ticker file: a fetch made before the fetch read that file took the provider's
// filer, which for a company that changed its legal entity is the old entity's and can be another company's, Trane's for
// Ingersoll Rand. The row's segment table, its other revenue tables, their growth and management's words are not on file,
// neither drawn nor handed to a writer, until the company's figures are fetched again; the provider's own parts stand.
// see: A stored fundamentals row read under a filer other than the company's own is not on file until its figures are fetched again
public static class OtherFiler
{
    // The parts the filings archive supplies a row, which the filer they were read under decides.
    public static IReadOnlyList<string> Parts { get; } = ["segments", "revenueTables", "tableGrowth", "guidance", "facts"];

    // A filer's number without its leading zeros, none where it states no digit.
    public static string? Digits(string? cik)
    {
        var digits = new string([.. (cik ?? string.Empty).Where(char.IsAsciiDigit)]).TrimStart('0');

        return digits.Length == 0 ? null : digits;
    }

    // The filer a row's archive parts were read under, ten digits, where it is not the company's own; none where the two
    // agree or either is not known.
    public static string? Of(JsonElement root, string? own) =>
        root.TryGetProperty("cik", out var stored) && stored.ValueKind == JsonValueKind.String
        && Digits(stored.GetString()) is { } read && Digits(own) is { } company && read != company
            ? read.PadLeft(10, '0')
            : null;

    public static string? Of(string payload, string? own)
    {
        using var document = JsonDocument.Parse(payload);

        return Of(document.RootElement, own);
    }

    // What a page says of a part left out.
    public static string Why(string stored, string own) =>
        $"not on file, read under the filer {stored} and not the company's own, {Digits(own)!.PadLeft(10, '0')}, until the company's figures are fetched again";

    // The row's payload with the archive's parts taken off, as a writer is handed it.
    public static string Without(string payload)
    {
        var root = JsonNode.Parse(payload)!.AsObject();

        foreach (var part in Parts)
        {
            root.Remove(part);
        }

        return root.ToJsonString();
    }
}
