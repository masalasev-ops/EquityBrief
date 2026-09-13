namespace EquityBrief.Core.Providers;

// What the research model answered, as the parser read it, with the provider's own
// token counts and the instant the provider stamped on the answer.
//
// The counts are what a call is priced from, read off the payload rather than
// estimated, and `Created` is the provider's timestamp rather than this machine's,
// because a rate that doubles at peak is decided by the provider's clock.
// see: Code owns every number
public sealed record ResearchAnswer(
    string Model,
    string Text,
    int PromptTokens,
    int CacheHitTokens,
    int CacheMissTokens,
    int CompletionTokens,
    int ReasoningTokens,
    string FinishReason,
    DateTimeOffset Created);

// The research model did not answer at all. A type of its own for the reason the
// local model's is: a pass stops asking on it and carries on past a refusal.
public sealed class ResearchModelUnavailable(string message) : Exception(message);

// The paid lane's model.
//
// One interface with an implementation per wire format, chosen by configuration and
// never falling back from one provider to another. It prices its own answers and
// bounds its own calls, because what a token costs is the provider's and the one
// component allowed to make a paid call judges the cap by these two figures without
// knowing whose model it is.
// see: The research model is one interface with an implementation per wire format, chosen by configuration and never falling back
// see: Every paid call is made through the spend cap, which holds the research model
public interface IResearchModelFeed
{
    int Requests { get; }

    // The model as a section records it: the model and the mode it was asked in,
    // because the same model thinking and not thinking are two writers.
    string Identity { get; }

    Task<ResearchAnswer> CompleteAsync(ModelRequest request, CancellationToken cancellation = default);

    // What an answer cost, in dollars, from its own token counts.
    decimal Price(ResearchAnswer answer);

    // The most a call could cost before it is made, in dollars.
    decimal Ceiling(ModelRequest request);
}
