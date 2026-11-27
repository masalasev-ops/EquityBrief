using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using EquityBrief.Web.App;
using EquityBrief.Web.Cloudflare;

namespace EquityBrief.Api.Cloudflare;

// The read-only copy's Home and Tonight laid out in a browser beside the app's on every index, in a desktop's window and a
// phone's, before anything is uploaded. The browser is one the machine already has, started headless on a profile of its
// own in the temporary folder, removed after; every page it asks for is answered from inside this process, the app's from
// the surface's own pipeline and the copy's from its folder, and every other request it would make is refused, so it asks
// nothing of any network. A browser not found, or a page it cannot lay out, is a fault like any other.
// see: The read-only copy draws each page as the app draws it, and a link it does not hold keeps its element
public static class CopyLayout
{
    const string AppOrigin = "http://app.equitybrief.invalid";

    const string CopyOrigin = "http://copy.equitybrief.invalid";

    static readonly TimeSpan Patience = TimeSpan.FromSeconds(60);

    // The browsers looked for, in order: Chrome, then Edge, where each installs on the machines this tool runs on.
    public static IEnumerable<string> Browsers()
    {
        foreach (var root in new[] { Environment.SpecialFolder.ProgramFiles, Environment.SpecialFolder.ProgramFilesX86, Environment.SpecialFolder.LocalApplicationData }
            .Select(Environment.GetFolderPath)
            .Where(root => root.Length > 0))
        {
            yield return Path.Combine(root, "Google", "Chrome", "Application", "chrome.exe");
            yield return Path.Combine(root, "Microsoft", "Edge", "Application", "msedge.exe");
        }

        yield return Path.Combine("/Applications", "Google Chrome.app", "Contents", "MacOS", "Google Chrome");
        yield return Path.Combine("/usr", "bin", "google-chrome");
        yield return Path.Combine("/usr", "bin", "chromium");
    }

