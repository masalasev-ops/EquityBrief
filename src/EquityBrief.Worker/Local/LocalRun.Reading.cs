using System.Globalization;
using System.Text.Json;
using EquityBrief.Core.Configuration;
using EquityBrief.Core.Providers;
using EquityBrief.Core.Releases;
using EquityBrief.Worker.Sweep;
using Microsoft.Data.Sqlite;

namespace EquityBrief.Worker.Local;

// One stored results release waiting for its reading: its accession, its filer, its text as stored and the hash and
// instant it was stored under.
sealed record UnreadRelease(string Accession, string Cik, byte[] Gzip, string Sha256, DateTimeOffset Accepted);

// What one reading came to: the four words or the cause the answer was refused for, how long the call took and the
// prompt's tokens.
sealed record ReadingTaken(ReleaseAnswerRead Read, double? Seconds, int? PromptTokens);

// From 20.3 the local run reads the stored results releases, each with every name and date in it replaced by a neutral
// word, under the four questions, and keeps each reading under the pin it was read under, read or refused with its
// cause: a slot the releases accepted in the week before its day that no reading under the pin holds, newest first,
// asking nothing past its last instant; the history by hand every release no reading under the pin holds, oldest
// first, stopping cleanly at the instant it is given and pausing for each slot and for the night, the pin's history
// marked complete once none is left.
// see: A release is read with every name and date in it replaced by a neutral word wherever it stands, and product names kept
// see: The release reading asks four closed questions written before any release is read, and code turns each answer into a reading
public sealed partial class LocalRun
{
    public const string HistoryRead = "history read";

    // The days before its own a slot reads the releases of, the history read taking every earlier one.
    public const int SlotReadsDays = 7;

    // The releases read from the store a batch at a time.
    const int Batch = 100;

    // A stored text that no longer hashes to what was stored with it is refused rather than read.
    public const string TextChanged = "the stored text is not its hash";

    // How long the history read keeps clear of a slot's start, and how long it waits for the slot's row.
    static readonly TimeSpan BeforeASlot = TimeSpan.FromMinutes(3);
    static readonly TimeSpan SlotAllowance = TimeSpan.FromMinutes(30);

    const string Unread = @"
        SELECT d.accession, d.cik, d.text_gzip, d.text_sha256, d.accepted_at
        FROM release_document d
        WHERE d.accepted_at >= $since
          AND NOT EXISTS (SELECT 1 FROM release_reading r WHERE r.accession = d.accession AND r.pin = $pin)
        ORDER BY d.accepted_at {0}, d.accession
        LIMIT $limit;
    ";

    const string FilerNames = "SELECT names, tickers FROM release_filer WHERE cik = $cik;";

    const string FilerTickers = @"
        SELECT ticker FROM company WHERE cik = $cik
        UNION SELECT ticker FROM pulled_company WHERE cik = $cik
        UNION SELECT ticker FROM sec_ticker WHERE cik = $cik;
    ";

    const string FilerTitles = "SELECT DISTINCT title FROM sec_ticker WHERE cik = $cik;";

    const string FilerPeople = "SELECT DISTINCT owners FROM insider_trade WHERE cik = $cik;";

    const string SlotRowSince = @"
        SELECT 1 FROM run_log
        WHERE stage = '" + Stage + @"' AND started_at >= $from
          AND json_extract(detail, '$.kind') IN ('" + EarlySlot + "', '" + LateSlot + @"')
        LIMIT 1;
    ";

    public const string Read = "read";
    public const string Refused = "refused";

    // A slot's reading: the week's releases not yet read under the pin, newest first, up to the slot's last instant.
    async Task<(int Read, IReadOnlyDictionary<string, int> Refused, List<double> Seconds, bool Stopped, LocalRunOutcome? Ended)> ReadRecentAsync(
        ReadingPin pin,
        DateTimeOffset started,
        DateTimeOffset limit,
        CancellationToken cancellation)
    {
        var since = new DateTimeOffset(DateOnly.FromDateTime(started.UtcDateTime).AddDays(-SlotReadsDays).ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);

        return await ReadReleasesAsync(pin, since, newestFirst: true, until: limit, pauseForSlots: false, cancellation);
    }

