using System.Globalization;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace EquityBrief.Core.Providers;

// A paid job's model over Claude's own messages interface.
//
// It names no model and no key: which model it asks for, the options it is asked with, the
// key and the prices are the profile's settings. Its readers are held to the provider's own
// answers, captured into the fixture: a section, one stopped at its budget, a refusal for a
// key naming no workspace, one for a model it does not serve, and the model list. The
// interface returns the answer as content blocks and the counts under `usage`, with the
// prompt tokens read from the provider's cache and those written to it counted apart from
// the rest, and a model's thinking counted inside the output it bills. It stamps no instant
// in the body, so the instant an answer carries is the response's own `Date` header, which
// is the provider's clock rather than this machine's.
// see: A paid model is one interface with an implementation per wire format, and a job never falls back from the profile it names
// see: A paid job names its model profile in one word the operator switches, and a profile is priced at its configured rates at its call's own timestamp
public sealed class AnthropicMessagesFeed(HttpClient client, ResearchModelSettings settings, ProviderRequest? request = null) : IResearchModelFeed
{
    public const string Path = "v1/messages";

    // The interface's own list of the models a key may call, which is what a probe asks for:
    // it bills nothing and answers only where the address and the key both do.
    public const string ModelsPath = "v1/models";

    // The headers every request carries: the key, never in an address, and the interface's
    // version, without which the provider answers nothing; and the workspace, where the key is
    // not scoped to one and the provider refuses it without one.
    public const string KeyHeader = "x-api-key";
    public const string VersionHeader = "anthropic-version";
    public const string Version = "2023-06-01";
    public const string WorkspaceHeader = "anthropic-workspace-id";

    // How long a probe waits, for the reason the other format's probe waits that long.
    public static readonly TimeSpan ProbeTimeout = TimeSpan.FromSeconds(30);

    // One attempt, for the other format's reason: a call that timed out may already be
    // billed, and a retry bills it again for the same answer.
    readonly ProviderRequest request = request
        ?? new ProviderRequest(new RetryPolicy(1, TimeSpan.Zero, settings.Timeout, settings.Timeout + settings.Timeout));

    public int Requests { get; private set; }

    public int Probes { get; private set; }

    public string Identity => settings.Identity;

    public static AnthropicMessagesFeed Live(ResearchModelSettings settings)
    {
        var client = new HttpClient
        {
            BaseAddress = new Uri(settings.BaseAddress),
            Timeout = settings.Timeout + settings.Timeout,
        };

        client.DefaultRequestHeaders.Add(KeyHeader, settings.ApiKey);

        foreach (var (name, values) in Headers(settings))
        {
            client.DefaultRequestHeaders.Add(name, values);
        }

        return new AnthropicMessagesFeed(client, settings);
    }

    // The headers every request carries beside the key, which is never among them so that
    // nothing reading these can print it: the interface's version, and the workspace where the
    // key is not scoped to one.
    public static IReadOnlyDictionary<string, string[]> Headers(ResearchModelSettings settings)
    {
        var headers = new Dictionary<string, string[]>(StringComparer.Ordinal) { [VersionHeader] = [Version] };

        if (settings.Workspace is { } workspace)
        {
            headers[WorkspaceHeader] = [workspace];
        }

        return headers;
    }

    public async Task<ResearchAnswer> CompleteAsync(ModelRequest wanted, CancellationToken cancellation = default)
    {
        Requests++;

        DateTimeOffset? answeredAt = null;

        var body = await request.SendAsync(
            async token =>
            {
                try
                {
                    using var content = new StringContent(Body(wanted, settings), Encoding.UTF8, "application/json");
                    using var response = await client.PostAsync(Path, content, token).ConfigureAwait(false);

                    var text = await response.Content.ReadAsStringAsync(token).ConfigureAwait(false);

                    answeredAt = response.Headers.Date;

                    return response.IsSuccessStatusCode
                        ? text
                        : throw new ProviderRefusal(Refused(settings.Job, wanted.Section, (int)response.StatusCode, text), transient: false);
                }
                catch (HttpRequestException unreachable)
                {
                    throw new ResearchModelUnavailable($"The {settings.Job} job's model could not be reached for {wanted.Section}: {unreachable.Message}");
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested && !cancellation.IsCancellationRequested)
                {
                    throw new ProviderRefusal(
                        $"The {settings.Job} job's model did not answer {wanted.Section} within {settings.Timeout.TotalSeconds.ToString(CultureInfo.InvariantCulture)} seconds.",
                        transient: false);
                }
            },
            cancellation).ConfigureAwait(false);

        return Parse(
            body,
            wanted.Section,
            answeredAt ?? throw new ProviderRefusal(
                $"The response for {wanted.Section} carries no Date header, and the instant a call is priced at is the provider's.",
                transient: false));
    }

