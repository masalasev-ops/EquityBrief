using System.Globalization;
using EquityBrief.Core.Chart;
using EquityBrief.Core.Components;
using EquityBrief.Core.History;
using EquityBrief.Core.Sweep;
using EquityBrief.Data;
using EquityBrief.Worker.Sweep;
using Microsoft.Data.Sqlite;

namespace EquityBrief.Worker.History;

// From the 23.8 correction what the long history's profit check reads on one index: each code's quarters off the SEC's facts,
// each year's members on its first session with how many read the check, and the first year 90 in every 100 do, none where
// no year does.
// see: The profit check over the long history reads the long facts from the first year they cover 90 in every 100 members, the years before it unchecked
public sealed record LongIncome(
    IReadOnlyDictionary<string, IReadOnlyList<Core.Readings.FiledIncome>> Income,
    IReadOnlyList<(int Year, int Members, int Read)> Years,
    int? FirstChecked);

// The long history's reader: the record the sweeps, the ledger build and the tester already take, read over the long
// spans of one index, each code's long prices with the store's own bars after them at the store's scale under its
// marks, the long prints beside the pulled ones and the calendar's, the long calendar's surprises before the pulled
// ones, and each code's sector as the store files it today. It reads the store read-only one statement at a time, as
// the sweep's own read does, writes nothing, and edits no source a rule pins.
// see: The long ledger is read over the long spans and the long prices with the store's own closes after them, each setup under the rule as ruled and with a stock whose prices stop ended at its last price
public sealed class LongHistoryReader : IComponent
{
    public static ComponentAccess Access => new(
        Stores:
        [
            new StoreTouch(Store.LongSpan, Touch.Read),
            new StoreTouch(Store.LongHolding, Touch.Read),
            new StoreTouch(Store.LongBar, Touch.Read),
            new StoreTouch(Store.PulledBar, Touch.Read),
            new StoreTouch(Store.LongSplit, Touch.Read),
            new StoreTouch(Store.PulledSplit, Touch.Read),
            new StoreTouch(Store.SplitEvent, Touch.Read),
            new StoreTouch(Store.LongMarketBar, Touch.Read),
            new StoreTouch(Store.Bar, Touch.Read),
            new StoreTouch(Store.SessionMark, Touch.Read),
            new StoreTouch(Store.MarkRuling, Touch.Read),
            new StoreTouch(Store.LongPrint, Touch.Read),
            new StoreTouch(Store.PulledEarnings, Touch.Read),
            new StoreTouch(Store.Calendar, Touch.Read),
            new StoreTouch(Store.PulledSurprise, Touch.Read),
            new StoreTouch(Store.Membership, Touch.Read),
            new StoreTouch(Store.Company, Touch.Read),
            new StoreTouch(Store.PulledCompany, Touch.Read),
            new StoreTouch(Store.LongFact, Touch.Read),
            new StoreTouch(Store.FiledFact, Touch.Read),
            new StoreTouch(Store.LongFiling, Touch.Read),
            new StoreTouch(Store.LongHeldNone, Touch.Read),
        ],
        Feeds: []);

    readonly string databaseFile;

    public LongHistoryReader(string databaseFile) => this.databaseFile = databaseFile;

    // The newest session the long prices or the store's own bars hold, which a long build reads its history through so
    // every trade it writes replays over the years after its span.
    public async Task<DateOnly> NewestSessionAsync(CancellationToken cancellation = default)
    {
        await using var connection = await OpenAsync(cancellation);
        DateOnly? newest = null;

        foreach (var table in new[] { "SELECT MAX(session_date) FROM long_bar;", "SELECT MAX(session_date) FROM bar;" })
        {
            await foreach (var row in RowsAsync(connection, table, [], cancellation))
            {
                if (!row.IsDBNull(0) && Day(row.GetString(0)) is var day && (newest is null || day > newest))
                {
                    newest = day;
                }
            }
        }

        return newest ?? throw new InvalidOperationException("the store holds no long price and no bar");
    }

    // The newest session the store's own bars hold, or the long prices' newest where it holds no bar, so a session an answer
    // asked in the day carries before the night stores it is never read as one a code holds no price for.
    // see: A code's member sessions still without a price after the provider was asked again are marked held none and never asked again
    public async Task<DateOnly> NewestStoredSessionAsync(CancellationToken cancellation = default)
    {
        await using (var connection = await OpenAsync(cancellation))
        {
            await foreach (var row in RowsAsync(connection, "SELECT MAX(session_date) FROM bar;", [], cancellation))
            {
                if (!row.IsDBNull(0))
                {
                    return Day(row.GetString(0));
                }
            }
        }

        return await NewestSessionAsync(cancellation);
    }

