using System.Net.Http.Json;
using System.Net.Http.Headers;
using Microsoft.Extensions.Configuration;

public sealed class GoogleTokenValidator
{
    private const string BearerPrefix = "Bearer ";
    private const string TokenInfoUrl = "https://oauth2.googleapis.com/tokeninfo";
    private const string UserInfoUrl = "https://www.googleapis.com/oauth2/v3/userinfo";
    private const string ConfigExpectedAudience = "GOOGLE_EXPECTED_AUDIENCE";
    private const string ConfigRequiredScopes = "GOOGLE_REQUIRED_SCOPES";
    private readonly HttpClient httpClient;
    private readonly IConfiguration configuration;

    public GoogleTokenValidator(HttpClient httpClient, IConfiguration configuration)
    {
        this.httpClient = httpClient;
        this.configuration = configuration;
    }

    public async Task<GoogleTokenInfo?> ValidateTokenAsync(string token, CancellationToken ct = default)
    {
        var normalizedToken = NormalizeToken(token);
        if (string.IsNullOrWhiteSpace(normalizedToken))
            throw new UnauthorizedAccessException("Invalid Google token: token is empty.");

        var tokenInfo = await GetTokenInfoAsync(normalizedToken, ct);
        GoogleTokenPolicy.Enforce(
            tokenInfo,
            configuration[ConfigExpectedAudience],
            GetRequiredScopes());

        var userInfo = await GetUserInfoAsync(normalizedToken, ct);
        MergeUserInfo(tokenInfo, userInfo);

        return tokenInfo;
    }

    private async Task<GoogleTokenInfo> GetTokenInfoAsync(string normalizedToken, CancellationToken ct)
    {
        var tokenInfoUrl = $"{TokenInfoUrl}?access_token={Uri.EscapeDataString(normalizedToken)}";
        using var resp = await httpClient.GetAsync(tokenInfoUrl, ct);

        if (!resp.IsSuccessStatusCode)
            throw new UnauthorizedAccessException($"Invalid Google token. Status={(int)resp.StatusCode}");

        return await resp.Content.ReadFromJsonAsync<GoogleTokenInfo>(cancellationToken: ct)
            ?? throw new UnauthorizedAccessException("Invalid Google token (empty tokeninfo response).");
    }

    private async Task<GoogleTokenInfo> GetUserInfoAsync(string normalizedToken, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, UserInfoUrl);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", normalizedToken);
        request.Headers.Accept.ParseAdd("application/json");
        using var resp = await httpClient.SendAsync(request, ct);

        if (!resp.IsSuccessStatusCode)
            throw new UnauthorizedAccessException($"Invalid Google token. Status={(int)resp.StatusCode}");

        var info = await resp.Content.ReadFromJsonAsync<GoogleTokenInfo>(cancellationToken: ct)
            ?? throw new UnauthorizedAccessException("Invalid Google token (empty response).");
        if (string.IsNullOrWhiteSpace(info.UserId))
            info.UserId = info.Sub;

        return info;
    }

    private string[] GetRequiredScopes()
    {
        return (configuration[ConfigRequiredScopes] ?? string.Empty)
            .Split(new[] { ' ', ',', ';' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }

    private static void MergeUserInfo(GoogleTokenInfo tokenInfo, GoogleTokenInfo userInfo)
    {
        tokenInfo.Email ??= userInfo.Email;
        tokenInfo.UserId ??= userInfo.UserId;
        tokenInfo.Sub ??= userInfo.Sub;
    }

    private static string NormalizeToken(string rawToken)
    {
        var token = rawToken?.Trim() ?? string.Empty;
        if (token.StartsWith(BearerPrefix, StringComparison.OrdinalIgnoreCase))
        {
            token = token[BearerPrefix.Length..].Trim();
        }

        return token.Trim('"');
    }
}
