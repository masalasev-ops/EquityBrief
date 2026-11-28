using System.Net.Http;
using System.Text;

namespace EquityBrief.Core.Providers;

// The ntfy topic the operator's phone is subscribed to, read from the worker's secrets file alone. Whoever knows it can read
// and send to it, so it is never printed, logged, stored or sent anywhere but ntfy, and its own text is the only thing it
// stands for.
// see: Phone messages go through ntfy on a topic only the secrets file holds and carry tickers, prices and dates alone
public sealed class NtfyTopic(string topic)
{
    public const string SettingName = "EquityBrief:Notify:NtfyTopic";

    public string Topic { get; } = string.IsNullOrWhiteSpace(topic) ? throw new ArgumentException("An ntfy topic is never blank.", nameof(topic)) : topic.Trim();

    // A text with the topic taken out, as every error and log line is written.
    public string Redact(string text) => text.Replace(Topic, "[topic]", StringComparison.Ordinal);

    public override string ToString() => "an ntfy topic, not shown";

    public static NtfyTopic? Read(string? stated) => string.IsNullOrWhiteSpace(stated) ? null : new NtfyTopic(stated);
}

// One message to the operator's phone: a title and its text, tickers, prices and dates alone.
public interface IPhoneMessageFeed
{
    // Whether a topic is saved to send to.
    bool CanSend { get; }

    // Sends the message, true where ntfy took it; false and nothing sent where no topic is saved.
    Task<bool> SendAsync(string title, string text, CancellationToken cancellation = default);
}

// ntfy over the network: one request a message, posted to the topic at ntfy.sh, its title in a header. A topic not saved
// sends nothing, and a refusal names ntfy and never the topic.
public sealed class NtfyMessages(HttpClient client, NtfyTopic? topic) : IPhoneMessageFeed
{
    public const string Address = "https://ntfy.sh/";

    public bool CanSend => topic is not null;

    public static NtfyMessages Live(NtfyTopic? topic) =>
        new(new HttpClient { BaseAddress = new Uri(Address), Timeout = TimeSpan.FromSeconds(30) }, topic);

    public async Task<bool> SendAsync(string title, string text, CancellationToken cancellation = default)
    {
        if (topic is null)
        {
            return false;
        }

        try
        {
            using var message = new HttpRequestMessage(HttpMethod.Post, Uri.EscapeDataString(topic.Topic))
            {
                Content = new StringContent(text, Encoding.UTF8, "text/plain"),
            };

            message.Headers.TryAddWithoutValidation("Title", title);

            using var response = await client.SendAsync(message, cancellation).ConfigureAwait(false);

            return response.IsSuccessStatusCode
                ? true
                : throw new ProviderRefusal($"ntfy answered {(int)response.StatusCode} to the message.", RetryPolicy.Transient((int)response.StatusCode));
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (HttpRequestException failure)
        {
            throw new ProviderRefusal("ntfy could not be reached: " + topic.Redact(failure.Message), transient: true);
        }
    }
}
