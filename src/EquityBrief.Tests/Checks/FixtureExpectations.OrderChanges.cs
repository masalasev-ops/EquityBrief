using System.Net;
using EquityBrief.Core.Cards;
using EquityBrief.Core.Loop;
using EquityBrief.Core.Paper;
using EquityBrief.Core.Providers;
using EquityBrief.Core.Time;
using EquityBrief.Tests.Harness;
using EquityBrief.Worker.Paper;

namespace EquityBrief.Tests.Checks;

// fixture-expectations, 24.47: the night's order changes for a constructed night on the S&P 500, every list worked by hand
// from the paper account's rows, none tonight under an empty one; a waiting card's fill odds over a constructed set of past
// orders tallied by hand, said at 30 and too few under it; the 4:30 pm check listing a constructed fill, writing every order
// with its low and sending nothing with no topic saved; and the topic kept out of every error, line and log row.
// see: Each night ends with the order changes for every live rule, book and the AI stock picker, on Paper trading and in one phone message
// see: A waiting order's fill odds are how often the ledger's past orders of its rule this far away with as long left filled before their last day
// see: The fill check asks EODHD's live quotes after the close on its own run, one request a page of symbols
// see: Phone messages go through ntfy on a topic only the secrets file holds and carry tickers, prices and dates alone
public partial class FixtureExpectations
{
    internal static string[] OrderChangeClaims =>
    [
        CheckReach.Key(Scope.LimitsTable, "Order changes"),
        CheckReach.Key(Scope.LimitsTable, "Fill odds"),
        CheckReach.Key(Scope.LimitsTable, "The fill check"),
    ];

    internal static string[] OrderChangeRows =>
    [
        .. ComponentAccess.OrderChangesRows,
        .. SchemaColumns.OrderChangesStoreRows,
        .. OrderChangeClaims,
        .. Reading.ReadSurface.OrderChangesPageRows,
    ];

    static readonly DateOnly ChangeNight = new(2026, 11, 25);

    static readonly DateOnly ChangePrevious = new(2026, 11, 24);

    // A live pullback's order at a price on the S&P 500: listed on a night at its level, waiting three sessions, placed with a
    // typical daily move of 2.
    static PlannedTradeRow Ordered(string ticker, DateOnly listed, double level, DateOnly? filledOn = null, decimal? fill = null, bool notFilled = false) =>
        new("the live Bounce in an uptrend on the S&P 500", "GSPC", "pullback", ticker, listed, 1, (decimal)level + 5, null, null, null, 10,
            new PlanOrder("the average within 3 sessions", PlanPoints.Average, level, 3, PlanPoints.TypicalStop(2), level - 4, null, 2, null, [], null, null, 10),
            filledOn, fill, notFilled, null, null, null);

    static IndexOrders ConstructedNight() => new(
        "GSPC",
        "S&P 500",
        [
            // Placed tonight at 50: a new order, its last day the third session after, Tue 1 Dec past Thanksgiving.
            Ordered("AAA", ChangeNight, 50),
            // Its window passed with no fill tonight.
            Ordered("BBB", new DateOnly(2026, 11, 20), 40, notFilled: true) with { Why = "its window passed with no fill", WhyOn = ChangeNight },
            // Waiting at 100 with tonight's close 101.20: 0.6 typical daily moves above, so it could fill tomorrow.
            Ordered("CCC", ChangePrevious, 100),
            // Waiting at 100 with tonight's close 103: 1.5 moves above, past the one move, so left out.
            Ordered("DDD", ChangePrevious, 100),
        ],
        [
            new AccountOrders(
                "New high on the S&P 500",
                false,
                [
                    // Bought today at 20, its stop at 19 and its take-profit at 22.
                    new PaperTrade("EEE", ChangeNight, 20m, 19m, null, null, null, 1, Target: 22m),
                    // Bought on 10 Nov at 30 and sold today at 33, its take-profit: 10% up.
                    new PaperTrade("FFF", new DateOnly(2026, 11, 10), 30m, 28m, ChangeNight, 33m, null, 1, Target: 33m),
                    // Held and not sold: in no list.
                    new PaperTrade("KKK", new DateOnly(2026, 11, 10), 30m, 28m, null, null, null, 2, Target: 36m),
                ],
                [
                    // Kept tonight for the next open: a new order.
                    ("GGG", ChangeNight, PaperAccount.Waiting),
                    // Kept last night, and today's fill step bought none of it.
                    ("HHH", ChangePrevious, "not bought: opened above its range"),
                ]),
        ],
        [
            // The picker's pick of tonight in its range.
            new PickerOrder("PPP", ChangeNight, new BuyRange(102, 96), null, null, null),
            // Its pick of last night, which opened below its range today.
            new PickerOrder("QQQ", ChangePrevious, new BuyRange(51, 48), null, BuyRanges.Below, ChangeNight),
        ],
        new Dictionary<string, decimal>(StringComparer.Ordinal) { ["CCC"] = 101.2m, ["DDD"] = 103m });

