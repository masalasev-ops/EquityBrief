using System.Globalization;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace EquityBrief.Core.Providers;

// The local model, over the OpenAI-compatible chat completions endpoint the
// operator's runtime serves.
//
// The interface LM Studio, llama.cpp's server and Ollama all serve, so the runtime
// is the operator's choice. What this was written against is two responses
// captured from LM Studio serving qwen/qwen3.5-9b before a line of it existed, and
// both of them are why it is shaped the way it is.
// see: The local model answers at an OpenAI-compatible endpoint, and which model answers is configuration
public sealed class OpenAiCompatibleModelFeed(
    HttpClient client,
    LocalModelSettings settings,
    ProviderRequest? request = null) : ILocalModelFeed
{
    // One attempt. A model call that timed out has already spent its time on the
    // operator's card, and a retry spends it again for the same answer.
    readonly ProviderRequest request = request
        ?? new ProviderRequest(new RetryPolicy(1, TimeSpan.Zero, settings.Timeout, settings.Timeout + settings.Timeout));

    public const string Path = "chat/completions";

    // The answer budget of one section call. A section is a paragraph or a sentence
    // per business unit, and a thousand tokens holds several of either.
    public const int AnswerTokens = 1024;

    public int Requests { get; private set; }

    public static OpenAiCompatibleModelFeed Live(LocalModelSettings settings) =>
        new(
            new HttpClient
            {
                BaseAddress = new Uri(settings.BaseAddress.EndsWith('/') ? settings.BaseAddress : settings.BaseAddress + "/"),
                Timeout = settings.Timeout + settings.Timeout,
            },
            settings);

    public async Task<ModelAnswer> CompleteAsync(ModelRequest wanted, CancellationToken cancellation = default)
    {
        Requests++;

        var body = await request.SendAsync(
            async token =>
            {
                try
                {
                    using var content = new StringContent(Body(wanted), Encoding.UTF8, "application/json");
                    using var response = await client.PostAsync(Path, content, token).ConfigureAwait(false);

                    var text = await response.Content.ReadAsStringAsync(token).ConfigureAwait(false);

                    return response.IsSuccessStatusCode
                        ? text
                        : throw new ProviderRefusal(Refused(wanted.Section, (int)response.StatusCode, text), transient: false);
                }
                catch (HttpRequestException unreachable)
                {
                    // The runtime not answering at all, which is section 18's local
                    // model unavailable: every section still to be asked for in the
                    // pass is left unwritten with this reason.
                    throw new LocalModelUnavailable(
                        $"The local model could not be reached for {wanted.Section}: {unreachable.Message}");
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested && !cancellation.IsCancellationRequested)
                {
                    // The call's own bound, rather than the caller giving up. A model
                    // that took longer than the timeout over one section may answer the
                    // next, so this is a refusal of the section and not of the runtime.
                    throw new ProviderRefusal(
                        $"The local model did not answer {wanted.Section} within {settings.Timeout.TotalSeconds.ToString(CultureInfo.InvariantCulture)} seconds.",
                        transient: false);
                }
            },
            cancellation).ConfigureAwait(false);

        return Parse(body, wanted.Section);
    }

    // The request as it is sent.
    //
    // `reasoning_effort` is none because the captured model is a thinking model:
    // without it, a one-sentence request spent its whole budget reasoning and came
    // back with an empty answer. Of four ways to turn reasoning off on the runtime
    // it was captured from, this field is the one that did; the other three were
    // accepted and ignored. Temperature zero, so a pass over one evidence set is as
    // repeatable as the runtime makes it, which is what a recording keyed on the
    // request assumes.
    public static string Body(ModelRequest wanted) =>
        JsonSerializer.Serialize(new
        {
            model = wanted.Model,
            messages = new object[]
            {
                new { role = "system", content = wanted.System },
                new { role = "user", content = wanted.Prompt },
            },
            temperature = 0,
            max_tokens = AnswerTokens,
            stream = false,
            reasoning_effort = "none",
        });

    // The finish reason a runtime gives an answer that ran out of budget.
    public const string CutOff = "length";

    // The longest stretch of a runtime's own refusal carried into a reason.
    const int RefusalExcerpt = 300;

    // A refusal from the runtime, with its own words where it sent any.
    //
    // Read as the overflow capture shows it arrives: a 400 whose `error` is a string
    // wrapping the engine's message, which names the prompt's token count and the
    // loaded context. The OpenAI shape, an object carrying `message`, is read too,
    // because the runtime is the operator's choice. What reaches the run log is the
    // message and not the body, cut to a length a line can hold.
    public static string Refused(string section, int status, string body)
    {
        var said = string.Empty;

        try
        {
            using var document = JsonDocument.Parse(body);

            if (document.RootElement.ValueKind == JsonValueKind.Object
                && document.RootElement.TryGetProperty("error", out var error))
            {
                said = error.ValueKind switch
                {
                    JsonValueKind.String => error.GetString()!,
                    JsonValueKind.Object when error.TryGetProperty("message", out var words) && words.ValueKind == JsonValueKind.String => words.GetString()!,
                    _ => string.Empty,
                };
            }
        }
        catch (JsonException)
        {
            // A body that is not JSON is stated as absent rather than quoted, since
            // what a runtime sends in its place is a page nobody wrote for a log.
        }

        said = said.Trim();

        return said.Length == 0
            ? $"The local model refused {section} with status {status}."
            : $"The local model refused {section} with status {status}: {(said.Length > RefusalExcerpt ? said[..RefusalExcerpt] : said)}";
    }

    // A response read as the captures showed it arrives.
    //
    // The answer is `content`. An empty one is refused rather than returned, and the
    // refusal says why, because the captured thinking response is exactly that: a
    // budget spent in `reasoning_content`, nothing in `content`, and a finish reason
    // of length. Returned as text it would be stored as a section with no prose. A
    // runtime that inlines its reasoning in the answer between think tags has it
    // taken out, because a reader of the section is owed the answer and not the
    // working.
    public static ModelAnswer Parse(string json, string section)
    {
        using var document = JsonDocument.Parse(json);

        var root = document.RootElement;

        if (!root.TryGetProperty("choices", out var choices)
            || choices.ValueKind != JsonValueKind.Array
            || choices.GetArrayLength() == 0)
        {
            throw new ProviderRefusal(
                $"The local model's response for {section} carries no choices, so there is no answer to store.",
                transient: false);
        }

        var choice = choices[0];
        var message = choice.GetProperty("message");
        var finish = choice.TryGetProperty("finish_reason", out var reason) && reason.ValueKind == JsonValueKind.String
            ? reason.GetString()!
            : "unstated";

        var text = message.TryGetProperty("content", out var content) && content.ValueKind == JsonValueKind.String
            ? Regex.Replace(content.GetString()!, @"<think>[\s\S]*?</think>", string.Empty).Trim()
            : string.Empty;

        var usage = root.TryGetProperty("usage", out var used) ? used : default;

        int Tokens(string name) =>
            usage.ValueKind == JsonValueKind.Object && usage.TryGetProperty(name, out var count) && count.ValueKind == JsonValueKind.Number
                ? count.GetInt32()
                : 0;

        if (text.Length == 0)
        {
            var reasoning = message.TryGetProperty("reasoning_content", out var worked) && worked.ValueKind == JsonValueKind.String
                ? worked.GetString()!.Length
                : 0;

            throw new ProviderRefusal(
                $"The local model returned no answer for {section}: the finish reason was {finish} after " +
                $"{Tokens("completion_tokens")} completion token(s), and {reasoning} character(s) of reasoning " +
                "arrived with nothing in the answer. A section stored from this would be empty.",
                transient: false,
                unusable: true);
        }

        // An answer that stopped on its budget rather than on its own end was cut,
        // and a section cut mid-sentence can pass the claim checker, because every
        // figure and citation in the part that arrived may be sound. Refused, where
        // the thinking capture's empty answer is refused above with its own reason.
        if (string.Equals(finish, CutOff, StringComparison.Ordinal))
        {
            throw new ProviderRefusal(
                $"The local model's answer for {section} stopped at its budget of {AnswerTokens} tokens rather than " +
                "ending, so what arrived is a section cut short and it is not stored.",
                transient: false,
                unusable: true);
        }

        return new ModelAnswer(
            root.TryGetProperty("model", out var model) && model.ValueKind == JsonValueKind.String ? model.GetString()! : "unstated",
            text,
            Tokens("prompt_tokens"),
            Tokens("completion_tokens"),
            finish);
    }
}
