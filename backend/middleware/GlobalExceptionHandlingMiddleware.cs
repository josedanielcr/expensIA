using System.Runtime.CompilerServices;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Middleware;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Npgsql;

[assembly: InternalsVisibleTo("backend.Tests")]

public sealed class GlobalExceptionHandlingMiddleware : IFunctionsWorkerMiddleware
{
    private const string ErrorUnauthorized = "Unauthorized request.";
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
            var (statusCode, payload, logMessage) = MapException(ex, context.InvocationId);
            logger.LogError(
                "{LogMessage} FunctionName={FunctionName} InvocationId={InvocationId} StatusCode={StatusCode} ExceptionType={ExceptionType} UserEmail={UserEmail} GoogleUserId={GoogleUserId}",
                logMessage,
                context.FunctionDefinition.Name,
                context.InvocationId,
                statusCode,
                ex.GetType().Name,
                GetTelemetryContextValue(context, TelemetryContextKeys.UserEmail),
                GetTelemetryContextValue(context, TelemetryContextKeys.GoogleUserId));

            context.GetInvocationResult().Value = new ObjectResult(payload)
            {
                StatusCode = statusCode,
            };
        }
    }

    internal static (int StatusCode, object Payload, string LogMessage) MapException(
        Exception ex,
        string invocationId)
    {
        return ex switch
        {
            UnauthorizedAccessException => (
                StatusCodes.Status401Unauthorized,
                new { error = ErrorUnauthorized, invocationId },
                "Unauthorized request."),

            JsonException => (
                StatusCodes.Status400BadRequest,
                new { error = ErrorInvalidJson, invocationId },
                "Invalid JSON payload."),

            BadHttpRequestException => (
                StatusCodes.Status400BadRequest,
                new { error = ErrorInvalidJson, invocationId },
                "Invalid JSON payload."),

            DbUpdateException => BuildDatabaseError(invocationId),

            NpgsqlException => BuildDatabaseError(invocationId),

            InvalidOperationException invalidOperation when HasDatabaseException(invalidOperation) =>
                BuildDatabaseError(invocationId),

            InvalidOperationException invalidOperation => (
                StatusCodes.Status500InternalServerError,
                new { error = ErrorOpenAiParsing, invocationId },
                "OpenAI parsing failed."),

            _ => (
                StatusCodes.Status500InternalServerError,
                new { error = ErrorInternalServer, invocationId },
                "Unhandled exception.")
        };
    }

    private static string GetTelemetryContextValue(FunctionContext context, string key)
    {
        if (!context.Items.TryGetValue(key, out var value))
            return string.Empty;

        return value?.ToString()?.Trim() ?? string.Empty;
    }

    private static (int StatusCode, object Payload, string LogMessage) BuildDatabaseError(string invocationId)
    {
        return (
            StatusCodes.Status500InternalServerError,
            new { error = ErrorDatabase, invocationId },
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
