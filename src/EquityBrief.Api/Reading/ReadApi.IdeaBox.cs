using System.Globalization;
using EquityBrief.Api.Passes;
using EquityBrief.Core.Loop;
using EquityBrief.Web.App;

namespace EquityBrief.Api.Reading;

// The idea box's press, its confirm and its read. A press writes the words typed and nothing else; the box's worker writes what
// the local model understood; a confirm writes the line the newest answer was read into, only where that answer understood an
// idea and the press holds no confirm already, and the Sunday run tests it first. Nothing joins a test without the confirm.
// see: An idea typed in the box is read into one grammar line by the local model and joins the week's tests only on the operator's confirm
public sealed partial class ReadApi
{
    // The presses the page draws, newest first.
    public const int IdeasShown = 10;

    const string NewestAnswerOf = "SELECT outcome, line FROM idea_box_answer WHERE press_id = $id ORDER BY attempt DESC LIMIT 1;";

    const string ConfirmOf = "SELECT COUNT(*) FROM idea_box_confirm WHERE press_id = $id;";

    const string Ideas = @"
        SELECT b.id, b.words, b.pressed_at,
               a.outcome, a.cause, a.line, a.reason,
               c.confirmed_at,
               (SELECT n.verdict FROM notebook n WHERE n.line = COALESCE(c.line, a.line) AND n.source = 'the idea box' ORDER BY n.id DESC LIMIT 1),
               (SELECT n.why FROM notebook n WHERE n.line = COALESCE(c.line, a.line) AND n.source = 'the idea box' ORDER BY n.id DESC LIMIT 1)
        FROM idea_box b
        LEFT JOIN idea_box_answer a ON a.press_id = b.id AND a.attempt = (SELECT MAX(attempt) FROM idea_box_answer WHERE press_id = b.id)
        LEFT JOIN idea_box_confirm c ON c.press_id = b.id
        ORDER BY b.id DESC LIMIT $shown;";

    // The words typed written as a press, its id handed back for the worker; none where nothing was typed.
    public async Task<long?> AskIdeaAsync(string? words)
    {
        var typed = IdeaBoxPrompt.Trimmed(words ?? string.Empty);

        if (typed.Length == 0)
        {
            return null;
        }

        await using var connection = Open();
        await using var command = connection.CreateCommand();

        command.CommandText = InsertIdea;
        command.Parameters.AddWithValue("$words", typed);
        command.Parameters.AddWithValue("$pressed_at", clock.UtcNow.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture));

        return (long)(await command.ExecuteScalarAsync())!;
    }

    // A confirm of a press's line: written where its newest answer understood an idea and no confirm stands for it.
    public async Task<RequestWritten> ConfirmIdeaAsync(long press)
    {
        await using var connection = Open();
        string? line = null;

        await using (var command = connection.CreateCommand())
        {
            command.CommandText = NewestAnswerOf;
            command.Parameters.AddWithValue("$id", press);

            await using var reader = await command.ExecuteReaderAsync();

            if (await reader.ReadAsync() && reader.GetString(0) == "understood" && !reader.IsDBNull(1))
            {
                line = reader.GetString(1);
            }
        }

        if (line is null)
        {
            return new RequestWritten(false, "Nothing was confirmed: the local model has not read this idea into a line.");
        }

        await using (var command = connection.CreateCommand())
        {
            command.CommandText = ConfirmOf;
            command.Parameters.AddWithValue("$id", press);

            if ((long)(await command.ExecuteScalarAsync())! > 0)
            {
                return new RequestWritten(false, "Nothing more was confirmed: this idea is confirmed already.");
            }
        }

        await using (var command = connection.CreateCommand())
        {
            command.CommandText = InsertConfirm;
            command.Parameters.AddWithValue("$id", press);
            command.Parameters.AddWithValue("$line", line);
            command.Parameters.AddWithValue("$confirmed_at", clock.UtcNow.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture));
            await command.ExecuteNonQueryAsync();
        }

        return new RequestWritten(true, "Confirmed: it is tested first in the next Sunday run.");
    }

    // The newest presses, each with the newest answer, its confirm and, once tested, the notebook's verdict and why.
    public async Task<IReadOnlyList<IdeaBoxView>> IdeaBoxAsync()
    {
        await using var connection = Open();
        await using var command = connection.CreateCommand();

        command.CommandText = Ideas;
        command.Parameters.AddWithValue("$shown", IdeasShown);

        var ideas = new List<IdeaBoxView>();

        await using var reader = await command.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            string? Text(int column) => reader.IsDBNull(column) ? null : reader.GetString(column);

            ideas.Add(new IdeaBoxView(reader.GetInt64(0), reader.GetString(1), reader.GetString(2)[..10], Text(3), Text(4), Text(5), Text(6), Text(7) is { } at ? at[..10] : null, Text(8), Text(9)));
        }

        return ideas;
    }
}
