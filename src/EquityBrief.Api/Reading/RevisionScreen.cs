using EquityBrief.Core.Bars;
using EquityBrief.Core.Prices;
using EquityBrief.Web.Marks;

namespace EquityBrief.Api.Reading;

// Each rule's and variant's orders as its Waiting to buy and Left the waiting list tabs draw them, the running revision's under the account's key and each earlier revision's under the key and its number:
// an order still waiting with its price, the night's close, the stop and the one take-profit its plan works from that price
// and its last session; and an order that left the list in the last five trading days, bought at its fill or cancelled at
// its last session. A stock a rule kept with no plan waits on the next open in the words its account stored.
// see: A trade under a plan names the one exit its walk sells by on every page, read from its plan and never from a stored target
public static class RevisionScreen
{
    // The key an earlier revision's lists are kept under.
    public static string EarlierKey(string account, int revision) => account + "#" + revision.ToString(System.Globalization.CultureInfo.InvariantCulture);

    public static PaperPageView WithOrders(
        PaperPageView page,
        IReadOnlyList<PaperAccountSource> sources,
        IReadOnlyList<PlannedTradeRow> planned,
        DateOnly night,
        IReadOnlyList<DateOnly> sessions,
        Func<string, DateOnly, decimal?> close,
        Func<string, string?> company)
    {
        var waiting = new Dictionary<string, List<WaitingRowView>>(StringComparer.Ordinal);
        var left = new Dictionary<string, List<LeftOrderView>>(StringComparer.Ordinal);
        var profitFrom = new Dictionary<string, string>(StringComparer.Ordinal);
        DateOnly[] recent = [.. sessions.Where(session => session <= night).OrderDescending().Take(LeftOrderView.Sessions)];
        var since = recent.Length > 0 ? recent.Min() : night;

        foreach (var account in sources.Where(one => one.Standing != PaperAccountView.You))
        {
            var owner = account.Listed is { } listed && !listed.StartsWith(ReadApi.PageList + " ", StringComparison.Ordinal) ? listed : account.Words;
            var rows = planned.Where(row => string.Equals(row.Owner, owner, StringComparison.Ordinal) && row.Listed <= night).ToArray();
            var covered = new HashSet<(string, DateOnly)>();

            string KeyOf(DateOnly placed) =>
                account.Since is { } start && placed >= start || account.Revisions.Count == 0
                    ? account.Key
                    : account.Revisions.LastOrDefault(revision => revision.From <= placed) is { } earlier ? EarlierKey(account.Key, earlier.Number) : account.Key;

            string RevisionAt(DateOnly placed) => PaperBooks.VersionAt(account.Revisions, placed);

            foreach (var row in rows)
            {
                covered.Add((row.Ticker, row.Listed));
                var last = PlannedTrades.LastDay(row.Listed, row.Order.Window);

                if (row.Fill is not null && row.EndedOn is null && PlannedTrades.TakeProfitFromWords(row) is { } placedBy)
                {
                    profitFrom[ProfitKey(KeyOf(row.Listed), row.Ticker, row.Listed)] = placedBy;
                }

                // An order that left the list unbought, whatever its step stored: its plan placed no price, its revision was
                // replaced, or its window passed.
                if (LeftUnbought(row, night, sessions) is { } gone)
                {
                    if (gone.On >= since && gone.On <= night)
                    {
                        Add(left, KeyOf(row.Listed), new LeftOrderView(row.Ticker, gone.On, false, gone.Words, RevisionAt(row.Listed)));
                    }
                }
                else if (PlannedTrades.IsWaiting(row))
                {
                    var exit = PlannedTrades.ExitOf(row);

                    Add(waiting, KeyOf(row.Listed), new WaitingRowView(
                        row.Ticker,
                        company(row.Ticker),
                        row.Listed,
                        PlannedTrades.Price(row),
                        close(row.Ticker, night),
                        PlannedTrades.Price(row) is null ? null : PlannedTrades.StopOf(row),
                        exit?.First is { } first ? Statistic.ToPrice(first) : null,
                        exit?.FirstIsPart ?? false,
                        row.Order.Level is null ? null : last,
                        row.Order.Level is null ? null : ExchangeClosures.SessionsUntil(night, last),
                        row.Order.Level is null ? EquityBrief.Core.Cards.PaperAccount.Waiting : row.Order.PlacesNoPrice() ? PlanOrderWords.NotFilledOf(row.Order) : null,
                        RevisionAt(row.Listed)) { TakeProfitFrom = PlannedTrades.TakeProfitFromWords(row) });
                }
                else if (row.FilledOn is { } filled && filled >= since && filled <= night && row.Fill is { } fill && PlannedTrades.BuysAtAPrice(row))
                {
                    Add(left, KeyOf(row.Listed), new LeftOrderView(row.Ticker, filled, true, LeftOrderView.BoughtWords(row.Ticker, filled, fill, PlannedTrades.Price(row)), RevisionAt(row.Listed)));
                }
            }

            // A stock kept with no plan, to buy at the next open, as its account stored it.
            foreach (var (ticker, placed, why) in account.Unfilled ?? [])
            {
                if (covered.Contains((ticker, placed)) || placed > night)
                {
                    continue;
                }

                Add(waiting, KeyOf(placed), new WaitingRowView(ticker, company(ticker), placed, null, close(ticker, night), null, null, false, null, null, why, RevisionAt(placed)));
            }
        }

        return page with
        {
            Waiting = waiting.ToDictionary(pair => pair.Key, pair => (IReadOnlyList<WaitingRowView>)[.. pair.Value.OrderBy(row => row.AboveBuy is null ? 1 : 0).ThenBy(row => row.AboveBuy).ThenBy(row => row.Placed).ThenBy(row => row.Ticker, StringComparer.Ordinal)], StringComparer.Ordinal),
            Left = left.ToDictionary(pair => pair.Key, pair => (IReadOnlyList<LeftOrderView>)[.. pair.Value.OrderByDescending(row => row.On).ThenBy(row => row.Ticker, StringComparer.Ordinal)], StringComparer.Ordinal),
            ProfitFrom = profitFrom,
        };
    }