    public async Task<string?> UnreachableAsync(CancellationToken cancellation = default)
    {
        Probes++;

        using var bounded = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
        bounded.CancelAfter(ProbeTimeout);

        try
        {
            using var response = await client.GetAsync(ModelsPath, bounded.Token).ConfigureAwait(false);

            return response.IsSuccessStatusCode
                ? null
                : Refused(settings.Job, "the model list", (int)response.StatusCode, await response.Content.ReadAsStringAsync(bounded.Token).ConfigureAwait(false));
        }
        catch (HttpRequestException unreachable)
        {
            return $"The {settings.Job} job's model could not be reached: {unreachable.Message}";
        }
        catch (OperationCanceledException) when (!cancellation.IsCancellationRequested)
        {
            return $"The {settings.Job} job's model did not answer a request for its model list within {ProbeTimeout.TotalSeconds.ToString(CultureInfo.InvariantCulture)} seconds.";
        }
    }

    // The request as it is sent: the profile's model, the answer's budget, the instructions as
    // the system prompt, the one user message and no stream, with the profile's own options
    // merged in beside them. The options cannot replace those five, which the settings refuse
    // at startup.
    public static string Body(ModelRequest wanted, ResearchModelSettings settings)
    {
        var body = new JsonObject
        {
            ["model"] = settings.Model,
            ["max_tokens"] = settings.AnswerTokens,
            ["system"] = wanted.System,
            ["messages"] = new JsonArray(new JsonObject { ["role"] = "user", ["content"] = wanted.Prompt }),
            ["stream"] = false,
        };

        if (wanted.DocumentIds.Count > 0)
        {
            body[OutputConfigField] = new JsonObject
            {
                ["format"] = new JsonObject
                {
                    ["type"] = "json_schema",
                    ["schema"] = EquityBrief.Core.News.NewsInstruction.IsLabel(wanted.Section) ? EquityBrief.Core.News.NewsInstruction.Schema()
                        : EquityBrief.Core.Research.RiskFields.IsRisks(wanted.Section) ? RisksSchema()
                        : SentencesSchema(),
                },
            };
        }

        // The profile's thinking: off, or an effort level, sent beside the answer's format in the same output settings;
        // none where the profile names none, which is the provider's default. Off is sent as thinking between tools,
        // which the models this build calls read as no thinking before the answer: they refuse thinking disabled
        // with a 400 that names this form, and a request with no tools has nothing between which to think.
        // see: Research names a profile per section as well as per job, and a Claude profile states its thinking
        if (settings.Thinking == ResearchModelSettings.ThinkingOff)
        {
            body[ThinkingField] = new JsonObject { ["type"] = ThinkingBetweenTools };
        }
        else if (settings.Thinking is { } effort)
        {
            var output = body[OutputConfigField] as JsonObject ?? new JsonObject();

            output[EffortField] = effort;
            body[OutputConfigField] = output;
        }

        if (settings.Options is { } options)
        {
            foreach (var (name, value) in JsonNode.Parse(options)!.AsObject())
            {
                body[name] = value?.DeepClone();
            }
        }

        return body.ToJsonString();
    }

    public const string ThinkingField = "thinking";
    public const string ThinkingBetweenTools = "between_tools";
    public const string EffortField = "effort";

    // Where a call lists documents, the answer is asked for in the shape the checker reads it by:
    // paragraphs of sentences, each sentence with the number of the listed document it rests on,
    // and the prose is built from them with every sentence ending in its marker. Asked for in plain
    // prose, the model opened the paragraphs of a short version with sentences citing nothing on
    // every draft the instructions told it to fold them away, and a sentence that carries its
    // document by the answer's shape cannot arrive without one. Whether that document supports the
    // sentence is still the checker's to read, as it is for a marker written in prose.
    public const string OutputConfigField = "output_config";

