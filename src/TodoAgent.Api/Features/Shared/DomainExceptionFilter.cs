namespace TodoAgent.Api.Features.Shared;

/// <summary>
/// Translates the two domain exceptions into RFC 9457 problem responses. Handlers
/// throw; endpoints stay thin; this filter, attached to the API route group in
/// Program.cs, is the one place that knows which exception is which status code.
/// Anything else propagates as a genuine 500.
/// </summary>
public sealed class DomainExceptionFilter : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        try
        {
            return await next(context);
        }
        catch (ValidationException ex)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>(ex.Errors));
        }
        catch (NotFoundException ex)
        {
            return Results.Problem(statusCode: StatusCodes.Status404NotFound, title: "Not found.", detail: ex.Message);
        }
    }
}