    // The key an open trade's take-profit words are kept under: its account's key, its stock and the night that listed it.
    public static string ProfitKey(string account, string ticker, DateOnly listed) => PaperPageView.ProfitKey(account, ticker, listed);

    // The running rules' orders and trades that moved in the last five trading days, newest first, which Home and Tonight draw
    // under their waiting cards: each order bought at its fill, or left unbought with why; and from the operator's correction
    // of 2026-11-24, item 5, every live rule's buy at the open and every sale, each revision's, the Fundamentals family's
    // among them, read from the same trades as Paper trading.
    // see: Home's left notes name every live rule's buy and sale and every order that left unbought, with why
    public static IReadOnlyList<LeftOrderView> LeftOf(IReadOnlyList<PlannedTradeRow> planned, DateOnly night, IReadOnlyList<DateOnly> sessions, IReadOnlyList<PaperAccountSource>? accounts = null)
    {
        DateOnly[] recent = [.. sessions.Where(session => session <= night).OrderDescending().Take(LeftOrderView.Sessions)];
        var since = recent.Length > 0 ? recent.Min() : night;
        var left = new List<LeftOrderView>();
        var noted = new HashSet<(string Ticker, DateOnly Bought)>();

        foreach (var row in planned.Where(row => row.Live).GroupBy(row => (row.Family, row.Ticker, row.Listed)).Select(group => group.First()))
        {
            if (row.FilledOn is { } filled && row.Fill is { } fill && PlannedTrades.BuysAtAPrice(row))
            {
                noted.Add((row.Ticker, filled));

                if (filled >= since && filled <= night)
                {
                    left.Add(new LeftOrderView(row.Ticker, filled, true, LeftOrderView.BoughtWords(row.Ticker, filled, fill, PlannedTrades.Price(row)), string.Empty)
                    {
                        Family = row.Family,
                        Listed = row.Listed,
                        Price = fill,
                        Result = BoughtResult(fill, PlannedTrades.Price(row), row.EndedOn is { } ended && ended <= night),
                    });
                }
            }
            else if (LeftUnbought(row, night, sessions) is { } gone && gone.On >= since && gone.On <= night)
            {
                var unplaced = row.Order.PlacesNoPrice();

                left.Add(new LeftOrderView(row.Ticker, gone.On, false, gone.Words, string.Empty)
                {
                    Family = row.Family,
                    Listed = row.Listed,
                    Unplaced = unplaced,
                    Price = unplaced ? null : PlannedTrades.Price(row),
                    Result = Why(row.Ticker, gone.Words),
                });
            }
        }

        foreach (var account in (accounts ?? []).Where(one => one.Standing is PaperAccountView.Live))
        {
            foreach (var (trade, version) in account.Trades.Select(one => (one.Trade, (string?)null)).Concat((account.Earlier ?? []).Select(one => (one.Trade, (string?)one.Version))))
            {
                var listed = trade.Listed ?? trade.Bought;

                if (trade.Bought >= since && trade.Bought <= night && noted.Add((trade.Ticker, trade.Bought)))
                {
                    var soldSince = trade.Ended is { } sold && sold <= night;

                    left.Add(new LeftOrderView(trade.Ticker, trade.Bought, true, LeftOrderView.BoughtAtOpenWords(trade.Ticker, trade.Bought, trade.Buy, soldSince), version ?? string.Empty)
                    {
                        Family = account.Family,
                        Listed = listed,
                        Price = trade.Buy,
                        Result = "At the open. " + (soldSince ? "Sold since." : "Now under Open trades."),
                    });
                }

                if (trade.Ended is { } ended && ended >= since && ended <= night && trade.Sale is { } sale)
                {
                    left.Add(new LeftOrderView(trade.Ticker, ended, false, LeftOrderView.SoldWords(trade.Ticker, ended, sale, trade.Buy), version ?? string.Empty)
                    {
                        Family = account.Family,
                        Listed = listed,
                        Sold = true,
                        Price = sale,
                        Result = trade.Buy > 0m ? FormattableString.Invariant($"{(sale / trade.Buy - 1m) * 100m:+0.0;-0.0;0.0}% on its buy of ${trade.Buy:N2}.") : "Sold.",
                    });
                }
            }
        }

        return [.. left.OrderByDescending(row => row.On).ThenBy(row => row.Ticker, StringComparer.Ordinal)];
    }

