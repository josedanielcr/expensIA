using Azure.Identity;
using Azure.Security.KeyVault.Secrets;
using Microsoft.Extensions.Configuration;
using Npgsql;

public sealed class SupabaseConnectionStringProvider
{
    private const string DefaultDatabase = "postgres";
    private const int DefaultPort = 5432;

    private readonly IConfiguration _configuration;
    private readonly object _connectionStringLock = new();
    private string? _connectionString;

    public SupabaseConnectionStringProvider(IConfiguration configuration)
    {
        _configuration = configuration;
    }

    public string GetConnectionString()
    {
        if (!string.IsNullOrWhiteSpace(_connectionString))
            return _connectionString;

        lock (_connectionStringLock)
        {
            if (!string.IsNullOrWhiteSpace(_connectionString))
                return _connectionString;

            _connectionString = BuildConnectionString();
            return _connectionString;
        }
    }

    private string BuildConnectionString()
    {
        var host = GetConfiguredValueOrSecret("SUPABASE_HOST", "supabase-host");
        var projectId = GetConfiguredValueOrSecret("SUPABASE_PROJECT_ID", "supabase-project-id");
        var password = GetConfiguredValueOrSecret("SUPABASE_DB_PASSWORD", "supabase-prod-db-password");
        if (string.IsNullOrWhiteSpace(host) ||
            string.IsNullOrWhiteSpace(projectId) ||
            string.IsNullOrWhiteSpace(password))
        {
            throw new InvalidOperationException(
                "SUPABASE_HOST, supabase-project-id, and supabase-prod-db-password are required.");
        }

        var builder = new NpgsqlConnectionStringBuilder
        {
            Host = host,
            Port = DefaultPort,
            Database = DefaultDatabase,
            Username = $"postgres.{projectId}",
            Password = password,
            SslMode = SslMode.Require,
            Pooling = true,
            IncludeErrorDetail = false,
        };

        return builder.ConnectionString;
    }

    private string? GetConfiguredValueOrSecret(string configKey, string secretName)
    {
        var directValue = _configuration[secretName] ?? _configuration[configKey];
        if (!string.IsNullOrWhiteSpace(directValue))
            return directValue;

        var vaultUri = _configuration["KEY_VAULT_URI"];
        if (string.IsNullOrWhiteSpace(vaultUri))
            return null;

        var client = new SecretClient(new Uri(vaultUri), new DefaultAzureCredential());
        var secret = client.GetSecret(secretName);
        return secret.Value.Value;
    }
}