    public static JsonObject SentencesSchema() => new()
    {
        ["type"] = "object",
        ["properties"] = new JsonObject
        {
            ["paragraphs"] = new JsonObject
            {
                ["type"] = "array",
                ["items"] = new JsonObject
                {
                    ["type"] = "array",
                    ["items"] = new JsonObject
                    {
                        ["type"] = "object",
                        ["properties"] = new JsonObject
                        {
                            ["sentence"] = new JsonObject { ["type"] = "string", ["description"] = "one sentence of the section, written without a document marker" },
                            ["document"] = new JsonObject { ["type"] = "integer", ["description"] = "the number of the listed document the sentence rests on: 1 for [D1], 2 for [D2], or 0 where it rests on figures listed under Facts alone" },
                            ["facts"] = new JsonObject { ["type"] = "boolean", ["description"] = "true where the sentence states figures listed under Facts, which ends it with [N]" },
                        },
                        ["required"] = new JsonArray("sentence", "document", "facts"),
                        ["additionalProperties"] = false,
                    },
                },
            },
        },
        ["required"] = new JsonArray("paragraphs"),
        ["additionalProperties"] = false,
    };

    // The risks are asked for as their fields, which code composes into the prose the page draws: each risk
    // with the document it rests on, a listed fact with its direction and level or an event with one kind of
    // the seven, and why with its document. The answer's text is the object itself, which the writer reads.
    // see: Each risk is returned as fields and confirmed by a listed fact or an event of one kind, and no two risks share either
    public static JsonObject RisksSchema() => new()
    {
        ["type"] = "object",
        ["properties"] = new JsonObject
        {
            ["risks"] = new JsonObject
            {
                ["type"] = "array",
                ["items"] = new JsonObject
                {
                    ["type"] = "object",
                    ["properties"] = new JsonObject
                    {
                        ["risk"] = new JsonObject { ["type"] = "string", ["description"] = "words completing \"The first risk is\", written without a document marker" },
                        ["riskDocument"] = new JsonObject { ["type"] = "integer", ["description"] = "the number of the listed document that states the risk" },
                        ["confirm"] = new JsonObject
                        {
                            ["type"] = "object",
                            ["properties"] = new JsonObject
                            {
                                ["fact"] = new JsonObject { ["type"] = "string", ["description"] = "a fact's name exactly as it is listed under Facts" },
                                ["event"] = new JsonObject { ["type"] = "string", ["description"] = "an event in words, only where no listed fact could show the risk" },
                                ["kind"] = new JsonObject { ["type"] = "string", ["enum"] = new JsonArray([.. EquityBrief.Core.Research.RiskFields.Kinds.Select(kind => (JsonNode)kind)]) },
                            },
                            ["additionalProperties"] = false,
                        },
                        ["direction"] = new JsonObject { ["type"] = "string", ["enum"] = new JsonArray([.. EquityBrief.Core.Research.RiskFields.Directions.Select(direction => (JsonNode)direction)]) },
                        ["level"] = new JsonObject { ["type"] = "string", ["description"] = "a figure listed under Facts, copied or rounded" },
                        ["why"] = new JsonObject { ["type"] = "string", ["description"] = "words completing \"because\", written without a document marker" },
                        ["whyDocument"] = new JsonObject { ["type"] = "integer", ["description"] = "the number of the listed document that says why" },
                    },
                    ["required"] = new JsonArray("risk", "riskDocument", "confirm", "why", "whyDocument"),
                    ["additionalProperties"] = false,
                },
            },
        },
        ["required"] = new JsonArray("risks"),
        ["additionalProperties"] = false,
    };

    static readonly Regex Marker = new(@"\s*\[D\d+\]", RegexOptions.Compiled);

