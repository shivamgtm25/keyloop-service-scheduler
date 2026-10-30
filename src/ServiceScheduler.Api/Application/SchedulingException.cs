using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace ServiceScheduler.Api.Application;

/// <summary>A business-rule failure carrying a stable machine-readable code and HTTP status.</summary>
public class SchedulingException(string code, int status, string message) : Exception(message)
{
    public string Code { get; } = code;
    public int Status { get; } = status;

    public static SchedulingException NotFound(string what, int id) =>
        new("not_found", StatusCodes.Status404NotFound, $"{what} {id} was not found.");

    public static SchedulingException Invalid(string code, string message) =>
        new(code, StatusCodes.Status422UnprocessableEntity, message);

    public static SchedulingException Conflict(string code, string message) =>
        new(code, StatusCodes.Status409Conflict, message);
}

/// <summary>Maps exceptions to RFC 7807 problem responses; unexpected errors never leak details.</summary>
public class SchedulingExceptionHandler(
    IProblemDetailsService problemDetails, ILogger<SchedulingExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext ctx, Exception ex, CancellationToken ct)
    {
        var problem = ex is SchedulingException se
            ? new ProblemDetails { Status = se.Status, Title = se.Code, Detail = se.Message }
            : new ProblemDetails { Status = 500, Title = "internal_error", Detail = "An unexpected error occurred." };

        if (ex is not SchedulingException)
            logger.LogError(ex, "Unhandled exception processing {Method} {Path}", ctx.Request.Method, ctx.Request.Path);

        problem.Extensions["code"] = problem.Title;
        problem.Extensions["traceId"] = System.Diagnostics.Activity.Current?.TraceId.ToString() ?? ctx.TraceIdentifier;

        ctx.Response.StatusCode = problem.Status!.Value;
        return await problemDetails.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = ctx, ProblemDetails = problem, Exception = ex,
        });
    }
}
