using System.Globalization;
using EquityBrief.Core.History;
using EquityBrief.Core.Providers;
using EquityBrief.Core.Sweep;
using EquityBrief.Data;
using Microsoft.Data.Sqlite;

namespace EquityBrief.Worker.History;

// The check's settling, by hand on the operator's word, after the folded build and its check on the three indices: each
// difference of 1% or more read off a stock's own prices held to Yahoo's closes, then Tiingo's, over the 260 sessions to it,
// its prices corrected from the source where they part from it and its stock-session marked unreliable with the reading named
// where they agree or no source holds them, and a difference not read off its prices marked the same way; each wild close kept
// where a source agrees, replaced where one holds the session, and marked otherwise; each stretch of another instrument's prices
// filled from a source as a hole is, or excluded as a block; the requests ordered by the stock-sessions each affects, largest
// first. Then the draw of stock-sessions held to Tiingo and the draw of corrected ones for the operator.
// see: A difference of 1% or more the check finds is settled by a second source's closes where one agrees with a side, and otherwise its stock-session is marked unreliable and skipped by every reader of the table
public sealed partial class SecondSourceFill
{
    public const string SettleStage = "check-settle";

    public const string Accepted = "accepted";

    public const string BuiltFix = "covered by a built fix";

    public const string Corrected = "corrected";

    public const string Agreeing = "marked: its prices agree with a second source";

    public const string NoSource = "marked: no second source holds its prices";

    public const string NotItsPrices = "marked: not read off its prices";

    public const string NextMonth = "marked: Tiingo's month is spent, retried with next month's";

    // A source holds a window where it holds nine in ten of its sessions and the session itself.
    public const double HeldShare = 0.9;

    // A stretch is excluded as a block where more than a tenth of its sessions would be marked.
    public const double MarkedShare = 0.1;

    // How each difference, wild close and stretch was settled.
    public sealed record Settled(string Category, string Index, string Ticker, DateOnly Session, string Reading, string Grouped, string Outcome, string? Source, int Sessions);

    // The differences the check found on the three indices settled, then every code's wild closes and its stretches of another
    // instrument's prices, without the word stating what it would ask and asking nothing.
    public async Task<IReadOnlyList<Settled>> SettleAsync(IReadOnlyList<SessionDifference> differences, bool word, TextWriter output, CancellationToken cancellation = default)
    {
        var runId = RunId(SettleStage);
        var startedAt = Instant(clock.UtcNow);
        var reader = new LongHistoryReader(databaseFile);
        var through = await reader.NewestStoredSessionAsync(cancellation);
        var settled = new List<Settled>();

        foreach (var group in differences.GroupBy(one => (one.Index, Grouped: CheckSettling.Group(one), one.Reading)).OrderBy(group => group.Key.Index, StringComparer.Ordinal).ThenBy(group => group.Key.Grouped, StringComparer.Ordinal).ThenByDescending(group => group.Count()))
        {
            output.WriteLine(FormattableString.Invariant($"{SettleStage}: {Named(group.Key.Index)}, {group.Key.Grouped}, {group.Key.Reading}: {group.Count()} difference(s)"));
        }

        var spans = await reader.SpansAsync(cancellation);
        var codes = spans.Select(span => span.Ticker).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
        var series = new Dictionary<string, SweepBar[]>(StringComparer.Ordinal);

        async Task<SweepBar[]> SeriesOf(string ticker)
        {
            if (!series.TryGetValue(ticker, out var held))
            {
                series[ticker] = held = (await reader.CodeSeriesAsync(ticker, through, cancellation)).Series;
            }

            return held;
        }

        var wild = new List<(string Ticker, IReadOnlyList<DateOnly> Sessions)>();

        foreach (var ticker in codes)
        {
            var found = PriceAudit.Wild(await SeriesOf(ticker)).Where(session => session >= LongLedger.From).ToArray();

            if (found.Length > 0)
            {
                wild.Add((ticker, found));
            }

            series.Remove(ticker);
        }

        var stretches = (await reader.PartedAsync(through, cancellation)).Where(one => one.Parted.Count > 0).ToArray();
        var priced = differences.Where(one => CheckSettling.Group(one) == CheckSettling.Priced).GroupBy(one => one.Ticker, StringComparer.Ordinal).ToArray();

        output.WriteLine(FormattableString.Invariant($"{SettleStage}: {priced.Length} code(s) with {priced.Sum(group => group.Select(one => one.Session).Distinct().Count())} stock-session(s) differing by 1% or more off their own prices, {wild.Count} code(s) with {wild.Sum(one => one.Sessions.Count)} wild close(s), {stretches.Length} code(s) with a stretch of another instrument's prices over {stretches.Sum(one => one.Parted.Count)} session(s)"));

        if (!word)
        {
            output.WriteLine($"{SettleStage}: nothing asked; give '--word' to ask the sources and write what they settle");

            return [];
        }

        foreach (var difference in differences.Where(one => CheckSettling.Group(one) != CheckSettling.Priced))
        {
            var grouped = CheckSettling.Group(difference);
            var outcome = grouped == CheckSettling.Small ? Accepted : grouped == CheckSettling.Built ? BuiltFix : NotItsPrices;

            settled.Add(new Settled("difference", difference.Index, difference.Ticker, difference.Session, difference.Reading, grouped, outcome, null, 1));
        }

        await WriteSettledAsync(runId, differences, settled, [], [.. settled.Where(one => one.Outcome == NotItsPrices).Select(one => (one.Ticker, one.Session, one.Session, one.Reading, MarkedSessions.Unreliable, "a difference of 1% or more on a reading not read off its own prices"))], [], cancellation);

        // The requests in the order of the stock-sessions each affects, largest first, across the three kinds.
        var units = priced.Select(group => (Weight: group.Select(one => one.Session).Distinct().Count(), Kind: 0, Ticker: group.Key))
            .Concat(wild.Select(one => (Weight: one.Sessions.Count, Kind: 1, one.Ticker)))
            .Concat(stretches.Select(one => (Weight: one.Parted.Count, Kind: 2, one.Ticker)))
            .OrderByDescending(unit => unit.Weight).ThenBy(unit => unit.Ticker, StringComparer.Ordinal)
            .ToArray();

        foreach (var (_, kind, ticker) in units)
        {
            var bars = await SeriesOf(ticker);
            var outcome = kind switch
            {
                0 => await SettlePricedAsync(runId, ticker, bars, [.. priced.Single(group => group.Key == ticker)], cancellation),
                1 => await SettleWildAsync(runId, ticker, bars, wild.Single(one => one.Ticker == ticker).Sessions, cancellation),
                _ => await SettleStretchAsync(runId, ticker, bars, stretches.Single(one => one.Ticker == ticker).Parted, cancellation),
            };

            settled.AddRange(outcome);
            series.Remove(ticker);
            output.WriteLine(FormattableString.Invariant($"{SettleStage}: {ticker}: {string.Join(", ", outcome.GroupBy(one => one.Outcome).Select(group => $"{group.Count()} {group.Key}"))}"));
        }

        await LogSettleAsync(runId, startedAt, settled, cancellation);

        foreach (var group in settled.GroupBy(one => (one.Category, one.Outcome)).OrderBy(group => group.Key.Category, StringComparer.Ordinal))
        {
            output.WriteLine(FormattableString.Invariant($"{SettleStage}: {group.Key.Category}: {group.Key.Outcome}, {group.Count()} ({group.Sum(one => one.Sessions)} stock-session(s))"));
        }

        return settled;
    }

