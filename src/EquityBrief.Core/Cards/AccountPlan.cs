using System.Globalization;
using System.Text.Json;

namespace EquityBrief.Core.Cards;

// The operator's account as the settings page keeps it: its size, the risk a trade in per cent of it, and the most one
// position may hold as a share of it. Never in the store, a log, the run log or an export.
// see: The account settings live in a file of their own under the data root and in nothing the store or the logs hold
public sealed record AccountSettings(decimal Size, decimal RiskPercent, decimal PositionCap)
{
    // The position cap the page proposes where none is set, a fifth of the account.
    public const decimal ProposedCap = 0.2m;

    // Settings the page can keep: each above nothing, the risk under the whole account and the cap no more than it.
    public static string? Refusal(decimal size, decimal riskPercent, decimal positionCap) =>
        size <= 0m ? "The account's size must be above nothing."
        : riskPercent is <= 0m or > 100m ? "The risk a trade must be above nothing and no more than the whole account."
        : positionCap is <= 0m or > 1m ? "The position cap must be above nothing and no more than the whole account."
        : null;
}

// The file the account settings live in, under the data root, written whole: the page writes a file beside it and
// moves it into place, so a write that fails leaves the file as it was.
public static class AccountFile
{
    public const string FileName = "account.json";

    public static string PathIn(string dataRoot) => Path.Combine(dataRoot, FileName);

    // The settings, or none where the file is absent or cannot be read, which the card draws as unset.
    public static AccountSettings? Read(string dataRoot)
    {
        var file = PathIn(dataRoot);

        try
        {
            if (!File.Exists(file))
            {
                return null;
            }

            using var document = JsonDocument.Parse(File.ReadAllText(file));
            var root = document.RootElement;

            decimal Value(string name) => decimal.Parse(root.GetProperty(name).GetString()!, NumberStyles.Number, CultureInfo.InvariantCulture);

            var (size, risk, cap) = (Value("size"), Value("riskPercent"), Value("positionCap"));

            return AccountSettings.Refusal(size, risk, cap) is null ? new AccountSettings(size, risk, cap) : null;
        }
        catch (Exception unreadable) when (unreadable is IOException or JsonException or FormatException or KeyNotFoundException or InvalidOperationException or OverflowException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    public static void Write(string dataRoot, AccountSettings settings)
    {
        Directory.CreateDirectory(dataRoot);

        var file = PathIn(dataRoot);
        var next = file + ".next";

        File.WriteAllText(next, JsonSerializer.Serialize(new
        {
            size = settings.Size.ToString(CultureInfo.InvariantCulture),
            riskPercent = settings.RiskPercent.ToString(CultureInfo.InvariantCulture),
            positionCap = settings.PositionCap.ToString(CultureInfo.InvariantCulture),
        }));
        File.Move(next, file, overwrite: true);
    }
}

// A pick's plan in the operator's money: the shares, the dollars at risk, the position's value and its share of the
// account, the round trip in dollars, and whether the cap or the book's equal share sized it.
public sealed record PositionPlan(long Shares, decimal AtRisk, decimal Value, decimal ShareOfAccount, decimal? RoundTrip, bool Capped, bool WholeAtRisk);

// The one function a screen calls to size a pick, worked where the card is drawn since the account is in no store.
// see: A screen reads and renders, and each figure it works out has one function in the core
public static class PositionSize
{
    // A pick with a stop: the shares the risk a trade buys over the stop's distance, rounded down, and no more than the
    // cap allows. A holding with no stop: an equal share of the account across the most holdings its book can hold, no
    // more than the cap allows, the whole position at risk. None where the plan states no buy, or a stop at or over it.
    // see: A holding with no stop is sized at an equal share of the account across the most holdings its book can hold
    public static PositionPlan? Of(AccountSettings account, decimal? buy, decimal? stop, int? bookHoldings, decimal? roundTripAShare)
    {
        if (buy is not { } entry || entry <= 0m)
        {
            return null;
        }

        var byCap = Whole(account.Size * account.PositionCap / entry);
        long shares;
        bool capped;
        decimal atRisk;

        if (stop is { } floor)
        {
            if (floor >= entry)
            {
                return null;
            }

            var byRisk = Whole(account.Size * account.RiskPercent / 100m / (entry - floor));

            (shares, capped) = byRisk <= byCap ? (byRisk, false) : (byCap, true);
            atRisk = shares * (entry - floor);
        }
        else
        {
            if (bookHoldings is not > 0)
            {
                return null;
            }

            var byShare = Whole(account.Size / bookHoldings.Value / entry);

            (shares, capped) = byShare <= byCap ? (byShare, false) : (byCap, true);
            atRisk = shares * entry;
        }

        var value = shares * entry;

        return new PositionPlan(
            shares,
            atRisk,
            value,
            value / account.Size,
            roundTripAShare is { } trip ? decimal.Round(shares * trip, 2, MidpointRounding.AwayFromZero) : null,
            capped,
            stop is null);
    }

    static long Whole(decimal amount) => amount <= 0m ? 0 : decimal.ToInt64(decimal.Floor(amount));
}
