using Microsoft.Data.Sqlite;

namespace EquityBrief.Data;

// One answer the idea generator stored for a prompt: its key, the reading version it was asked under, what it came to,
// the cause where it was refused and the answer's text as the runtime sent it.
public sealed record StoredIdea(string Key, long Pin, string Outcome, string? Cause, string? Answer);

// The one reader of the idea generator's stored answers, from 20.9: the tester reads what the local model answered here
// and nowhere else, so what a model wrote reaches the tester through one door, and the generator reads here which prompts
// its pin has answered already.
// see: The idea generator proposes conditions in the winners' grammar by closed labels code turns into levels, judged as a procedure inside each fold
public static class LoopIdeas
{
    public const string Answered = "answered";

    public const string Refused = "refused";

    // Each prompt's newest answer, by the order the answers were stored, whatever version asked it.
    const string Newest = @"
        SELECT i.prompt_key, i.pin, i.outcome, i.cause, i.answer FROM loop_idea i
        WHERE i.rowid = (SELECT MAX(j.rowid) FROM loop_idea j WHERE j.prompt_key = i.prompt_key);
    ";

    // The prompts a reading version has answered, or refused, already.
    const string UnderPin = "SELECT prompt_key FROM loop_idea WHERE pin = $pin;";

    public static async Task<IReadOnlyDictionary<string, StoredIdea>> NewestAsync(SqliteConnection connection, CancellationToken cancellation = default)
    {
        await using var command = connection.CreateCommand();

        command.CommandText = Newest;

        await using var reader = await command.ExecuteReaderAsync(cancellation);
        var answers = new Dictionary<string, StoredIdea>(StringComparer.Ordinal);

        while (await reader.ReadAsync(cancellation))
        {
            answers[reader.GetString(0)] = new StoredIdea(
                reader.GetString(0),
                reader.GetInt64(1),
                reader.GetString(2),
                reader.IsDBNull(3) ? null : reader.GetString(3),
                reader.IsDBNull(4) ? null : reader.GetString(4));
        }

        return answers;
    }

    public static async Task<IReadOnlySet<string>> AskedAsync(SqliteConnection connection, long pin, CancellationToken cancellation = default)
    {
        await using var command = connection.CreateCommand();

        command.CommandText = UnderPin;
        command.Parameters.AddWithValue("$pin", pin);

        await using var reader = await command.ExecuteReaderAsync(cancellation);
        var asked = new HashSet<string>(StringComparer.Ordinal);

        while (await reader.ReadAsync(cancellation))
        {
            asked.Add(reader.GetString(0));
        }

        return asked;
    }
}
