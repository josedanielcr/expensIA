using Azure.Identity;
using Microsoft.Extensions.Configuration;

public static class ConfigurationExtensions
{
    public static IConfigurationManager AddKeyVaultIfConfigured(this IConfigurationManager configuration)
    {
        var useKeyVaultOnStartup = configuration.GetValue<bool?>("KEY_VAULT_LOAD_ON_STARTUP") ?? false;
        var keyVaultUri = configuration["KEY_VAULT_URI"];
        if (useKeyVaultOnStartup && !string.IsNullOrWhiteSpace(keyVaultUri))
        {
            configuration.AddAzureKeyVault(new Uri(keyVaultUri), new DefaultAzureCredential());
        }

        return configuration;
    }
}
