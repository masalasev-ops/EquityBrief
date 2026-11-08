using System.Text;

namespace EquityBrief.Web.App;

// One index's labels for the industry news mood split: the members holding a stored story, those holding one stored before
// the newest session no label stands on, the day every such story stood labelled from, and the sessions after it.
public sealed record NewsMoodRow(string Index, int Holding, int Open, string? LabelledFrom, int SessionsAfter);

// From 23.9 the History page's labels row: the industry news mood split waits until every member holding a stored story has
// held its labels for 126 sessions, and the row says how far each index stands from it.
// see: The sector splits read a sector as it stood off the funds' industry headings through a stated map, and the filed GICS sector after the schedules end
public sealed partial class SinglePageApp
{
    public const int NewsMoodSessions = 126;

    public const string NewsMoodLede =
        "The split of each family's setups by how bad its industry's news read waits for every member holding a story to have its stories labelled, then for 126 sessions of them. Until then nothing is measured.";

    static readonly (string Index, string Name)[] MoodIndices = [("GSPC", "S&P 500"), ("MID", "S&P 400"), ("SML", "S&P 600")];

    string NewsMoodCard(IReadOnlyList<NewsMoodRow> rows)
    {
        var body = new StringBuilder("<div class=\"tbl-wrap\"><table class=\"history-table\" data-table=\"news-mood\"><thead><tr><th>Index</th><th class=\"r\">Members holding a story</th><th class=\"r\">Holding one unlabelled</th><th>Labelled from</th><th class=\"r\">Sessions of 126</th></tr></thead><tbody>");

        foreach (var (index, name) in MoodIndices)
        {
            var row = rows.FirstOrDefault(one => one.Index == index);

            if (row is null)
            {
                continue;
            }

            var from = row.LabelledFrom is { } day ? Escaped(day) : "not yet";
            var sessions = row.LabelledFrom is null ? "none" : Invariant($"{Math.Min(row.SessionsAfter, NewsMoodSessions)}");

            body.Append(Invariant($"<tr data-mood=\"{index}\" data-ready=\"{(row.LabelledFrom is not null && row.SessionsAfter >= NewsMoodSessions ? "yes" : "no")}\"><td>{Escaped(name)}</td><td class=\"n\">{row.Holding:N0}</td><td class=\"n\">{row.Open:N0}</td><td>{from}</td><td class=\"n\">{sessions}</td></tr>"));
        }

        body.Append("</tbody></table></div>");

        return Cards.Computed("News mood", body.ToString(), title: "Labels for the industry news mood", lede: NewsMoodLede, region: "news-mood");
    }
}