    // An order's buy in words: under its buy price at the open, at its buy price, or at the open with none; and whether it has
    // sold since.
    static string BoughtResult(decimal fill, decimal? price, bool soldSince) =>
        (price is { } level && fill < level
            ? FormattableString.Invariant($"Opened under its buy price of ${level:N2}.")
            : price is { } at ? FormattableString.Invariant($"The price fell to its buy price of ${at:N2}.") : "At the open.")
        + (soldSince ? " Sold since." : " Now under Open trades.");

    // Why an order left unbought, its stock and the word before the colon taken off: "Not bought by its last day, 21 Dec.".
    static string Why(string ticker, string words)
    {
        var said = words.StartsWith(ticker + " ", StringComparison.Ordinal) ? words[(ticker.Length + 1)..] : words;
        var colon = said.IndexOf(": ", StringComparison.Ordinal);
        var reason = colon >= 0 && colon < 20 ? said[(colon + 2)..] : said;

        return reason.Length == 0 ? reason : char.ToUpperInvariant(reason[0]) + reason[1..];
    }

    // Why an order of a live rule's plan left the waiting list unbought, and on which session; none while it waits and once it
    // is bought: its plan placed no price, on its own night; its revision was replaced, on the session its fill row said so;
    // or its window passed, at its last session.
    public static (DateOnly On, string Words)? LeftUnbought(PlannedTradeRow row, DateOnly night, IReadOnlyList<DateOnly> sessions)
    {
        if (row.Fill is not null)
        {
            return null;
        }

        if (row.Order.PlacesNoPrice())
        {
            return (row.Listed, row.Ticker + " not placed: " + PlanOrderWords.NotFilledOf(row.Order));
        }

        if (row.NotFilled && PlanOrderWords.IsReplaced(row.Why))
        {
            var written = row.WhyOn is { } on && on < night ? on : night;

            return (sessions.Where(session => session <= written).DefaultIfEmpty(written).Max(), row.Ticker + " " + row.Why + ".");
        }

        if (row.NotFilled)
        {
            var last = PlannedTrades.LastDay(row.Listed, row.Order.Window);

            return (last, LeftOrderView.CancelledWords(row.Ticker, last));
        }

        return null;
    }

