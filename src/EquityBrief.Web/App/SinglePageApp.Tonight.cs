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
        SetupRecords? longRecords = null,
        TonightExtras? extras = null)
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
        var planless = PlanlessBuys(universe, improving, heavyweights, quality, longRecords);

        // From the operator's order of 2026-11-26 Tonight redesigned: five tiles at a glance, the markets in one strip, every plan
        // in one list beside the plan chosen, the setups one table row each with one opened beneath its row, and the page's
        // words in a fold.
        // see: Tonight lists every plan the rules hold on the night in one place, each at its own address in one design
        var view = new OneMinuteView(line, tiles, ended, null, night, line.Groups, improving?.Bought) { HeldAgain = [.. families.SelectMany(family => family.HeldAgain.Select(card => (card.Ticker, card.Family)))], NotFilled = families.SelectMany(family => family.HeldAgain).Where(card => card.HeldAgain is { Bought: false }).Select(card => card.Ticker).ToHashSet(StringComparer.Ordinal), OnDip = points.Count(point => MarkRenderer.WaitsForAPrice(point, line.Groups)) };
        var entries = PlanEntries(families, points, planless, line.Groups, extras?.Placed);
        var newIdeas = entries.Count(entry => entry.Group == PlanEntryView.New);

        region.Append("<div class=\"tn\" data-redesigned=\"2026-11-26\">");
        region.Append(marks.Glance(view, newIdeas, entries, (new PaperLink(universe) { Tab = MarkRenderer.RecentTab }).Href));
        region.Append(MarkRenderer.OpenLine(view));
        region.Append(Invariant($"<section class=\"tn-markets\" id=\"mk\" aria-label=\"The markets\">{extras?.Strip ?? string.Empty}{marks.MarketCheckLine(line)}</section>"));
        // A book's holding bought on an earlier night keeps its card at its own address, opened from its row on the book's panel.
        // see: A stock's full card is drawn only in its plan view on Tonight, and every other page shows it as one line that opens the plan
        // One chosen tonight and listed among the new ideas carries its card on that plan.
        var booked = (heavyweights?.Holdings ?? []).Where(holding => holding.Card is not null).ToArray();

        entries = [.. entries.Select(entry => entry is { Planless: not null, Card: null, Family: EquityBrief.Core.Families.HeavyweightRule.Name } && booked.FirstOrDefault(holding => holding.Ticker == entry.Ticker) is { } chosen ? entry with { Card = chosen.Card } : entry)];

        PlanEntryView[] addressed = [.. booked.Where(holding => !entries.Any(entry => entry.Ticker == holding.Ticker)).Select(holding => new PlanEntryView(PlanEntryView.Held, EquityBrief.Core.Families.HeavyweightRule.Name, holding.Ticker) { Company = holding.Company, Card = holding.Card, Since = holding.HeldSince, Close = holding.Close, Holding = true })];

        region.Append(marks.EveryPlan(entries, night, universe, extras?.Plan, open, plans, addressed));

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

        // From the operator's order of 2026-11-28 each setup's panel beneath its row of the setups' table, in place of its card:
        // the row names the setup, its revision and what it does, so the panel draws no heading of its own.
        // see: Tonight's setup rows start closed and each opens a panel of its own on a click
        var panels = new Dictionary<string, string>(StringComparer.Ordinal);
        var word = Universes.Of(universe).Word;

        foreach (var card in families)
        {
            panels[card.Family] = BookPanel(card.Family, "family", night, marks.SetupPanel(card, word));
        }

        if (improving is not null)
        {
            panels[EquityBrief.Core.Families.ImprovingRule.Name] = BookPanel(EquityBrief.Core.Families.ImprovingRule.Name, EquityBrief.Core.Families.ImprovingRule.Name, night, marks.ImprovingCard(improving));
        }

        if (heavyweights is not null)
        {
            panels[EquityBrief.Core.Families.HeavyweightRule.Name] = BookPanel(EquityBrief.Core.Families.HeavyweightRule.Name, "heavyweights", night, (heavyweights.RuleChoice is { } rule && !marks.Copying ? MarkRenderer.ShowingOf(EquityBrief.Core.Families.HeavyweightRule.Name, rule) : string.Empty) + marks.HeavyweightCard(heavyweights));
        }

        if (quality is not null)
        {
            panels[EquityBrief.Core.Families.QualityRule.Name] = BookPanel(EquityBrief.Core.Families.QualityRule.Name, "quality", night, marks.QualityCard(quality));
        }

        region.Append(SetupsTable(marks, shown, liveSince, tonight, line.FirstLive, panels, extras?.Setup, view, line));
        region.Append(marks.Copying ? string.Empty : marks.CloseToBuy(line.CloseRead ? closeToBuy ?? [] : null, closeOpen, newest, universe));
        region.Append(Invariant($"<section class=\"card tn-words\" id=\"words\" data-card=\"words\"><details><summary><h2>Words used on this page ({marks.WordsCount})</h2></summary><div class=\"card-b\">{marks.WordsOnThisPage()}</div></details></section>"));
        region.Append("</div>");

        return region.ToString();
    }

    // What a page reading Tonight hands it beyond its cards: the market strip, drawn beneath the tiles; the card each waiting
    // order's night stored for it, by its setup, stock and night; the plan its address names; and the setup its link opens.
    public sealed record TonightExtras(string Strip, IReadOnlyDictionary<string, DecisionCardView> Placed, string? Plan = null, string? Setup = null)
    {
        public static string Key(string family, string ticker, DateOnly night) =>
            family + "|" + ticker + "|" + night.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
    }

    // Every plan the rules hold on the night, in the list's order: tonight's new ideas in the order the page draws the setups,
    // the books' buys among them; the orders waiting from earlier nights nearest their buy price first, each drawn from the card
    // the night it was placed stored and marked where its stock met the rules again; then the stocks held that met the rules
    // again.
    // see: A waiting order's plan is the one stored on the night it was placed, its checks said as that night's
    public static IReadOnlyList<PlanEntryView> PlanEntries(IReadOnlyList<FamilyCardView> families, IReadOnlyList<BuyPointView> points, IReadOnlyList<PlanlessBuyView> planless, TradeGroupsView? groups, IReadOnlyDictionary<string, DecisionCardView>? placed)
    {
        var entries = new List<PlanEntryView>();

        foreach (var (point, book) in MarkRenderer.BuysInOrder(points, planless))
        {
            if (point is { } drawn)
            {
                entries.Add(new PlanEntryView(PlanEntryView.New, drawn.Family, drawn.Pick.Row.Ticker)
                {
                    Company = drawn.Pick.Row.Distance?.Name,
                    Card = drawn.Pick.Card,
                    Pick = drawn.Pick,
                    Close = drawn.Pick.Card?.Zone?.Close ?? drawn.Pick.Card?.Entry ?? drawn.Pick.Buy,
                    OnDip = MarkRenderer.WaitsForAPrice(drawn, groups),
                });
            }
            else if (book is { } bought)
            {
                entries.Add(new PlanEntryView(PlanEntryView.New, bought.Family, bought.Ticker) { Company = bought.Company, Planless = bought, Close = bought.Close });
            }
        }

        var again = families.SelectMany(family => family.HeldAgain).Where(card => card.HeldAgain is not null).ToArray();
        var orders = (groups?.StillWaitingOrders ?? [])
            .Select(order => new PlanEntryView(PlanEntryView.Waiting, order.Family, order.Ticker)
            {
                Company = order.Company,
                Card = placed?.GetValueOrDefault(TonightExtras.Key(order.Family, order.Ticker, order.Listed)),
                Order = order,
                Since = order.Listed,
                MetAgain = again.Any(card => card.Ticker == order.Ticker && card.HeldAgain is { Bought: false }),
                Again = again.FirstOrDefault(card => card.Ticker == order.Ticker && card.HeldAgain is { Bought: false }),
                Close = order.LastClose,
            })
            .OrderBy(entry => entry.Gap ?? decimal.MaxValue)
            .ThenBy(entry => entry.Ticker, StringComparer.Ordinal)
            .ToArray();

        entries.AddRange(orders);

        // A stock met again while its order waits with no waiting order drawn for it stands in the waiting group on its own card.
        entries.AddRange(again
            .Where(card => card.HeldAgain is { Bought: false, Ordered: false } && !orders.Any(order => order.Ticker == card.Ticker))
            .Select(card => new PlanEntryView(PlanEntryView.Waiting, card.HeldAgain!.Family, card.Ticker) { Card = card, Since = card.HeldAgain.Night, MetAgain = true, Close = card.HeldAgain.LastClose }));

        entries.AddRange(again
            .Where(card => card.HeldAgain is { Bought: true } or { Ordered: true })
            .Select(card => new PlanEntryView(PlanEntryView.Held, card.Family, card.Ticker) { Card = card, Since = card.HeldAgain!.BoughtOn ?? card.HeldAgain.Night, MetAgain = true, Close = card.HeldAgain.LastClose }));

        return entries;
    }

    // A setup's panel beneath its row, a book's its card's body: no head, the row naming the setup, its revision and what it does.
    static string BookPanel(string family, string kind, DateOnly night, string body) =>
        FormattableString.Invariant($"<section class=\"card setup-card\" id=\"setup-{family}\" data-card=\"{kind}\" data-night=\"{night:yyyy-MM-dd}\"><div class=\"card-b\">{body}</div></section>");

    // The setups' table: a row a setup, its plain words and its rule's name, its revision, tonight's line, its quiet nights and
    // its record from 2019, one row opened at a time with its card beneath it, the one the link names or the first.
    static string SetupsTable(MarkRenderer marks, IReadOnlyList<SetupTileView> shown, IReadOnlyDictionary<string, DateOnly?> liveSince, IReadOnlyDictionary<string, string> tonight, IReadOnlyDictionary<string, DateOnly?>? firstLive, IReadOnlyDictionary<string, string> panels, string? named, OneMinuteView view, MarketLineView line)
    {
        var html = new StringBuilder();
        var finding = shown.Count(tile => tile.Family != EquityBrief.Core.Families.HeavyweightRule.Name && tile.Family != EquityBrief.Core.Families.QualityRule.Name && tile.Count > 0);
        // Every row starts closed; only a link naming a setup, which a click on a book's buy follows, opens its row.
        var opened = named is not null && panels.ContainsKey(named) ? named : null;

        html.Append(Invariant($"<section class=\"card tn-setups\" id=\"setups\" data-card=\"setups\"><div class=\"card-h\"><div><h2>{Escaped(SetupsTitle(shown.Count))} tonight</h2></div></div><div class=\"card-b\">"));
        html.Append(Invariant($"<p class=\"tn-sub card-summary\" data-summary>{Escaped(MarkRenderer.FindingWords(finding, shown.Count))} {Escaped(MarkRenderer.QuietWords(view))} Open a setup for its rule, how the stocks it read were narrowed, its variants and its record.</p>"));
        html.Append(marks.OneStepLine(line));
        html.Append("<div class=\"tbl-wrap\"><table class=\"list-table st-tbl\"><thead><tr><th>Setup</th><th>Running</th><th>Tonight</th><th>Quiet nights</th><th>From 2019, against the index's stocks</th><th></th></tr></thead><tbody>");

        foreach (var tile in shown)
        {
            var since = liveSince.GetValueOrDefault(tile.Family);
            var first = since is null ? null : firstLive?.GetValueOrDefault(tile.Family) ?? since;
            // From 24.48 the row alone names the revision, with the freeze beside it, the panel drawing no line of its own.
            var frozen = since is { } held ? RuleFreeze.Beside(held) : string.Empty;
            var running = RevisionTags.For(tile.Family, DateOnly.MaxValue) is { Length: > 0 } tag
                ? tag + (frozen.Length > 0 ? Invariant($"<small class=\"st-frozen\">{Escaped(frozen.TrimStart(';', ' '))}</small>") : string.Empty)
                : Invariant($"<span class=\"status-pill {(since is null ? "provisional" : "live")}\">{Escaped(first is { } live ? "First ran " + live.ToString("d MMM yyyy", System.Globalization.CultureInfo.InvariantCulture) + frozen : "Not yet tested")}</span>");
            var on = tile.Family == opened;

            html.Append(Invariant($"<tr class=\"st-row{(on ? " on" : string.Empty)}\" data-setup=\"{Escaped(tile.Family)}\" data-count=\"{tile.Count}\" data-state=\"{Escaped(tile.State)}\">"));
            html.Append(Invariant($"<td><i class=\"fam-swatch\" data-family=\"{Escaped(tile.Family)}\"></i> <b>{Escaped(MarkRenderer.ShortOf(tile.Family))}</b><small>{Escaped(tile.Label)}</small></td>"));
            html.Append(Invariant($"<td>{running}</td><td>{Escaped(tonight.GetValueOrDefault(tile.Family) ?? tile.State)}</td><td>{Escaped(tile.Quiet ?? string.Empty)}</td><td>{MarkRenderer.TileRecordLine(tile.LongRecord)}</td>"));
            html.Append(Invariant($"<td>{(panels.ContainsKey(tile.Family) ? Invariant($"<button type=\"button\" class=\"btn-2\" data-setup-toggle=\"{Escaped(tile.Family)}\" aria-expanded=\"{(on ? "true" : "false")}\">{(on ? "Close" : "Open")}</button>") : string.Empty)}</td></tr>"));

            if (panels.GetValueOrDefault(tile.Family) is { } panel)
            {
                html.Append(Invariant($"<tr class=\"st-open\" data-for=\"{Escaped(tile.Family)}\"{(on ? string.Empty : " hidden")}><td colspan=\"6\"><div class=\"st-panel fam-edge\" data-family=\"{Escaped(tile.Family)}\">{panel}</div></td></tr>"));
            }
        }

        return html.Append("</tbody></table></div></div></section>").ToString();
    }

    // The books' buys: the Fundamentals family's in its card's order, then the sector heavyweights' and Quality at a fair
    // price's holdings chosen and not yet bought, each linking to its row on its card, with the stop and take-profit its card
    // placed where it holds them.
    // see: Tonight's buys name every buy on the index, a book's buy drawn with its stop and take-profit where its card placed them
    // see: A book's pick draws the stop and the take-profit its exit places, worked from the close it was chosen at
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
            row.EntryCheck) { Stop = row.Stop, TakeProfit = row.TakeProfit }),
        .. (heavyweights?.Holdings ?? []).Where(holding => holding.SinceBought is null).Select(holding => new PlanlessBuyView(
            EquityBrief.Core.Families.HeavyweightRule.Name,
            "Sector heavyweights",
            holding.Ticker,
            holding.Company,
            Invariant($"Chosen at the reshuffle as one of the largest companies leading {holding.Sector}."),
            "not read",
            holding.Close,
            MarkRenderer.SellsAtAReshuffle,
            TonightHref(universe, EquityBrief.Core.Families.HeavyweightRule.Name, holding.Ticker)) { Stop = holding.Stop ?? holding.Card?.Stop, TakeProfit = holding.TakeProfit ?? holding.Card?.TakeProfit }),
        .. (quality?.Cells ?? []).Where(cell => cell.SinceBought is null).Select(cell => new PlanlessBuyView(
            EquityBrief.Core.Families.QualityRule.Name,
            EquityBrief.Core.Families.QualityRule.Label,
            cell.Ticker,
            cell.Company,
            "Chosen at the reshuffle: a strong business at a price below its own usual one.",
            cell is { Passed: { } passed, Applied: { } applied } ? MarkRenderer.ChecksApply(passed, applied) : "not read",
            cell.Close,
            MarkRenderer.SellsAtAReshuffle,
            TonightHref(universe, EquityBrief.Core.Families.QualityRule.Name, cell.Ticker)) { Stop = cell.Stop, TakeProfit = cell.TakeProfit }),
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
