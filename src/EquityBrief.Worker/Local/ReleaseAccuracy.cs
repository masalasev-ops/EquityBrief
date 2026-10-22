using System.Globalization;
using System.Text.Json;
using EquityBrief.Core.Components;
using EquityBrief.Core.Providers;
using EquityBrief.Core.Releases;
using EquityBrief.Core.Research;
using EquityBrief.Core.Time;
using EquityBrief.Data;
using Microsoft.Data.Sqlite;

namespace EquityBrief.Worker.Local;

// One reference label the build session wrote from the text the model reads, before the model read it.
public sealed record ReferenceLabel(string Accession, string Results, string Outlook, string Tone, string Demand);

// The reference set as committed: its labels and the instant its manifest states they were written, before their commit.
public sealed record ReferenceSet(DateTimeOffset LabelledAt, IReadOnlyList<ReferenceLabel> Labels);

// The reference set's file read: when its labels were written and each release's four words, a word its question does
// not list refused by name.
public static class ReleaseLabels
{
    public static ReferenceSet Read(string json)
    {
        using var document = JsonDocument.Parse(json);

        var root = document.RootElement;
        var at = DateTimeOffset.ParseExact(root.GetProperty("labelledAt").GetString()!, "yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal);
        var labels = new List<ReferenceLabel>();

        foreach (var label in root.GetProperty("labels").EnumerateArray())
        {
            string Word(string question)
            {
                var word = label.GetProperty(question).GetString() ?? string.Empty;

                return ReleaseQuestions.For(question).Words.Any(listed => listed.Word == word)
                    ? word
                    : throw new FormatException($"The reference label of {label.GetProperty("accession").GetString()} answers {question} with '{word}', a word its question does not list.");
            }

            labels.Add(new ReferenceLabel(
                label.GetProperty("accession").GetString()!,
                Word(ReleaseQuestions.Results),
                Word(ReleaseQuestions.Outlook),
                Word(ReleaseQuestions.Tone),
                Word(ReleaseQuestions.Demand)));
        }

        return new ReferenceSet(at, labels);
    }
}

// How well the pinned model reads, from 20.3, by hand and writing nothing: each answer against every reference held
// apart from the model's answers, before the model's cutoff and after it. The results reading against the sign of the
// provider's surprise for the same print; the outlook reading against the outlook a release's guided figures show
// beside the release before's, where the facts file pairs them; all four against the reference set the build session
// labelled; the three probes' shares; and what the drift's tone variant would keep of each index's settled drift
// setups. It joins nothing to the catalogue, the tester or the engines, and a reading of a reference release written
// before its label is named.
// see: The release reading asks four closed questions written before any release is read, and code turns each answer into a reading
// see: The drift's tone variant lists only where its print's release reads better than expected and its outlook was not lowered or withdrawn
public sealed class ReleaseAccuracy(IClock clock, string databaseFile) : IComponent
{
    public static ComponentAccess Access => new(
        Stores:
        [
            new StoreTouch(Store.ReadingPin, Touch.Read),
            new StoreTouch(Store.ReleaseReading, Touch.Read),
            new StoreTouch(Store.ReleaseDocument, Touch.Read),
            new StoreTouch(Store.ReleaseCheck, Touch.Read),
            new StoreTouch(Store.ReleaseProbe, Touch.Read),
            new StoreTouch(Store.Company, Touch.Read),
            new StoreTouch(Store.PulledCompany, Touch.Read),
            new StoreTouch(Store.EarningsReaction, Touch.Read),
            new StoreTouch(Store.PulledSurprise, Touch.Read),
            new StoreTouch(Store.Facts, Touch.Read),
            new StoreTouch(Store.Setup, Touch.Read),
            new StoreTouch(Store.SetupNight, Touch.Read),
        ],
        Feeds: []);

    const string NewestPin = "SELECT id, cutoff FROM reading_pin ORDER BY id DESC LIMIT 1;";

    // From the 20.3 correction of 2026-10-22 a reading of the exhibit its filing holds as its release, and the filings
    // whose release a check replaced, whose reference label was given on the exhibit replaced.
    const string Readings = @"
        SELECT r.accession, d.cik, d.accepted_at, r.outcome, r.results, r.outlook, r.tone, r.demand, r.read_at
        FROM release_reading r JOIN release_document d ON d.accession = r.accession AND d.exhibit = r.exhibit AND d.release = 1
        WHERE r.pin = $pin;
    ";

