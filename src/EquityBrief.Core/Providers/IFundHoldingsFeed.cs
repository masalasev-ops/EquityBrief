using System.Globalization;
using System.Text;

namespace EquityBrief.Core.Providers;

// One stock a fund's holdings file lists: its ticker as it trades, its name and its sector as the file states them.
public sealed record FundHolding(string Ticker, string Name, string? Sector);

// One fund's holdings file: the fund, the day its holdings are stated as of, and each stock it holds.
public sealed record FundHoldingsFile(string Fund, DateOnly AsOf, IReadOnlyList<FundHolding> Holdings);

// The holdings files the night reads the S&P 400's and 600's members from, one a fund, each index read from the fund
// that holds it whole. A file is asked for once a night, at no weight on the provider's allowance.
// see: The S&P 400's and 600's members are read each night from their funds' own holdings files
public interface IFundHoldingsFeed
{
    // How many network requests this feed has made, read off the feed as every other feed's count is.
    int Requests { get; }

    // The indices whose members this feed reads, each from its fund's file, in the order the night loads them.
    IReadOnlyList<string> Indices { get; }

    // The file of the fund holding the index whole, or a refusal where it cannot be fetched or read.
    Task<FundHoldingsFile> HoldingsAsync(string indexCode, CancellationToken cancellation = default);
}

// The funds holding the S&P 400 and 600 whole and the reader of the file iShares serves for each. A row is a member
// where its type is a stock: a fund also holds index futures, cash, collateral, swaps on its members and warrants, none
// of them a member. A class of shares the file names with a space, as MOG A, is the provider's MOG-A.
public static class FundHoldings
{
    public const string MidCapIndex = "MID";

    public const string SmallCapIndex = "SML";

    // The fund each index is read from, its iShares product number and the path its current holdings are served at.
    public static IReadOnlyDictionary<string, (string Fund, string Path)> Funds { get; } = new Dictionary<string, (string, string)>(StringComparer.Ordinal)
    {
        [MidCapIndex] = ("IJH", "us/products/239763/ishares-core-s-p-mid-cap-etf/latest-holdings.csv"),
        [SmallCapIndex] = ("IJR", "us/products/239774/ishares-core-s-p-small-cap-etf/latest-holdings.csv"),
    };

    public static IReadOnlyList<string> Indices { get; } = [MidCapIndex, SmallCapIndex];

    const string AsOfLabel = "Fund Holdings as of";

    const string Stock = "EQUITY";

    // The file read into its date and its stocks, or a refusal naming what it lacks: a body carrying no date, no row
    // of column names or no stock is not a holdings file, the product page a dated link answers among them.
    public static FundHoldingsFile Parse(string body, string fund)
    {
        var lines = body.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        DateOnly? asOf = null;
        string[]? columns = null;
        var holdings = new List<FundHolding>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var line in lines)
        {
            var fields = Fields(line.TrimStart('﻿'));

            if (columns is null)
            {
                if (fields.Count >= 2 && fields[0] == AsOfLabel
                    && DateTime.TryParseExact(fields[1], "MMM dd, yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out var stated))
                {
                    asOf = DateOnly.FromDateTime(stated);
                }
                else if (fields.Count > 0 && fields[0] == "Ticker")
                {
                    columns = [.. fields];
                }

                continue;
            }

            if (fields.Count != columns.Length)
            {
                continue;
            }

            var row = columns.Zip(fields).ToDictionary(pair => pair.First, pair => pair.Second, StringComparer.Ordinal);

            if (row.GetValueOrDefault("Type") != Stock)
            {
                continue;
            }

            var ticker = TickerOf(row["Ticker"]);

            if (ticker is null || !seen.Add(ticker))
            {
                continue;
            }

            holdings.Add(new FundHolding(ticker, row.GetValueOrDefault("Name") ?? ticker, Blank(row.GetValueOrDefault("Sector"))));
        }

        return asOf is null
            ? throw Unreadable(fund, "states no date its holdings are as of")
            : columns is null
                ? throw Unreadable(fund, "carries no row of column names")
                : !columns.Contains("Type")
                    ? throw Unreadable(fund, "carries no column naming each holding's type")
                    : holdings.Count == 0
                        ? throw Unreadable(fund, "lists no stock")
                        : new FundHoldingsFile(fund, asOf.Value, holdings);
    }

    // The ticker as the provider writes it, or none where the file states none.
    public static string? TickerOf(string stated)
    {
        var ticker = stated.Trim();

        return ticker.Length == 0 || ticker == "-"
            ? null
            : ticker.Replace(' ', '-').Replace('.', '-').ToUpperInvariant();
    }

    public static ProviderRefusal Unreadable(string fund, string what) =>
        new($"The {fund} holdings file {what}, so it was not read.", transient: false);

    static string? Blank(string? text) => string.IsNullOrWhiteSpace(text) || text == "-" ? null : text.Trim();

    // One line's fields, a quoted field keeping the commas inside it and a doubled quote standing for one.
    static List<string> Fields(string line)
    {
        var fields = new List<string>();
        var field = new StringBuilder();
        var quoted = false;

        for (var at = 0; at < line.Length; at++)
        {
            var character = line[at];

            if (quoted)
            {
                if (character == '"' && at + 1 < line.Length && line[at + 1] == '"')
                {
                    field.Append('"');
                    at++;
                }
                else if (character == '"')
                {
                    quoted = false;
                }
                else
                {
                    field.Append(character);
                }
            }
            else if (character == '"')
            {
                quoted = true;
            }
            else if (character == ',')
            {
                fields.Add(field.ToString());
                field.Clear();
            }
            else
            {
                field.Append(character);
            }
        }

        if (line.Length > 0)
        {
            fields.Add(field.ToString());
        }

        return fields;
    }
}
