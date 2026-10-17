using System.Text.Json;

namespace EquityBrief.Core.Providers;

// One line of the SEC's ticker file: a listed ticker, the filer it names padded to the ten digits the archive is
// addressed by, and the filer's name as the file states it.
public sealed record SecTicker(string Ticker, string Cik, string Title);

// The SEC's file of every listed ticker and its filer, one request, free and keyless; none where the archive holds no
// such file, and a refusal throws. Read where the provider files no CIK for a member, as it files none for a ticker
// changed since it last read the company, so the member's filer is the SEC's own.
// see: A member the provider files no CIK for is matched to its filer by the SEC's own ticker file
public interface ISecTickersFeed
{
    // The documents asked for, each one request.
    int Documents { get; }

    Task<string?> TickersAsync(CancellationToken cancellation = default);
}

public static class SecEdgarTickers
{
    // Where the archive serves the file, on its document host.
    public const string Path = "files/company_tickers.json";

    // Every line of the file in its own order, a ticker the file lists twice read once by its first line. A file that is
    // no object of lines, or a line holding no ticker or no filer, is refused, since a map read wrong matches a member
    // to another company's filings.
    public static IReadOnlyList<SecTicker> Parse(string json)
    {
        using var document = JsonDocument.Parse(json);

        if (document.RootElement.ValueKind != JsonValueKind.Object)
        {
            throw new FormatException("The ticker file is not an object of lines.");
        }

        var lines = new List<SecTicker>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var line in document.RootElement.EnumerateObject())
        {
            var value = line.Value;

            if (value.ValueKind != JsonValueKind.Object
                || !value.TryGetProperty("ticker", out var ticker) || ticker.ValueKind != JsonValueKind.String || ticker.GetString() is not { Length: > 0 } code
                || !value.TryGetProperty("cik_str", out var cik) || cik.ValueKind != JsonValueKind.Number || !cik.TryGetInt64(out var number) || number <= 0)
            {
                throw new FormatException($"The ticker file's line {line.Name} holds no ticker or no filer.");
            }

            if (seen.Add(code))
            {
                lines.Add(new SecTicker(
                    code,
                    number.ToString(System.Globalization.CultureInfo.InvariantCulture).PadLeft(10, '0'),
                    value.TryGetProperty("title", out var title) && title.ValueKind == JsonValueKind.String ? title.GetString() ?? string.Empty : string.Empty));
            }
        }

        return lines;
    }
}

// The SEC's ticker file answered from its captured cut, the file named for it in the fixture's folder, none where the
// folder holds none.
public sealed class RecordedSecTickersFeed(string? captured) : ISecTickersFeed
{
    public const string File = "sec-company-tickers.json";

    public int Documents { get; private set; }

    public static RecordedSecTickersFeed FromFolder(string folder) =>
        new(System.IO.File.Exists(System.IO.Path.Combine(folder, File)) ? System.IO.File.ReadAllText(System.IO.Path.Combine(folder, File)) : null);

    public Task<string?> TickersAsync(CancellationToken cancellation = default)
    {
        Documents++;

        return Task.FromResult(captured);
    }
}