    // A code's differences of 1% or more off its own prices: its closes over the 260 sessions to each held to Yahoo's, then
    // Tiingo's; corrected from the source where some part from it by 1% or more, marked where none does or no source holds them.
    public async Task<IReadOnlyList<Settled>> SettlePricedAsync(string runId, string ticker, SweepBar[] series, IReadOnlyList<SessionDifference> differences, CancellationToken cancellation = default)
    {
        var sessions = differences.Select(one => one.Session).Distinct().Order().ToArray();
        var windows = sessions.ToDictionary(session => session, session => (IReadOnlyCollection<DateOnly>)series.Where(bar => bar.Session <= session).TakeLast(CheckSettling.SettledSessions).Select(bar => bar.Session).ToHashSet());
        var from = windows.Values.Where(window => window.Count > 0).Select(window => window.Min()).DefaultIfEmpty(sessions[0]).Min().AddDays(-5);
        var to = sessions[^1].AddDays(5);
        var answers = new Dictionary<string, SourceAnswer?>(StringComparer.Ordinal);
        var corrections = new Dictionary<DateOnly, (KeptBar Bar, string Source, decimal Ratio)>();
        var marks = new List<(DateOnly Session, string Reading, string Why)>();
        var checks = new List<(string Purpose, string Index, DateOnly Session, decimal Stored, string Source, decimal? Own, decimal? Scaled, bool? Agrees)>();
        var settled = new List<Settled>();

        foreach (var session in sessions)
        {
            var window = windows[session];
            (string Source, Audit Audit)? decided = null;
            var monthSpent = false;

            foreach (var source in new[] { SecondSources.Yahoo, SecondSources.Tiingo })
            {
                if (!answers.TryGetValue(source, out var answer))
                {
                    var (asked, why, _) = await AskAsync(runId, source, ticker, SecondSources.Symbol(ticker, source), from, to, (from.AddDays(5), sessions[^1]), cancellation);

                    answers[source] = answer = asked;
                    monthSpent |= why.Contains("month", StringComparison.Ordinal);
                }

                if (answer is { Bars.Count: > 0 } && PriceAudit.Hold(series, answer.Bars, window) is { } audit
                    && audit.Held.Count >= HeldShare * window.Count && audit.Held.Any(held => held.Session == session))
                {
                    decided = (source, audit);

                    break;
                }
            }

            var here = differences.Where(one => one.Session == session).ToArray();

            if (decided is not { } settledBy)
            {
                var why = monthSpent ? NextMonth : NoSource;

                marks.AddRange(here.Select(one => (session, one.Reading, why)));
                settled.AddRange(here.Select(one => new Settled("difference", one.Index, ticker, session, one.Reading, CheckSettling.Priced, why, null, 1)));

                continue;
            }

            var apart = settledBy.Audit.Apart.ToArray();
            var own = settledBy.Audit.Held.Single(held => held.Session == session);

            checks.Add(("settle", here[0].Index, session, own.Stored, settledBy.Source, null, own.Source, own.Agrees));

            if (apart.Length == 0)
            {
                marks.AddRange(here.Select(one => (session, one.Reading, FormattableString.Invariant($"its closes over the {window.Count} sessions to it agree with {settledBy.Source}'s within 1%, so the difference is not in its prices"))));
                settled.AddRange(here.Select(one => new Settled("difference", one.Index, ticker, session, one.Reading, CheckSettling.Priced, Agreeing, settledBy.Source, 1)));

                continue;
            }

            var answered = answers[settledBy.Source]!.Bars.GroupBy(bar => bar.Session).ToDictionary(group => group.Key, group => group.First());

            foreach (var parted in apart)
            {
                var stored = series.Single(bar => bar.Session == parted.Session);

                corrections[parted.Session] = (Correction(stored, answered[parted.Session], settledBy.Audit.Ratio), settledBy.Source, settledBy.Audit.Ratio);
                checks.Add(("settle", here[0].Index, parted.Session, parted.Stored, settledBy.Source, null, parted.Source, false));
            }

            settled.AddRange(here.Select(one => new Settled("difference", one.Index, ticker, session, one.Reading, CheckSettling.Priced, Corrected, settledBy.Source, 1)));
        }

        await WriteSettledAsync(runId, differences, settled, [.. corrections.Select(pair => (ticker, pair.Value.Bar, pair.Value.Source, pair.Value.Ratio))], [.. marks.Select(mark => (ticker, mark.Session, mark.Session, mark.Reading, MarkedSessions.Unreliable, mark.Why))], [.. checks.Select(check => (check.Purpose, check.Index, ticker, check.Session, check.Stored, check.Source, check.Own, check.Scaled, check.Agrees))], cancellation);

        return settled;
    }

