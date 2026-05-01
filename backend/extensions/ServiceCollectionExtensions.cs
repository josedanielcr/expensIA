using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddSupabaseDatabase(this IServiceCollection services)
    {
        services.AddSingleton<SupabaseConnectionStringProvider>();
        services.AddDbContext<AiGastosDbContext>((serviceProvider, options) =>
        {
            var connectionString = serviceProvider
                .GetRequiredService<SupabaseConnectionStringProvider>()
                .GetConnectionString();

            options.UseNpgsql(connectionString, npgsqlOptions =>
            {
                npgsqlOptions.MapEnum<SyncRunStatus>("sync_run_status", "public");
                npgsqlOptions.MapEnum<TransactionReviewStatus>("transaction_review_status", "public");
                npgsqlOptions.MapEnum<SheetSyncStatus>("sheet_sync_status", "public");
                npgsqlOptions.MapEnum<ReviewEventType>("review_event_type", "public");
                npgsqlOptions.MapEnum<MerchantRuleStatus>("merchant_rule_status", "public");
                npgsqlOptions.MapEnum<MerchantRuleMatchType>("merchant_rule_match_type", "public");
            });
        });

        return services;
    }
}
