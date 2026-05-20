public static class GoogleTokenPolicy
{
    public static void Enforce(GoogleTokenInfo info, string? expectedAudience, params string[] expectedScopes)
    {
        if (string.IsNullOrWhiteSpace(expectedAudience))
            throw new UnauthorizedAccessException("Google token audience policy is not configured.");

        if (expectedScopes.Length == 0)
            throw new UnauthorizedAccessException("Google token scope policy is not configured.");

        var tokenAudience = info.Aud ?? info.Audience ?? info.IssuedTo;
        if (!string.Equals(tokenAudience, expectedAudience, StringComparison.Ordinal))
            throw new UnauthorizedAccessException("Token audience mismatch.");

        if (!info.ExpiresIn.HasValue || info.ExpiresIn.Value <= 0)
            throw new UnauthorizedAccessException("Google token is expired.");

        var scopes = (info.Scope ?? "")
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToHashSet(StringComparer.Ordinal);

        foreach (var s in expectedScopes)
        {
            if (!scopes.Contains(s))
                throw new UnauthorizedAccessException($"Missing required scope: {s}");
        }
    }
}
