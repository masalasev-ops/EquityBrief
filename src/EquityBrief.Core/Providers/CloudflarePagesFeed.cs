using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using EquityBrief.Core.Cloudflare;

namespace EquityBrief.Core.Providers;

// Cloudflare Pages by direct upload, as the host's own command-line tool uploads a folder: the project is read and made
// where it is missing, an upload token is asked for, the host is asked which files it lacks by their keys, those are sent in
// buckets, every key is confirmed, and a deployment is made on the production branch naming each address's key, with the
// headers file beside it. The API token is sent in a header to Cloudflare's API alone and the account in its addresses;
// neither is written to a log, a line printed or the copy, and every failure is said with both taken out. The project the
// operator named for another site is refused before any request.
// see: A read-only copy of the site is published to Cloudflare Pages from the read surface's own screens with no control, a link it does not hold drawn as plain text
public sealed class CloudflarePagesFeed(HttpClient http, string token, string account, string project) : ICloudflarePagesFeed
{
    public const string Api = "https://api.cloudflare.com/client/v4";

    public static CloudflarePagesFeed Live(string token, string account, string project) =>
        new(new HttpClient { Timeout = TimeSpan.FromMinutes(10) }, token, account, project);

    public async Task<string> DeployAsync(IReadOnlyList<PagesFile> files, string headers, Action<string> say)
    {
        if (string.Equals(project, PublishedSite.NeverProject, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"the project '{project}' is the operator's other site, and no upload names it");
        }

        try
        {
            return await Deploy(files, headers, say).ConfigureAwait(false);
        }
        catch (HttpRequestException failed)
        {
            throw new InvalidOperationException(Scrubbed("the host could not be reached: " + failed.Message), failed);
        }
    }

    async Task<string> Deploy(IReadOnlyList<PagesFile> files, string headers, Action<string> say)
    {
        if (await ProjectAsync().ConfigureAwait(false) is null)
        {
            say($"the project '{project}' does not exist, so it is made with '{PublishedSite.Branch}' its production branch");
            await SendAsync(HttpMethod.Post, $"/accounts/{account}/pages/projects", Json(new { name = project, production_branch = PublishedSite.Branch }), token, "make the project").ConfigureAwait(false);
        }

        var jwt = (await SendAsync(HttpMethod.Get, $"/accounts/{account}/pages/projects/{project}/upload-token", null, token, "ask for an upload token").ConfigureAwait(false))?["jwt"]?.GetValue<string>()
            ?? throw new InvalidOperationException("the upload token's answer carried no token");
        var hashes = files.Select(file => file.Hash).Distinct(StringComparer.Ordinal).ToArray();
        var missing = (await SendAsync(HttpMethod.Post, "/pages/assets/check-missing", Json(new { hashes }), jwt, "ask which files the host lacks").ConfigureAwait(false))?.AsArray().Select(hash => hash!.GetValue<string>()).ToHashSet(StringComparer.Ordinal) ?? [];
        var sending = files.Where(file => missing.Contains(file.Hash)).DistinctBy(file => file.Hash).OrderByDescending(file => file.Bytes.Length).ToArray();

        say($"{files.Count} files, {sending.Length} the host lacks, {sending.Sum(file => (long)file.Bytes.Length):N0} bytes to send");

        foreach (var bucket in PagesFiles.Buckets(sending))
        {
            var payload = new JsonArray([.. bucket.Select(file => (JsonNode)new JsonObject
            {
                ["key"] = file.Hash,
                ["value"] = Convert.ToBase64String(file.Bytes),
                ["metadata"] = new JsonObject { ["contentType"] = file.ContentType },
                ["base64"] = true,
            })]);

            await SendAsync(HttpMethod.Post, "/pages/assets/upload", new StringContent(payload.ToJsonString(), Encoding.UTF8, "application/json"), jwt, "send a bucket of files").ConfigureAwait(false);
        }

        await SendAsync(HttpMethod.Post, "/pages/assets/upsert-hashes", Json(new { hashes }), jwt, "confirm every file's hash").ConfigureAwait(false);

        using var form = new MultipartFormDataContent
        {
            { new StringContent(JsonSerializer.Serialize(files.ToDictionary(file => file.Address, file => file.Hash))), "manifest" },
            { new StringContent(PublishedSite.Branch), "branch" },
            { new StringContent("The read-only copy"), "commit_message" },
        };
        var headersFile = new ByteArrayContent(Encoding.UTF8.GetBytes(headers));

        headersFile.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        form.Add(headersFile, "_headers", "_headers");

        var deployment = await SendAsync(HttpMethod.Post, $"/accounts/{account}/pages/projects/{project}/deployments", form, token, "make the deployment").ConfigureAwait(false);

        return deployment?["url"]?.GetValue<string>() ?? "(the deployment's answer named no address)";
    }

    // The project, or none where the host answers that it does not exist.
    async Task<JsonNode?> ProjectAsync()
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, Api + $"/accounts/{account}/pages/projects/{project}");

        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        using var answer = await http.SendAsync(request).ConfigureAwait(false);

        if (answer.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        return Result(await answer.Content.ReadAsStringAsync().ConfigureAwait(false), answer.StatusCode, "read the project");
    }

    static StringContent Json(object body) => new(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");

    async Task<JsonNode?> SendAsync(HttpMethod method, string path, HttpContent? content, string bearer, string step)
    {
        using var request = new HttpRequestMessage(method, Api + path) { Content = content };

        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearer);

        using var answer = await http.SendAsync(request).ConfigureAwait(false);

        return Result(await answer.Content.ReadAsStringAsync().ConfigureAwait(false), answer.StatusCode, step);
    }

    // The answer's result, or a failure naming the step, the status and the host's own messages, with the token and the
    // account taken out of them.
    JsonNode? Result(string body, HttpStatusCode status, string step)
    {
        JsonNode? parsed = null;

        try
        {
            parsed = JsonNode.Parse(body);
        }
        catch (JsonException)
        {
        }

        if ((int)status is >= 200 and < 300 && parsed?["success"]?.GetValue<bool>() == true)
        {
            return parsed["result"];
        }

        var said = parsed?["errors"] is JsonArray errors && errors.Count > 0
            ? string.Join("; ", errors.Select(error => $"{error?["code"]}: {error?["message"]}"))
            : body.Length > 300 ? body[..300] : body;

        throw new InvalidOperationException(Scrubbed($"could not {step}: the host answered {(int)status}, {said}"));
    }

    public string Scrubbed(string said) =>
        new[] { token, account }.Where(secret => secret.Length > 0).Aggregate(said, (text, secret) => text.Replace(secret, "(withheld)", StringComparison.Ordinal));
}
