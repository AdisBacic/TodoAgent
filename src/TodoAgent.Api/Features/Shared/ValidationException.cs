namespace TodoAgent.Api.Features.Shared;

/// <summary>
/// Thrown by a handler when the request fails validation. Carries the errors
/// keyed by field so the HTTP layer can render a standard validation problem
/// and the agent layer can hand the message straight back to the model.
/// </summary>
public sealed class ValidationException(IReadOnlyDictionary<string, string[]> errors)
    : Exception(string.Join(" ", errors.SelectMany(e => e.Value)))
{
    public IReadOnlyDictionary<string, string[]> Errors { get; } = errors;
}
