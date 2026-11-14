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
        QualityCardView? quality = null,
        ImprovingCardView? improving = null,
        SetupRecords? longRecords = null)
    {
        var region = new StringBuilder();

        // The fundamentals-first family's card, tile, buy points and stocks one step short are drawn no more, its rule
        // running out of sight; the Fundamentals family's tile stands in its slot and its card in its place.
        // see: Business first leaves Tonight, paper trading and Past picks while its rule keeps running out of sight
        families = [.. families.Where(card => card.Family != EquityBrief.Core.Families.FundamentalsRule.Name)];
        closeToBuy = closeToBuy is null ? null : [.. closeToBuy.Where(row => row.Cell.Family != EquityBrief.Core.Families.FundamentalsRule.Name)];

        // From 22.17 the read-only copy draws no stock one step short, on its card or in a tile's line.
        // see: The read-only copy is published to Cloudflare Pages with no control and no part of the method, and its upload refuses a page that leaks one
        closeToBuy = marks.Copying ? null : closeToBuy;

        // The page's links: what ran tonight, and on the S&P 500 the watch list with how many it holds.
        region.Append(Invariant($"<p class=\"tonight-links\"><a class=\"market-run\" href=\"{Escaped(runHref)}\">What ran tonight</a>"));
        region.Append(watching is { } held ? Invariant($" · <a class=\"market-watch\" data-watching=\"{held}\" href=\"{WatchRoute}\">Your watch list ({held})</a>") : string.Empty);
        region.Append("</p>");

        var (tiles, points) = TopOf(families, heavyweights, line.Open, night, line.Checks, line.DipOpen);

        // The buys' title names the session they are bought at, as their sentences do: Monday's buys on a Friday.
        var opens = MarkRenderer.OpenDay(night);

        region.Append(Cards.Computed("Tonight in one minute", marks.OneMinute(new OneMinuteView(line, tiles, ended, null, night, line.Groups, improving?.Bought) { HeldAgain = [.. families.SelectMany(family => family.HeldAgain.Select(card => (card.Ticker, card.Family)))] }), stamp: Cards.Night(night), id: "one-minute", region: "one-minute"));
        region.Append(Cards.Computed(char.ToUpperInvariant(opens[0]) + opens[1..] + " buys", marks.TomorrowsBuys(points, open, links, plans, night, PlanlessBuys(universe, improving, heavyweights, quality, longRecords)), stamp: Cards.Night(night), id: "buy-points", region: "buy-points"));
        region.Append(marks.Copying ? string.Empty : marks.CloseToBuy(line.CloseRead ? closeToBuy ?? [] : null, closeOpen, newest, universe));

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

        // The Fundamentals family's tile in the fundamentals-first family's slot, after the drift's and before the dip's.
        // see: The Fundamentals family buys a company whose newly read filing lifts its scorecard to eight in nine, and flags one whose release reads better
        if (improving is not null)
        {
            var slot = tiles.TakeWhile(tile => tile.Family != EquityBrief.Core.Families.DipRule.Name && tile.Family != EquityBrief.Core.Families.HeavyweightRule.Name).Count();

            shown = [.. tiles.Take(slot), new SetupTileView(EquityBrief.Core.Families.ImprovingRule.Name, EquityBrief.Core.Families.ImprovingRule.Label, improving.Bought, improving.Bought == 1 ? "pick" : "picks", improving.Fault is null ? "read nightly" : "not computed tonight"), .. tiles.Skip(slot)];
            liveSince[EquityBrief.Core.Families.ImprovingRule.Name] = improving.LiveSince;
            tonight[EquityBrief.Core.Families.ImprovingRule.Name] = ImprovingTileLine(improving);
        }

        if (quality is not null)
        {
            shown = [.. shown, new SetupTileView(EquityBrief.Core.Families.QualityRule.Name, EquityBrief.Core.Families.QualityRule.Label, quality.Cells.Count, quality.Cells.Count == 1 ? "holding" : "holdings", quality.Fault is null ? "kept monthly" : "not computed tonight")];
            liveSince[EquityBrief.Core.Families.QualityRule.Name] = quality.LiveSince;
            tonight[EquityBrief.Core.Families.QualityRule.Name] = QualityTileLine(quality, night);
        }

        // From the 22.6 correction of 2026-11-09 each tile ends on its setup's record over the long history on the index.
        shown = [.. shown.Select(tile => tile with { LongRecord = longRecords?.Of(Universes.Of(universe).Code, tile.Family) })];

        region.Append(Cards.Computed(SetupsTitle(shown.Count), marks.SixSetups(shown, liveSince, tonight, line.FirstLive), stamp: Cards.Night(night), id: "setups", region: "setups"));

        // The Fundamentals family's card in the fundamentals-first family's place, before the dip's.
        var improvingDrawn = improving is null;

        foreach (var card in families)
        {
            if (!improvingDrawn && card.Family == EquityBrief.Core.Families.DipRule.Name)
            {
                region.Append(ImprovingSection(marks, improving!, night));
                improvingDrawn = true;
            }

            region.Append(SetupCard(marks, card, night));
        }

        if (!improvingDrawn)
        {
            region.Append(ImprovingSection(marks, improving!, night));
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

    // The buys no plan with a stop and a target sells: the Fundamentals family's in its card's order, then the sector
    // heavyweights' and Quality at a fair price's holdings chosen and not yet bought, each linking to its row on its card.
    // see: Tonight's buys name every buy on the index, a buy no plan sells drawn with how it sells in its stop's and target's place
    public static IReadOnlyList<PlanlessBuyView> PlanlessBuys(string universe, ImprovingCardView? improving, HeavyweightCardView? heavyweights, QualityCardView? quality, SetupRecords? records = null) =>
    [
        .. PlanlessOf(universe, improving, heavyweights, quality).Select(buy => buy with { LongRecord = records?.Of(Universes.Of(universe).Code, buy.Family) }),
    ];

    static IReadOnlyList<PlanlessBuyView> PlanlessOf(string universe, ImprovingCardView? improving, HeavyweightCardView? heavyweights, QualityCardView? quality) =>
    [
        .. (improving?.Improving ?? []).Where(row => row.Status == MarkRenderer.BoughtWords).Select(row => new PlanlessBuyView(
            EquityBrief.Core.Families.ImprovingRule.Name,
            EquityBrief.Core.Families.ImprovingRule.Label,
            row.Ticker,
            row.Company,
            FundamentalsWhy(row, (improving!.Setting ?? EquityBrief.Core.Families.ImprovingGrid.Starting).BuyInNine),
            MarkRenderer.ChecksApply(row.Passed, row.Applied),
            row.Close,
            MarkRenderer.SellsOnFilings,
            TonightHref(universe, EquityBrief.Core.Families.ImprovingRule.Name, row.Ticker),
            row.EntryCheck)),
        .. (heavyweights?.Holdings ?? []).Where(holding => holding.SinceBought is null).Select(holding => new PlanlessBuyView(
            EquityBrief.Core.Families.HeavyweightRule.Name,
            "Sector heavyweights",
            holding.Ticker,
            holding.Company,
            Invariant($"Chosen at the reshuffle as one of the largest companies leading {holding.Sector}."),
            "not read",
            holding.Close,
            MarkRenderer.SellsAtAReshuffle,
            TonightHref(universe, EquityBrief.Core.Families.HeavyweightRule.Name, holding.Ticker))),
        .. (quality?.Cells ?? []).Where(cell => cell.SinceBought is null).Select(cell => new PlanlessBuyView(
            EquityBrief.Core.Families.QualityRule.Name,
            EquityBrief.Core.Families.QualityRule.Label,
            cell.Ticker,
            cell.Company,
            "Chosen at the reshuffle: a strong business at a price below its own usual one.",
            cell is { Passed: { } passed, Applied: { } applied } ? MarkRenderer.ChecksApply(passed, applied) : "not read",
            cell.Close,
            MarkRenderer.SellsAtAReshuffle,
            TonightHref(universe, EquityBrief.Core.Families.QualityRule.Name, cell.Ticker))),
    ];

    // Why the Fundamentals family bought a stock, in the reader's words: its rule and the checks its newest filing passes.
    public static string FundamentalsWhy(ImprovingRowCell row, int buyInNine) =>
        Invariant($"Bought because its newest filing passes {MarkRenderer.InEveryNine(buyInNine, atLeast: true)} to the company: {MarkRenderer.ChecksApply(row.Passed, row.Applied)}{(row is { BeforePassed: { } before } ? Invariant($", up from {before}") : string.Empty)}.");

    // The setups card's title, its tiles counted in words.
    static string SetupsTitle(int tiles) =>
        "The " + (tiles switch { 5 => "five", 6 => "six", 7 => "seven", 8 => "eight", _ => tiles.ToString(System.Globalization.CultureInfo.InvariantCulture) }) + " setups";

    // The Fundamentals family's card as a setup card, its press from the tile scrolling to it.
    static string ImprovingSection(MarkRenderer marks, ImprovingCardView improving, DateOnly night) =>
        FormattableString.Invariant($"<section class=\"card setup-card\" id=\"setup-{EquityBrief.Core.Families.ImprovingRule.Name}\" data-card=\"{EquityBrief.Core.Families.ImprovingRule.Name}\" data-night=\"{night:yyyy-MM-dd}\"><div class=\"card-h\"><div>{marks.ImprovingCardHead(improving)}</div>{(improving.Read ? Cards.Night(night) : string.Empty)}</div><div class=\"card-b\">{marks.ImprovingCard(improving)}</div></section>");

    // The Fundamentals family's tile line: tonight's picks and the stocks flagged and waiting for their filing.
    static string ImprovingTileLine(ImprovingCardView improving)
    {
        if (improving.Fault is not null)
        {
            return "Not computed tonight.";
        }

        if (!improving.Read)
        {
            return "Its first night has not run yet.";
        }

        return (improving.Bought == 1 ? "1 pick tonight." : Invariant($"{improving.Bought} picks tonight."))
            + (improving.Flagged == 1 ? " 1 flagged, waiting for the filing." : Invariant($" {improving.Flagged} flagged, waiting for the filing."));
    }

    // A book's holdings in words: those bought, and those chosen and to buy at tomorrow's open, which are held by nothing yet.
    // see: Every count of trades names its group and one function works the four groups
    static string HoldingsWords(int chosen, int toBuy, DateOnly night)
    {
        var bought = chosen - toBuy;
        var held = bought == 1 ? "1 holding" : Invariant($"{bought} holdings");

        return toBuy == 0 ? held + "." : bought == 0 ? Invariant($"{toBuy} {TradeGroupsView.ToBuyWords(night)}.") : Invariant($"{held}, {toBuy} {TradeGroupsView.ToBuyWords(night)}.");
    }

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

        var held = HoldingsWords(quality.Cells.Count, quality.Cells.Count(cell => cell.SinceBought is null), night);

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
            ? Invariant($"No picks. Closest: {nearest.Cell.Ticker}, {ReaderWords.MissedBy(card.Family, nearest.Cell.Gate, nearest.Cell.Words) ?? "one step short"}.")
            : "No picks tonight.";
    }

    // The sector heavyweights' tile line: its holdings and when it next reshuffles.
    static string HeavyweightTileLine(HeavyweightCardView heavyweights, DateOnly night)
    {
        var held = HoldingsWords(heavyweights.Holdings.Count, heavyweights.Holdings.Count(holding => holding.SinceBought is null), night);

        if (heavyweights.LastRebalance == night)
        {
            return held + " Reshuffled tonight.";
        }

        return heavyweights.NextRebalance is { } next && EquityBrief.Core.Bars.ExchangeClosures.SessionsUntil(night, next) is { } days
            ? held + Invariant($" Next reshuffle in {days} trading {(days == 1 ? "day" : "days")}.")
            : held + " Its next reshuffle is not read.";
    }
}
