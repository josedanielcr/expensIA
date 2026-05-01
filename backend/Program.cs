using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

var builder = FunctionsApplication.CreateBuilder(args);

builder.Configuration.AddKeyVaultIfConfigured();

builder.ConfigureFunctionsWebApplication();
builder.UseMiddleware<GlobalExceptionHandlingMiddleware>();

builder.Services
    .AddApplicationInsightsTelemetryWorkerService()
    .ConfigureFunctionsApplicationInsights();

builder.Services.AddHttpClient<GoogleTokenValidator>();
builder.Services.AddHttpClient<IOpenAiExpenseParser, OpenAiExpenseParser>();
builder.Services.AddHttpClient<ExchangeRateService>();
builder.Services.AddSupabaseDatabase();
builder.Services.AddScoped<TransactionPersistenceService>();

builder.Build().Run();