    // A code's wild closes: each kept where Yahoo's close, then Tiingo's, agrees with it within 1%; replaced from the first
    // source holding the session whose closes around it agree with the store's; and marked unreliable otherwise.
    public async Task<IReadOnlyList<Settled>> SettleWildAsync(string runId, string ticker, SweepBar[] series, IReadOnlyList<DateOnly> sessions, CancellationToken cancellation = default)
    {
        var (from, to) = (sessions[0].AddDays(-SecondSources.Margin), sessions[^1].AddDays(SecondSources.Margin));
        var answers = new Dictionary<string, SourceAnswer?>(StringComparer.Ordinal);
        var corrections = new List<(string, KeptBar, string, decimal)>();
        var marks = new List<(string, DateOnly, DateOnly, string, string, string)>();
        var checks = new List<(string, string, string, DateOnly, decimal, string, decimal?, decimal?, bool?)>();
        var settled = new List<Settled>();

        foreach (var session in sessions)
        {
            var around = PriceAudit.Around(series, session);
            var outcome = (string?)null;
            string? by = null;
            var monthSpent = false;
            (string Source, Audit Audit, SourceBar Bar)? replaceWith = null;

            foreach (var source in new[] { SecondSources.Yahoo, SecondSources.Tiingo })
            {
                if (!answers.TryGetValue(source, out var answer))
                {
                    var (asked, why, _) = await AskAsync(runId, source, ticker, SecondSources.Symbol(ticker, source), from, to, (sessions[0], sessions[^1]), cancellation);

                    answers[source] = answer = asked;
                    monthSpent |= why.Contains("month", StringComparison.Ordinal);
                }

                if (answer is not { Bars.Count: > 0 } || PriceAudit.Hold(series, answer.Bars, [.. around.Where(day => day != session)]) is not { } audit)
                {
                    continue;
                }

                var theirs = answer.Bars.FirstOrDefault(bar => bar.Session == session);

                if (theirs is null)
                {
                    continue;
                }

                var stored = series.Single(bar => bar.Session == session);
                var scaled = (theirs.Adjusted ?? theirs.Close) * audit.Ratio;
                var agrees = scaled > 0 && Math.Abs(stored.Close / scaled - 1m) < PriceAudit.Agree;

                checks.Add(("wild", "all", ticker, session, stored.Close, source, theirs.Close, decimal.Round(scaled, 4), agrees));

                if (agrees)
                {
                    (outcome, by) = ($"kept: {source} agrees", source);

                    break;
                }

                if (replaceWith is null && audit.Held.Count(held => held.Agrees) >= HeldShare * audit.Held.Count)
                {
                    replaceWith = (source, audit, theirs);
                }
            }

            if (outcome is null && replaceWith is { } replacing)
            {
                corrections.Add((ticker, Correction(series.Single(bar => bar.Session == session), replacing.Bar, replacing.Audit.Ratio), replacing.Source, replacing.Audit.Ratio));
                (outcome, by) = ($"replaced from {replacing.Source}", replacing.Source);
            }

            if (outcome is null)
            {
                outcome = monthSpent ? NextMonth : "marked: no source agrees with it or replaces it";
                marks.Add((ticker, session, session, "close", MarkedSessions.Unreliable, "a wild close no second source agrees with or replaces"));
            }

            settled.Add(new Settled("wild", "all", ticker, session, "close", "d", outcome, by, 1));
        }

        await WriteSettledAsync(runId, [], settled, corrections, marks, checks, cancellation);

        return settled;
    }

