using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using EquityBrief.Web.App;
using EquityBrief.Web.Cloudflare;

namespace EquityBrief.Api.Cloudflare;

// The read-only copy's Home and Tonight laid out in a browser beside the app's on every index, in a desktop's window and a
// phone's, before anything is uploaded. The browser is the machine's own, driven by PageBrowser: every page it asks for is
// answered from inside this process, the app's from the surface's own pipeline and the copy's from its folder, and every other
// request it would make is refused, so it asks nothing of any network. A browser not found, or a page it cannot lay out, is a
// fault like any other.
// see: The read-only copy draws each page as the app draws it, and a link it does not hold keeps its element
public static class CopyLayout
{
    const string AppOrigin = "http://app.equitybrief.invalid";

    const string CopyOrigin = "http://copy.equitybrief.invalid";

    // The browsers looked for, in order: Chrome, then Edge, where each installs on the machines this tool runs on.
    public static IEnumerable<string> Browsers() => PageBrowser.Browsers();

    // Each fault of Home and Tonight on every index in each window, the copy's against the app's, each naming its page. The
    // app's shell is the surface's own; the screens it asks for are those given, Home and Tonight as the app draws them in the
    // copy's words with every link as the app links it, so what the check reads apart is what the copy's links and its shell
    // change.
    public static async Task<IReadOnlyList<string>> FaultsAsync(Func<string, Task<(int Status, string Body)>> surface, IReadOnlyDictionary<string, string> screens, string folder, Action<string> say)
    {
        var faults = new List<string>();

        try
        {
            await using var browser = await PageBrowser.OpenAsync(url =>
                url.StartsWith(AppOrigin + "/", StringComparison.Ordinal) ? Answer(surface, screens, folder, url, copy: false)
                : url.StartsWith(CopyOrigin + "/", StringComparison.Ordinal) ? Answer(surface, screens, folder, url, copy: true)
                : Task.FromResult<(int Status, string Type, byte[] Body)?>(null));

            if (browser is null)
            {
                return ["no browser was found to lay the copy out beside the app, so Home's and Tonight's layout went unread"];
            }

            foreach (var index in Universes.Offered)
            {
                foreach (var screen in new[] { SinglePageApp.HomeRoute, SinglePageApp.TonightRoute })
                {
                    var hash = SiteCopy.Canonical(index == Universes.Large ? screen : screen + "?" + Universes.Query + "=" + index.Word);

                    foreach (var window in SiteCopy.Windows)
                    {
                        var page = $"{(screen == SinglePageApp.HomeRoute ? "Home" : "Tonight")} on the {index.Name} at {window.Name}";
                        var app = await browser.LayOutAsync(AppOrigin + "/" + hash, window);
                        var copy = await browser.LayOutAsync(CopyOrigin + "/" + hash, window);

                        faults.AddRange(SiteCopy.LayoutFaults(page, app, copy));
                    }
                }

                say($"{index.Name}: Home and Tonight laid out beside the app's at {string.Join(" and ", SiteCopy.Windows.Select(window => window.Name))}");
            }
        }
        catch (Exception failed) when (failed is WebSocketException or InvalidOperationException or TimeoutException or IOException or JsonException)
        {
            faults.Add("the browser could not lay the copy out beside the app: " + failed.Message);
        }

        return faults;
    }

    // A page the browser asks for: the app's from the surface, the copy's from its folder, and none for any other address.
    static async Task<(int Status, string Type, byte[] Body)?> Answer(Func<string, Task<(int Status, string Body)>> surface, IReadOnlyDictionary<string, string> screens, string folder, string url, bool copy)
    {
        var address = new Uri(url);
        var route = address.PathAndQuery;

        if (!copy)
        {
            if (screens.TryGetValue(route, out var screen))
            {
                return (200, "text/html; charset=utf-8", Encoding.UTF8.GetBytes(screen));
            }

            try
            {
                var (status, body) = await surface(route);

                return (status, "text/html; charset=utf-8", Encoding.UTF8.GetBytes(body));
            }
            catch (Exception failed) when (failed is InvalidOperationException or IOException or ArgumentException)
            {
                return null;
            }
        }

        var relative = Uri.UnescapeDataString(address.AbsolutePath.TrimStart('/'));
        var file = Path.GetFullPath(Path.Combine([folder, .. (relative.Length == 0 ? SiteCopy.ShellFile : relative).Split('/')]));

        return file.StartsWith(Path.GetFullPath(folder), StringComparison.Ordinal) && File.Exists(file)
            ? (200, "text/html; charset=utf-8", await File.ReadAllBytesAsync(file))
            : (404, "text/html; charset=utf-8", Encoding.UTF8.GetBytes(SiteCopy.NotHeld));
    }
}
