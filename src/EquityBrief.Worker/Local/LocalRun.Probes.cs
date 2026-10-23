using System.Globalization;
using EquityBrief.Core.Providers;
using EquityBrief.Core.Releases;
using Microsoft.Data.Sqlite;

namespace EquityBrief.Worker.Local;

// From 20.3 the probes over their sample, each answer kept under the pin with the answer it is set against and whether
// the two agree, going on from the releases a run has not probed; and the reference set's releases written out with
// their names and dates removed, the text the model reads, for the build session to label before the model reads them.
// see: A release is read with every name and date in it replaced by a neutral word wherever it stands, and product names kept
public sealed partial class LocalRun
{
    public const string Probing = "probe";

    // From the 20.3 correction of 2026-10-22 a filing's release is the exhibit marked so, and a filing is drawn once.
    const string StoredRelease = "SELECT cik, text_gzip, text_sha256 FROM release_document WHERE accession = $accession AND release = 1;";

    const string Candidates = "SELECT accession, cik, accepted_at FROM release_document WHERE release = 1 ORDER BY accession;";

    const string TickersByFiler = @"
        SELECT cik, ticker FROM company WHERE cik IS NOT NULL AND fetched_at < $before
        UNION SELECT cik, ticker FROM pulled_company WHERE cik IS NOT NULL;
    ";

    const string LedgerIndices = "SELECT ticker, index_code, COUNT(*) FROM setup WHERE session_date < $before GROUP BY ticker, index_code;";

    const string MembersByIndex = @"
        SELECT index_code, COUNT(DISTINCT ticker) FROM membership
        WHERE (joined IS NULL OR joined <= $session) AND (""left"" IS NULL OR ""left"" > $session)
        GROUP BY index_code;
    ";

    const string Probed = "SELECT accession FROM release_probe WHERE kind = $kind AND pin = $pin;";

    // One probe over the probes' sample under the newest pin, going on from the releases no run has probed.
    public async Task<LocalRunOutcome> ProbeAsync(string kind, TextWriter output, CancellationToken cancellation = default)
    {
        var started = clock.UtcNow;
        var named = Probing + " " + kind;

        runId = RunIdFor(started, named);

        if (!ReleaseProbes.Kinds.Contains(kind, StringComparer.Ordinal))
        {
            return await EndAsync(started, named, Ended(named, NotSet, $"no probe is named {kind}; the probes are {string.Join(", ", ReleaseProbes.Kinds)}"), null, output, cancellation);
        }

        await using var held = await LocalLocks.TakeAsync(dataRoot, null, clock, wait, cancellation);

        var ready = await ReadyAsync(started, cancellation);

        if (ready.Refusal is { } refused)
        {
            return await EndAsync(started, named, refused with { Kind = named }, null, output, cancellation);
        }

        var pin = ready.Pin!;
        var sample = await SampleAsync(ReleaseSamples.ProbeCount, pin.Cutoff, cancellation);
        var done = new HashSet<string>(await ColumnOfAsync(Probed, ("$kind", kind), ("$pin", pin.Id), cancellation), StringComparer.Ordinal);
        var waiting = sample.Where(accession => !done.Contains(accession)).ToArray();
        var filers = new Dictionary<string, ReleaseFiler>(StringComparer.Ordinal);
        var seconds = new List<double>();
        var (compared, agreed) = (0, 0);
        var loaded = false;

        try
        {
            if (waiting.Length > 0)
            {
                loaded = await feed.PrepareAsync(cancellation);
            }

            foreach (var accession in waiting)
            {
                if (await StoredTextAsync(accession, cancellation) is not { } stored)
                {
                    continue;
                }

                if (!filers.TryGetValue(stored.Cik, out var filer))
                {
                    filer = await FilerAsync(stored.Cik, cancellation);
                    filers[stored.Cik] = filer;
                }

                var anonymised = ReleaseAnonymiser.Anonymise(stored.Text, filer);
                var (answer, against, matched, cause, spent) = kind switch
                {
                    ReleaseProbes.Twice => Pair(await AskAsync(pin, ReleaseQuestions.Instruction, anonymised, cancellation), await AskAsync(pin, ReleaseQuestions.Instruction, anonymised, cancellation)),
                    ReleaseProbes.Distract => Pair(await AskAsync(pin, ReleaseQuestions.Instruction, anonymised, cancellation), await AskAsync(pin, ReleaseQuestions.Instruction, stored.Text, cancellation)),
                    _ => await NameAsync(pin, anonymised, filer, cancellation),
                };

                if (spent is { } time)
                {
                    seconds.Add(time);
                }

                compared++;
                agreed += matched == true ? 1 : 0;

                await StoreProbeAsync(kind, accession, pin, answer, against, matched, cause, spent, cancellation);
            }
        }
        catch (PinBroken broken)
        {
            await feed.ReleaseAsync(loaded, cancellation);

            return await EndAsync(started, named, Ended(named, PinRefused, broken.Message) with { Pin = pin, FileCheck = ready.Check, HashSeconds = ready.HashSeconds, Compared = compared, Identical = agreed }, null, output, cancellation, rowsWritten: compared);
        }
        catch (ReadingRuntimeUnavailable gone)
        {
            return await EndAsync(started, named, Ended(named, gone.Outcome, gone.Message) with { Pin = pin, FileCheck = ready.Check, HashSeconds = ready.HashSeconds, Compared = compared, Identical = agreed }, null, output, cancellation, rowsWritten: compared);
        }

        await feed.ReleaseAsync(loaded, cancellation);

        return await EndAsync(
            started,
            named,
            new LocalRunOutcome(named, Finished, FormattableString.Invariant($"{done.Count + compared} of {sample.Count} sampled releases probed"), 0, compared, NoRefusals, Median(seconds), seconds.Count == 0 ? null : seconds.Max(), pin, ready.Check, ready.HashSeconds, compared, agreed),
            null,
            output,
            cancellation,
            rowsWritten: compared);
    }

