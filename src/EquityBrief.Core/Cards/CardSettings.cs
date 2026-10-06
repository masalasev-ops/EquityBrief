using System.Globalization;
using System.Text.Json;
using EquityBrief.Core.Readings;

namespace EquityBrief.Core.Cards;

// The values a pick's card warns at, each stated on the card in its own words: read from the card's settings block where
// it names one and these defaults where it does not. Each card stores the values it was read with, so a value moved later
// never rewrites an earlier card.
// see: A pick's card advises on the trade and removes no pick, and code computes every figure on it
public sealed record CardSettings(int SectorBottom, decimal CoverFloor, double HeldShare, double RoundTripRisks)
{
    public const string Section = "EquityBrief:Card";

    // The places at the foot of the index's sectors, ranked by their members' mean return, at which the market and sector
    // line warns.
    public const int DefaultSectorBottom = 3;

    // The operating income each dollar of interest expense asks for over the four newest quarters, the coverage's own.
    public const decimal DefaultCoverFloor = MemberReadings.CoverageFloor;

    // The share of the rule's replayed trades whose holding time a report inside the hold warns at.
    public const double DefaultHeldShare = 0.75;

    // The round trip, in multiples of the trade's risk, at or over which the liquidity and cost line warns.
    public const double DefaultRoundTripRisks = 0.10;

    public static CardSettings Defaults { get; } = new(DefaultSectorBottom, DefaultCoverFloor, DefaultHeldShare, DefaultRoundTripRisks);

    // The block's values, each read where the block names it and its default where it does not; a value that cannot be
    // read, or one out of its range, is refused by its key.
    public static CardSettings From(Func<string, string?> value)
    {
        var bottom = Read(value, nameof(SectorBottom), DefaultSectorBottom, text => int.Parse(text, NumberStyles.Integer, CultureInfo.InvariantCulture));
        var cover = Read(value, nameof(CoverFloor), DefaultCoverFloor, text => decimal.Parse(text, NumberStyles.Number, CultureInfo.InvariantCulture));
        var held = Read(value, nameof(HeldShare), DefaultHeldShare, text => double.Parse(text, NumberStyles.Float, CultureInfo.InvariantCulture));
        var trip = Read(value, nameof(RoundTripRisks), DefaultRoundTripRisks, text => double.Parse(text, NumberStyles.Float, CultureInfo.InvariantCulture));

        if (bottom < 0 || cover < 0m || held is <= 0 or > 1 || trip < 0)
        {
            throw new InvalidOperationException(
                $"The card's settings under '{Section}' hold a value out of its range: the sector bottom and the round trip at or above nothing, the cover floor at or above nothing and the held share above nothing and at most one.");
        }

        return new CardSettings(bottom, cover, held, trip);
    }

    // The values as a card stores them.
    public string Json() => JsonSerializer.Serialize(new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["sectorBottom"] = SectorBottom.ToString(CultureInfo.InvariantCulture),
        ["coverFloor"] = CoverFloor.ToString(CultureInfo.InvariantCulture),
        ["heldShare"] = HeldShare.ToString("R", CultureInfo.InvariantCulture),
        ["roundTripRisks"] = RoundTripRisks.ToString("R", CultureInfo.InvariantCulture),
    });

    static T Read<T>(Func<string, string?> value, string key, T fallback, Func<string, T> parse)
    {
        var text = value($"{Section}:{key}");

        if (string.IsNullOrWhiteSpace(text))
        {
            return fallback;
        }

        try
        {
            return parse(text.Trim());
        }
        catch (FormatException)
        {
            throw new InvalidOperationException($"The card's setting '{Section}:{key}' holds '{text}', which is not a number.");
        }
        catch (OverflowException)
        {
            throw new InvalidOperationException($"The card's setting '{Section}:{key}' holds '{text}', which is out of range.");
        }
    }
}
