using EquityBrief.Web.App;

namespace EquityBrief.Api.Reading;

// What the History page's labels row reads for the industry news mood split: on each index, the members on the newest
// session, joined by it and not yet left, holding a
// stored story, those whose every story stored before the newest session holds a label, the end of the newest backlog, the
// newest label written for a story stored more than a session before it, and the sessions the bar store holds after it.
// see: The sector splits read a sector as it stood off the funds' industry headings through a stated map, and the filed GICS sector after the schedules end
public sealed partial class ReadApi
{
    const string NewsMoodOn = @"
        WITH members AS (
                 SELECT DISTINCT ticker FROM membership WHERE index_code = $index
                   AND (joined IS NULL OR joined <= $session) AND (""left"" IS NULL
                    OR ""left"" > $session)),
             held AS (SELECT DISTINCT a.ticker FROM news_article a JOIN members m ON m.ticker = a.ticker),
             open AS (
                 SELECT DISTINCT a.ticker FROM news_article a JOIN members m ON m.ticker = a.ticker
                 WHERE a.session_date < $session
                   AND NOT EXISTS (SELECT 1 FROM news_label l WHERE l.ticker = a.ticker AND l.article_id = a.article_id))
        SELECT (SELECT COUNT(*) FROM held), (SELECT COUNT(*) FROM open),
               (SELECT MAX(substr(l.labelled_at, 1, 10)) FROM news_label l JOIN news_article a ON a.ticker = l.ticker AND a.article_id = l.article_id
                JOIN members m ON m.ticker = a.ticker
                WHERE a.session_date < date(substr(l.labelled_at, 1, 10), '-1 day'));";

    const string SessionsAfter = "SELECT COUNT(DISTINCT session_date) FROM bar WHERE session_date > $day;";

    public async Task<IReadOnlyList<NewsMoodRow>> NewsMoodAsync()
    {
        var rows = new List<NewsMoodRow>();

        await using var connection = Open();

        string? session;

        await using (var newest = connection.CreateCommand())
        {
            newest.CommandText = "SELECT MAX(session_date) FROM bar;";
            session = await newest.ExecuteScalarAsync() as string;
        }

        foreach (var index in new[] { "GSPC", "MID", "SML" })
        {
            int holding;
            int open;
            string? backlog;

            await using (var command = connection.CreateCommand())
            {
                command.CommandText = NewsMoodOn;
                command.Parameters.AddWithValue("$index", index);
                command.Parameters.AddWithValue("$session", (object?)session ?? DBNull.Value);

                await using var reader = await command.ExecuteReaderAsync();
                await reader.ReadAsync();

                holding = reader.GetInt32(0);
                open = reader.GetInt32(1);
                backlog = reader.IsDBNull(2) ? null : reader.GetString(2);
            }

            var after = 0;

            if (open == 0 && backlog is not null)
            {
                await using var command = connection.CreateCommand();
                command.CommandText = SessionsAfter;
                command.Parameters.AddWithValue("$day", backlog);
                after = Convert.ToInt32(await command.ExecuteScalarAsync(), System.Globalization.CultureInfo.InvariantCulture);
            }

            rows.Add(new NewsMoodRow(index, holding, open, open == 0 ? backlog : null, after));
        }

        return rows;
    }
}