    // A code's stretch of another instrument's prices: held to Tiingo's, then Yahoo's, over it; kept where the source agrees
    // with nine in ten of its closes, otherwise filled from the source as a hole is, and excluded as a block where no source
    // fills it or more than a tenth of it would be marked.
    public async Task<IReadOnlyList<Settled>> SettleStretchAsync(string runId, string ticker, SweepBar[] series, IReadOnlyList<DateOnly> parted, CancellationToken cancellation = default)
    {
        var (first, last) = (parted.Min(), parted.Max());
        var stretch = series.Where(bar => bar.Session >= first && bar.Session <= last).Select(bar => bar.Session).ToArray();
        var outside = series.Where(bar => bar.Session < first || bar.Session > last).ToArray();
        var (from, to) = (first.AddDays(-SecondSources.Margin), last.AddDays(SecondSources.Margin));
        var corrections = new List<(string, KeptBar, string, decimal)>();
        var marks = new List<(string, DateOnly, DateOnly, string, string, string)>();
        var outcome = "excluded: no source fills it";
        string? by = null;
        var monthSpent = false;

        foreach (var source in new[] { SecondSources.Tiingo, SecondSources.Yahoo })
        {
            var (answer, why, _) = await AskAsync(runId, source, ticker, SecondSources.Symbol(ticker, source), from, to, (first, last), cancellation);

            monthSpent |= why.Contains("month", StringComparison.Ordinal);

            if (answer is not { Bars.Count: > 0 })
            {
                continue;
            }

            // The stretch held at the ratio of the sessions around it, which its own prices cannot set.
            var around = outside.Where(bar => bar.Session < first).TakeLast(SecondSources.Side).Concat(outside.Where(bar => bar.Session > last).Take(SecondSources.Side)).Select(bar => bar.Session).ToHashSet();

            if (PriceAudit.Hold(series, answer.Bars, around) is { } scale
                && PriceAudit.Hold(series, answer.Bars, stretch, scale.Ratio) is { } audit && audit.Held.Count >= HeldShare * stretch.Length && audit.Held.Count(held => !held.Agrees) <= MarkedShare * stretch.Length)
            {
                (outcome, by) = ($"kept: {source} agrees with its prices", source);

                break;
            }

            var judgement = SecondSources.Judge(outside, stretch, answer.Bars, partial: true);

            if (judgement.Kept.Count == 0)
            {
                continue;
            }

            var unkept = stretch.Except(judgement.Kept.Select(bar => bar.Session)).ToArray();

            if (unkept.Length > MarkedShare * stretch.Length)
            {
                outcome = FormattableString.Invariant($"excluded: {source} fills {judgement.Kept.Count} of its {stretch.Length} session(s)");
                by = source;

                break;
            }

            corrections.AddRange(judgement.Kept.Select(bar => (ticker, bar, source, judgement.Ratio!.Value)));
            marks.AddRange(unkept.Select(session => (ticker, session, session, "close", MarkedSessions.Unreliable, $"a session of a stretch {source} filled the rest of")));
            (outcome, by) = ($"filled from {source}", source);

            break;
        }

        if (outcome.StartsWith("excluded", StringComparison.Ordinal))
        {
            if (monthSpent && by is null)
            {
                outcome = NextMonth;
            }

            marks.Add((ticker, first, last, MarkedSessions.Stretch, MarkedSessions.Excluded, outcome));
        }

        Settled[] settled = [new Settled("stretch", "all", ticker, first, MarkedSessions.Stretch, "e", outcome, by, stretch.Length)];

        await WriteSettledAsync(runId, [], settled, corrections, marks, [], cancellation);

        return settled;
    }

