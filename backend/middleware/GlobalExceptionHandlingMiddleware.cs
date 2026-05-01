using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Middleware;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Npgsql;

public sealed class GlobalExceptionHandlingMiddleware : IFunctionsWorkerMiddleware
{
    private const string ErrorInvalidJson = "Invalid JSON body.";
    private const string ErrorInternalServer = "Internal server error while processing emails.";
    private const string ErrorOpenAiParsing = "OpenAI parsing failed.";
    private const string ErrorDatabase = "Database operation failed.";

    public async Task Invoke(FunctionContext context, FunctionExecutionDelegate next)
    {
        try
        {
            await next(context);
        }
        catch (Exception ex)
        {
            var logger = context.GetLogger<GlobalExceptionHandlingMiddleware>();
            var (statusCode, payload, logMessage) = MapException(ex);
            logger.LogError(ex, "{LogMessage} InvocationId={InvocationId}", logMessage, context.InvocationId);

            context.GetInvocationResult().Value = new ObjectResult(payload)
            {
                StatusCode = statusCode,
            };
        }
    }

    private static (int StatusCode, object Payload, string LogMessage) MapException(Exception ex)
    {
        return ex switch
        {
            UnauthorizedAccessException unauthorized => (
                StatusCodes.Status401Unauthorized,
                new { error = unauthorized.Message },
                "Unauthorized request."),

            JsonException => (
                StatusCodes.Status400BadRequest,
                new { error = ErrorInvalidJson },
                "Invalid JSON payload."),

            BadHttpRequestException => (
                StatusCodes.Status400BadRequest,
                new { error = ErrorInvalidJson },
                "Invalid JSON payload."),

            DbUpdateException database => BuildDatabaseError(database),

            NpgsqlException database => BuildDatabaseError(database),

            InvalidOperationException invalidOperation when HasDatabaseException(invalidOperation) =>
                BuildDatabaseError(invalidOperation),

            InvalidOperationException invalidOperation => (
                StatusCodes.Status500InternalServerError,
                new
                {
                    error = ErrorOpenAiParsing,
                    details = invalidOperation.Message,
                    innerError = invalidOperation.InnerException?.Message ?? string.Empty,
                },
                "OpenAI parsing failed."),

            _ => (
                StatusCodes.Status500InternalServerError,
                new { error = ErrorInternalServer },
                "Unhandled exception.")
        };
    }

    private static (int StatusCode, object Payload, string LogMessage) BuildDatabaseError(Exception exception)
    {
        return (
            StatusCodes.Status500InternalServerError,
            new
            {
                error = ErrorDatabase,
                details = exception.Message,
                innerError = exception.InnerException?.Message ?? string.Empty,
            },
            "Database operation failed.");
    }

    private static bool HasDatabaseException(Exception exception)
    {
        var current = exception.InnerException;
        while (current is not null)
        {
            if (current is NpgsqlException or DbUpdateException)
                return true;

            current = current.InnerException;
        }

        return false;
    }
}