    // Each fault of Home and Tonight on every index in each window, the copy's against the app's, each naming its page. The
    // app's shell is the surface's own; the screens it asks for are those given, Home and Tonight as the app draws them in the
    // copy's words with every link as the app links it, so what the check reads apart is what the copy's links and its shell
    // change.
    public static async Task<IReadOnlyList<string>> FaultsAsync(Func<string, Task<(int Status, string Body)>> surface, IReadOnlyDictionary<string, string> screens, string folder, Action<string> say)
    {
        if (Browsers().FirstOrDefault(File.Exists) is not { } browser)
        {
            return ["no browser was found to lay the copy out beside the app, so Home's and Tonight's layout went unread"];
        }

        var profile = Path.Combine(Path.GetTempPath(), "equitybrief-copy-layout-" + Guid.NewGuid().ToString("N"));
        var start = new ProcessStartInfo(browser) { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };

        foreach (var argument in new[] { "--headless=new", "--disable-gpu", "--no-first-run", "--no-default-browser-check", "--disable-background-networking", "--disable-component-update", "--disable-sync", "--disable-extensions", "--disable-default-apps", "--hide-scrollbars", "--remote-debugging-port=0", "--user-data-dir=" + profile, "about:blank" })
        {
            start.ArgumentList.Add(argument);
        }

        Directory.CreateDirectory(profile);

        using var process = Process.Start(start) ?? throw new InvalidOperationException("the browser did not start");

        process.OutputDataReceived += (_, _) => { };
        process.ErrorDataReceived += (_, _) => { };
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        var faults = new List<string>();

        try
        {
            await using var browserSession = await Session.OpenAsync(profile, (route, copy) => Answer(surface, screens, folder, route, copy));

            foreach (var index in Universes.Offered)
            {
                foreach (var screen in new[] { SinglePageApp.HomeRoute, SinglePageApp.TonightRoute })
                {
                    var hash = SiteCopy.Canonical(index == Universes.Large ? screen : screen + "?" + Universes.Query + "=" + index.Word);

                    foreach (var window in SiteCopy.Windows)
                    {
                        var page = $"{(screen == SinglePageApp.HomeRoute ? "Home" : "Tonight")} on the {index.Name} at {window.Name}";
                        var app = await browserSession.LayOutAsync(AppOrigin + "/" + hash, window);
                        var copy = await browserSession.LayOutAsync(CopyOrigin + "/" + hash, window);

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
        finally
        {
            process.Kill(entireProcessTree: true);
            await process.WaitForExitAsync();
            Removed(profile);
        }

        return faults;
    }

    // The browser's profile, removed whole once it has stopped, tried again while it still lets go of its files.
    static void Removed(string profile)
    {
        for (var tried = 1; ; tried++)
        {
            try
            {
                if (Directory.Exists(profile))
                {
                    Directory.Delete(profile, recursive: true);
                }

                return;
            }
            catch (IOException) when (tried < 10)
            {
                Thread.Sleep(500);
            }
            catch (UnauthorizedAccessException) when (tried < 10)
            {
                Thread.Sleep(500);
            }
        }
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

    // One browser's page driven over its debugging socket on the loopback address, every request it makes paused and
    // answered here.
    sealed class Session : IAsyncDisposable
    {
        readonly ClientWebSocket socket = new();
        readonly ConcurrentDictionary<int, TaskCompletionSource<JsonElement>> waiting = new();
        readonly Func<string, bool, Task<(int Status, string Type, byte[] Body)?>> answer;
        readonly CancellationTokenSource stopping = new();
        Task? reading;
        int next;
        string session = string.Empty;
        TaskCompletionSource loaded = new(TaskCreationOptions.RunContinuationsAsynchronously);

        Session(Func<string, bool, Task<(int Status, string Type, byte[] Body)?>> answer) => this.answer = answer;

        public static async Task<Session> OpenAsync(string profile, Func<string, bool, Task<(int Status, string Type, byte[] Body)?>> answer)
        {
            var port = Path.Combine(profile, "DevToolsActivePort");
            var waited = Stopwatch.StartNew();

            while (!File.Exists(port) || File.ReadAllLines(port).Length < 2)
            {
                if (waited.Elapsed > Patience)
                {
                    throw new TimeoutException("the browser opened no debugging port");
                }

                await Task.Delay(100);
            }

            var lines = File.ReadAllLines(port);
            var opened = new Session(answer);

            await opened.socket.ConnectAsync(new Uri($"ws://127.0.0.1:{lines[0].Trim()}{lines[1].Trim()}"), CancellationToken.None);
            opened.reading = Task.Run(opened.ReadAsync);

            var target = await opened.SendAsync("Target.createTarget", new { url = "about:blank" }, browser: true);
            var attached = await opened.SendAsync("Target.attachToTarget", new { targetId = target.GetProperty("targetId").GetString(), flatten = true }, browser: true);

            opened.session = attached.GetProperty("sessionId").GetString() ?? throw new InvalidOperationException("the browser attached no page");

            await opened.SendAsync("Page.enable", new { });
            await opened.SendAsync("Fetch.enable", new { patterns = new[] { new { urlPattern = "*" } } });

            return opened;
        }

        public async Task<LaidOut> LayOutAsync(string url, (string Name, int Width, int Height, double Scale, bool Mobile) window)
        {
            await SendAsync("Emulation.setDeviceMetricsOverride", new { width = window.Width, height = window.Height, deviceScaleFactor = window.Scale, mobile = window.Mobile });
            await SendAsync("Emulation.setTouchEmulationEnabled", new { enabled = window.Mobile });
            await NavigateAsync("about:blank");
            await NavigateAsync(url);

            var measured = await SendAsync("Runtime.evaluate", new { expression = SiteCopy.LayoutScript, awaitPromise = true, returnByValue = true });

            if (measured.TryGetProperty("exceptionDetails", out var thrown))
            {
                throw new InvalidOperationException($"{url} threw while it was laid out: {thrown.GetRawText()}");
            }

            return JsonSerializer.Deserialize<LaidOut>(measured.GetProperty("result").GetProperty("value").GetString() ?? "{}")
                ?? throw new InvalidOperationException($"{url} was laid out as nothing");
        }

        async Task NavigateAsync(string url)
        {
            loaded = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            await SendAsync("Page.navigate", new { url });

            if (await Task.WhenAny(loaded.Task, Task.Delay(Patience)) != loaded.Task)
            {
                throw new TimeoutException($"{url} did not load");
            }
        }

        async Task<JsonElement> SendAsync(string method, object parameters, bool browser = false)
        {
            var id = Interlocked.Increment(ref next);
            var done = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);

            waiting[id] = done;

            var message = browser
                ? JsonSerializer.SerializeToUtf8Bytes(new { id, method, @params = parameters })
                : JsonSerializer.SerializeToUtf8Bytes(new { id, method, @params = parameters, sessionId = session });

            lock (socket)
            {
                socket.SendAsync(message, WebSocketMessageType.Text, true, CancellationToken.None).GetAwaiter().GetResult();
            }

            if (await Task.WhenAny(done.Task, Task.Delay(Patience)) != done.Task)
            {
                throw new TimeoutException($"the browser did not answer {method}");
            }

            return await done.Task;
        }

        async Task ReadAsync()
        {
            var buffer = new byte[1 << 16];

            while (!stopping.IsCancellationRequested && socket.State == WebSocketState.Open)
            {
                using var whole = new MemoryStream();
                WebSocketReceiveResult part;

                try
                {
                    do
                    {
                        part = await socket.ReceiveAsync(buffer, stopping.Token);
                        whole.Write(buffer, 0, part.Count);
                    }
                    while (!part.EndOfMessage);
                }
                catch (Exception failed) when (failed is OperationCanceledException or WebSocketException)
                {
                    break;
                }

                if (part.MessageType == WebSocketMessageType.Close)
                {
                    break;
                }

                using var document = JsonDocument.Parse(whole.ToArray());
                var root = document.RootElement.Clone();

                if (root.TryGetProperty("id", out var id) && waiting.TryRemove(id.GetInt32(), out var done))
                {
                    if (root.TryGetProperty("error", out var error))
                    {
                        done.SetException(new InvalidOperationException("the browser refused: " + error.GetRawText()));
                    }
                    else
                    {
                        done.SetResult(root.TryGetProperty("result", out var result) ? result : default);
                    }
                }
                else if (root.TryGetProperty("method", out var method))
                {
                    switch (method.GetString())
                    {
                        case "Page.loadEventFired":
                            loaded.TrySetResult();
                            break;
                        case "Fetch.requestPaused":
                            _ = Task.Run(() => AnswerAsync(root.GetProperty("params")));
                            break;
                    }
                }
            }

            foreach (var pending in waiting.Values)
            {
                pending.TrySetException(new WebSocketException("the browser's socket closed"));
            }
        }

        async Task AnswerAsync(JsonElement paused)
        {
            var id = paused.GetProperty("requestId").GetString();
            var url = paused.GetProperty("request").GetProperty("url").GetString() ?? string.Empty;
            var answered = url.StartsWith(AppOrigin + "/", StringComparison.Ordinal) ? await answer(url, false)
                : url.StartsWith(CopyOrigin + "/", StringComparison.Ordinal) ? await answer(url, true)
                : null;

            try
            {
                if (answered is { } page)
                {
                    await SendAsync("Fetch.fulfillRequest", new { requestId = id, responseCode = page.Status, responseHeaders = new[] { new { name = "Content-Type", value = page.Type } }, body = Convert.ToBase64String(page.Body) });
                }
                else
                {
                    await SendAsync("Fetch.failRequest", new { requestId = id, errorReason = "BlockedByClient" });
                }
            }
            catch (Exception failed) when (failed is InvalidOperationException or TimeoutException or WebSocketException)
            {
            }
        }

        public async ValueTask DisposeAsync()
        {
            stopping.Cancel();

            if (reading is not null)
            {
                await reading;
            }

            socket.Dispose();
            stopping.Dispose();
        }
    }
}