    // A stored session corrected from a source's bar at the ratio the source's closes hold to the store's around it, its close as
    // traded at the store's own adjustment of the session and its volume the store's.
    static KeptBar Correction(SweepBar stored, SourceBar bar, decimal ratio)
    {
        var own = bar.Close > 0 ? (bar.Adjusted ?? bar.Close) / bar.Close * ratio : ratio;
        var close = Core.Prices.PriceForm.Round((bar.Adjusted ?? bar.Close) * ratio);

        return new KeptBar(
            stored.Session,
            Core.Prices.PriceForm.Round(bar.Open * own),
            Core.Prices.PriceForm.Round(Math.Max(bar.High * own, close)),
            Core.Prices.PriceForm.Round(Math.Min(bar.Low * own, close)),
            close,
            stored.RawClose > 0 && stored.Close > 0 ? Core.Prices.PriceForm.Round(close * stored.RawClose / stored.Close) : close,
            stored.Volume);
    }

    // The stock-sessions corrected by the newest settling whose recheck still finds the table and the ledger apart by 1% or
    // more, each marked unreliable with its reading named, and every corrected one written with whether it stood.
    public async Task<IReadOnlyList<(string Ticker, DateOnly Session, bool Stands)>> ProveAsync(IReadOnlyList<SessionDifference> recheck, bool word, TextWriter output, CancellationToken cancellation = default)
    {
        var corrected = await CorrectedAsync(cancellation);
        var still = recheck.Where(one => CheckSettling.Size(one) >= CheckSettling.Accepted).ToArray();
        var proved = corrected.Select(pair => (pair.Ticker, pair.Session, Stands: !still.Any(one => one.Ticker == pair.Ticker && one.Session == pair.Session))).ToArray();

        output.WriteLine(FormattableString.Invariant($"{SettleStage}: {proved.Count(one => one.Stands)} of {proved.Length} corrected stock-session(s) stand, the recheck finding the table and the ledger agreeing; {proved.Count(one => !one.Stands)} marked unreliable instead"));

        foreach (var one in proved.Where(one => !one.Stands))
        {
            output.WriteLine(FormattableString.Invariant($"{SettleStage}: not proved {one.Ticker} {one.Session:yyyy-MM-dd}: {string.Join(", ", still.Where(difference => difference.Ticker == one.Ticker && difference.Session == one.Session).Select(difference => difference.Reading).Distinct())}"));
        }

        if (word)
        {
            var runId = RunId(SettleStage + "-proof");

            await WriteSettledAsync(runId, [], [.. proved.Select(one => new Settled("proof", "all", one.Ticker, one.Session, "close", "proof", one.Stands ? "stands" : "marked: the recheck still differs", null, 1))], [],
                [.. proved.Where(one => !one.Stands).SelectMany(one => still.Where(difference => difference.Ticker == one.Ticker && difference.Session == one.Session).Select(difference => (one.Ticker, one.Session, one.Session, difference.Reading, MarkedSessions.Unreliable, "corrected, and the recheck still finds the table and the ledger apart")))], [], cancellation);
        }

        return proved;
    }

    // The stock-sessions the newest settling corrected, each on its index.
    public async Task<IReadOnlyList<(string Index, string Ticker, DateOnly Session)>> CorrectedOnAsync(CancellationToken cancellation = default)
    {
        var corrected = new List<(string, string, DateOnly)>();

        await using var connection = await OpenAsync(cancellation);
        await using var select = connection.CreateCommand();

        select.CommandText = $"SELECT DISTINCT index_code, ticker, session_date FROM check_settled WHERE outcome = '{Corrected}' AND run_id = (SELECT MAX(run_id) FROM check_settled WHERE outcome = '{Corrected}') ORDER BY index_code, ticker, session_date;";

        await using var rows = await select.ExecuteReaderAsync(cancellation);

        while (await rows.ReadAsync(cancellation))
        {
            corrected.Add((rows.GetString(0), rows.GetString(1), Day(rows.GetString(2))));
        }

        return corrected;
    }

    // The stock-sessions the newest settling corrected.
    public async Task<IReadOnlyList<(string Ticker, DateOnly Session)>> CorrectedAsync(CancellationToken cancellation = default)
    {
        var corrected = new List<(string, DateOnly)>();

        await using var connection = await OpenAsync(cancellation);
        await using var select = connection.CreateCommand();

        select.CommandText = $"SELECT DISTINCT ticker, session_date FROM check_settled WHERE outcome = '{Corrected}' AND run_id = (SELECT MAX(run_id) FROM check_settled WHERE outcome = '{Corrected}') ORDER BY ticker, session_date;";

        await using var rows = await select.ExecuteReaderAsync(cancellation);

        while (await rows.ReadAsync(cancellation))
        {
            corrected.Add((rows.GetString(0), Day(rows.GetString(1))));
        }

        return corrected;
    }

