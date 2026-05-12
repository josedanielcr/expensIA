using Azure.Identity;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

var builder = FunctionsApplication.CreateBuilder(args);

var useKeyVaultOnStartup = builder.Configuration.GetValue<bool?>("KEY_VAULT_LOAD_ON_STARTUP") ?? false;
var keyVaultUri = builder.Configuration["KEY_VAULT_URI"];
if (useKeyVaultOnStartup && !string.IsNullOrWhiteSpace(keyVaultUri))
{
    builder.Configuration.AddAzureKeyVault(new Uri(keyVaultUri), new DefaultAzureCredential());
}

builder.ConfigureFunctionsWebApplication();
builder.UseMiddleware<GlobalExceptionHandlingMiddleware>();

builder.Services
    .AddApplicationInsightsTelemetryWorkerService()
    .ConfigureFunctionsApplicationInsights();

builder.Logging.Services.Configure<LoggerFilterOptions>(options =>
{
    var defaultRule = options.Rules.FirstOrDefault(rule =>
        string.Equals(
            rule.ProviderName,
            "Microsoft.Extensions.Logging.ApplicationInsights.ApplicationInsightsLoggerProvider",
            StringComparison.Ordinal));
    if (defaultRule is not null)
    {
        options.Rules.Remove(defaultRule);
    }
});

builder.Services.AddHttpClient<GoogleTokenValidator>();
builder.Services.AddHttpClient<IOpenAiExpenseParser, OpenAiExpenseParser>();
builder.Services.AddHttpClient<ExchangeRateService>();

builder.Build().Run();