    // A stock's open trades and waiting orders under its index's rules' plans, every revision's, as the top of its page names
    // them: the revision that placed each, its price, stop and one take-profit, its days left and where it stands tonight.
    public static IReadOnlyList<NameTradeView> NameTrades(string ticker, IReadOnlyList<PlannedTradeRow> planned, IReadOnlyList<CandidateRow> register, bool large, string indexName, DateOnly night, decimal? close)
    {
        var rows = new List<NameTradeView>();
        using var reading = PaperBooks.ReadingNight(night);

        foreach (var row in planned.Where(one => one.Live && one.Ticker == ticker && one.Listed <= night && !one.NotFilled && (one.Fill is null || one.EndedOn is null)).GroupBy(one => (one.Family, one.Listed)).Select(group => group.First()))
        {
            var lineage = RuleRegister.Registrations(row.Family, large, indexName, register).Where(one => EquityBrief.Core.Candidates.FamilyRecords.IsLive(one.Candidate)).ToArray();
            var revision = PaperBooks.RevisionViews(lineage).LastOrDefault(one => one.From <= row.Listed);
            var waiting = row.Fill is null;
            var stop = PlannedTrades.StopOf(row);
            var take = PlannedTrades.TakeProfitOf(row);
            var price = waiting ? PlannedTrades.Price(row) : row.Fill;
            int? left = waiting
                ? ExchangeClosures.SessionsUntil(night, PlannedTrades.LastDay(row.Listed, row.Order.Window))
                : row.FilledOn is { } bought && EquityBrief.Core.Cards.PaperAccount.HeldDays(bought, night) is { } held ? Math.Max(0, row.Cap - held) : null;
            string Away(decimal? to, string what) =>
                close is { } now && to is { } at && now > 0m ? FormattableString.Invariant($"{Math.Abs(Statistic.FromRatio((at / now) - 1m)) * 100:0.0}% {(at >= now ? "above" : "below")} the last close, {what}") : string.Empty;
            var standing = waiting
                ? price is { } buy && close is { } last && buy > 0m ? FormattableString.Invariant($"The last close stands {Math.Abs(Statistic.FromRatio((last / buy) - 1m)) * 100:0.0}% {(last >= buy ? "above" : "below")} its buy price.") : "It buys at the next open."
                : string.Join("; ", new[] { Away(stop, "its stop"), Away(take, "its take-profit") }.Where(part => part.Length > 0)) is { Length: > 0 } said ? Capitalise(said) + "." : "Held.";

            rows.Add(new NameTradeView(row.Family, revision?.Tag ?? "before its rule was first registered", waiting, row.Listed, row.FilledOn, price, stop, take, left, close, standing));
        }

        return rows;
    }

    static string Capitalise(string words) => words.Length == 0 ? words : char.ToUpperInvariant(words[0]) + words[1..];

    // Each running rule's plan rows by its account's key, and each earlier revision's by the key and its number, every
    // variant's by its own key.
    public static IReadOnlyDictionary<string, PlanRowsView> Plans(IReadOnlyList<PaperAccountSource> sources, IReadOnlyList<CandidateRow> register, string index)
    {
        var plans = new Dictionary<string, PlanRowsView>(StringComparer.Ordinal);

        foreach (var source in sources.Where(one => one.Standing != PaperAccountView.You))
        {
            var standing = register.LastOrDefault(row => row.Event == EquityBrief.Core.Candidates.CandidateFamily.Registered && string.Equals(row.Candidate, source.Words, StringComparison.Ordinal));

            var rows = PlanRows(source.Family, index, standing);

            plans[source.Key] = source.Standing == PaperAccountView.Live && standing is not null && Planned(standing) ? rows with { Profit = rows.Profit + ChosenExit } : rows;

            foreach (var revision in source.Revisions.Take(Math.Max(0, source.Revisions.Count - 1)))
            {
                plans[EarlierKey(source.Key, revision.Number)] = PlanRows(source.Family, index, register.FirstOrDefault(row => row.Id == revision.Registration));
            }
        }

        return plans;
    }

