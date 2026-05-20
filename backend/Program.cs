using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

var builder = FunctionsApplication.CreateBuilder(args);

builder.Configuration.AddKeyVaultIfConfigured();

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
builder.Services.AddSupabaseDatabase();
builder.Services.AddScoped<TransactionPersistenceService>();
builder.Services.AddScoped<TransactionReviewService>();

builder.Build().Run();
