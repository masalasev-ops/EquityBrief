using System.Text;
using EquityBrief.Web.Marks;
using static System.FormattableString;

namespace EquityBrief.Web.App;

// Tonight redesigned, from 22.6: on a night the families drew its list, the page opens on its links, then tonight in one
// minute, tomorrow's buys, the stocks one step from qualifying, the six setups, each setup's card, the sector heavyweights'
// card and the page's words at its foot, every card opening on one sentence from tonight's numbers in the reader's words.
// see: A redesigned page speaks in the reader's words: every card opens on a sentence from its numbers, and no internal term reaches the screen outside the glossary
public sealed partial class SinglePageApp
{
    // The cards of a night the families drew, top to bottom, on every index.
    static string TonightCards(
        MarkRenderer marks,
        DateOnly night,
        string runHref,
        int? watching,
        MarketLineView line,
        IReadOnlyList<FamilyCardView> families,
        HeavyweightCardView? heavyweights,
        IReadOnlyList<CloseToBuyRow>? closeToBuy,
        bool closeOpen,
        bool newest,
        string universe,
        string? open,
        EndedTonightView? ended,
        IReadOnlyDictionary<string, string>? links = null,
        IReadOnlyDictionary<string, string>? plans = null,
        QualityCardView? quality = null)
    {
        var region = new StringBuilder();

        // The page's links: what ran tonight, and on the S&P 500 the watch list with how many it holds.
        region.Append(Invariant($"<p class=\"tonight-links\"><a class=\"market-run\" href=\"{Escaped(runHref)}\">What ran tonight</a>"));
        region.Append(watching is { } held ? Invariant($" · <a class=\"market-watch\" data-watching=\"{held}\" href=\"{WatchRoute}\">Your watch list ({held})</a>") : string.Empty);
        region.Append("</p>");

        var (tiles, points) = TopOf(families, heavyweights, line.Open, night, line.Checks, line.DipOpen);

        region.Append(Cards.Computed("Tonight in one minute", marks.OneMinute(new OneMinuteView(line, tiles, ended, null)), stamp: Cards.Night(night), id: "one-minute", region: "one-minute"));
        region.Append(Cards.Computed("Tomorrow's buys", marks.TomorrowsBuys(points, open, links, plans), stamp: Cards.Night(night), id: "buy-points", region: "buy-points"));
        region.Append(marks.CloseToBuy(line.CloseRead ? closeToBuy ?? [] : null, closeOpen, newest, universe));

        // The setups: each tile's badge from its card's standing, and its line from tonight's picks, its holdings or the
        // stock closest to it; from 22.9 Quality at a fair price's tile after the sector heavyweights', the card's title
        // counting the tiles in words.
        var liveSince = families.ToDictionary(card => card.Family, card => card.LiveSince, StringComparer.Ordinal);
        var tonight = families.ToDictionary(card => card.Family, card => TileLine(card, closeToBuy, line), StringComparer.Ordinal);

        if (heavyweights is not null)
        {
            liveSince[EquityBrief.Core.Families.HeavyweightRule.Name] = heavyweights.LiveSince;
            tonight[EquityBrief.Core.Families.HeavyweightRule.Name] = HeavyweightTileLine(heavyweights, night);
        }

        IReadOnlyList<SetupTileView> shown = tiles;

        if (quality is not null)
        {
            shown = [.. tiles, new SetupTileView(EquityBrief.Core.Families.QualityRule.Name, EquityBrief.Core.Families.QualityRule.Label, quality.Cells.Count, quality.Cells.Count == 1 ? "holding" : "holdings", quality.Fault is null ? "kept monthly" : "not computed tonight")];
            liveSince[EquityBrief.Core.Families.QualityRule.Name] = quality.LiveSince;
            tonight[EquityBrief.Core.Families.QualityRule.Name] = QualityTileLine(quality, night);
        }

        region.Append(Cards.Computed(SetupsTitle(shown.Count), marks.SixSetups(shown, liveSince, tonight), stamp: Cards.Night(night), id: "setups", region: "setups"));

        foreach (var card in families)
        {
            region.Append(SetupCard(marks, card, night));
        }

        if (heavyweights is not null)
        {
            region.Append(HeavyweightsCard(marks, heavyweights, night));
        }

        if (quality is not null)
        {
            region.Append(FormattableString.Invariant($"<section class=\"card setup-card\" id=\"setup-{EquityBrief.Core.Families.QualityRule.Name}\" data-card=\"quality\" data-night=\"{night:yyyy-MM-dd}\"><div class=\"card-h\"><div>{marks.QualityCardHead(quality)}</div></div><div class=\"card-b\">{marks.QualityCard(quality)}</div></section>"));
        }

        region.Append(Cards.Computed("Words used on this page", marks.WordsOnThisPage(), id: "words", region: "words"));

        return region.ToString();
    }