    // The history by hand: every release no reading under the newest pin holds, oldest first, until the instant given,
    // pausing for each slot and for the night, and the pin's history marked complete once none is left.
    public async Task<LocalRunOutcome> ReadHistoryAsync(DateTimeOffset? until, TextWriter output, CancellationToken cancellation = default)
    {
        var started = clock.UtcNow;

        runId = RunIdFor(started, HistoryRead);

        var ready = await ReadyAsync(started, cancellation);

        if (ready.Refusal is { } refused)
        {
            return await EndAsync(started, HistoryRead, refused with { Kind = HistoryRead }, until, output, cancellation);
        }

        var pin = ready.Pin!;
        var taken = await ReadReleasesAsync(pin, DateTimeOffset.MinValue, newestFirst: false, until: until, pauseForSlots: true, cancellation, output);

        if (taken.Ended is { } ended)
        {
            return await EndAsync(started, HistoryRead, ended with { Pin = pin, FileCheck = ready.Check, HashSeconds = ready.HashSeconds, Read = taken.Read, Refused = taken.Refused }, until, output, cancellation);
        }

        var why = taken.Stopped ? FormattableString.Invariant($"stopped at {until:yyyy-MM-ddTHH:mm:ssZ} as asked") : null;

        if (!taken.Stopped && await LeftUnreadAsync(pin, cancellation) == 0)
        {
            await using var connection = await OpenAsync(cancellation);

            await WaitToWriteAsync(null, output, cancellation);

            await using var command = connection.CreateCommand();

            command.CommandText = CompleteHistory;
            command.Parameters.AddWithValue("$at", Instant(clock.UtcNow));
            command.Parameters.AddWithValue("$id", pin.Id);

            await command.ExecuteNonQueryAsync(cancellation);

            why = FormattableString.Invariant($"every stored release is read or refused under pin {pin.Id}, its history marked complete");
        }

        return await EndAsync(
            started,
            HistoryRead,
            new LocalRunOutcome(HistoryRead, taken.Stopped ? LimitReached : Finished, why, 0, taken.Read, taken.Refused, Median(taken.Seconds), taken.Seconds.Count == 0 ? null : taken.Seconds.Max(), pin, ready.Check, ready.HashSeconds),
            until,
            output,
            cancellation,
            rowsWritten: taken.Read + taken.Refused.Values.Sum());
    }

    async Task<int> LeftUnreadAsync(ReadingPin pin, CancellationToken cancellation)
    {
        await using var connection = await OpenAsync(cancellation);
        await using var command = connection.CreateCommand();

        command.CommandText = "SELECT COUNT(*) FROM release_document d WHERE NOT EXISTS (SELECT 1 FROM release_reading r WHERE r.accession = d.accession AND r.pin = $pin);";
        command.Parameters.AddWithValue("$pin", pin.Id);

        return Convert.ToInt32(await command.ExecuteScalarAsync(cancellation), CultureInfo.InvariantCulture);
    }

    // Reads the releases not yet read under the pin from the instant given, a batch at a time, each with its names and
    // dates removed, loading the model where it is not loaded and leaving the runtime as it found it.
    async Task<(int Read, IReadOnlyDictionary<string, int> Refused, List<double> Seconds, bool Stopped, LocalRunOutcome? Ended)> ReadReleasesAsync(
        ReadingPin pin,
        DateTimeOffset since,
        bool newestFirst,
        DateTimeOffset? until,
        bool pauseForSlots,
        CancellationToken cancellation,
        TextWriter? output = null)
    {
        var seconds = new List<double>();
        var refusals = new Dictionary<string, int>(StringComparer.Ordinal);
        var filers = new Dictionary<string, ReleaseFiler>(StringComparer.Ordinal);
        var read = 0;
        var loaded = false;
        var prepared = false;
        FileStream? held = null;

        try
        {
            if (pauseForSlots)
            {
                held = await LocalLocks.TakeAsync(dataRoot, until, clock, wait, cancellation);

                if (held is null)
                {
                    return (0, refusals, seconds, true, null);
                }
            }

            while (true)
            {
                var batch = await UnreadBatchAsync(pin, since, newestFirst, cancellation);

                if (batch.Count == 0)
                {
                    return (read, refusals, seconds, false, null);
                }

                foreach (var release in batch)
                {
                    if (until is { } last && clock.UtcNow >= last)
                    {
                        return (read, refusals, seconds, true, null);
                    }

                    if (pauseForSlots && await MustPauseAsync(cancellation))
                    {
                        await (output ?? TextWriter.Null).WriteLineAsync($"{Stage}: the history read pauses for a slot or the night");

                        if (held is not null)
                        {
                            await held.DisposeAsync();
                            held = null;
                        }

                        while (await MustPauseAsync(cancellation))
                        {
                            if (until is { } end && clock.UtcNow >= end)
                            {
                                return (read, refusals, seconds, true, null);
                            }

                            await wait(Poll, cancellation);
                        }

                        held = await LocalLocks.TakeAsync(dataRoot, until, clock, wait, cancellation);

                        if (held is null)
                        {
                            return (read, refusals, seconds, true, null);
                        }
                    }

                    if (!prepared)
                    {
                        loaded = await feed.PrepareAsync(cancellation);
                        prepared = true;
                    }

                    var taken = await ReadReleaseAsync(pin, release, filers, cancellation);

                    if (taken.Seconds is { } spent)
                    {
                        seconds.Add(spent);
                    }

                    await StoreReadingAsync(pin, release.Accession, taken, cancellation);

                    if (taken.Read.Answer is not null)
                    {
                        read++;
                    }
                    else
                    {
                        var cause = taken.Read.Cause ?? ReleaseAnswers.Empty;

                        refusals[cause] = refusals.GetValueOrDefault(cause) + 1;
                    }
                }
            }
        }
        catch (PinBroken broken)
        {
            return (read, refusals, seconds, false, Ended(string.Empty, PinRefused, broken.Message));
        }
        catch (ReadingRuntimeUnavailable gone)
        {
            return (read, refusals, seconds, false, Ended(string.Empty, gone.Outcome, gone.Message));
        }
        finally
        {
            if (prepared)
            {
                await feed.ReleaseAsync(loaded, CancellationToken.None);
            }

            if (held is not null)
            {
                await held.DisposeAsync();
            }
        }
    }