    // Two readings set against each other: the second the probe's answer, the first what it is set against, alike where
    // their four words are or both were refused for one cause.
    static (string? Answer, string? Against, bool? Matched, string? Cause, double? Seconds) Pair(ReadingTaken first, ReadingTaken second) =>
        (Words(second.Read), Words(first.Read), Alike(first.Read, second.Read), second.Read.Cause ?? first.Read.Cause, (first.Seconds ?? 0) + (second.Seconds ?? 0));

    static string? Words(ReleaseAnswerRead read) =>
        read.Answer is { } answer ? string.Join("|", answer.Results, answer.Outlook, answer.Tone, answer.Demand) : null;

    // The re-identification probe: the model asked which company issued the release, and whether its answer names the
    // filer.
    async Task<(string? Answer, string? Against, bool? Matched, string? Cause, double? Seconds)> NameAsync(ReadingPin pin, string anonymised, ReleaseFiler filer, CancellationToken cancellation)
    {
        ReadingReply reply;

        try
        {
            reply = await feed.AskAsync(new ReadingRequest(pin.Model, ReleaseProbes.ReidentifyInstruction, anonymised, pin.Seed, pin.AnswerTokens), cancellation);
        }
        catch (ReadingRefused refused)
        {
            return (null, filer.Names.FirstOrDefault(), null, refused.Cause, null);
        }

        if (ReadingPins.AnswerDiffers(pin, reply.Model) is { } part)
        {
            throw new PinBroken($"an answer came from {part} the pin does not name: {reply.Model ?? "no model"} on {reply.RuntimeName ?? "no runtime"} {reply.RuntimeVersion}");
        }

        var said = (reply.Content ?? string.Empty).Trim();

        return (said.Length > 200 ? said[..200] : said, filer.Names.FirstOrDefault(), ReleaseProbes.Names(said, filer), null, reply.Seconds);
    }

    async Task StoreProbeAsync(string kind, string accession, ReadingPin pin, string? answer, string? against, bool? matched, string? cause, double? seconds, CancellationToken cancellation)
    {
        await using var connection = await OpenAsync(cancellation);

        await WaitToWriteAsync(null, TextWriter.Null, cancellation);

        await using var command = connection.CreateCommand();

        command.CommandText = InsertProbe;
        command.Parameters.AddWithValue("$kind", kind);
        command.Parameters.AddWithValue("$accession", accession);
        command.Parameters.AddWithValue("$pin", pin.Id);
        command.Parameters.AddWithValue("$outcome", matched is null && answer is null ? Refused : Read);
        command.Parameters.AddWithValue("$cause", (object?)cause ?? DBNull.Value);
        command.Parameters.AddWithValue("$answer", (object?)answer ?? DBNull.Value);
        command.Parameters.AddWithValue("$against", (object?)against ?? DBNull.Value);
        command.Parameters.AddWithValue("$matched", matched is { } agree ? (agree ? 1 : 0) : DBNull.Value);
        command.Parameters.AddWithValue("$seconds", (object?)seconds ?? DBNull.Value);
        command.Parameters.AddWithValue("$read_at", Instant(clock.UtcNow));
        command.Parameters.AddWithValue("$run_id", runId);

        await command.ExecuteNonQueryAsync(cancellation);
    }

    async Task<(string Cik, string Text)?> StoredTextAsync(string accession, CancellationToken cancellation)
    {
        await using var connection = await OpenAsync(cancellation);
        await using var command = connection.CreateCommand();

        command.CommandText = StoredRelease;
        command.Parameters.AddWithValue("$accession", accession);

        await using var reader = await command.ExecuteReaderAsync(cancellation);

        if (!await reader.ReadAsync(cancellation))
        {
            return null;
        }

        var text = ResultsReleases.Decompressed((byte[])reader[1]);

        return ResultsReleases.HashOf(text) == reader.GetString(2) ? (reader.GetString(0), text) : null;
    }

