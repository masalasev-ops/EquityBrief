using System.Globalization;
using EquityBrief.Web.App;
using EquityBrief.Web.Marks;

namespace EquityBrief.Api.Reading;

// The comparison command: the read surface started with the verb reads every report the store holds through the read
// API, writes each file the comparisons warrant into the folder it is handed, a file of the same name written again
// over the one before, and says for each comparison whose window is not yet full how far it has come. It writes nothing
// to the store and starts no surface.
// see: The drafts compared beside a report and the research template's before and after counts are written to files by a command, and drawn on no page
public static class ComparisonCommand
{
    public static async Task<IReadOnlyList<string>> WriteAsync(ReadApi read, ComparisonFiles files, string folder)
    {
        var view = RunScreen.Comparisons(await read.ReportRowsAsync(), await read.ReportVersionsAsync());
        var lines = new List<string>();

        Directory.CreateDirectory(folder);

        foreach (var (name, document) in files.Files(view))
        {
            await File.WriteAllTextAsync(Path.Combine(folder, name), document);
            lines.Add("wrote " + name);
        }

        if (!view.RatesReady)
        {
            lines.Add(string.Create(
                CultureInfo.InvariantCulture,
                $"section rates: {view.After.Count} of {ComparisonView.RatesWindow} reports written since the addendum merged and {view.Before.Count} of {ComparisonView.RatesWindow} before it, so no file yet"));
        }

        if (!view.BothSidesReady)
        {
            lines.Add(string.Create(
                CultureInfo.InvariantCulture,
                $"two cases on both sides: {view.SinceTheAsk.Count} of {ComparisonView.BothSidesWindow} reports drafting them since the ask changed, {view.BothSides} with a figure on both sides so far, so no file yet"));
        }

        if (view.Trials.Count == 0)
        {
            lines.Add("drafts: no trial or review has asked beside a report yet");
        }

        return lines;
    }
}