    // The hidden error rate: stock-sessions drawn by a seed across the three indices' members and every year, flagged or not, at
    // most the codes given and as many sessions a code as the count needs, each close held to Tiingo's at the ratio its closes
    // hold to the store's around it; the share agreeing within 1% by index and period.
    public async Task<IReadOnlyList<(string Index, string Ticker, DateOnly Session, bool? Agrees)>> SampleAsync(int count, int codes, string seed, bool word, TextWriter output, CancellationToken cancellation = default)
    {
        var reader = new LongHistoryReader(databaseFile);
        var through = await reader.NewestStoredSessionAsync(cancellation);
        var spans = await reader.SpansAsync(cancellation);
        var pairs = spans.Select(span => (span.Index, span.Ticker)).Distinct().ToArray();
        var drawnCodes = pairs.OrderBy(pair => Hash($"{seed}|{pair.Index}|{pair.Ticker}"), StringComparer.Ordinal).GroupBy(pair => pair.Ticker, StringComparer.Ordinal).Take(codes).Select(group => group.First()).ToArray();
        var each = (count + drawnCodes.Length - 1) / Math.Max(1, drawnCodes.Length);
        var drawn = new List<(string Index, string Ticker, DateOnly Session)>();
        var held = new Dictionary<string, SweepBar[]>(StringComparer.Ordinal);

        foreach (var (index, ticker) in drawnCodes)
        {
            var series = (await reader.CodeSeriesAsync(ticker, through, cancellation)).Series;
            var own = spans.Where(span => span.Index == index && span.Ticker == ticker).ToArray();

            held[ticker] = series;
            drawn.AddRange(series
                .Where(bar => bar.Session >= LongLedger.From && own.Any(span => (span.Joined is not { } joined || joined <= bar.Session) && (span.Left is not { } left || left > bar.Session)))
                .OrderBy(bar => Hash(FormattableString.Invariant($"{seed}|{index}|{ticker}|{bar.Session:yyyy-MM-dd}")), StringComparer.Ordinal)
                .Take(each)
                .Select(bar => (index, ticker, bar.Session)));
        }

        drawn = [.. drawn.Take(count)];
        output.WriteLine(FormattableString.Invariant($"{SettleStage}: {drawn.Count} stock-session(s) drawn by the seed {seed} from {drawnCodes.Length} code(s), {each} a code, each held to Tiingo's close; at most {drawnCodes.Length} request(s) of Tiingo, within its {TiingoAllowance.PerHour} an hour, {TiingoAllowance.PerDay} a day and {TiingoAllowance.CodesPerMonth} codes a month"));

        if (!word)
        {
            output.WriteLine($"{SettleStage}: nothing asked; give '--word' to ask Tiingo");

            return [];
        }

        var runId = RunId(SettleStage + "-sample");
        var results = new List<(string Index, string Ticker, DateOnly Session, bool? Agrees)>();
        var checks = new List<(string, string, string, DateOnly, decimal, string, decimal?, decimal?, bool?)>();

        foreach (var code in drawn.GroupBy(one => one.Ticker, StringComparer.Ordinal))
        {
            var sessions = code.Select(one => one.Session).Order().ToArray();
            var (answer, _, _) = await AskAsync(runId, SecondSources.Tiingo, code.Key, SecondSources.Symbol(code.Key, SecondSources.Tiingo), sessions[0].AddDays(-SecondSources.Margin), sessions[^1].AddDays(SecondSources.Margin), (sessions[0], sessions[^1]), cancellation);
            var series = held[code.Key];

            foreach (var (index, ticker, session) in code)
            {
                var audit = answer is { Bars.Count: > 0 } ? PriceAudit.Hold(series, answer.Bars, PriceAudit.Around(series, session)) : null;
                var one = audit?.Held.FirstOrDefault(close => close.Session == session);
                var stored = series.First(bar => bar.Session == session).Close;

                results.Add((index, ticker, session, one?.Agrees));
                checks.Add(("sample", index, ticker, session, stored, SecondSources.Tiingo, answer?.Bars.FirstOrDefault(bar => bar.Session == session)?.Close, one?.Source, one?.Agrees));
            }
        }

        await WriteSettledAsync(runId, [], [], [], [], checks, cancellation);

        foreach (var group in results.GroupBy(one => (one.Index, Period: PriceAudit.Period(one.Session))).OrderBy(group => group.Key.Index, StringComparer.Ordinal).ThenBy(group => group.Key.Period, StringComparer.Ordinal))
        {
            var read = group.Where(one => one.Agrees is not null).ToArray();

            output.WriteLine(FormattableString.Invariant($"{SettleStage}: sample {Named(group.Key.Index)}, {group.Key.Period}: {read.Count(one => one.Agrees == true)} of {read.Length} agree within 1% ({(read.Length == 0 ? 0 : 100.0 * read.Count(one => one.Agrees == true) / read.Length):0.0}%), {group.Count() - read.Length} Tiingo holds no price for"));
        }

        foreach (var stock in results.Where(one => one.Agrees == false).GroupBy(one => one.Ticker, StringComparer.Ordinal).OrderByDescending(group => group.Count()))
        {
            output.WriteLine(FormattableString.Invariant($"{SettleStage}: sample disagrees {stock.Key}: {string.Join(", ", stock.Select(one => one.Session.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)))}"));
        }

        return results;
    }

