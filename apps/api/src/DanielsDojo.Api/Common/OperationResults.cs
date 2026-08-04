using DanielsDojo.Application.Common;

namespace DanielsDojo.Api.Common;

/// <summary>
/// Turns an application <see cref="OperationResult"/> into an HTTP response.
/// </summary>
/// <remarks>
/// One mapping, used by every endpoint, so a given failure always produces the same status
/// code and the same stable <c>code</c> extension. Clients branch on that code rather than on
/// prose, which means wording can be improved without breaking them.
/// </remarks>
internal static class OperationResults
{
    /// <summary>Extension member carrying the stable machine-readable code.</summary>
    public const string CodeExtension = "code";

    /// <summary>Maps a failed result. Never call this with a successful one.</summary>
    public static IResult ToProblem(OperationResult outcome)
    {
        return outcome.Failure switch
        {
            OperationFailure.NotFound => Results.NotFound(),
            OperationFailure.Validation => Results.ValidationProblem(
                errors: outcome.Errors ?? new Dictionary<string, string[]>(StringComparer.Ordinal),
                detail: outcome.Message,
                statusCode: StatusCodes.Status400BadRequest,
                extensions: Extensions(outcome.Code)),
            OperationFailure.Concurrency => Results.Problem(
                detail: outcome.Message,
                statusCode: StatusCodes.Status409Conflict,
                title: "Conflicting change",
                extensions: Extensions(outcome.Code)),
            OperationFailure.Conflict => Results.Problem(
                detail: outcome.Message,
                statusCode: StatusCodes.Status409Conflict,
                title: "Conflict",
                extensions: Extensions(outcome.Code)),
            OperationFailure.Forbidden => Results.Problem(
                detail: outcome.Message,
                statusCode: StatusCodes.Status403Forbidden,
                title: "Not permitted",
                extensions: Extensions(outcome.Code)),
            _ => Results.Problem(statusCode: StatusCodes.Status500InternalServerError),
        };
    }

    /// <summary>Returns the value on success, or the mapped problem response.</summary>
    public static IResult ToResponse<T>(OperationResult<T> result) =>
        result.Succeeded ? Results.Ok(result.Value) : ToProblem(result.Outcome);

    /// <summary>Returns 201 with the value on success, or the mapped problem response.</summary>
    public static IResult ToCreated<T>(OperationResult<T> result, Func<T, string> location) =>
        result.Succeeded
            ? Results.Created(location(result.Value!), result.Value)
            : ToProblem(result.Outcome);

    private static Dictionary<string, object?> Extensions(string? code) =>
        new(StringComparer.Ordinal) { [CodeExtension] = code };
}