    const string Replaced = "SELECT accession FROM release_check WHERE replaced = 1;";

    const string FilersByTicker = @"
        SELECT ticker, cik FROM company WHERE cik IS NOT NULL
        UNION SELECT ticker, cik FROM pulled_company WHERE cik IS NOT NULL;
    ";

    const string Prints = @"
        SELECT ticker, report_date, surprise_pct FROM earnings_reaction
        UNION ALL SELECT ticker, event_date, surprise_percent FROM pulled_surprise;
    ";

    const string GuidedFacts = "SELECT ticker, session_date, payload FROM facts WHERE payload LIKE '%" + FactOwners.GuidanceBefore + "%';";

    const string DriftSetups = "SELECT index_code, ticker, session_date FROM setup WHERE family = 'drift' AND settled = 1;";

    const string LedgerSessions = "SELECT DISTINCT session_date FROM setup_night;";

    const string Probes = "SELECT kind, accession, matched FROM release_probe WHERE pin = $pin;";

    sealed record Reading(string Accession, string Cik, DateTimeOffset Accepted, ReleaseAnswer? Answer, DateTimeOffset ReadAt);

    public async Task<string> ReportAsync(ReferenceSet? reference, CancellationToken cancellation = default)
    {
        await using var connection = new SqliteConnection(StoreConnection.For(databaseFile));

        await connection.OpenAsync(cancellation);

        long pin;
        DateOnly cutoff;

        await using (var command = connection.CreateCommand())
        {
            command.CommandText = NewestPin;

            await using var reader = await command.ExecuteReaderAsync(cancellation);

            if (!await reader.ReadAsync(cancellation))
            {
                return "accuracy: no reading version is pinned, so no reading is held to measure";
            }

            pin = reader.GetInt64(0);
            cutoff = DateOnly.ParseExact(reader.GetString(1), "yyyy-MM-dd", CultureInfo.InvariantCulture);
        }

        var readings = new List<Reading>();

        await using (var command = connection.CreateCommand())
        {
            command.CommandText = Readings;
            command.Parameters.AddWithValue("$pin", pin);

            await using var reader = await command.ExecuteReaderAsync(cancellation);

            while (await reader.ReadAsync(cancellation))
            {
                var answer = reader.GetString(3) == LocalRun.Read
                    ? new ReleaseAnswer(reader.GetString(4), reader.GetString(5), reader.GetString(6), reader.GetString(7))
                    : null;

                readings.Add(new Reading(reader.GetString(0), reader.GetString(1), Instant(reader.GetString(2)), answer, Instant(reader.GetString(8))));
            }
        }

        var filerOf = await PairsAsync(connection, FilersByTicker, cancellation);
        var byFiler = readings.GroupBy(reading => reading.Cik).ToDictionary(group => group.Key, group => group.ToArray(), StringComparer.Ordinal);
        var zone = clock.SessionZone;
        var calendar = new ReleaseCalendar(await SessionsAsync(connection, cancellation));
        bool Before(DateTimeOffset accepted) => clock.SessionDateAt(accepted) <= cutoff;

        var report = new List<string> { FormattableString.Invariant($"accuracy under pin {pin}: {readings.Count} release(s) read or refused, {readings.Count(reading => reading.Answer is not null)} read; before the cutoff {cutoff:yyyy-MM-dd} and after it") };

        // The results reading against the provider's surprise for the same print, a print answered once a filer, read on
        // the night of the session after its report date; a print the calendar cannot place is left out and counted.
        var prints = await PrintsAsync(connection, cancellation);
        var results = new List<(bool Before, string? Results, double? Surprise)>();
        var seen = new HashSet<(string, DateOnly)>();
        var unplaced = 0;

        foreach (var (ticker, day, surprise) in prints)
        {
            if (!filerOf.TryGetValue(ticker, out var cik) || !byFiler.TryGetValue(cik, out var held) || !seen.Add((cik, day)))
            {
                continue;
            }

            if (calendar.SessionAfter(day) is not { } night || ReleaseAvailability.WindowFrom(day, zone, calendar) is null)
            {
                unplaced++;

                continue;
            }

            if (ReleaseAvailability.ReleaseFor(held, reading => reading.Accepted, day, night, zone, calendar) is { } release)
            {
                results.Add((Before(release.Accepted), release.Answer?.Results, surprise));
            }
        }

        report.Add(Line("results against the surprise's sign", results.Where(row => row.Before).Select(row => (row.Results, row.Surprise)), results.Where(row => !row.Before).Select(row => (row.Results, row.Surprise)))
            + FormattableString.Invariant($"; {unplaced} print(s) of a read filer the calendar cannot place left out"));

        // The outlook reading against the guided figures the facts file pairs by place.
        var outlook = new List<(bool Before, string? Model, string? Reference)>();

        await using (var command = connection.CreateCommand())
        {
            command.CommandText = GuidedFacts;

            await using var reader = await command.ExecuteReaderAsync(cancellation);

            while (await reader.ReadAsync(cancellation))
            {
                var (ticker, session) = (reader.GetString(0), DateOnly.ParseExact(reader.GetString(1), "yyyy-MM-dd", CultureInfo.InvariantCulture));

                if (!filerOf.TryGetValue(ticker, out var cik) || !byFiler.TryGetValue(cik, out var held))
                {
                    continue;
                }

                var (newest, before) = Guided(reader.GetString(2));

                if (ReleaseAgreement.OutlookFromGuidance(newest, before) is not { } shown)
                {
                    continue;
                }

                var release = held.Where(reading => ReleaseAvailability.ReadableOn(reading.Accepted, session)).MaxBy(reading => reading.Accepted);

                if (release is not null)
                {
                    outlook.Add((Before(release.Accepted), release.Answer?.Outlook, shown));
                }
            }
        }

        report.Add(Against("outlook against the guided figures", outlook));

        // All four against the reference set, a reading written before its label named, and from the 20.3 correction of
        // 2026-10-22 a label given on an exhibit a check has since replaced left out and counted.
        if (reference is null)
        {
            report.Add("reference set: none named, so no answer is measured against it");
        }
        else
        {
            var replaced = new HashSet<string>(StringComparer.Ordinal);

            await using (var command = connection.CreateCommand())
            {
                command.CommandText = Replaced;

                await using var reader = await command.ExecuteReaderAsync(cancellation);

                while (await reader.ReadAsync(cancellation))
                {
                    replaced.Add(reader.GetString(0));
                }
            }

            var labels = reference.Labels.Where(label => !replaced.Contains(label.Accession)).ToArray();
            var byAccession = readings.ToDictionary(reading => reading.Accession, StringComparer.Ordinal);
            var early = labels.Count(label => byAccession.TryGetValue(label.Accession, out var reading) && reading.ReadAt <= reference.LabelledAt);

            report.Add(FormattableString.Invariant($"reference set: {reference.Labels.Count} label(s) written by {reference.LabelledAt:yyyy-MM-ddTHH:mm:ssZ}, {reference.Labels.Count - labels.Length} given on an exhibit a check has since replaced and left out, {labels.Count(label => byAccession.ContainsKey(label.Accession))} read under the pin, {early} read before its label"));

            foreach (var question in ReleaseQuestions.All)
            {
                var rows = labels.Select(label =>
                {
                    byAccession.TryGetValue(label.Accession, out var reading);

                    return (Before: reading is null || Before(reading.Accepted), Model: reading?.Answer is { } answer ? Word(answer, question.Key) : null, Reference: (string?)Word(label, question.Key));
                }).ToArray();

                report.Add(Against(question.Key + " against the reference set", rows));
            }
        }

        // The probes' shares.
        var probes = new List<(string Kind, string Accession, bool? Matched)>();

        await using (var command = connection.CreateCommand())
        {
            command.CommandText = Probes;
            command.Parameters.AddWithValue("$pin", pin);

            await using var reader = await command.ExecuteReaderAsync(cancellation);

            while (await reader.ReadAsync(cancellation))
            {
                probes.Add((reader.GetString(0), reader.GetString(1), reader.IsDBNull(2) ? null : reader.GetInt64(2) == 1));
            }
        }

        var acceptedOf = readings.ToDictionary(reading => reading.Accession, reading => reading.Accepted, StringComparer.Ordinal);

        foreach (var kind in ReleaseProbes.Kinds)
        {
            var rows = probes.Where(probe => probe.Kind == kind && probe.Matched is not null).ToArray();
            var before = rows.Where(probe => acceptedOf.TryGetValue(probe.Accession, out var at) && Before(at)).ToArray();
            var after = rows.Except(before).ToArray();

            report.Add(FormattableString.Invariant($"probe {kind}: {rows.Length} compared; agreeing before {before.Count(probe => probe.Matched == true)} of {before.Length}, after {after.Count(probe => probe.Matched == true)} of {after.Length}"));
        }

        // What the tone variant's condition keeps of each index's settled drift setups.
        var printsOf = prints.GroupBy(print => print.Ticker).ToDictionary(group => group.Key, group => group.Select(print => print.Day).Distinct().Order().ToArray(), StringComparer.Ordinal);
        var tone = new List<(string Index, bool Before, ReleaseAnswer? Answer)>();

        await using (var command = connection.CreateCommand())
        {
            command.CommandText = DriftSetups;

            await using var reader = await command.ExecuteReaderAsync(cancellation);

            while (await reader.ReadAsync(cancellation))
            {
                var (index, ticker, session) = (reader.GetString(0), reader.GetString(1), DateOnly.ParseExact(reader.GetString(2), "yyyy-MM-dd", CultureInfo.InvariantCulture));
                var reported = printsOf.TryGetValue(ticker, out var days) ? days.Where(day => day <= session).Select(day => (DateOnly?)day).LastOrDefault() : null;
                var release = reported is { } day && filerOf.TryGetValue(ticker, out var cik) && byFiler.TryGetValue(cik, out var held)
                    ? ReleaseAvailability.ReleaseFor(held, reading => reading.Accepted, day, session, zone, calendar)
                    : null;

                tone.Add((index, release is null ? session <= cutoff : Before(release.Accepted), release?.Answer));
            }
        }

        foreach (var index in tone.Select(row => row.Index).Distinct().Order(StringComparer.Ordinal))
        {
            foreach (var before in new[] { true, false })
            {
                var shares = ReleaseAgreement.Tone(tone.Where(row => row.Index == index && row.Before == before).Select(row => row.Answer));

                report.Add(FormattableString.Invariant(
                    $"tone variant on {index} {(before ? "before" : "after")} the cutoff: {shares.Setups} settled drift setup(s), {shares.NotHeld} with no reading held, {shares.Passing} passing, {shares.FailingResults} failing on results, {shares.FailingOutlook} failing on outlook alone, passing share {Percent(shares.PassingShare)}"));
            }
        }

        return string.Join(Environment.NewLine, report);
    }