    // Corrected stock-sessions drawn by a seed for the operator to check, each with the provider's close, Tiingo's and Yahoo's
    // as each sends it, and the date.
    public async Task<IReadOnlyList<(string Ticker, DateOnly Session, decimal? Provider, decimal? Tiingo, decimal? Yahoo)>> ProofDrawAsync(int count, string seed, bool word, TextWriter output, CancellationToken cancellation = default)
    {
        var corrected = await CorrectionsAsync(cancellation);
        var drawn = corrected.OrderBy(one => Hash(FormattableString.Invariant($"{seed}|{one.Ticker}|{one.Session:yyyy-MM-dd}")), StringComparer.Ordinal).Take(count).OrderBy(one => one.Ticker, StringComparer.Ordinal).ThenBy(one => one.Session).ToArray();
        var reader = new LongHistoryReader(databaseFile);
        var shown = new List<(string, DateOnly, decimal?, decimal?, decimal?)>();

        output.WriteLine(FormattableString.Invariant($"{SettleStage}: {drawn.Length} of {corrected.Count} corrected stock-session(s) drawn by the seed {seed}; at most {drawn.Length} request(s) each of Yahoo and Tiingo"));

        if (!word)
        {
            return [];
        }

        var runId = RunId(SettleStage + "-proof-draw");
        var checks = new List<(string, string, string, DateOnly, decimal, string, decimal?, decimal?, bool?)>();

        foreach (var (ticker, session) in drawn)
        {
            var provider = await reader.ProviderCloseAsync(ticker, session, cancellation);
            var (yahoo, _, _) = await AskAsync(runId, SecondSources.Yahoo, ticker, SecondSources.Symbol(ticker, SecondSources.Yahoo), session.AddDays(-7), session.AddDays(7), (session, session), cancellation);
            var (tiingoAnswer, _, _) = await AskAsync(runId, SecondSources.Tiingo, ticker, SecondSources.Symbol(ticker, SecondSources.Tiingo), session.AddDays(-7), session.AddDays(7), (session, session), cancellation);
            var y = yahoo?.Bars.FirstOrDefault(bar => bar.Session == session)?.Close;
            var t = tiingoAnswer?.Bars.FirstOrDefault(bar => bar.Session == session)?.Close;

            shown.Add((ticker, session, provider?.RawClose, t, y));
            checks.Add(("proof", "all", ticker, session, provider?.RawClose ?? 0m, SecondSources.Tiingo, t, null, null));
            checks.Add(("proof", "all", ticker, session, provider?.RawClose ?? 0m, SecondSources.Yahoo, y, null, null));
            output.WriteLine(FormattableString.Invariant($"{SettleStage}: proof {session:yyyy-MM-dd} {ticker}: the provider's close {Shown(provider?.RawClose)}, Tiingo's {Shown(t)}, Yahoo's {Shown(y)}"));
        }

        await WriteSettledAsync(runId, [], [], [], [], checks, cancellation);

        return shown;
    }

    static string Shown(decimal? close) => close is { } value ? value.ToString("0.####", CultureInfo.InvariantCulture) : "none";

    async Task<IReadOnlyList<(string Ticker, DateOnly Session)>> CorrectionsAsync(CancellationToken cancellation)
    {
        var corrected = new List<(string, DateOnly)>();

        await using var connection = await OpenAsync(cancellation);
        await using var select = connection.CreateCommand();

        select.CommandText = $"SELECT DISTINCT ticker, session_date FROM long_fill_bar WHERE kind = '{SecondSources.CorrectionKind}' ORDER BY ticker, session_date;";

        await using var rows = await select.ExecuteReaderAsync(cancellation);

        while (await rows.ReadAsync(cancellation))
        {
            corrected.Add((rows.GetString(0), Day(rows.GetString(1))));
        }

        return corrected;
    }

    // What one settling writes, in one transaction: each difference and its outcome, each correction, each mark and each close
    // held to a source.
    async Task WriteSettledAsync(
        string runId,
        IReadOnlyList<SessionDifference> differences,
        IReadOnlyList<Settled> settled,
        IReadOnlyList<(string Ticker, KeptBar Bar, string Source, decimal Ratio)> corrections,
        IReadOnlyList<(string Ticker, DateOnly First, DateOnly Last, string Reading, string Kind, string Why)> marks,
        IReadOnlyList<(string Purpose, string Index, string Ticker, DateOnly Session, decimal Stored, string Source, decimal? Own, decimal? Scaled, bool? Agrees)> checks,
        CancellationToken cancellation)
    {
        var markedAt = Instant(clock.UtcNow);

        await using var connection = await OpenAsync(cancellation);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellation);