    // Worked by hand: each list in its order with the words its rows give, the waiting orders within one typical daily move
    // listed nearest first and the one past it left out.
    [Fact]
    public void TheNightsOrderChangesListEachChangeFromThePaperAccountsOwnRowsAndNoneTonightUnderAnEmptyList()
    {
        var summary = OrderChanges.Of(ChangeNight, ChangePrevious, [ConstructedNight()]);

        Assert.Equal(
            [
                "AAA, Bounce in an uptrend on the S&P 500: buy at $50.00, last day Tue 1 Dec",
                "GGG, New high on the S&P 500: buy at the next open",
                "PPP, the AI stock picker on the S&P 500: buy between $96.00 and $102.00, at the open or on a dip back to its top within 2 days",
            ],
            summary.Of(OrderChanges.NewOrders).Select(change => change.Words));
        Assert.Equal(["EEE, New high on the S&P 500: bought at $20.00; set the stop at $19.00 and the take-profit at $22.00"], summary.Of(OrderChanges.Filled).Select(change => change.Words));
        Assert.Equal(["FFF, New high on the S&P 500: sold at $33.00 on its take-profit, +10.0%"], summary.Of(OrderChanges.Sold).Select(change => change.Words));
        Assert.Equal(
            [
                "BBB, Bounce in an uptrend on the S&P 500: its window passed with no fill",
                "HHH, New high on the S&P 500: not bought: opened above its range",
                "QQQ, the AI stock picker on the S&P 500: not bought: opened below its range",
            ],
            summary.Of(OrderChanges.Ended).Select(change => change.Words));
        Assert.Equal(["CCC, Bounce in an uptrend on the S&P 500: buy at $100.00, last close $101.20, 0.6 typical daily moves above"], summary.Of(OrderChanges.CouldFill).Select(change => change.Words));

        // An empty night says none tonight under every heading, and no check's list without a check.
        var empty = OrderChanges.Sections(OrderChanges.Of(ChangeNight, ChangePrevious, []));

        Assert.Equal(OrderChanges.Lists.Select(list => list.Heading), empty.Select(section => section.Heading));
        Assert.All(empty, section => Assert.Equal([OrderChanges.NoneTonight], section.Lines));

        // The message: the night, then each list, tickers, prices and dates and no account's size.
        var message = OrderChanges.Message(summary);

        Assert.StartsWith("Order changes, Wed 25 Nov\n\nNew orders: place these\n- AAA, Bounce in an uptrend on the S&P 500: buy at $50.00, last day Tue 1 Dec\n", message, StringComparison.Ordinal);
        Assert.DoesNotContain("shares", message, StringComparison.Ordinal);

        // Where the 4:30 pm check ran, the order it read filled that the night did not fill is named, and with none read the
        // list says none tonight.
        var checkedNight = OrderChanges.Of(ChangeNight, ChangePrevious, [ConstructedNight()], [new FillCheckRow("GSPC", "Bounce in an uptrend on the S&P 500", "CCC", ChangePrevious, 100m, 99.8m)]);

        Assert.Equal(["CCC, Bounce in an uptrend on the S&P 500: the 4:30 pm check read its low at $99.80, at or under its buy at $100.00, and tonight's bars did not fill it"], checkedNight.Of(OrderChanges.Differs).Select(change => change.Words));
        Assert.Equal(OrderChanges.DiffersHeading, OrderChanges.Sections(checkedNight)[^1].Heading);

        // From the 24.47 correction of 2026-11-28 a fill with no take-profit says how it sells: a breakout's stop raised as the
        // price climbs, the dip's own exit signal, and a time limit only where neither is set.
        var filled = new PaperTrade("KKK", ChangeNight, 50m, 47m, null, null, null, 1);

        Assert.Equal("set the stop at $47.00, raised as the price climbs and never lowered, with no take-profit", OrderChanges.Exits(filled with { Trail = 3m }));
        Assert.Equal("set the stop at $47.00; it sells on its own exit signal, with no take-profit", OrderChanges.Exits(filled with { Exit = 65 }));
        Assert.Equal("set the stop at $47.00; it sells by its time limit, with no take-profit", OrderChanges.Exits(filled));
    }

