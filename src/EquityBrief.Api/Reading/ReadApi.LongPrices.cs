using System.Text.Json;
using EquityBrief.Web.App;

namespace EquityBrief.Api.Reading;

// What the History page reads of the long history's prices: the newest run that wrote them, as its row on the run log states what
// it read and wrote.
// see: The long history's prices are asked once a code from 2004-01-02 and kept apart from the bars, the pull's tables and every night
public sealed partial class ReadApi
{
    const string LongPricesRun = "SELECT run_id, ended_at, detail FROM run_log WHERE stage = 'long-prices' ORDER BY ended_at DESC, run_id DESC LIMIT 1;";

    // The ends marked where the provider's prices run on past them, as the marks stand: a write finding a mark already
    // standing writes none, so the newest run's own count says nothing of what stands.
    const string LongEndsMarked = "SELECT COUNT(*) FROM session_mark WHERE kind = $kind;";

    public async Task<LongPricesView> LongPricesAsync()
    {
        await using var connection = Open();

        int marked;

        await using (var count = connection.CreateCommand())
        {
            count.CommandText = LongEndsMarked;
            count.Parameters.AddWithValue("$kind", EquityBrief.Core.Chart.SessionMarking.HistoryEnds);
            marked = Convert.ToInt32(await count.ExecuteScalarAsync(), System.Globalization.CultureInfo.InvariantCulture);
        }

        await using var command = connection.CreateCommand();

        command.CommandText = LongPricesRun;

        await using var reader = await command.ExecuteReaderAsync();

        if (!await reader.ReadAsync() || reader.IsDBNull(2))
        {
            return new LongPricesView(null, null, 0, 0, 0, 0, 0, null, 0, [], [], []);
        }

        using var detail = JsonDocument.Parse(reader.GetString(2));
        var root = detail.RootElement;

        int Count(string name) => root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number ? value.GetInt32() : 0;

        IReadOnlyList<string> Lines(string name) =>
            root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Array ? [.. value.EnumerateArray().Select(one => one.GetString()).OfType<string>()] : [];

        return new LongPricesView(
            reader.GetString(0),
            reader.GetString(1),
            Count("codes"),
            Count("priced"),
            root.TryGetProperty("bars", out var bars) && bars.ValueKind == JsonValueKind.Number ? bars.GetInt64() : 0,
            Count("ends"),
            marked,
            root.TryGetProperty("cutover", out var cutover) ? cutover.GetString() : null,
            Count("apartAtMost"),
            Lines("lowest"),
            Lines("nothing"),
            Lines("unsettled"));
    }
}