    // The prose an answer asked for as sentences comes to: each sentence with any marker it wrote
    // taken out and its own put before its closing stop, the sentences of a paragraph joined by a
    // space and the paragraphs by a blank line. None where the text is not that answer's shape.
    public static string? FromSentences(string text)
    {
        if (!text.StartsWith('{'))
        {
            return null;
        }

        JsonDocument parsed;

        try
        {
            parsed = JsonDocument.Parse(text);
        }
        catch (JsonException)
        {
            return null;
        }

        using (parsed)
        {
            if (parsed.RootElement.ValueKind != JsonValueKind.Object
                || !parsed.RootElement.TryGetProperty("paragraphs", out var paragraphs)
                || paragraphs.ValueKind != JsonValueKind.Array)
            {
                return null;
            }

            return string.Join("\n\n", paragraphs.EnumerateArray()
                .Where(paragraph => paragraph.ValueKind == JsonValueKind.Array)
                .Select(paragraph => string.Join(" ", paragraph.EnumerateArray()
                    .Where(one => one.ValueKind == JsonValueKind.Object && one.TryGetProperty("sentence", out var said) && said.ValueKind == JsonValueKind.String)
                    .Select(one => Cited(
                        AnswerText.Visible(Marker.Replace(one.GetProperty("sentence").GetString()!, string.Empty)),
                        one.TryGetProperty("document", out var cited) && cited.ValueKind == JsonValueKind.Number ? cited.GetInt32() : null,
                        one.TryGetProperty("facts", out var stated) && stated.ValueKind == JsonValueKind.True))
                    .Where(sentence => sentence.Length > 0)))
                .Where(paragraph => paragraph.Length > 0));
        }
    }

    // A sentence's marks before its closing stop: the document's where it names one above nothing, and [N] where it
    // states figures listed under Facts or names no document.
    static string Cited(string sentence, int? document, bool facts = false)
    {
        if (sentence.Length == 0 || (document is null && !facts))
        {
            return sentence;
        }

        var marker = (document is > 0 and var number ? " [D" + number.ToString(CultureInfo.InvariantCulture) + "]" : string.Empty)
            + (facts || document is 0 ? " " + EquityBrief.Core.Research.ClaimRules.NightMark : string.Empty);

        if (marker.Length == 0)
        {
            return sentence;
        }

        var end = sentence.Length;

        while (end > 0 && sentence[end - 1] is '"' or '\'' or ')' or '”' or '’')
        {
            end--;
        }

        return end > 0 && sentence[end - 1] is '.' or '!' or '?'
            ? sentence[..(end - 1)] + marker + sentence[(end - 1)..]
            : sentence + marker + ".";
    }