    // The sample drawn over every release stored and accepted before the samples' day whose filer's setups the ledger
    // holds, by the index they mostly stand under, at the seed, half on either side of the model's cutoff and in
    // proportion to each index's members on the day before the samples' day.
    async Task<IReadOnlyList<string>> SampleAsync(int count, DateOnly cutoff, CancellationToken cancellation)
    {
        await using var connection = await OpenAsync(cancellation);

        var before = ReleaseSamples.Before.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var tickersOf = new Dictionary<string, List<string>>(StringComparer.Ordinal);

        await using (var command = connection.CreateCommand())
        {
            command.CommandText = TickersByFiler;
            command.Parameters.AddWithValue("$before", before);

            await using var reader = await command.ExecuteReaderAsync(cancellation);

            while (await reader.ReadAsync(cancellation))
            {
                var cik = SecEdgarArchive.Padded(reader.GetString(0));

                if (!tickersOf.TryGetValue(cik, out var list))
                {
                    tickersOf[cik] = list = [];
                }

                list.Add(reader.GetString(1));
            }
        }

        var indexOf = new Dictionary<string, (string Index, long Count)>(StringComparer.Ordinal);

        await using (var command = connection.CreateCommand())
        {
            command.CommandText = LedgerIndices;
            command.Parameters.AddWithValue("$before", before);

            await using var reader = await command.ExecuteReaderAsync(cancellation);

            while (await reader.ReadAsync(cancellation))
            {
                var (ticker, index, setups) = (reader.GetString(0), reader.GetString(1), reader.GetInt64(2));

                if (!indexOf.TryGetValue(ticker, out var held) || setups > held.Count || setups == held.Count && string.CompareOrdinal(index, held.Index) < 0)
                {
                    indexOf[ticker] = (index, setups);
                }
            }
        }

        var members = new Dictionary<string, int>(StringComparer.Ordinal);

        await using (var command = connection.CreateCommand())
        {
            command.CommandText = MembersByIndex;
            command.Parameters.AddWithValue("$session", ReleaseSamples.Before.AddDays(-1).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));

            await using var reader = await command.ExecuteReaderAsync(cancellation);

            while (await reader.ReadAsync(cancellation))
            {
                members[reader.GetString(0)] = reader.GetInt32(1);
            }
        }

        var candidates = new List<SampleCandidate>();

        await using (var command = connection.CreateCommand())
        {
            command.CommandText = Candidates;

            await using var reader = await command.ExecuteReaderAsync(cancellation);

            while (await reader.ReadAsync(cancellation))
            {
                var cik = reader.GetString(1);

                // The index the filer's tickers' setups mostly stand under in the ledger, a filer the ledger holds none of
                // drawn by no sample.
                var index = (tickersOf.GetValueOrDefault(cik) ?? [])
                    .Where(indexOf.ContainsKey)
                    .Select(ticker => indexOf[ticker])
                    .OrderByDescending(entry => entry.Count)
                    .ThenBy(entry => entry.Index, StringComparer.Ordinal)
                    .Select(entry => entry.Index)
                    .FirstOrDefault();

                if (index is null)
                {
                    continue;
                }

                var accepted = clock.SessionDateAt(DateTimeOffset.ParseExact(reader.GetString(2), "yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal));

                if (accepted < ReleaseSamples.Before)
                {
                    candidates.Add(new SampleCandidate(reader.GetString(0), index, accepted));
                }
            }
        }

        return ReleaseSamples.Draw(candidates, count, cutoff, members);
    }

    async Task<IReadOnlyList<string>> ColumnOfAsync(string sql, (string Name, object Value) first, (string Name, object Value) second, CancellationToken cancellation)
    {
        await using var connection = await OpenAsync(cancellation);
        await using var command = connection.CreateCommand();

        command.CommandText = sql;
        command.Parameters.AddWithValue(first.Name, first.Value);
        command.Parameters.AddWithValue(second.Name, second.Value);

        await using var reader = await command.ExecuteReaderAsync(cancellation);
        var values = new List<string>();

        while (await reader.ReadAsync(cancellation))
        {
            values.Add(reader.GetString(0));
        }

        return values;
    }

    // The reference set's releases written out as the model reads them, a file each named by its accession, with the
    // list of them, for the build session to label before any reading of them is asked. Calls no model and writes no
    // store row.
    public async Task<IReadOnlyList<string>> ReferenceAsync(string folder, DateOnly cutoff, CancellationToken cancellation = default)
    {
        var sample = await SampleAsync(ReleaseSamples.ReferenceCount, cutoff, cancellation);
        var filers = new Dictionary<string, ReleaseFiler>(StringComparer.Ordinal);

        Directory.CreateDirectory(folder);

        foreach (var accession in sample)
        {
            if (await StoredTextAsync(accession, cancellation) is not { } stored)
            {
                continue;
            }

            if (!filers.TryGetValue(stored.Cik, out var filer))
            {
                filer = await FilerAsync(stored.Cik, cancellation);
                filers[stored.Cik] = filer;
            }

            await File.WriteAllTextAsync(Path.Combine(folder, accession + ".txt"), ReleaseAnonymiser.Anonymise(stored.Text, filer), cancellation);
        }

        await File.WriteAllLinesAsync(Path.Combine(folder, "sample.txt"), sample, cancellation);

        return sample;
    }
}