    // Worked by hand: a past order waiting at 100 placed with a typical move of 2, its night closing at 101, half a move above
    // and three days left, in band 1; one filling on its second session counts filled from both nights it waited, and one never
    // filling counts unfilled from all three. Twenty that filled and ten that did not put 30 in the cell (1, 3), 20 filled.
    [Fact]
    public void AWaitingCardsFillOddsAreHowOftenItsRulesPastOrdersThisFarAwayWithAsLongLeftFilledAndTooFewUnderThirty()
    {
        double[] filling = [101, 101.5, 99.5, 99];
        double[] never = [101, 102, 103, 104];

        var filled = FillOdds.States(filling, 0, 100, 2, 3, 2);

        Assert.Equal([new OrderState(0.5, 3, true), new OrderState(0.75, 2, true)], filled);
        Assert.Equal([new OrderState(0.5, 3, false), new OrderState(1, 2, false), new OrderState(1.5, 1, false)], FillOdds.States(never, 0, 100, 2, 3, null));

        var states = Enumerable.Range(0, 20).SelectMany(_ => filled).Concat(Enumerable.Range(0, 10).SelectMany(_ => FillOdds.States(never, 0, 100, 2, 3, null))).ToArray();
        var cells = FillOdds.Tally(states);
        var cell = FillOdds.CellOf(cells, 0.5, 3);

        Assert.Equal(new FillOddsCell(1, 3, 30, 20), cell);
        Assert.Equal(
            "1.0% above its buy price, 3 trading days left. Past orders this far away with this long left were filled 67% of the time (30 orders). Its rule's past orders, not this stock's chance.",
            FillOdds.Line(101m, 100m, 3, cell));

        // Under 30 says too few, and no cell says the same.
        Assert.Equal("1.0% above its buy price, 3 trading days left. Too few past orders to say how often they filled.", FillOdds.Line(101m, 100m, 3, cell! with { Orders = 29 }));
        Assert.Equal("1.0% above its buy price, 3 trading days left. Too few past orders to say how often they filled.", FillOdds.Line(101m, 100m, 3, null));
        Assert.Equal("Bought on 25 Nov at $99.50", FillOdds.BoughtLine(ChangeNight, 99.5m));
    }