    // Whether a registration states a plan the test of its buy and sell points chose.
    static bool Planned(CandidateRow row)
    {
        try
        {
            return EquityBrief.Core.Loop.PlanHook.Of(EquityBrief.Core.Candidates.CandidateEvaluator.Read(row.Parameters)) is not null;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    // A revision's plan in five plain rows, read from its registration: what it looks for, how it buys, when it sells at a
    // loss, when it takes profit and its time limit, the take-profit row naming the one way its trades take profit.
    // see: A trade under a plan names the one exit its walk sells by on every page, read from its plan and never from a stored target
    public static PlanRowsView PlanRows(string family, string index, CandidateRow? row)
    {
        var parameters = row is null ? new Dictionary<string, double>() : EquityBrief.Core.Candidates.CandidateEvaluator.Read(row.Parameters);
        var chips = row is null ? [] : RuleRegister.ChipsOf(row);
        EquityBrief.Core.Loop.PlanCombination? plan;

        try
        {
            plan = EquityBrief.Core.Loop.PlanHook.Of(parameters);
        }
        catch (ArgumentException)
        {
            plan = null;
        }
        var looksFor = LooksFor(family);
        string? Chip(string key) => chips.FirstOrDefault(chip => chip.Key == key)?.Words;

        if (family is EquityBrief.Core.Families.HeavyweightRule.Name or EquityBrief.Core.Families.QualityRule.Name or EquityBrief.Core.Families.ImprovingRule.Name)
        {
            var exit = EquityBrief.Core.Loop.BookExits.Of(family, index);

            return new PlanRowsView(
                looksFor,
                "At the open the day after it is chosen.",
                FormattableString.Invariant($"At the next open after a close {exit.Moves:0.##} typical daily moves under the price it paid."),
                exit.Half
                    ? FormattableString.Invariant($"Half at the next open after a close {exit.Multiple:0.##} times the risk above the price it paid, its stop then moved up to that price; the other half when it leaves the holdings.")
                    : exit.Placing == EquityBrief.Core.Loop.TakeProfitPlacing.Turned
                        ? FormattableString.Invariant($"At the next open after a close at the nearest price above the one it paid where it turned down before, or {exit.Multiple:0.##} times the risk above where none stands.")
                        : exit.Placing == EquityBrief.Core.Loop.TakeProfitPlacing.Beyond
                            ? FormattableString.Invariant($"At the next open after a close at the first price it turned down at {exit.Multiple:0.##} times the risk or more above the price it paid, or {exit.Multiple:0.##} times the risk where none stands.")
                            : FormattableString.Invariant($"At the next open after a close {exit.Multiple:0.##} times the risk above the price it paid."),
                family == EquityBrief.Core.Families.ImprovingRule.Name
                    ? "Otherwise it sells when a filing drops it below its hold score, or after " + (Chip(EquityBrief.Core.Candidates.ImprovingCandidate.HoldParameter) ?? "its sessions") + "."
                    : "Otherwise it sells when it no longer qualifies at the reshuffle.");
        }

        var cap = family == EquityBrief.Core.Families.DipRule.Name ? null : parameters.TryGetValue(EquityBrief.Core.Candidates.IndexRuleCandidate.HoldParameter, out var hold) && hold > 0 ? (int?)hold : EquityBrief.Core.Families.SetupFamilies.Named(family)?.CapSessions;
        var limit = family == EquityBrief.Core.Families.DipRule.Name
            ? "Otherwise it sells at the close of its last day: " + (Chip("limit") ?? "its time limit") + "."
            : FormattableString.Invariant($"Otherwise it sells after {cap} trading days.");

        if (plan is null)
        {
            return new PlanRowsView(
                looksFor,
                "At the open the day after it is picked.",
                family == EquityBrief.Core.Families.DipRule.Name
                    ? "At the next open after a close under its stop: " + (Chip("stop") ?? "its own stop") + "."
                    : "At the next open after a close under the stop its setup placed when it was picked.",
                family == EquityBrief.Core.Families.DipRule.Name
                    ? "At the next open after " + (Chip("exit") ?? "its own exit") + "."
                    : "At the next open after a close at the price its setup placed to take profit at when it was picked" + (Chip("target") is { } target ? ", " + target.Replace("target ", string.Empty, StringComparison.Ordinal) : string.Empty) + ".",
                limit);
        }

        return new PlanRowsView(looksFor, Buys(plan), Loss(plan), Profit(plan, family, Chip("exit")), limit);
    }

    // What a family's rule looks for, in one plain sentence naming no setting and no sale: its plan's rows say how it buys and
    // sells, and a variant's sentence what it changes.
    public static string LooksFor(string family) => family switch
    {
        EquityBrief.Core.Families.SetupFamilies.Pullback => "A strong stock in an uptrend dips to a price band where it bounced before, and turns back up.",
        EquityBrief.Core.Families.BreakoutRule.Name => "A stock closes above its highest price of the past months on heavy volume, after its daily ranges narrowed.",
        EquityBrief.Core.Families.DriftRule.Name =>"A company beats its earnings estimate and the stock closes up on heavy volume, and it is bought within a few trading days while it holds.",
        EquityBrief.Core.Families.DipRule.Name => "A stock in an uptrend falls sharply over a few days, to be sold as it bounces back.",
        EquityBrief.Core.Families.HeavyweightRule.Name => "Each month, the largest companies of each sector whose returns beat their sector's fund by the most, held while they lead.",
        EquityBrief.Core.Families.QualityRule.Name => EquityBrief.Core.Families.QualityRule.Description,
        EquityBrief.Core.Families.ImprovingRule.Name => EquityBrief.Core.Families.ImprovingRule.Description,
        _ => "The stocks its rule picks.",
    };

    static string Buys(EquityBrief.Core.Loop.PlanCombination plan)
    {
        var within = FormattableString.Invariant($"for up to {plan.Window} trading days. If it opens below that price, it buys at the open.");

        return plan.Entry == EquityBrief.Core.Loop.PlanPoints.NextOpen ? "At the open the day after it is picked."
            : plan.Entry == EquityBrief.Core.Loop.PlanPoints.FirstPullback ? FormattableString.Invariant($"At the open after its first close under the close before, within {plan.Window} trading days.")
            : plan.Entry == EquityBrief.Core.Loop.PlanPoints.Average ? "Waits for the price to fall to its 20-day average, " + within
            : EquityBrief.Core.Loop.PlanPoints.DistanceOf(plan.Entry) is { } days ? FormattableString.Invariant($"Waits for the price to fall {days:0.##} typical daily moves under the close it was picked at, ") + within
            : "Waits for the price to fall into " + plan.Entry.Replace("the ", "the top of the ", StringComparison.Ordinal) + " of prices where it bounced before, " + within;
    }

    static string Loss(EquityBrief.Core.Loop.PlanCombination plan) =>
        plan.Stop == EquityBrief.Core.Loop.PlanPoints.RuleStop ? "At the next open after a close under the stop its setup placed when it was picked."
        : plan.Stop == EquityBrief.Core.Loop.PlanPoints.ZoneStop ? "At the next open after a close under the bottom of the zone it bought in."
        : "At the next open after a close " + plan.Stop.Replace("typical days under the buy", "typical daily moves under the price it paid", StringComparison.Ordinal).Replace("typical day under the buy", "typical daily move under the price it paid", StringComparison.Ordinal) + ".";

    static string Profit(EquityBrief.Core.Loop.PlanCombination plan, string family, string? dipExit)
    {
        var sale = plan.Exit == EquityBrief.Core.Loop.PlanPoints.RuleSale
            ? family == EquityBrief.Core.Families.DipRule.Name ? "after " + (dipExit ?? "its own exit") : "a close at the price its setup placed when it was picked"
            : plan.Exit == EquityBrief.Core.Loop.PlanPoints.FirstBand ? "a close at the first price band above the price it buys at where the stock turned down before, read on the night it was picked"
            : plan.Exit == EquityBrief.Core.Loop.PlanPoints.StagedSales ? "a close at each of the levels above the price it paid, a part at each"
            : null;

        if (plan.TakeProfit is not { } multiple)
        {
            return sale is null ? "It names no price: it sells on its stop or its time limit." : "At the next open after " + sale + ".";
        }

        var taking = FormattableString.Invariant($"{multiple:0.##} times the risk above the price it paid");

        if (plan.Half)
        {
            return "Half at the next open after a close " + taking + ", its stop then moved up to that price, where that comes before "
                + (sale ?? "its time limit") + "; the rest " + (sale is null ? "on its time limit" : "at the next open after " + sale) + ".";
        }

        var placed = plan.Placing switch
        {
            EquityBrief.Core.Loop.TakeProfitPlacing.Risk => "a close " + taking,
            EquityBrief.Core.Loop.TakeProfitPlacing.Beyond => FormattableString.Invariant($"a close at the first price it turned down at {multiple:0.##} times the risk or more above the price it paid, or {multiple:0.##} times the risk where none stands"),
            _ => "a close at the nearest price above the one it paid where it turned down before, or " + taking + " where none stands",
        };

        return sale is null || plan.Exit == EquityBrief.Core.Loop.PlanPoints.TimeLimit
            ? "At the next open after " + placed + "."
            : "At the next open after " + sale + ". Where that night read no such price above it, at the next open after " + placed + ".";
    }

    // The sentence beneath a running revision's take-profit row: the take-profit is the one the test of the plan's buy and sell
    // points chose on the long ledger's years.
    public static string ChosenExit { get; } = FormattableString.Invariant($" The test of its plan's buy and sell points chose this take-profit on the years {EquityBrief.Core.History.LongLedger.From.Year} to {EquityBrief.Core.History.LongLedger.Through.Year}.");

    static void Add<T>(Dictionary<string, List<T>> into, string key, T row)
    {
        if (!into.TryGetValue(key, out var list))
        {
            into[key] = list = [];
        }

        list.Add(row);
    }
}