    static string Line(string what, IEnumerable<(string? Results, double? Surprise)> before, IEnumerable<(string? Results, double? Surprise)> after)
    {
        var (one, two) = (ReleaseAgreement.ResultsAgainstSurprise(before), ReleaseAgreement.ResultsAgainstSurprise(after));

        return FormattableString.Invariant($"{what}: before {one.Agreed} of {one.Compared} agree ({Percent(one.Share)}), {one.Apart} apart; after {two.Agreed} of {two.Compared} agree ({Percent(two.Share)}), {two.Apart} apart");
    }

    static string Against(string what, IReadOnlyList<(bool Before, string? Model, string? Reference)> rows)
    {
        var one = ReleaseAgreement.Against(rows.Where(row => row.Before).Select(row => (row.Model, row.Reference)));
        var two = ReleaseAgreement.Against(rows.Where(row => !row.Before).Select(row => (row.Model, row.Reference)));

        return FormattableString.Invariant($"{what}: before {one.Agreed} of {one.Compared} agree ({Percent(one.Share)}), {one.Apart} apart; after {two.Agreed} of {two.Compared} agree ({Percent(two.Share)}), {two.Apart} apart");
    }

    static string Percent(double? share) => share is { } value ? FormattableString.Invariant($"{value * 100:0.0} per cent") : "none";

