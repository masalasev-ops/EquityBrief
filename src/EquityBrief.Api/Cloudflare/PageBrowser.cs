using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net.WebSockets;
using System.Text.Json;
using EquityBrief.Web.Cloudflare;

namespace EquityBrief.Api.Cloudflare;

// A browser the machine already has, started headless on a profile of its own in the temporary folder and removed after, its
// one page driven over its debugging socket on the loopback address. Every request the page makes is paused and answered by the
// caller, and one the caller does not answer is refused, so it asks nothing of any network. The read-only copy's layout check
// and the suite's alignment check both lay their pages out in it.
public sealed class PageBrowser : IAsyncDisposable
{
    static readonly TimeSpan Patience = TimeSpan.FromSeconds(60);

    readonly ClientWebSocket socket = new();
    readonly ConcurrentDictionary<int, TaskCompletionSource<JsonElement>> waiting = new();
    readonly Func<string, Task<(int Status, string Type, byte[] Body)?>> answer;
    readonly CancellationTokenSource stopping = new();
    readonly Process process;
    readonly string profile;
    Task? reading;
    int next;
    string session = string.Empty;
    TaskCompletionSource loaded = new(TaskCreationOptions.RunContinuationsAsynchronously);

    PageBrowser(Process process, string profile, Func<string, Task<(int Status, string Type, byte[] Body)?>> answer)
    {
        this.process = process;
        this.profile = profile;
        this.answer = answer;
    }

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

    // The browser started and its page opened, every request it makes answered by the function given; none where the machine
    // has no browser.
    public static async Task<PageBrowser?> OpenAsync(Func<string, Task<(int Status, string Type, byte[] Body)?>> answer)
    {
        if (Browsers().FirstOrDefault(File.Exists) is not { } browser)
        {
            return null;
        }

        var profile = Path.Combine(Path.GetTempPath(), "equitybrief-copy-layout-" + Guid.NewGuid().ToString("N"));
        var start = new ProcessStartInfo(browser) { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };

        foreach (var argument in new[] { "--headless=new", "--disable-gpu", "--no-first-run", "--no-default-browser-check", "--disable-background-networking", "--disable-component-update", "--disable-sync", "--disable-extensions", "--disable-default-apps", "--hide-scrollbars", "--remote-debugging-port=0", "--user-data-dir=" + profile, "about:blank" })
        {
            start.ArgumentList.Add(argument);
        }

        Directory.CreateDirectory(profile);

        var process = Process.Start(start) ?? throw new InvalidOperationException("the browser did not start");

        process.OutputDataReceived += (_, _) => { };
        process.ErrorDataReceived += (_, _) => { };
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        var opened = new PageBrowser(process, profile, answer);

        try
        {
            await opened.ConnectAsync();
        }
        catch
        {
            await opened.DisposeAsync();

            throw;
        }

        return opened;
    }

    async Task ConnectAsync()
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

        await socket.ConnectAsync(new Uri($"ws://127.0.0.1:{lines[0].Trim()}{lines[1].Trim()}"), CancellationToken.None);
        reading = Task.Run(ReadAsync);

        var target = await SendAsync("Target.createTarget", new { url = "about:blank" }, browser: true);
        var attached = await SendAsync("Target.attachToTarget", new { targetId = target.GetProperty("targetId").GetString(), flatten = true }, browser: true);

        session = attached.GetProperty("sessionId").GetString() ?? throw new InvalidOperationException("the browser attached no page");

        await SendAsync("Page.enable", new { });
        await SendAsync("Fetch.enable", new { patterns = new[] { new { urlPattern = "*" } } });
    }

    // A page laid out as the read-only copy's check reads it.
    public async Task<LaidOut> LayOutAsync(string url, (string Name, int Width, int Height, double Scale, bool Mobile) window) =>
        JsonSerializer.Deserialize<LaidOut>(await EvaluateAsync(url, window, SiteCopy.LayoutScript))
            ?? throw new InvalidOperationException($"{url} was laid out as nothing");

    // A page opened in the window given, in the dark palette where asked, and the script given run on it, its answer the string
    // the script returns.
    public async Task<string> EvaluateAsync(string url, (string Name, int Width, int Height, double Scale, bool Mobile) window, string script, bool dark = false)
    {
        await SendAsync("Emulation.setDeviceMetricsOverride", new { width = window.Width, height = window.Height, deviceScaleFactor = window.Scale, mobile = window.Mobile });
        await SendAsync("Emulation.setTouchEmulationEnabled", new { enabled = window.Mobile });
        await SendAsync("Emulation.setEmulatedMedia", new { features = new[] { new { name = "prefers-color-scheme", value = dark ? "dark" : "light" } } });
        await NavigateAsync("about:blank");
        await NavigateAsync(url);

        var measured = await SendAsync("Runtime.evaluate", new { expression = script, awaitPromise = true, returnByValue = true });

        if (measured.TryGetProperty("exceptionDetails", out var thrown))
        {
            throw new InvalidOperationException($"{url} threw while it was laid out: {thrown.GetRawText()}");
        }

        return measured.GetProperty("result").GetProperty("value").GetString() ?? "{}";
    }

    // The page as it stands, its whole height, as a PNG: scrolled to its top and the window grown to the page's height, so a bar
    // fixed to the window's top is drawn once at the page's top.
    public async Task<byte[]> ScreenshotAsync((string Name, int Width, int Height, double Scale, bool Mobile) window)
    {
        var height = await SendAsync("Runtime.evaluate", new { expression = "window.scrollTo(0, 0); Math.max(document.documentElement.scrollHeight, document.body.scrollHeight, innerHeight)", returnByValue = true });
        var tall = Math.Max(window.Height, height.GetProperty("result").GetProperty("value").GetInt32());

        await SendAsync("Emulation.setDeviceMetricsOverride", new { width = window.Width, height = tall, deviceScaleFactor = window.Scale, mobile = window.Mobile });

        var shot = await SendAsync("Page.captureScreenshot", new { format = "png" });

        return Convert.FromBase64String(shot.GetProperty("data").GetString() ?? string.Empty);
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
        var answered = await answer(url);

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

        try
        {
            process.Kill(entireProcessTree: true);
            await process.WaitForExitAsync();
        }
        catch (InvalidOperationException)
        {
        }

        process.Dispose();
        Removed(profile);
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
}
