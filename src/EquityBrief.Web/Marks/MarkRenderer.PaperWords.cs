using System.Globalization;
using EquityBrief.Core.Families;

namespace EquityBrief.Web.Marks;

// From 21.13 each card of paper trading opens on one sentence from its numbers, and each setup's tile on its return since
// its record began on the account's size with the trades it holds open, in the reader's words.
// see: A redesigned page speaks in the reader's words: every card opens on a sentence from its numbers, and no internal term reaches the screen outside the glossary
public sealed partial class MarkRenderer
{
    // A card's summary sentence, the first thing its body draws.
    public static string CardSummary(string sentence) => Formatted($"<p class=\"card-summary\" data-summary>{Escaped(sentence)}</p>");

    static string Short(DateOnly day) => day.ToString("d MMM", CultureInfo.InvariantCulture);

    // A return as the reader says it: up or down by a tenth of a per cent, or flat.
    static string Moved(double fraction)
    {
        var tenth = Unsigned(fraction * 100, 1);

        return tenth > 0 ? Formatted($"Up {tenth:0.0}%") : tenth < 0 ? Formatted($"Down {-tenth:0.0}%") : "Flat";
    }

    // A tile's sentence: its return since the day it counts from on the account's size, and the trades it holds open.
    public static string TileSaid(double? made, DateOnly? since, decimal? size, int open) =>
        made is not { } figure || since is not { } from
            ? "No trade yet."
            : Formatted($"{Moved(figure)} since {Short(from)}{(size is { } on ? Formatted($" on ${on:#,0}") : string.Empty)}, with {(open == 0 ? "no trades" : Counted(open, "trade", "trades"))} open.");

    // The account card's sentence: the account each setup trades a copy of, and how each of its positions is sized, its risk
    // the settings' and its cap a tenth of the account whatever a pick's card is capped at.
    // see: Each rule's paper account sizes a position by its risk and caps it at a tenth of the account, whatever a pick's card is capped at
    public static string PaperAccountSaid((decimal Size, decimal Risk, decimal Cap)? settings, (decimal Size, decimal Risk, decimal Cap)? example) =>
        settings is { } kept
            ? Formatted($"Each setup trades its own copy of your account of ${kept.Size:#,0}. {PositionSaid(kept.Risk)}")
            : example is { } shown
                ? Formatted($"Until you save your own, each setup trades its own copy of an example account of ${shown.Size:#,0}. {PositionSaid(shown.Risk)}")
                : "No account is set yet, so no setup's trades are walked in money.";

    // The account card's sentence as the card opens on it, the sentence on a position's size the operator's words drawn as
    // worded.
    public static string PaperAccountSummary((decimal Size, decimal Risk, decimal Cap)? settings, (decimal Size, decimal Risk, decimal Cap)? example) =>
        (settings ?? example) is { } walked
            ? Formatted($"<p class=\"card-summary\" data-summary>{Escaped(settings is null ? Formatted($"Until you save your own, each setup trades its own copy of an example account of ${walked.Size:#,0}.") : Formatted($"Each setup trades its own copy of your account of ${walked.Size:#,0}."))} <span{ReaderText.AsWordedMark}>{Escaped(PositionSaid(walked.Risk))}</span></p>")
            : CardSummary(PaperAccountSaid(settings, example));

    // How each position of a rule's account is sized, in the operator's words.
    public static string PositionSaid(decimal risk) =>
        Formatted($"Each position risks {risk:0.##}% and is capped at {(EquityBrief.Core.Cards.PaperAccount.AccountCap == 0.1m ? "a tenth" : Formatted($"{EquityBrief.Core.Cards.PaperAccount.AccountCap * 100:0.##}%"))} of the account.");

    public static string PaperChartSaid(IReadOnlyList<PaperAccountView> accounts, PaperFundView? fund, bool walked)
    {
        if (!walked)
        {
            return "Set your account above to draw each setup's return.";
        }

        var made = accounts.Where(account => account.Head && account.Trades > 0 && account.Walk?.Return is not null).ToArray();

        if (made.Length == 0)
        {
            return "No live setup has a trade yet, so no return is drawn.";
        }

        var best = made.OrderByDescending(account => account.Walk!.Return!.Value).ThenBy(account => SetupFamilies.OrderOf(account.Family)).First();
        var index = best.Walk!.FundReturn is { } held && fund is { } named ? Formatted($"; the index's fund, {named.Fund}, is {Moved(held).ToLowerInvariant()} over the same days") : string.Empty;

        return Formatted($"Of {Counted(made.Length, "live setup", "live setups")} with a trade, {FamilyName(best.Family)} has done best, {Moved(best.Walk!.Return!.Value).ToLowerInvariant()}{(best.Since is { } since ? " since " + Short(since) : string.Empty)}{index}.");
    }

    public static string PaperCombinedSaid(PaperCombinedView? combined, bool walked) =>
        !walked
            ? "Set your account above to walk the live setups' accounts together."
            : combined is not { } all
                ? "No live setup's account has a day to count from yet, so the averaged account holds none."
                : Formatted($"The live setups' accounts, averaged: {(all.Walk.Return is { } made ? Moved(made).ToLowerInvariant() : "no return yet")} since {Short(all.Since)}, the day every one of the {all.Accounts} counts, with {Counted(all.Open, "position", "positions")} open across them.");

    // From the 22.25 correction of 2026-11-11 this version's whole count as the one function works it, with how many of them
    // the accounts did not buy and why, the open and the closed each.
    // see: Every count of trades names its group and one function works the four groups
    public static string PaperBoughtSaid(PaperPageView page)
    {
        var newest = page.Open.Concat(page.Closed).Select(trade => (DateOnly?)trade.Bought).Max();
        string[] skippedOpen = [.. page.Skipped.Where(one => one.Open).Select(one => one.Why)];
        string[] skippedClosed = [.. page.Skipped.Where(one => !one.Open).Select(one => one.Why)];
        var open = page.Groups?.ThisVersion.Open ?? page.Open.Count + skippedOpen.Length;
        var closed = page.Groups?.ThisVersion.Closed ?? page.Closed.Count + skippedClosed.Length;
        var selling = page.Groups?.ThisVersion.Selling ?? page.Open.Count(trade => trade.Selling is not null);
        var after = (selling > 0 ? ", " + TradeGroupsView.Selling(selling, page.Groups?.Night) : string.Empty) + NotBoughtWords(skippedOpen);

        return open + closed == 0
            ? "No live setup has bought a trade under its current version yet."
            : Formatted($"This version's trades: {open} open{after}{(after.Length > 0 ? "," : string.Empty)} and {Counted(closed, "has", "have")} closed{NotBoughtWords(skippedClosed)}{(newest is { } day ? ", the newest bought on " + Short(day) : string.Empty)}.");
    }

    // How many of a count of this version's trades the accounts did not buy, each reason the walk gave in the reader's words.
    static string NotBoughtWords(IReadOnlyList<string> whys)
    {
        static string Why(string why) => why switch
        {
            Core.Cards.PaperAccount.UnderTheCash => "for want of free cash",
            Core.Cards.PaperAccount.NotSized => "as its plan sized no shares",
            _ => "as " + why,
        };

        var reasons = whys.GroupBy(why => why, StringComparer.Ordinal).OrderByDescending(group => group.Count()).ThenBy(group => group.Key, StringComparer.Ordinal).ToArray();

        return reasons.Length switch
        {
            0 => string.Empty,
            1 => Formatted($", {whys.Count} of them not bought by the account {Why(reasons[0].Key)}"),
            _ => Formatted($", {whys.Count} of them not bought by the account, ") + string.Join(" and ", reasons.Select(group => Formatted($"{group.Count()} {Why(group.Key)}"))),
        };
    }

    public static string PaperVariantsSaid(PaperPageView page, string family)
    {
        var versions = page.Accounts.Count(account => account.Standing == PaperAccountView.Variant && account.Family == family);

        return versions == 0
            ? Formatted($"{FamilyName(family)} has no other version trading beside its live setup.")
            : Formatted($"{FamilyName(family)} has {Counted(versions, "other version", "other versions")} trading beside its live setup; between them they took {Counted(page.OnlyVariants.Count, "trade", "trades")} it did not.");
    }

    public static string PaperScorecardSaid(PaperPageView page, string family)
    {
        var head = page.Accounts.FirstOrDefault(account => account.Head && account.Family == family);

        return head is null
            ? Formatted($"{FamilyName(family)} has no live setup on this index.")
            : Formatted($"{FamilyName(family)}'s live setup: {head.Verdict} on the test years, and {Counted(head.Trades, "trade", "trades")} since it went live.");
    }

    public static string PaperYoursSaid(PaperAccountView? you) =>
        you is null || you.Trades == 0
            ? "You have recorded no trade on this index."
            : Formatted($"You recorded {Counted(you.Trades, "trade", "trades")} on this index, and {Counted(you.Ended, "has", "have")} ended.");
}