    async Task<IReadOnlyList<UnreadRelease>> UnreadBatchAsync(ReadingPin pin, DateTimeOffset since, bool newestFirst, CancellationToken cancellation)
    {
        await using var connection = await OpenAsync(cancellation);
        await using var command = connection.CreateCommand();

        command.CommandText = string.Format(CultureInfo.InvariantCulture, Unread, newestFirst ? "DESC" : "ASC");
        command.Parameters.AddWithValue("$since", Instant(since < DateTimeOffset.UnixEpoch ? DateTimeOffset.UnixEpoch : since));
        command.Parameters.AddWithValue("$pin", pin.Id);
        command.Parameters.AddWithValue("$limit", Batch);

        await using var reader = await command.ExecuteReaderAsync(cancellation);
        var batch = new List<UnreadRelease>();

        while (await reader.ReadAsync(cancellation))
        {
            batch.Add(new UnreadRelease(
                reader.GetString(0),
                reader.GetString(1),
                (byte[])reader[2],
                reader.GetString(3),
                DateTimeOffset.ParseExact(reader.GetString(4), "yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal)));
        }

        return batch;
    }

    // One release read: its text read back to its hash, its names and dates removed, and the four questions asked of it.
    async Task<ReadingTaken> ReadReleaseAsync(ReadingPin pin, UnreadRelease release, Dictionary<string, ReleaseFiler> filers, CancellationToken cancellation)
    {
        var text = ResultsReleases.Decompressed(release.Gzip);

        if (!string.Equals(ResultsReleases.HashOf(text), release.Sha256, StringComparison.Ordinal))
        {
            return new ReadingTaken(new ReleaseAnswerRead(null, TextChanged), null, null);
        }

        if (!filers.TryGetValue(release.Cik, out var filer))
        {
            filer = await FilerAsync(release.Cik, cancellation);
            filers[release.Cik] = filer;
        }

        return await AskAsync(pin, ReleaseQuestions.Instruction, ReleaseAnonymiser.Anonymise(text, filer), cancellation);
    }

    // The filer a release is read without: its names and tickers as its submissions state them, the tickers and titles
    // the store holds for it, and the people its insiders' filings name.
    async Task<ReleaseFiler> FilerAsync(string cik, CancellationToken cancellation)
    {
        await using var connection = await OpenAsync(cancellation);

        var names = new List<string>();
        var tickers = new List<string>();

        await using (var command = connection.CreateCommand())
        {
            command.CommandText = FilerNames;
            command.Parameters.AddWithValue("$cik", cik);

            await using var reader = await command.ExecuteReaderAsync(cancellation);

            if (await reader.ReadAsync(cancellation))
            {
                names.AddRange(JsonSerializer.Deserialize<string[]>(reader.GetString(0)) ?? []);
                tickers.AddRange(JsonSerializer.Deserialize<string[]>(reader.GetString(1)) ?? []);
            }
        }

        names.AddRange(await ColumnAsync(connection, FilerTitles, cik, cancellation));
        tickers.AddRange(await ColumnAsync(connection, FilerTickers, cik, cancellation));

        var people = await ColumnAsync(connection, FilerPeople, cik, cancellation);

        return new ReleaseFiler(
            [.. names.Distinct(StringComparer.OrdinalIgnoreCase)],
            [.. tickers.Distinct(StringComparer.Ordinal)],
            people);
    }

    static async Task<List<string>> ColumnAsync(SqliteConnection connection, string sql, string cik, CancellationToken cancellation)
    {
        await using var command = connection.CreateCommand();

        command.CommandText = sql;
        command.Parameters.AddWithValue("$cik", cik);

        await using var reader = await command.ExecuteReaderAsync(cancellation);
        var values = new List<string>();

        while (await reader.ReadAsync(cancellation))
        {
            if (!reader.IsDBNull(0))
            {
                values.Add(reader.GetString(0));
            }
        }

        return values;
    }

    // One reading asked: asked once more after an answer that cannot be read, refused at once where the runtime refuses
    // it or does not answer in time, and the run ended where an answer is neither the pin's model's nor its runtime's.
    async Task<ReadingTaken> AskAsync(ReadingPin pin, string instruction, string text, CancellationToken cancellation)
    {
        ReleaseAnswerRead read = new(null, ReleaseAnswers.Empty);
        double? seconds = null;
        int? tokens = null;

        for (var ask = 0; ask <= ReadingSettings.AskedAgain; ask++)
        {
            ReadingReply reply;

            try
            {
                reply = await feed.AskAsync(new ReadingRequest(pin.Model, instruction, text, pin.Seed, pin.AnswerTokens), cancellation);
            }
            catch (ReadingRefused refused)
            {
                return new ReadingTaken(new ReleaseAnswerRead(null, refused.Cause), seconds, tokens);
            }

            seconds = (seconds ?? 0) + reply.Seconds;
            tokens = reply.PromptTokens;

            if (ReadingPins.AnswerDiffers(pin, reply.Model, reply.RuntimeName, reply.RuntimeVersion) is { } part)
            {
                throw new PinBroken($"an answer came from {part} the pin does not name: {reply.Model ?? "no model"} on {reply.RuntimeName ?? "no runtime"} {reply.RuntimeVersion}");
            }

            read = ReleaseAnswers.Read(reply.Content, reply.FinishReason);

            if (read.Answer is not null)
            {
                return new ReadingTaken(read, seconds, tokens);
            }
        }

        return new ReadingTaken(read, seconds, tokens);
    }

    async Task StoreReadingAsync(ReadingPin pin, string accession, ReadingTaken taken, CancellationToken cancellation)
    {
        await using var connection = await OpenAsync(cancellation);

        await WaitToWriteAsync(null, TextWriter.Null, cancellation);

        await using var command = connection.CreateCommand();
        var answer = taken.Read.Answer;

        command.CommandText = InsertReading;
        command.Parameters.AddWithValue("$accession", accession);
        command.Parameters.AddWithValue("$pin", pin.Id);
        command.Parameters.AddWithValue("$outcome", answer is null ? Refused : Read);
        command.Parameters.AddWithValue("$cause", (object?)taken.Read.Cause ?? DBNull.Value);
        command.Parameters.AddWithValue("$results", (object?)answer?.Results ?? DBNull.Value);
        command.Parameters.AddWithValue("$outlook", (object?)answer?.Outlook ?? DBNull.Value);
        command.Parameters.AddWithValue("$tone", (object?)answer?.Tone ?? DBNull.Value);
        command.Parameters.AddWithValue("$demand", (object?)answer?.Demand ?? DBNull.Value);
        command.Parameters.AddWithValue("$seconds", (object?)taken.Seconds ?? DBNull.Value);
        command.Parameters.AddWithValue("$prompt_tokens", (object?)taken.PromptTokens ?? DBNull.Value);
        command.Parameters.AddWithValue("$read_at", Instant(clock.UtcNow));
        command.Parameters.AddWithValue("$run_id", runId);

        await command.ExecuteNonQueryAsync(cancellation);
    }

    // The history read keeps clear of the night, from its window to the end of its lock, and of each slot from just
    // before its start until it has written its row or its allowance has passed.
    async Task<bool> MustPauseAsync(CancellationToken cancellation)
    {
        var now = clock.UtcNow;

        if (NightLock.Holder(dataRoot) is not null || SweepRunner.InTheNightsWindow(now, TimeSpan.FromMinutes(10)))
        {
            return true;
        }

        foreach (var start in new[] { LocalSlots.EarlyStart, LocalSlots.LateStart })
        {
            var at = new DateTimeOffset(DateOnly.FromDateTime(now.UtcDateTime).ToDateTime(start), TimeSpan.Zero);

            if (now < at - BeforeASlot || now >= at + SlotAllowance)
            {
                continue;
            }

            await using var connection = await OpenAsync(cancellation);
            await using var command = connection.CreateCommand();

            command.CommandText = SlotRowSince;
            command.Parameters.AddWithValue("$from", Instant(at - BeforeASlot));

            if (await command.ExecuteScalarAsync(cancellation) is null)
            {
                return true;
            }
        }

        return false;
    }
}