    static string Word(ReleaseAnswer answer, string question) => question switch
    {
        ReleaseQuestions.Results => answer.Results,
        ReleaseQuestions.Outlook => answer.Outlook,
        ReleaseQuestions.Tone => answer.Tone,
        _ => answer.Demand,
    };

    static string Word(ReferenceLabel label, string question) => question switch
    {
        ReleaseQuestions.Results => label.Results,
        ReleaseQuestions.Outlook => label.Outlook,
        ReleaseQuestions.Tone => label.Tone,
        _ => label.Demand,
    };

    // The guided figures the facts file names by place, the newest release's and the release before's.
    static (IReadOnlyList<decimal> Newest, IReadOnlyList<decimal> Before) Guided(string payload)
    {
        using var document = JsonDocument.Parse(payload);

        var named = new Dictionary<string, decimal>(StringComparer.Ordinal);

        void Walk(JsonElement element)
        {
            if (element.ValueKind == JsonValueKind.Object)
            {
                if (element.TryGetProperty("name", out var name) && name.ValueKind == JsonValueKind.String
                    && element.TryGetProperty("value", out var value) && value.ValueKind == JsonValueKind.String
                    && decimal.TryParse(value.GetString(), NumberStyles.Number, CultureInfo.InvariantCulture, out var figure))
                {
                    named[name.GetString()!] = figure;
                }

                foreach (var property in element.EnumerateObject())
                {
                    Walk(property.Value);
                }
            }
            else if (element.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in element.EnumerateArray())
                {
                    Walk(item);
                }
            }
        }

