using System.Text.Json;
using Microsoft.AspNetCore.Http;

public sealed class GlobalExceptionHandlingMiddlewareTests
{
    private const string InvocationId = "test-invocation-id";

    public static IEnumerable<object[]> SanitizedErrorCases()
    {
        yield return
        [
            new UnauthorizedAccessException("Missing required scope: https://www.googleapis.com/auth/gmail.modify"),
            StatusCodes.Status401Unauthorized,
            "Unauthorized request.",
            new[] { "Missing required scope", "gmail.modify" },
        ];

        yield return
        [
            new JsonException("Unexpected token at $.clientSecret"),
            StatusCodes.Status400BadRequest,
            "Invalid JSON body.",
            new[] { "Unexpected token", "clientSecret" },
        ];

        yield return
        [
            new BadHttpRequestException("Malformed request body detail", StatusCodes.Status400BadRequest),
            StatusCodes.Status400BadRequest,
            "Invalid JSON body.",
            new[] { "Malformed request body detail" },
        ];

        yield return
        [
            new InvalidOperationException(
                "OpenAI request failed. Status=400 Body={\"error\":\"provider payload\"}",
                new ApplicationException("Inner provider failure detail")),
            StatusCodes.Status500InternalServerError,
            "OpenAI parsing failed.",
            new[] { "Status=400", "provider payload", "Inner provider failure detail" },
        ];

        yield return
        [
            new ApplicationException("NpgsqlException: relation public.expenses does not exist"),
            StatusCodes.Status500InternalServerError,
            "Internal server error while processing emails.",
            new[] { "NpgsqlException", "public.expenses" },
        ];
    }

    [Theory]
    [MemberData(nameof(SanitizedErrorCases))]
    public void MapException_ReturnsSanitizedPayload(
        Exception exception,
        int expectedStatusCode,
        string expectedError,
        string[] deniedFragments)
    {
        var (statusCode, payload, _) = GlobalExceptionHandlingMiddleware.MapException(exception, InvocationId);
        var payloadJson = JsonSerializer.Serialize(payload);

        Assert.Equal(expectedStatusCode, statusCode);

        using var document = JsonDocument.Parse(payloadJson);
        var root = document.RootElement;
        Assert.Equal(expectedError, root.GetProperty("error").GetString());
        Assert.Equal(InvocationId, root.GetProperty("invocationId").GetString());
        Assert.False(root.TryGetProperty("details", out _));
        Assert.False(root.TryGetProperty("innerError", out _));
        Assert.Equal(2, root.EnumerateObject().Count());

        foreach (var deniedFragment in deniedFragments)
        {
            Assert.DoesNotContain(deniedFragment, payloadJson, StringComparison.Ordinal);
        }
    }
}
