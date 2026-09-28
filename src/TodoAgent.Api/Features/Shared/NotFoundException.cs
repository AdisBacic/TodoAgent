namespace TodoAgent.Api.Features.Shared;

/// <summary>Thrown by a handler when the entity a request refers to does not exist.</summary>
public sealed class NotFoundException(string message) : Exception(message);