    // A response read as the captures show it arrives.
    //
    // The answer is the text of the `text` blocks in order, read back to prose where it was asked
    // for as sentences; a `thinking` block beside them is
    // never stored as a section. The counts are the interface's own: `input_tokens` is the
    // prompt the provider neither read from its cache nor wrote to it, and the two cache
    // counts are the rest. Thinking is billed inside `output_tokens`, so nothing is added for
    // it. A response carrying no usage is refused, because a call nobody can price is spend
    // the cap cannot see.
    public static ResearchAnswer Parse(string json, string section, DateTimeOffset answeredAt)
    {
        using var document = JsonDocument.Parse(json);

        var root = document.RootElement;

        if (!root.TryGetProperty("content", out var content) || content.ValueKind != JsonValueKind.Array)
        {
            throw new ProviderRefusal($"The response for {section} carries no content, so there is no answer to store.", transient: false);
        }

        if (!root.TryGetProperty("usage", out var usage) || usage.ValueKind != JsonValueKind.Object)
        {
            throw new ProviderRefusal(
                $"The response for {section} carries no usage, so it cannot be priced, and a call nobody can price is spend " +
                "the cap cannot see.",
                transient: false);
        }

        static int Count(JsonElement element, string name) =>
            element.TryGetProperty(name, out var count) && count.ValueKind == JsonValueKind.Number ? count.GetInt32() : 0;

        var said = AnswerText.Visible(string.Concat(content.EnumerateArray()
            .Where(block => block.TryGetProperty("type", out var type) && type.GetString() == "text"
                && block.TryGetProperty("text", out var one) && one.ValueKind == JsonValueKind.String)
            .Select(block => block.GetProperty("text").GetString())));

        var text = FromSentences(said) ?? said;

        var uncached = Count(usage, "input_tokens");
        var read = Count(usage, "cache_read_input_tokens");
        var written = Count(usage, "cache_creation_input_tokens");
        var output = Count(usage, "output_tokens");
        var stop = root.TryGetProperty("stop_reason", out var reason) && reason.ValueKind == JsonValueKind.String ? reason.GetString()! : "unstated";

        var answer = new ResearchAnswer(
            root.TryGetProperty("model", out var model) && model.ValueKind == JsonValueKind.String ? model.GetString()! : "unstated",
            text,
            uncached + read + written,
            read,
            uncached,
            output,
            0,
            stop,
            answeredAt,
            written);

        // Billed all the same, and thrown with the counts, so the call is priced at what the
        // provider charged for it rather than recorded as costing nothing.
        if (string.Equals(stop, "refusal", StringComparison.Ordinal))
        {
            throw new UnusableResearchAnswer(
                $"The model declined to answer {section}, and a section stored from a declined answer would be empty.",
                answer);
        }

        if (string.Equals(stop, "max_tokens", StringComparison.Ordinal))
        {
            throw new UnusableResearchAnswer(
                $"The answer for {section} stopped at its budget of tokens rather than ending, so what arrived is a section " +
                "cut short and it is not stored.",
                answer);
        }

        // What an answer with no visible text held, named by its blocks, since the captures
        // show the model thinking by default and ending after the thinking with no text at all.
        if (text.Length == 0)
        {
            var thinking = usage.TryGetProperty("output_tokens_details", out var details) && details.ValueKind == JsonValueKind.Object
                ? Count(details, "thinking_tokens")
                : 0;

            throw new UnusableResearchAnswer(
                $"The answer for {section} carried no text: it held {Held(content)}, and the stop reason was {stop} after " +
                $"{output} output token(s), {thinking} of them thinking. A section stored from this would be empty.",
                answer);
        }

        return answer;
    }

    // The blocks an answer held, counted by type in the order each type first arrived.
    public static string Held(JsonElement content)
    {
        var types = content.EnumerateArray()
            .Select(block => block.TryGetProperty("type", out var type) && type.ValueKind == JsonValueKind.String ? type.GetString()! : "untyped")
            .ToArray();

        return types.Length == 0
            ? "no block"
            : string.Join(" and ", types.Distinct(StringComparer.Ordinal).Select(type =>
            {
                var count = types.Count(one => string.Equals(one, type, StringComparison.Ordinal));

                return count.ToString(CultureInfo.InvariantCulture) + " " + type + (count == 1 ? " block" : " blocks");
            }));
    }

    // A recording of this interface: the response's `Date` header and its body as they
    // arrived, which is what a replay needs to price the call at the instant it was made.
    public const string RecordedDate = "date";
    public const string RecordedResponse = "response";

    public static ResearchAnswer ParseRecorded(string recording, string section)
    {
        using var document = JsonDocument.Parse(recording);

        var root = document.RootElement;
        var date = root.GetProperty(RecordedDate).GetString()!;

        return Parse(
            root.GetProperty(RecordedResponse).GetRawText(),
            section,
            DateTimeOffset.Parse(date, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal));
    }

    // A refusal from the provider, with its own words where it sent any, at a length a line
    // can hold. The interface's error is an object carrying `message`.
    public static string Refused(string job, string section, int status, string body)
    {
        var said = string.Empty;

        try
        {
            using var document = JsonDocument.Parse(body);

            if (document.RootElement.ValueKind == JsonValueKind.Object
                && document.RootElement.TryGetProperty("error", out var error)
                && error.ValueKind == JsonValueKind.Object
                && error.TryGetProperty("message", out var words)
                && words.ValueKind == JsonValueKind.String)
            {
                said = words.GetString()!.Trim();
            }
        }
        catch (JsonException)
        {
        }

        return said.Length == 0
            ? $"The {job} job's model refused {section} with status {status}."
            : $"The {job} job's model refused {section} with status {status}: {(said.Length > 300 ? said[..300] : said)}";
    }

    public decimal Price(ResearchAnswer answer) => settings.Pricing.Price(answer);

    public decimal Ceiling(ModelRequest wanted) => settings.Pricing.Ceiling(wanted, settings.AnswerTokens);
}
