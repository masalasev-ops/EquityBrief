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

    public static string PaperAccountSaid((decimal Size, decimal Risk, decimal Cap)? settings, (decimal Size, decimal Risk, decimal Cap)? example) =>
        settings is { } kept
            ? Formatted($"Each setup trades its own copy of your account of ${kept.Size:#,0}, risking {kept.Risk:0.##}% a trade with at most {kept.Cap * 100:0.##}% in one stock.")
            : example is { } shown
                ? Formatted($"Until you save your own, each setup trades its own copy of an example account of ${shown.Size:#,0}, risking {shown.Risk:0.##}% a trade with at most {shown.Cap * 100:0.##}% in one stock.")
                : "No account is set yet, so no setup's trades are walked in money.";

    public static string PaperChartSaid(IReadOnlyList<PaperAccountView> accounts, PaperFundView? fund, bool walked)
    {
        if (!walked)
        {
            return "Set your account above to draw each setup's return.";
        }

        var made = accounts.Where(account => account.Head && account.Walk?.Return is not null).ToArray();

        if (made.Length == 0)
        {
            return "No live setup has a trade yet, so no return is drawn.";
        }

        var best = made.OrderByDescending(account => account.Walk!.Return!.Value).ThenBy(account => SetupFamilies.OrderOf(account.Family)).First();
        var index = fund?.Return is { } held ? Formatted($"; the index's fund is {Moved(held).ToLowerInvariant()} over the same days") : string.Empty;

        return Formatted($"Of {made.Length} live setups with a trade, {FamilyName(best.Family)} has done best, {Moved(best.Walk!.Return!.Value).ToLowerInvariant()}{(best.Since is { } since ? " since " + Short(since) : string.Empty)}{index}.");
    }

    public static string PaperCombinedSaid(PaperCombinedView? combined, bool walked) =>
        !walked
            ? "Set your account above to walk every setup's trades together."
            : combined is not { } all
                ? "No live setup has a trade yet, so the account of every setup together holds none."
                : Formatted($"All setups together: {(all.Walk.Return is { } made ? Moved(made).ToLowerInvariant() : "no return yet")} since {Short(all.Since)}, {Counted(all.Open, "position", "positions")} open, {Counted(all.Skipped, "pick", "picks")} skipped.");

    public static string PaperBoughtSaid(PaperPageView page)
    {
        var newest = page.Open.Concat(page.Closed).Select(trade => (DateOnly?)trade.Bought).Max();

        return page.Open.Count + page.Closed.Count == 0
            ? "No live setup has bought a trade yet."
            : Formatted($"{Capital(Counted(page.Open.Count, "trade is", "trades are"))} open and {Counted(page.Closed.Count, "has", "have")} closed, the newest bought on {Short(newest!.Value)}.");
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