    // One index's long history through the session given: every code its long spans hold, each over its own spans, none a
    // survivor.
    public async Task<SweepHistoryInputs> ReadAsync(string index, DateOnly through, Action<string>? progress = null, CancellationToken cancellation = default)
    {
        await using var connection = await OpenAsync(cancellation);

        var stamp = Text(through);
        var spans = new Dictionary<string, List<(DateOnly?, DateOnly?)>>(StringComparer.Ordinal);

        await foreach (var row in RowsAsync(connection, "SELECT ticker, joined, \"left\" FROM long_span WHERE index_code = $index ORDER BY ticker, joined;", [("$index", index)], cancellation))
        {
            var ticker = row.GetString(0);

            if (!spans.TryGetValue(ticker, out var held))
            {
                spans[ticker] = held = [];
            }

            held.Add((row.IsDBNull(1) ? null : Day(row.GetString(1)), row.IsDBNull(2) ? null : Day(row.GetString(2))));
        }

        var marks = await MarksAsync(connection, cancellation);

        lastJoinsFound.Clear();

        var prints = new Dictionary<string, SortedSet<DateOnly>>(StringComparer.Ordinal);

        foreach (var query in new[]
        {
            "SELECT ticker, event_date FROM long_print;",
            "SELECT ticker, event_date FROM pulled_earnings;",
            "SELECT ticker, event_date FROM calendar WHERE kind = 'earnings';",
        })
        {
            await foreach (var row in RowsAsync(connection, query, [], cancellation))
            {
                var ticker = row.GetString(0);

                if (!prints.TryGetValue(ticker, out var dates))
                {
                    prints[ticker] = dates = [];
                }

                dates.Add(Day(row.GetString(1)));
            }
        }

        var surprises = await SurprisesAsync(connection, cancellation);
        var sectors = new Dictionary<string, string>(StringComparer.Ordinal);

        // The sector as the store files it today: the S&P 500's membership's newest, then a company's as its fetch or the
        // companies pull filed it, a code the store files no sector for reading none.
        foreach (var query in new[]
        {
            "SELECT ticker, sector FROM pulled_company WHERE sector IS NOT NULL ORDER BY pull;",
            "SELECT ticker, sector FROM company WHERE sector IS NOT NULL ORDER BY fetched_at;",
            "SELECT ticker, sector FROM membership WHERE index_code = 'GSPC' AND sector IS NOT NULL ORDER BY observed_at;",
        })
        {
            await foreach (var row in RowsAsync(connection, query, [], cancellation))
            {
                sectors[row.GetString(0)] = row.GetString(1);
            }
        }

        var splitDays = await Bars.SplitDays.ReadAsync(connection, [.. Bars.SplitDays.NightTables, "long_split"], cancellation);
        var calendar = await CalendarAsync(connection, stamp, cancellation);
        var companies = await CompaniesAsync(connection, cancellation);
        var settled = await SettledAsync(connection, marks, calendar, stamp, cancellation);
        var parts = new List<LongPart>();
        var read = 0;

        foreach (var (ticker, held) in spans.OrderBy(pair => pair.Key, StringComparer.Ordinal))
        {
            var own = marks.GetValueOrDefault(ticker) ?? [];
            var series = await SeriesAsync(connection, ticker, own, stamp, cancellation);
            var readThrough = settled.GetValueOrDefault(ticker)?.Outcome == LongMarks.ReadThrough;
            var end = readThrough ? null : own.Where(mark => mark.Kind == SessionMarking.HistoryEnds).Select(mark => (DateOnly?)mark.Session).Min();

            foreach (var (bars, partSpans) in LongMarks.Parts(series, own, held, readThrough))
            {
                var after = end is { } mark && partSpans.Any(span => span.Joined is { } joined && joined > mark) ? end : null;
                var upTo = end is not null && after is null ? end : null;

                var part = await RenamedAsync(connection, ticker, bars, partSpans, (after, upTo), companies, marks, calendar, stamp, cancellation);

                parts.Add(part with { Spans = await EndedAsync(connection, part, calendar, cancellation) });
            }

            if (++read % 250 == 0)
            {
                progress?.Invoke(FormattableString.Invariant($"long history: read {read} of {spans.Count} code(s) on {index}"));
            }
        }

        var names = new List<SweepName>();
        var fingerprint = new System.Text.StringBuilder();
        var oneSeries = OneSeriesNotCounted(parts, calendar);

        foreach (var part in parts)
        {
            fingerprint.Append(part.Ticker).Append(':').Append(part.Bars.Length.ToString(CultureInfo.InvariantCulture)).Append(':')
                .Append(part.Bars.Sum(bar => bar.Close).ToString(CultureInfo.InvariantCulture)).Append(';');

            names.Add(new SweepName(
                part.Ticker,
                part.Bars,
                LongRenames.Without(part.Spans, NotCounted(part, parts, companies, calendar).Union(oneSeries.GetValueOrDefault(part) ?? []).ToHashSet(), calendar),
                [.. (prints.GetValueOrDefault(part.Ticker) ?? [])],
                sectors.GetValueOrDefault(part.Ticker),
                [.. (surprises.GetValueOrDefault(part.Ticker) ?? []).OrderBy(surprise => surprise.EventDate)],
                SplitsStored: splitDays.GetValueOrDefault(part.Ticker)));
        }

        lastJoins = [.. lastJoinsFound];

        var sessions = SweepHistory.Sessions(names);
        var (indexNights, withoutBar, namesWithout, namesMissing) = Missing(names, sessions);

        return new SweepHistoryInputs(
            through,
            sessions,
            names,
            indexNights,
            withoutBar,
            namesWithout,
            namesMissing,
            Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(fingerprint.ToString())))[..16].ToLowerInvariant(),
            surprises.Count > 0 ? SweepHistory.PulledSurprises : SweepHistory.NoSurprises,
            surprises.Sum(pair => pair.Value.Count),
            names.Count(name => name.Sector is null),
            [],
            0,
            names.Count);
    }

    // From 23.7 the named market series' closes through the session given from the long pull, which holds each from 2004 or
    // its start through the newest session it was asked for, one statement a series, none for a series it holds no session
    // of; the record the sweeps' own reads take, their pull named as the long one.
    public async Task<IReadOnlyList<SweepMarketSeries>> SeriesOfAsync(IReadOnlyList<string> named, DateOnly through, CancellationToken cancellation = default)
    {
        await using var connection = await OpenAsync(cancellation);

        var found = new List<SweepMarketSeries>();

        foreach (var series in named)
        {
            var closes = new List<(DateOnly, double)>();

            await foreach (var row in RowsAsync(
                connection,
                "SELECT session_date, close FROM long_market_bar WHERE series = $series AND session_date <= $through ORDER BY session_date;",
                [("$series", series), ("$through", Text(through))],
                cancellation))
            {
                closes.Add((Day(row.GetString(0)), Core.Prices.Statistic.FromPrice(Money.FromStorage(row.GetString(1)))));
            }

            if (closes.Count > 0)
            {
                found.Add(new SweepMarketSeries(series, closes, LongPull));
            }
        }

        return found;
    }

    // From 24.8 each code's industry headings on an index as the funds' schedules filed them, by quarter end in order, which the
    // heavyweights' lay over the long history reads a sector as it stood from before today's eleven began.
    // see: The sector splits read a sector as it stood off the funds' industry headings through a stated map, and the filed GICS sector after the schedules end
    public async Task<IReadOnlyDictionary<string, IReadOnlyList<(DateOnly Period, string Heading)>>> HeadingsAsync(string index, CancellationToken cancellation = default)
    {
        await using var connection = await OpenAsync(cancellation);

        var headings = new Dictionary<string, List<(DateOnly Period, string Heading)>>(StringComparer.Ordinal);

        await foreach (var row in RowsAsync(
            connection,
            "SELECT period, ticker, industry FROM long_holding WHERE index_code = $index AND ticker IS NOT NULL AND industry IS NOT NULL ORDER BY period;",
            [("$index", index)],
            cancellation))
        {
            if (!headings.TryGetValue(row.GetString(1), out var held))
            {
                headings[row.GetString(1)] = held = [];
            }

            held.Add((Day(row.GetString(0)), row.GetString(2)));
        }

        return headings.ToDictionary(pair => pair.Key, pair => (IReadOnlyList<(DateOnly, string)>)pair.Value, StringComparer.Ordinal);
    }

    // On the operator's answer of 2026-11-14 to item 15 each filer's facts as first filed by the history's end, the standing
    // table's and the long one's, whose periods meet and do not overlap, which the books' long test scores and values each
    // member from as the quality and Fundamentals families' history tests do from the standing table alone.
    // see: The monthly run's last step judges every registered rule over the long history from each family's own first test year, capped at three hours
    public async Task<IReadOnlyDictionary<string, IReadOnlyList<Core.Ledger.FiledFactRow>>> FiledFactsAsync(IEnumerable<string> ciks, DateOnly through, CancellationToken cancellation = default)
    {
        await using var connection = await OpenAsync(cancellation);

        var facts = new Dictionary<string, IReadOnlyList<Core.Ledger.FiledFactRow>>(StringComparer.Ordinal);

        foreach (var cik in ciks.Distinct(StringComparer.Ordinal))
        {
            var rows = new List<Core.Ledger.FiledFactRow>();

            foreach (var table in new[] { "filed_fact", "long_fact" })
            {
                await foreach (var row in RowsAsync(
                    connection,
                    $"SELECT concept, period_start, period_end, dollars, filed, form, accession FROM {table} WHERE cik = $cik AND filed <= $through;",
                    [("$cik", cik), ("$through", Text(through))],
                    cancellation))
                {
                    rows.Add(new Core.Ledger.FiledFactRow(row.GetString(0), Day(row.GetString(1)), Day(row.GetString(2)), Money.FromStorage(row.GetString(3)), Day(row.GetString(4)), row.GetString(5), row.GetString(6)));
                }
            }

            facts[cik] = rows;
        }

        return facts;
    }

    public const string LongPull = "long";

    // From the 23.8 correction each code's quarters of net income, operating income and interest expense off the SEC's facts
    // as first filed, the long facts and the standing ones, by the filer the store files the code under; and over one index's
    // codes, each year's members on its first session and how many of them read the profit check off the quarters filed
    // before it, with the first year 90 in every 100 do.
    // see: The profit check over the long history reads the long facts from the first year they cover 90 in every 100 members, the years before it unchecked
    public async Task<LongIncome> IncomeAsync(IReadOnlyList<SweepName> names, IReadOnlyList<DateOnly> sessions, CancellationToken cancellation = default)
    {
        await using var connection = await OpenAsync(cancellation);

        var filerOf = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var query in new[]
        {
            "SELECT ticker, cik FROM pulled_company WHERE cik IS NOT NULL;",
            "SELECT ticker, cik FROM company WHERE cik IS NOT NULL ORDER BY fetched_at;",
            "SELECT DISTINCT ticker, cik FROM long_filing;",
        })
        {
            await foreach (var row in RowsAsync(connection, query, [], cancellation))
            {
                filerOf[row.GetString(0)] = row.GetString(1).TrimStart('0');
            }
        }

        var wanted = names.Select(name => name.Ticker).Where(filerOf.ContainsKey).Select(ticker => filerOf[ticker]).ToHashSet(StringComparer.Ordinal);
        var concepts = string.Join(", ", Core.Ledger.FiledFacts.NetIncome.Concat(Core.Ledger.FiledFacts.OperatingIncome).Concat(Core.Ledger.FiledFacts.InterestExpense).Select(concept => "'" + concept + "'"));
        var facts = new Dictionary<string, List<Core.Ledger.FiledFactRow>>(StringComparer.Ordinal);

        foreach (var query in new[]
        {
            $"SELECT cik, concept, period_start, period_end, dollars, filed, form, accession FROM long_fact WHERE concept IN ({concepts});",
            $"SELECT cik, concept, period_start, period_end, dollars, filed, form, accession FROM filed_fact WHERE concept IN ({concepts});",
        })
        {
            await foreach (var row in RowsAsync(connection, query, [], cancellation))
            {
                var cik = row.GetString(0).TrimStart('0');

                if (!wanted.Contains(cik))
                {
                    continue;
                }

                if (!facts.TryGetValue(cik, out var rows))
                {
                    facts[cik] = rows = [];
                }

                rows.Add(new Core.Ledger.FiledFactRow(row.GetString(1), Day(row.GetString(2)), Day(row.GetString(3)), Money.FromStorage(row.GetString(4)), Day(row.GetString(5)), row.GetString(6), row.GetString(7)));
            }
        }

        var quartersOf = facts.ToDictionary(pair => pair.Key, pair => LongProfit.Quarters(pair.Value), StringComparer.Ordinal);
        var income = new Dictionary<string, IReadOnlyList<Core.Readings.FiledIncome>>(StringComparer.Ordinal);

        foreach (var name in names)
        {
            if (filerOf.TryGetValue(name.Ticker, out var cik) && quartersOf.TryGetValue(cik, out var quarters))
            {
                income[name.Ticker] = quarters;
            }
        }

        var years = new List<(int Year, int Members, int Read)>();

        foreach (var first in sessions.GroupBy(session => session.Year).Select(year => year.Min()).Order())
        {
            var members = names.Where(name => name.MemberOn(first)).Select(name => name.Ticker).Distinct(StringComparer.Ordinal).ToArray();

            years.Add((first.Year, members.Length, members.Count(ticker => income.TryGetValue(ticker, out var quarters) && LongProfit.Reads(quarters, first))));
        }

        return new LongIncome(income, years, LongProfit.FirstCheckedYear(years));
    }

    // Every odd history-ends mark through the session given, each with what its filer and its prices say and what the reader
    // does with it, over every index's spans.
    // see: A history-ends mark ends only the member span it falls in, and a mark inside a span the stock trades on past is settled by its filer and its prices
    public async Task<IReadOnlyList<SettledMark>> OddMarksAsync(DateOnly through, CancellationToken cancellation = default)
    {
        await using var connection = await OpenAsync(cancellation);

        var stamp = Text(through);
        var marks = await MarksAsync(connection, cancellation);
        var settled = await SettledAsync(connection, marks, await CalendarAsync(connection, stamp, cancellation), stamp, cancellation);

        return [.. settled.Values.OrderBy(mark => mark.Ticker, StringComparer.Ordinal)];
    }

    // Every pair of codes the reader joins on an index through the session given, and every pair of one company's codes it
    // leaves apart for being priced together, each with the member sessions it covers.
    // see: Two codes of one filer join as one history where they are never priced on the same day or trade in one ratio on the days they share
    public async Task<IReadOnlyList<LongJoin>> JoinsAsync(string index, DateOnly through, CancellationToken cancellation = default)
    {
        await ReadAsync(index, through, null, cancellation);

        return [.. lastJoins];
    }

    IReadOnlyList<LongJoin> lastJoins = [];

    public const string ListsVerb = "long-history-lists";

    // What the reader reads on an index against what it read before the marks were read span by span and renames joined:
    // each odd mark and its outcome, each pair joined and each left apart, each code whose member sessions hold a price they
    // held none of, each code's member sessions past its last traded price where its span ended, each code ended there on
    // the operator's word, each code's member sessions not counted for its company being counted under another code, and each
    // code's member sessions still holding no price, by whether its span runs past its last price. It writes nothing.
    public async Task ListsAsync(string index, DateOnly through, TextWriter output, CancellationToken cancellation = default)
    {
        var inputs = await ReadAsync(index, through, null, cancellation);

        foreach (var mark in await OddMarksAsync(through, cancellation))
        {
            output.WriteLine(FormattableString.Invariant($"{ListsVerb}: mark {mark.Ticker} {Text(mark.Mark)} on {string.Join(" ", mark.Indices)}: its filer {mark.Filer}, its price {mark.Price}; {mark.Outcome}{(mark.Ruled ? " on the operator's ruling" : string.Empty)}"));
        }

        foreach (var join in lastJoins.OrderBy(join => join.Ticker, StringComparer.Ordinal).ThenBy(join => join.Other, StringComparer.Ordinal))
        {
            output.WriteLine(join.Test is { } test
                ? FormattableString.Invariant($"{ListsVerb}: joined {join.Ticker} with {join.Other}, {test}, {join.Sessions} member session(s) priced from it")
                : FormattableString.Invariant($"{ListsVerb}: apart {join.Ticker} and {join.Other}, priced together, {join.Sessions} member session(s) it would have priced"));
        }

        await using var connection = await OpenAsync(cancellation);

        var stamp = Text(through);
        var marks = await MarksAsync(connection, cancellation);
        var calendar = inputs.Sessions;
        var byTicker = inputs.Names.GroupBy(name => name.Ticker, StringComparer.Ordinal);
        var (back, without, past, notCounted, ended, noneHeld) = (0L, 0L, 0L, 0L, 0L, 0L);
        var heldNone = new Dictionary<string, List<(DateOnly First, DateOnly Last, string AskedAt)>>(StringComparer.Ordinal);

        await foreach (var row in RowsAsync(connection, "SELECT ticker, first_session, last_session, asked_at FROM long_held_none;", [], cancellation))
        {
            if (!heldNone.TryGetValue(row.GetString(0), out var runs))
            {
                heldNone[row.GetString(0)] = runs = [];
            }

            runs.Add((Day(row.GetString(1)), Day(row.GetString(2)), row.GetString(3)));
        }

        lastMissing.Clear();
        lastSessions = calendar;

        foreach (var code in byTicker)
        {
            var held = code.SelectMany(name => name.Spans).ToArray();
            var spans = new List<(DateOnly? Joined, DateOnly? Left)>();

            await foreach (var row in RowsAsync(connection, "SELECT joined, \"left\" FROM long_span WHERE index_code = $index AND ticker = $ticker;", [("$index", index), ("$ticker", code.Key)], cancellation))
            {
                spans.Add((Day(row.GetString(0)), row.IsDBNull(1) ? null : Day(row.GetString(1))));
            }

            var before = LongLedger.Marked(await BarsAsync(connection, "long_bar", code.Key, stamp, cancellation), marks.GetValueOrDefault(code.Key) ?? []);
            var stored = LongLedger.Marked(await BarsAsync(connection, "bar", code.Key, stamp, cancellation), marks.GetValueOrDefault(code.Key) ?? []);
            var then = SweepHistory.Merged(before, stored).Select(bar => bar.Session).ToHashSet();
            var memberDays = calendar.Where(session => Holds(spans, session)).ToArray();
            var priced = code.Select(name => (Days: name.Bars.Select(bar => bar.Session).ToHashSet(), name.Spans)).ToArray();
            var now = memberDays.Where(session => priced.Any(one => Holds(one.Spans, session) && one.Days.Contains(session))).ToArray();
            var returned = now.Where(session => !then.Contains(session)).ToArray();
            // A member session the reader's spans no longer hold is one past the code's last traded price, its span ended there,
            // or one counted under another code of its company.
            var lastOwn = code.SelectMany(name => LongTraded.Of(name.Bars)).Select(bar => (DateOnly?)bar.Session).Max();
            var droppedDays = memberDays.Where(session => !Holds(held, session)).ToArray();
            var endedDays = droppedDays.Count(session => lastOwn is { } lastDay && session > lastDay);
            var dropped = droppedDays.Length - endedDays;
            var missing = memberDays.Where(session => Holds(held, session) && !now.Contains(session)).ToArray();

            if (missing.Length > 0)
            {
                lastMissing[code.Key] = missing;
            }

            // The sessions the provider was asked for again and holds no price of are named apart and never asked for again.
            // see: A code's member sessions still without a price after the provider was asked again are marked held none and never asked again
            var runsHeldNone = heldNone.GetValueOrDefault(code.Key) ?? [];
            var askedAgain = missing.Where(session => runsHeldNone.Any(run => session >= run.First && session <= run.Last)).ToArray();

            if (askedAgain.Length > 0)
            {
                noneHeld += askedAgain.Length;
                missing = [.. missing.Except(askedAgain)];
                output.WriteLine(FormattableString.Invariant($"{ListsVerb}: held none {code.Key} {askedAgain.Length} member session(s) from {Text(askedAgain[0])} to {Text(askedAgain[^1])} the provider holds no price for, asked again {string.Join(", ", runsHeldNone.Select(run => run.AskedAt).Distinct(StringComparer.Ordinal))}"));
            }

            if (LongMarks.EndedOnTheWord.Contains(code.Key) && code.SelectMany(name => LongTraded.Of(name.Bars)).Select(bar => (DateOnly?)bar.Session).Max() is { } lastTraded)
            {
                output.WriteLine(FormattableString.Invariant($"{ListsVerb}: ended {code.Key} at its last traded price of {Text(lastTraded)} on the operator's word, its spans {string.Join(", ", held.Select(span => $"{(span.Joined is { } j ? Text(j) : "-")} to {(span.Left is { } l ? Text(l) : "-")}"))}"));
            }

            if (returned.Length > 0)
            {
                back += returned.Length;
                output.WriteLine(FormattableString.Invariant($"{ListsVerb}: back {code.Key} {returned.Length} member session(s) from {Text(returned[0])} to {Text(returned[^1])}, years {string.Join(" ", returned.Select(session => session.Year).Distinct())}; spans {string.Join(", ", spans.Select(span => $"{(span.Joined is { } j ? Text(j) : "-")} to {(span.Left is { } l ? Text(l) : "-")}"))}"));
            }

            if (endedDays > 0)
            {
                ended += endedDays;
                output.WriteLine(FormattableString.Invariant($"{ListsVerb}: past {code.Key} {endedDays} member session(s) after its last traded price of {Text(lastOwn!.Value)}, its span ended there"));
            }

            if (dropped > 0)
            {
                notCounted += dropped;
                output.WriteLine(FormattableString.Invariant($"{ListsVerb}: once {code.Key} {dropped} member session(s) counted under another code of its company"));
            }

            if (missing.Length > 0)
            {
                var last = priced.SelectMany(one => one.Days).DefaultIfEmpty().Max();
                var runsPast = missing.Count(session => last == default || session > last);

                without += missing.Length;
                past += runsPast;
                output.WriteLine(FormattableString.Invariant($"{ListsVerb}: without {code.Key} {missing.Length} member session(s) with no price from {Text(missing[0])} to {Text(missing[^1])}, {runsPast} of them after its last price{(last == default ? ", which it holds none of" : " of " + Text(last))}"));
            }
        }

        output.WriteLine(FormattableString.Invariant($"{ListsVerb}: {DecisionCardsName(index)} through {stamp}: {back} member session(s) priced that were not, {ended} past their code's last traded price with its span ended there, {notCounted} counted under another code of their company, {noneHeld} the provider holds no price for, {without} still with no price, {past} of them after their code's last price"));
    }

    readonly Dictionary<string, DateOnly[]> lastMissing = new(StringComparer.Ordinal);

    IReadOnlyList<DateOnly> lastSessions = [];

    // Each code's member sessions on an index through the session given that hold no price, those the provider was asked for
    // again among them, as the lists read them, and the index's sessions. It writes nothing.
    public async Task<(IReadOnlyDictionary<string, DateOnly[]> Missing, IReadOnlyList<DateOnly> Sessions)> MissingAsync(string index, DateOnly through, CancellationToken cancellation = default)
    {
        await ListsAsync(index, through, TextWriter.Null, cancellation);

        return (new Dictionary<string, DateOnly[]>(lastMissing, StringComparer.Ordinal), lastSessions);
    }

    static string DecisionCardsName(string index) => index switch
    {
        "GSPC" => "the S&P 500",
        "MID" => "the S&P 400",
        _ => "the S&P 600",
    };

    static async Task<Dictionary<string, List<SessionMark>>> MarksAsync(SqliteConnection connection, CancellationToken cancellation)
    {
        var marks = new Dictionary<string, List<SessionMark>>(StringComparer.Ordinal);

        await foreach (var row in RowsAsync(connection, "SELECT ticker, session_date, kind, says FROM session_mark ORDER BY ticker, session_date;", [], cancellation))
        {
            var ticker = row.GetString(0);

            if (!marks.TryGetValue(ticker, out var held))
            {
                marks[ticker] = held = [];
            }

            held.Add(new SessionMark(Day(row.GetString(1)), row.GetString(2), row.GetString(3)));
        }

        return marks;
    }

    // The sessions through the session given: the S&P 500's own from the long pull, and the store's bars' after its newest.
    internal static async Task<DateOnly[]> CalendarAsync(SqliteConnection connection, string through, CancellationToken cancellation)
    {
        var sessions = new SortedSet<DateOnly>();

        await foreach (var row in RowsAsync(connection, "SELECT session_date FROM long_market_bar WHERE series = 'GSPC' AND session_date <= $through;", [("$through", through)], cancellation))
        {
            sessions.Add(Day(row.GetString(0)));
        }

        var newest = sessions.Count == 0 ? "0000-00-00" : Text(sessions.Max);

        await foreach (var row in RowsAsync(connection, "SELECT DISTINCT session_date FROM bar WHERE session_date > $newest AND session_date <= $through;", [("$newest", newest), ("$through", through)], cancellation))
        {
            sessions.Add(Day(row.GetString(0)));
        }

        return [.. sessions];
    }

    // Each code's company: the codes the store files under one filer, and a code with its delisted spellings, linked as one.
    static async Task<LongCompanies> CompaniesAsync(SqliteConnection connection, CancellationToken cancellation)
    {
        var filers = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        var codes = new HashSet<string>(StringComparer.Ordinal);

        foreach (var query in new[]
        {
            "SELECT ticker, cik FROM pulled_company WHERE cik IS NOT NULL;",
            "SELECT ticker, cik FROM company WHERE cik IS NOT NULL;",
            "SELECT DISTINCT ticker, cik FROM long_filing;",
        })
        {
            await foreach (var row in RowsAsync(connection, query, [], cancellation))
            {
                var cik = row.GetString(1).TrimStart('0');

                if (!filers.TryGetValue(cik, out var held))
                {
                    filers[cik] = held = new HashSet<string>(StringComparer.Ordinal);
                }

                held.Add(row.GetString(0));
            }
        }

        foreach (var query in new[] { "SELECT DISTINCT ticker FROM long_bar;", "SELECT DISTINCT ticker FROM bar;" })
        {
            await foreach (var row in RowsAsync(connection, query, [], cancellation))
            {
                codes.Add(row.GetString(0));
            }
        }

        return LongCompanies.Of(filers.ToDictionary(pair => pair.Key, pair => (IEnumerable<string>)pair.Value, StringComparer.Ordinal), codes);
    }

    // Each odd history-ends mark on a code, settled by its filer's reports and its traded prices around it; a mark that is
    // not odd is not held here and ends its span. From the January pin move the sweeps' history reads the same.
    // see: A history-ends mark ends only the member span it falls in, and a mark inside a span the stock trades on past is settled by its filer and its prices
    internal static async Task<Dictionary<string, SettledMark>> SettledAsync<TMarks>(SqliteConnection connection, IReadOnlyDictionary<string, TMarks> marks, IReadOnlyList<DateOnly> calendar, string through, CancellationToken cancellation)
        where TMarks : IReadOnlyList<SessionMark>
    {
        var settled = new Dictionary<string, SettledMark>(StringComparer.Ordinal);
        var ruled = new Dictionary<(string, DateOnly), string>();

        await foreach (var row in RowsAsync(connection, "SELECT ticker, session_date, ruling FROM mark_ruling;", [], cancellation))
        {
            ruled[(row.GetString(0), Day(row.GetString(1)))] = row.GetString(2);
        }

        foreach (var (ticker, held) in marks)
        {
            if (held.Where(mark => mark.Kind == SessionMarking.HistoryEnds).Select(mark => (DateOnly?)mark.Session).Min() is not { } end)
            {
                continue;
            }

            var spans = new List<(string Index, DateOnly Joined, DateOnly? Left)>();

            await foreach (var row in RowsAsync(connection, "SELECT index_code, joined, \"left\" FROM long_span WHERE ticker = $ticker;", [("$ticker", ticker)], cancellation))
            {
                spans.Add((row.GetString(0), Day(row.GetString(1)), row.IsDBNull(2) ? null : Day(row.GetString(2))));
            }

            var traded = LongTraded.Of(await SeriesAsync(connection, ticker, held, through, cancellation)).ToArray();

            if (!LongMarks.Odd(end, spans, [.. traded.Select(bar => bar.Session)]))
            {
                continue;
            }

            var filings = new List<(DateOnly, string)>();

            await foreach (var row in RowsAsync(connection, "SELECT filed, form FROM long_filing WHERE ticker = $ticker;", [("$ticker", ticker)], cancellation))
            {
                filings.Add((Day(row.GetString(0)), row.GetString(1)));
            }

            var filer = LongMarks.FilerOf(end, filings);
            var price = LongMarks.PriceOf(traded, end, calendar);

            // A mark the evidence leaves to the operator reads as they ruled it on the marking page.
            var outcome = LongMarks.Settle(filer, price) is var found && found == LongMarks.ToOperator && ruled.TryGetValue((ticker, end), out var ruling) ? ruling : found;

            settled[ticker] = new SettledMark(ticker, end, [.. spans.Select(span => span.Index).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)], filer, price, outcome)
            {
                Ruled = outcome != found,
            };
        }

        return settled;
    }

    // A code's prices, the long pull's and the store's at the store's scale, the sessions before a start left out and a bad
    // print skipped, then read against the newest pull holding the code as the class the two fall in; its history-ends mark is
    // read by the parts.
    // see: A code whose long prices part from the newest pull's is read at the pull's adjustment where both are one instrument, and from the pull where the long side is another and thin
    static async Task<SweepBar[]> SeriesAsync(SqliteConnection connection, string ticker, IReadOnlyList<SessionMark> marks, string through, CancellationToken cancellation)
    {
        var kept = marks.Where(mark => mark.Kind != SessionMarking.HistoryEnds).ToArray();
        var longBars = LongMarks.Parts(await BarsAsync(connection, "long_bar", ticker, through, cancellation), kept, [], true)[0].Bars;
        var stored = LongMarks.Parts(await BarsAsync(connection, "bar", ticker, through, cancellation), kept, [], true)[0].Bars;
        var pulled = LongMarks.Parts(await BarsAsync(connection, "pulled_bar", ticker, through, cancellation), kept, [], true)[0].Bars;

        return LongPulled.Read(SweepHistory.Merged(longBars, stored), pulled, stored.Length > 0 ? stored[0].Session : null);
    }

    // A part of a code's history with the prices another code of its company holds on its member sessions it holds none of,
    // joined where the two pass the rename test; the other code's prices read only on the part's side of its mark.
    async Task<LongPart> RenamedAsync(
        SqliteConnection connection,
        string ticker,
        SweepBar[] bars,
        (DateOnly? Joined, DateOnly? Left)[] spans,
        (DateOnly? After, DateOnly? UpTo) side,
        LongCompanies companies,
        IReadOnlyDictionary<string, List<SessionMark>> marks,
        IReadOnlyList<DateOnly> calendar,
        string through,
        CancellationToken cancellation)
    {
        var joined = new HashSet<DateOnly>();
        var with = new List<(string, string)>();

        // A link the operator made by hand reads the later code's prices from its session on in place of the code's own, where a
        // member span runs past it and the later code's first close there continues the code's last; listed apart otherwise.
        // see: DDR reads SITC's prices from SITE Centers' 2018 rename by hand where SITC's close there continues DDR's
        // see: Three more codes read their later codes' prices by hand where the later close continues the earlier, each listed as made by hand
        foreach (var (_, later, from) in LongRenames.ByHand.Where(link => link.Ticker == ticker && spans.Any(span => span.Left is not { } left || left > link.From)))
        {
            var theirs = (await SeriesAsync(connection, later, marks.GetValueOrDefault(later) ?? [], through, cancellation))
                .Where(bar => (side.After is not { } after || bar.Session > after) && (side.UpTo is not { } upTo || bar.Session <= upTo))
                .ToArray();
            bool Member(DateOnly session) => spans.Any(span => (span.Joined is not { } start || start <= session) && (span.Left is not { } left || left > session));

            if (LongRenames.ByHandJoined(bars, theirs, from, calendar) is { } linked)
            {
                bars = linked.Bars;
                joined.UnionWith(linked.Joined);
                with.Add((later, linked.Test));
                lastJoinsFound.Add(new LongJoin(ticker, later, linked.Test, linked.Joined.Count(Member)));
            }
            else
            {
                lastJoinsFound.Add(new LongJoin(ticker, later, null, theirs.Count(bar => bar.Session >= from && Member(bar.Session))));
            }
        }

        var held = bars.Select(bar => bar.Session).ToHashSet();
        var missing = calendar.Where(session => !held.Contains(session) && spans.Any(span => (span.Joined is not { } begun || begun <= session) && (span.Left is not { } left || left > session))).ToHashSet();

        if (missing.Count == 0)
        {
            return new LongPart(ticker, bars, spans, joined, with);
        }

        foreach (var other in companies.Linked(ticker))
        {
            var theirs = (await SeriesAsync(connection, other, marks.GetValueOrDefault(other) ?? [], through, cancellation))
                .Where(bar => (side.After is not { } after || bar.Session > after) && (side.UpTo is not { } upTo || bar.Session <= upTo))
                .ToArray();

            if (!theirs.Any(bar => missing.Contains(bar.Session)))
            {
                continue;
            }

            if (LongRenames.Test(bars, theirs) is not { } test)
            {
                lastJoinsFound.Add(new LongJoin(ticker, other, null, theirs.Count(bar => missing.Contains(bar.Session))));

                continue;
            }

            var (together, taken) = LongRenames.Joined(bars, theirs);

            bars = together;
            joined.UnionWith(taken);
            missing.ExceptWith(taken);
            with.Add((other, test));
            lastJoinsFound.Add(new LongJoin(ticker, other, test, taken.Count(session => spans.Any(span => (span.Joined is not { } start || start <= session) && (span.Left is not { } left || left > session)))));
        }

        return new LongPart(ticker, bars, spans, joined, with);
    }

    readonly List<LongJoin> lastJoinsFound = [];

    // A part's spans the schedules ended past its last traded price, a session whose close moved with no volume traded, ended
    // at that price where its filer's reports stop there, it runs a quarter at most or the operator ended its code there; the
    // filer's reports read only where a span runs on further.
    // see: A bar whose close moved with no volume traded with its volume missing
    static async Task<(DateOnly? Joined, DateOnly? Left)[]> EndedAsync(SqliteConnection connection, LongPart part, IReadOnlyList<DateOnly> calendar, CancellationToken cancellation)
    {
        var last = LongTraded.Of(part.Bars).Select(bar => (DateOnly?)bar.Session).Max();
        var filings = new List<(DateOnly, string)>();
        var onTheWord = LongMarks.EndedOnTheWord.Contains(part.Ticker);

        if (!onTheWord && last is { } lastDay && part.Spans.Any(span => span.Left is { } left && calendar.Count(session => session > lastDay && session < left) > LongMarks.QuarterSessions))
        {
            await foreach (var row in RowsAsync(connection, "SELECT filed, form FROM long_filing WHERE ticker = $ticker;", [("$ticker", part.Ticker)], cancellation))
            {
                filings.Add((Day(row.GetString(0)), row.GetString(1)));
            }
        }

        return LongMarks.EndedAtLastPrice(part.Spans, last, calendar, day => LongMarks.FilerOf(day, filings) == LongMarks.FilerStops, onTheWord);
    }

    // The member sessions a part is not counted on: those it holds no price of its own for, its bar missing or taken from
    // another code, where another code of its company is a member holding a price of its own.
    static HashSet<DateOnly> NotCounted(LongPart part, IReadOnlyList<LongPart> parts, LongCompanies companies, IReadOnlyList<DateOnly> calendar)
    {
        var notCounted = new HashSet<DateOnly>();
        var others = parts.Where(other => !ReferenceEquals(other, part) && other.Ticker != part.Ticker && companies.Same(part.Ticker, other.Ticker)).ToArray();

        if (others.Length == 0)
        {
            return notCounted;
        }

        var own = part.Bars.Where(bar => !part.Joined.Contains(bar.Session)).Select(bar => bar.Session).ToHashSet();
        var ownOf = others.Select(other => other.Bars.Where(bar => !other.Joined.Contains(bar.Session)).Select(bar => bar.Session).ToHashSet()).ToArray();

        foreach (var session in calendar.Where(session => !own.Contains(session) && Holds(part.Spans, session)))
        {
            for (var at = 0; at < others.Length; at++)
            {
                if (ownOf[at].Contains(session) && Holds(others[at].Spans, session))
                {
                    notCounted.Add(session);

                    break;
                }
            }
        }

        return notCounted;
    }

    // The member sessions each part holding its own price is not counted on for its company being counted under another
    // member code: one holding its own price that session, carrying one price series with the part over the sessions both
    // are members and both traded, and whose own prices stop more than a quarter of sessions before the part's last, the
    // provider pricing a later code back over the years its company traded under the earlier one. Share classes, whose moves
    // part, and two codes both still trading are each counted. A pair is read in full only where its first moves pass.
    // see: Two member codes carrying one price series are one company counted once, under the code that stops trading until it stops
    static Dictionary<LongPart, HashSet<DateOnly>> OneSeriesNotCounted(IReadOnlyList<LongPart> parts, IReadOnlyList<DateOnly> calendar)
    {
        var at = calendar.Select((session, index) => (session, index)).ToDictionary(pair => pair.session, pair => pair.index);
        var views = parts.Select(part =>
        {
            var closes = new Dictionary<int, decimal>();

            foreach (var bar in LongTraded.Of(part.Bars).Where(bar => bar.Close > 0 && !part.Joined.Contains(bar.Session)))
            {
                if (at.TryGetValue(bar.Session, out var index))
                {
                    closes[index] = bar.Close;
                }
            }

            var member = calendar.Select(session => Holds(part.Spans, session)).ToArray();

            return (Part: part, Closes: closes, Member: member, First: Array.IndexOf(member, true), Last: Array.LastIndexOf(member, true), LastTraded: closes.Count == 0 ? -1 : closes.Keys.Max());
        }).ToArray();
        var notCounted = new Dictionary<LongPart, HashSet<DateOnly>>(ReferenceEqualityComparer.Instance);

        foreach (var own in views)
        {
            if (own.First < 0 || own.LastTraded < 0)
            {
                continue;
            }

            foreach (var other in views)
            {
                if (other.Part.Ticker == own.Part.Ticker || other.First < 0 || other.LastTraded < 0 || own.LastTraded - other.LastTraded <= LongMarks.QuarterSessions)
                {
                    continue;
                }

                var from = Math.Max(own.First, other.First);
                var to = Math.Min(Math.Min(own.Last, other.Last), other.LastTraded);
                var both = new List<int>();

                for (var index = from; index <= to; index++)
                {
                    if (own.Member[index] && other.Member[index] && own.Closes.ContainsKey(index) && other.Closes.ContainsKey(index))
                    {
                        both.Add(index);

                        if (both.Count == LongRenames.RatioDays + 1 && !LongRenames.OneSeries(Paired(both)))
                        {
                            break;
                        }
                    }
                }

                if (both.Count <= LongRenames.RatioDays || !LongRenames.OneSeries(Paired(both)))
                {
                    continue;
                }

                if (!notCounted.TryGetValue(own.Part, out var sessions))
                {
                    notCounted[own.Part] = sessions = [];
                }

                sessions.UnionWith(both.Select(index => calendar[index]));

                IReadOnlyList<(decimal Own, decimal Other)> Paired(List<int> indices) => [.. indices.Select(index => (own.Closes[index], other.Closes[index]))];
            }
        }

        return notCounted;
    }

    static bool Holds(IReadOnlyList<(DateOnly? Joined, DateOnly? Left)> spans, DateOnly session) =>
        spans.Any(span => (span.Joined is not { } joined || joined <= session) && (span.Left is not { } left || left > session));

    // Each code's surprises: the pulled ones where the pull holds the day, and the long calendar's on every other day it
    // holds a percent for.
    static async Task<Dictionary<string, List<SweepSurprise>>> SurprisesAsync(SqliteConnection connection, CancellationToken cancellation)
    {
        var surprises = new Dictionary<string, List<SweepSurprise>>(StringComparer.Ordinal);
        var held = new HashSet<(string, DateOnly)>();

        foreach (var query in new[]
        {
            "SELECT ticker, event_date, timing, surprise_percent FROM pulled_surprise WHERE surprise_percent IS NOT NULL;",
            "SELECT ticker, event_date, timing, surprise_percent FROM long_print WHERE read_from = 'provider' AND surprise_percent IS NOT NULL;",
        })
        {
            await foreach (var row in RowsAsync(connection, query, [], cancellation))
            {
                var ticker = row.GetString(0);
                var day = Day(row.GetString(1));

                if (!held.Add((ticker, day)))
                {
                    continue;
                }

                if (!surprises.TryGetValue(ticker, out var list))
                {
                    surprises[ticker] = list = [];
                }

                list.Add(new SweepSurprise(day, row.GetString(2) == "after", row.GetDouble(3)));
            }
        }

        return surprises;
    }

    // The index-nights no bar was served for, and the names holding no bar or missing a session inside their own series.
    static (long IndexNights, long WithoutBar, int NamesWithout, int NamesMissing) Missing(IReadOnlyList<SweepName> names, DateOnly[] sessions)
    {
        long nights = 0;
        long without = 0;
        var namesWithout = 0;
        var namesMissing = 0;

        foreach (var name in names)
        {
            var days = name.Bars.Select(bar => bar.Session).ToHashSet();
            var memberNights = sessions.Where(name.MemberOn).ToArray();

            nights += memberNights.Length;
            without += memberNights.Count(session => !days.Contains(session));

            if (name.Bars.Length == 0)
            {
                namesWithout++;
            }
            else if (sessions.Any(session => session >= name.Bars[0].Session && session <= name.Bars[^1].Session && !days.Contains(session)))
            {
                namesMissing++;
            }
        }

        return (nights, without, namesWithout, namesMissing);
    }

    async Task<SqliteConnection> OpenAsync(CancellationToken cancellation)
    {
        var connection = new SqliteConnection(SweepHistory.ConnectionString(databaseFile));
        await connection.OpenAsync(cancellation);

        return connection;
    }

    static async Task<SweepBar[]> BarsAsync(SqliteConnection connection, string table, string ticker, string through, CancellationToken cancellation)
    {
        var bars = new List<SweepBar>();

        await foreach (var row in RowsAsync(
            connection,
            table switch
            {
                "bar" => "SELECT session_date, high, low, close, volume, open, raw_close FROM bar WHERE ticker = $ticker AND session_date <= $through ORDER BY session_date;",
                "pulled_bar" => "SELECT session_date, high, low, close, volume, open, raw_close FROM pulled_bar WHERE ticker = $ticker AND pull = (SELECT MAX(pull) FROM pulled_bar WHERE ticker = $ticker) AND session_date <= $through ORDER BY session_date;",
                _ => "SELECT session_date, high, low, close, volume, open, raw_close FROM long_bar WHERE ticker = $ticker AND session_date <= $through ORDER BY session_date;",
            },
            [("$ticker", ticker), ("$through", through)],
            cancellation))
        {
            bars.Add(new SweepBar(
                Day(row.GetString(0)),
                Money.FromStorage(row.GetString(1)),
                Money.FromStorage(row.GetString(2)),
                Money.FromStorage(row.GetString(3)),
                row.GetInt64(4),
                Money.FromStorage(row.GetString(5)),
                row.IsDBNull(6) ? 0m : Money.FromStorage(row.GetString(6))));
        }

        return [.. bars];
    }

    // One statement, read to its end and closed before the caller computes anything over what it returned.
    static async IAsyncEnumerable<SqliteDataReader> RowsAsync(
        SqliteConnection connection,
        string query,
        IReadOnlyList<(string Name, string Value)> parameters,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellation)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = query;

        foreach (var (name, value) in parameters)
        {
            command.Parameters.AddWithValue(name, value);
        }

        await using var reader = await command.ExecuteReaderAsync(cancellation);

        while (await reader.ReadAsync(cancellation))
        {
            yield return reader;
        }
    }

    static DateOnly Day(string text) => DateOnly.ParseExact(text, "yyyy-MM-dd", CultureInfo.InvariantCulture);

    static string Text(DateOnly day) => day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
}