        Walk(document.RootElement);

        IReadOnlyList<decimal> Figures(string prefix) =>
        [
            .. Enumerable.Range(1, 200)
                .Select(place => prefix + place.ToString(CultureInfo.InvariantCulture))
                .TakeWhile(named.ContainsKey)
                .Select(key => named[key]),
        ];

        return (Figures(FactOwners.GuidanceFigure), Figures(FactOwners.GuidanceBefore));
    }

    static async Task<IReadOnlyList<(string Ticker, DateOnly Day, double? Surprise)>> PrintsAsync(SqliteConnection connection, CancellationToken cancellation)
    {
        await using var command = connection.CreateCommand();

        command.CommandText = Prints;

        await using var reader = await command.ExecuteReaderAsync(cancellation);
        var prints = new Dictionary<(string, DateOnly), double?>();

        while (await reader.ReadAsync(cancellation))
        {
            var key = (reader.GetString(0), DateOnly.ParseExact(reader.GetString(1), "yyyy-MM-dd", CultureInfo.InvariantCulture));
            double? surprise = reader.IsDBNull(2) ? null : reader.GetDouble(2);

            if (!prints.TryGetValue(key, out var held) || held is null)
            {
                prints[key] = surprise;
            }
        }

        return [.. prints.Select(entry => (entry.Key.Item1, entry.Key.Item2, entry.Value)).OrderBy(print => print.Item1, StringComparer.Ordinal).ThenBy(print => print.Item2)];
    }

    static async Task<Dictionary<string, string>> PairsAsync(SqliteConnection connection, string sql, CancellationToken cancellation)
    {
        await using var command = connection.CreateCommand();

        command.CommandText = sql;

        await using var reader = await command.ExecuteReaderAsync(cancellation);
        var pairs = new Dictionary<string, string>(StringComparer.Ordinal);

        while (await reader.ReadAsync(cancellation))
        {
            pairs[reader.GetString(0)] = SecEdgarArchive.Padded(reader.GetString(1));
        }

        return pairs;
    }

    // The sessions the ledger read, which place a day the closure table's years do not reach.
    static async Task<IReadOnlyList<DateOnly>> SessionsAsync(SqliteConnection connection, CancellationToken cancellation)
    {
        await using var command = connection.CreateCommand();

        command.CommandText = LedgerSessions;

        await using var reader = await command.ExecuteReaderAsync(cancellation);
        var sessions = new List<DateOnly>();

        while (await reader.ReadAsync(cancellation))
        {
            sessions.Add(DateOnly.ParseExact(reader.GetString(0), "yyyy-MM-dd", CultureInfo.InvariantCulture));
        }

        return sessions;
    }

    static DateTimeOffset Instant(string text) =>
        DateTimeOffset.ParseExact(text, "yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal);
}