        foreach (var one in settled.Where(one => one.Category == "difference"))
        {
            foreach (var difference in differences.Where(difference => difference.Ticker == one.Ticker && difference.Session == one.Session && difference.Reading == one.Reading && difference.Index == one.Index))
            {
                await ExecuteAsync(connection, transaction, InsertSettled,
                    [("$run_id", runId), ("$index", difference.Index), ("$kind", difference.Kind), ("$family", difference.Family ?? string.Empty), ("$ticker", difference.Ticker), ("$session", Text(difference.Session)), ("$reading", difference.Reading),
                     ("$table", (object?)difference.Table ?? DBNull.Value), ("$other", (object?)difference.Other ?? DBNull.Value), ("$grouped", one.Grouped), ("$outcome", one.Outcome), ("$source", (object?)one.Source ?? DBNull.Value)], cancellation);
            }
        }

        foreach (var one in settled.Where(one => one.Category != "difference"))
        {
            await ExecuteAsync(connection, transaction, InsertSettled,
                [("$run_id", runId), ("$index", one.Index), ("$kind", one.Category), ("$family", string.Empty), ("$ticker", one.Ticker), ("$session", Text(one.Session)), ("$reading", one.Reading),
                 ("$table", DBNull.Value), ("$other", DBNull.Value), ("$grouped", one.Grouped), ("$outcome", one.Outcome), ("$source", (object?)one.Source ?? DBNull.Value)], cancellation);
        }

        foreach (var (ticker, bar, source, ratio) in corrections)
        {
            await ExecuteAsync(connection, transaction, InsertCorrection,
                [("$ticker", ticker), ("$session", Text(bar.Session)), ("$open", Money.ToStorage(bar.Open)), ("$high", Money.ToStorage(bar.High)), ("$low", Money.ToStorage(bar.Low)), ("$close", Money.ToStorage(bar.Close)), ("$raw", Money.ToStorage(bar.RawClose)),
                 ("$volume", bar.Volume), ("$source", source), ("$ratio", ratio.ToString(RatioForm, CultureInfo.InvariantCulture)), ("$run_id", runId)], cancellation);
        }

        foreach (var (ticker, first, last, reading, kind, why) in marks)
        {
            await ExecuteAsync(connection, transaction, InsertMark,
                [("$run_id", runId), ("$ticker", ticker), ("$first", Text(first)), ("$last", Text(last)), ("$reading", reading), ("$kind", kind), ("$why", why), ("$at", markedAt)], cancellation);
        }

        foreach (var (purpose, index, ticker, session, stored, source, own, scaled, agrees) in checks)
        {
            await ExecuteAsync(connection, transaction, InsertCheck,
                [("$run_id", runId), ("$purpose", purpose), ("$index", index), ("$ticker", ticker), ("$session", Text(session)), ("$stored", Money.ToStorage(stored)), ("$source", source),
                 ("$own", own is { } o ? Money.ToStorage(o) : DBNull.Value), ("$scaled", scaled is { } s ? Money.ToStorage(s) : DBNull.Value), ("$agrees", agrees is { } a ? (a ? 1 : 0) : DBNull.Value)], cancellation);
        }

        await transaction.CommitAsync(cancellation);
    }

    async Task LogSettleAsync(string runId, string startedAt, IReadOnlyList<Settled> settled, CancellationToken cancellation)
    {
        await using var connection = await OpenAsync(cancellation);

        await ExecuteAsync(connection, null, @"
            INSERT INTO run_log (run_id, stage, started_at, ended_at, outcome, rows_written, model_calls, network_requests, spend, detail)
            VALUES ($run_id, $stage, $started_at, $ended_at, 'ok', $rows, 0, 0, '0', $detail);",
            [("$run_id", runId), ("$stage", SettleStage), ("$started_at", startedAt), ("$ended_at", Instant(clock.UtcNow)), ("$rows", settled.Count),
             ("$detail", string.Join("; ", settled.GroupBy(one => (one.Category, one.Outcome)).Select(group => FormattableString.Invariant($"{group.Key.Category} {group.Key.Outcome}: {group.Count()}"))))], cancellation);
    }

    static async Task ExecuteAsync(SqliteConnection connection, SqliteTransaction? transaction, string sql, IReadOnlyList<(string Name, object Value)> parameters, CancellationToken cancellation)
    {
        await using var command = connection.CreateCommand();

        command.Transaction = transaction;
        command.CommandText = sql;

        foreach (var (name, value) in parameters)
        {
            command.Parameters.AddWithValue(name, value);
        }

        await command.ExecuteNonQueryAsync(cancellation);
    }

    string RunId(string stage) => FormattableString.Invariant($"{stage}-{clock.UtcNow:yyyyMMddTHHmmssZ}-{Guid.NewGuid():N}")[..(stage.Length + 25)];

    static string Hash(string text) => Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(text)));
}