    // Over a constructed store: two orders waiting on a trading day, AAA's low at 49.50 under its buy of 50 and BBB's at 81 over
    // its 80; the check lists AAA alone, writes both with their lows, sends nothing with no topic saved, and asks nothing on a
    // day the exchange is closed; and the reading instant is half past four in New York in winter and in summer.
    [Fact]
    public async Task TheFillCheckListsAConstructedFillWritesEveryOrderWithItsLowAndSendsNothingWithNoTopicSaved()
    {
        using var store = new TemporaryStore().Migrated();
        var clock = FixedClock.At(new DateTimeOffset(2026, 11, 25, 21, 30, 0, TimeSpan.Zero), SessionZones.UnitedStates);
        var feed = new RecordedLows([new DayLow("AAA", new DateTimeOffset(2026, 11, 25, 21, 0, 0, TimeSpan.Zero), 49.5m), new DayLow("BBB", new DateTimeOffset(2026, 11, 25, 21, 0, 0, TimeSpan.Zero), 81m)]);
        var handler = new RecordedNtfy(HttpStatusCode.OK);
        var phone = new NtfyMessages(new HttpClient(handler) { BaseAddress = new Uri(NtfyMessages.Address) }, null);
        var output = new StringWriter();
        FillCheck.Waiting[] waiting = [new("GSPC", "Bounce in an uptrend on the S&P 500", "AAA", ChangePrevious, 50m), new("GSPC", "New high on the S&P 500", "BBB", ChangePrevious, 80m)];

        Assert.Equal(0, await new FillCheck(clock, store.DatabaseFile, feed, phone).RunAsync(ChangeNight, waiting, true, output));

        Assert.Contains("- AAA, Bounce in an uptrend on the S&P 500: low $49.50, at or under its buy at $50.00", output.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("- BBB", output.ToString(), StringComparison.Ordinal);
        Assert.Equal("AAA 49.5, BBB 81", (string)One(store, "SELECT group_concat(ticker || ' ' || low, ', ') FROM (SELECT ticker, low FROM fill_check ORDER BY ticker);")!);
        Assert.Contains("no message sent: no ntfy topic is saved", (string)One(store, "SELECT detail FROM run_log WHERE stage = 'fill-check';")!, StringComparison.Ordinal);
        Assert.Equal(0, handler.Sent);
        Assert.Equal(1, feed.Requests);
        Assert.Throws<Microsoft.Data.Sqlite.SqliteException>(() => store.Execute("DELETE FROM fill_check;"));

        // Thanksgiving: no trading day, nothing asked.
        Assert.Equal(0, await new FillCheck(clock, store.DatabaseFile, feed, phone).RunAsync(new DateOnly(2026, 11, 26), waiting, true, TextWriter.Null));
        Assert.Equal(1, feed.Requests);

        // Half past four in New York: 21:30 UTC in winter and 20:30 UTC in summer.
        var zone = SessionZones.UnitedStates;

        Assert.Equal(new DateTimeOffset(2026, 11, 25, 21, 30, 0, TimeSpan.Zero), FillCheck.ReadingAt(ChangeNight, TimeZoneInfo.FindSystemTimeZoneById(zone)));
        Assert.Equal(new DateTimeOffset(2026, 6, 15, 20, 30, 0, TimeSpan.Zero), FillCheck.ReadingAt(new DateOnly(2026, 6, 15), TimeZoneInfo.FindSystemTimeZoneById(zone)));
    }

    // The topic, a constructed one, goes in the request's address to ntfy and nowhere else: a refusal, a failure naming it, the
    // check's printed lines and its run log row hold none of it, and the topic's own text says it is not shown.
    [Fact]
    public async Task TheNtfyTopicReachesNtfyAloneAndNoErrorLineOrLogRowHoldsIt()
    {
        const string topic = "eb-constructed-topic-7f3k2q";
        var topicOf = new NtfyTopic(topic);

        Assert.DoesNotContain(topic, topicOf.ToString(), StringComparison.Ordinal);

        var taken = new RecordedNtfy(HttpStatusCode.OK);

        Assert.True(await new NtfyMessages(new HttpClient(taken) { BaseAddress = new Uri(NtfyMessages.Address) }, topicOf).SendAsync("Order changes", "AAA: buy at $50.00"));
        Assert.Equal(new Uri(NtfyMessages.Address + topic), taken.Asked);

        var refused = await Assert.ThrowsAsync<ProviderRefusal>(() => new NtfyMessages(new HttpClient(new RecordedNtfy(HttpStatusCode.InternalServerError)) { BaseAddress = new Uri(NtfyMessages.Address) }, topicOf).SendAsync("t", "m"));

        Assert.DoesNotContain(topic, refused.Message, StringComparison.Ordinal);

        var failing = new RecordedNtfy(HttpStatusCode.OK, new HttpRequestException("No such host is known. (ntfy.sh:443/" + topic + ")"));
        var unreached = await Assert.ThrowsAsync<ProviderRefusal>(() => new NtfyMessages(new HttpClient(failing) { BaseAddress = new Uri(NtfyMessages.Address) }, topicOf).SendAsync("t", "m"));

        Assert.DoesNotContain(topic, unreached.Message, StringComparison.Ordinal);

        using var store = new TemporaryStore().Migrated();
        var output = new StringWriter();
        var phone = new NtfyMessages(new HttpClient(failing) { BaseAddress = new Uri(NtfyMessages.Address) }, topicOf);

        await new FillCheck(FixedClock.At(new DateTimeOffset(2026, 11, 25, 21, 30, 0, TimeSpan.Zero), SessionZones.UnitedStates), store.DatabaseFile, new RecordedLows([]), phone)
            .RunAsync(ChangeNight, [new("GSPC", "Bounce in an uptrend on the S&P 500", "AAA", ChangePrevious, 50m)], true, output);

        Assert.DoesNotContain(topic, output.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain(topic, (string)One(store, "SELECT group_concat(detail, ' | ') FROM run_log;")!, StringComparison.Ordinal);
        Assert.Contains("the message not sent: ntfy could not be reached", (string)One(store, "SELECT detail FROM run_log WHERE stage = 'fill-check';")!, StringComparison.Ordinal);
    }

    // The provider's live quote for several listings as it sends it: an array, each under its code with the day's low and the
    // instant; one stating no low left out.
    [Fact]
    public void TheLiveQuotesDayLowsAreReadAsTheProviderSendsThem()
    {
        var lows = EodhdDayLowFeed.Parse("""[{"code":"AAA.US","timestamp":1764104400,"low":49.5},{"code":"BBB.US","timestamp":1764104400,"low":"NA"},{"code":"CCC.US","timestamp":1764104400,"low":"12.25"}]""");

        Assert.Equal([("AAA", 49.5m), ("CCC", 12.25m)], lows.Select(low => (low.Ticker, low.Low)));
        Assert.Equal(new DateTimeOffset(2025, 11, 25, 21, 0, 0, TimeSpan.Zero), lows[0].At);
        Assert.Single(EodhdDayLowFeed.Parse("""{"code":"AAA.US","timestamp":1764104400,"low":49.5}"""));
    }

    // The day lows as recorded answers, a request counted a call.
    sealed class RecordedLows(IReadOnlyList<DayLow> lows) : IDayLowFeed
    {
        public int Requests { get; private set; }

        public Task<IReadOnlyList<DayLow>> LowsAsync(IReadOnlyList<string> tickers, CancellationToken cancellation = default)
        {
            Requests++;

            return Task.FromResult<IReadOnlyList<DayLow>>([.. lows.Where(low => tickers.Contains(low.Ticker))]);
        }
    }

    // ntfy as a recorded answer: the address it was asked at and how many messages were sent, or a failure to reach it.
    sealed class RecordedNtfy(HttpStatusCode status, Exception? failure = null) : HttpMessageHandler
    {
        public int Sent { get; private set; }

        public Uri? Asked { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (failure is not null)
            {
                throw failure;
            }

            Sent++;
            Asked = request.RequestUri;

            return Task.FromResult(new HttpResponseMessage(status));
        }
    }
}
