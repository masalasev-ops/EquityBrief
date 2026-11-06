using EquityBrief.Core.Components;
using EquityBrief.Core.History;
using EquityBrief.Core.Providers;

namespace EquityBrief.Worker.History;

// A filer's filings as the SEC's archive lists them, kept whole in a folder before anything reads them, and the session its common
// stock's history ends on read from them by the core's rule. The company's filing index and each of its older pages, and the
// document of each removal from listing and each registration of a class on an exchange, one request a file; nothing in the store
// is read or written.
// see: A stock's history ends by rule on its company's last regular session read from its Form 25 or the 8-K completing its merger
public sealed class HistoryEndFilings(IFilerFilingsFeed feed) : IComponent
{
    public static ComponentAccess Access => new(Stores: [], Feeds: [Feed.FilingsArchive]);

    // At most this many documents a filer, the newest first, so a bank filing a removal for every note it retires costs a bounded read.
    public const int DocumentsAtMost = 120;

    public const string IndexFile = "submissions.json";

    public int Requests => feed.Requests;

    // Every file the read asks for, written under the folder as the archive sent it, and the end the rule reads from them.
    public async Task<HistoryEndRead> CaptureAsync(string cik, string folder, CancellationToken cancellation = default)
    {
        Directory.CreateDirectory(folder);

        var index = await feed.IndexAsync(cik, cancellation);
        await File.WriteAllTextAsync(Path.Combine(folder, IndexFile), index, cancellation);

        var pages = new List<string> { index };

        foreach (var page in FilerFilings.Pages(index))
        {
            var text = await feed.PageAsync(page, cancellation);

            await File.WriteAllTextAsync(Path.Combine(folder, page), text, cancellation);
            pages.Add(text);
        }

        var documents = Path.Combine(folder, FilerFilings.DocumentsFolder);

        Directory.CreateDirectory(documents);

        foreach (var filing in FilerFilings.Read(pages).Where(filing => HistoryEnd.NeedsItsDocument(filing.Form)).OrderByDescending(filing => filing.Filed).Take(DocumentsAtMost))
        {
            var text = await feed.DocumentAsync(cik, filing.Accession, FilerFilings.AsFiled(filing.PrimaryDocument), cancellation);

            await File.WriteAllTextAsync(Path.Combine(documents, FilerFilings.DocumentFile(filing)), text, cancellation);
        }

        return HistoryEnd.Of(FilerFilings.Read(folder));
    }
}