    // The setups card's title, its tiles counted in words.
    static string SetupsTitle(int tiles) =>
        "The " + (tiles switch { 5 => "five", 6 => "six", 7 => "seven", 8 => "eight", _ => tiles.ToString(System.Globalization.CultureInfo.InvariantCulture) }) + " setups";

    // Quality at a fair price's tile line: its holdings and when it next reshuffles.
    static string QualityTileLine(QualityCardView quality, DateOnly night)
    {
        if (quality.Fault is not null)
        {
            return "Not computed tonight.";
        }

        if (!quality.Read)
        {
            return "Its first holdings are chosen on its first night.";
        }

        var held = quality.Cells.Count == 1 ? "1 holding." : Invariant($"{quality.Cells.Count} holdings.");

        if (quality.LastReshuffle == night)
        {
            return held + " Reshuffled tonight.";
        }

        return quality.DaysToNext is { } days
            ? held + Invariant($" Next reshuffle in {days} trading {(days == 1 ? "day" : "days")}.")
            : held + " Its next reshuffle is not read.";
    }

    // A setup's tile line tonight: its picks, or that its part of the night was not computed, or that the market check
    // closed its list, or that it found none with the stock closest to it and the step that stock missed.
    static string TileLine(FamilyCardView card, IReadOnlyList<CloseToBuyRow>? closeToBuy, MarketLineView line)
    {
        if (card.Picks.Count > 0)
        {
            return card.Picks.Count == 1 ? "1 pick tonight." : Invariant($"{card.Picks.Count} picks tonight.");
        }

        if (card.NotComputed)
        {
            return "No picks: its part of the night was not computed.";
        }

        var closed = card.Family == EquityBrief.Core.Families.DipRule.Name
            ? !line.DipOpen && line.Dip is not null
            : line.Checks?.FirstOrDefault(check => check.Family == card.Family) is { } own ? !own.Open : !line.Open;

        if (closed)
        {
            return "No picks: the market check closed its list.";
        }

        return closeToBuy?.FirstOrDefault(row => row.Cell.Family == card.Family) is { } nearest
            ? Invariant($"No picks. Closest: {nearest.Cell.Ticker}, {ReaderWords.Missed(card.Family, nearest.Cell.Gate) ?? "one step short"}.")
            : "No picks tonight.";
    }

    // The sector heavyweights' tile line: its holdings and when it next reshuffles.
    static string HeavyweightTileLine(HeavyweightCardView heavyweights, DateOnly night)
    {
        var held = heavyweights.Holdings.Count == 1 ? "1 holding." : Invariant($"{heavyweights.Holdings.Count} holdings.");

        if (heavyweights.LastRebalance == night)
        {
            return held + " Reshuffled tonight.";
        }

        return heavyweights.NextRebalance is { } next && EquityBrief.Core.Bars.ExchangeClosures.SessionsUntil(night, next) is { } days
            ? held + Invariant($" Next reshuffle in {days} trading {(days == 1 ? "day" : "days")}.")
            : held + " Its next reshuffle is not read.";
    }
}
