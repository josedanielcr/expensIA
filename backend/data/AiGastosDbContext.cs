using Microsoft.EntityFrameworkCore;

public sealed class AiGastosDbContext : DbContext
{
    public AiGastosDbContext(DbContextOptions<AiGastosDbContext> options)
        : base(options)
    {
    }

    public DbSet<SyncRun> SyncRuns => Set<SyncRun>();

    public DbSet<StoredTransaction> Transactions => Set<StoredTransaction>();

    public DbSet<ReviewEvent> ReviewEvents => Set<ReviewEvent>();

    public DbSet<MerchantRule> MerchantRules => Set<MerchantRule>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasPostgresEnum<SyncRunStatus>("public", "sync_run_status");
        modelBuilder.HasPostgresEnum<TransactionReviewStatus>("public", "transaction_review_status");
        modelBuilder.HasPostgresEnum<SheetSyncStatus>("public", "sheet_sync_status");
        modelBuilder.HasPostgresEnum<ReviewEventType>("public", "review_event_type");
        modelBuilder.HasPostgresEnum<MerchantRuleStatus>("public", "merchant_rule_status");
        modelBuilder.HasPostgresEnum<MerchantRuleMatchType>("public", "merchant_rule_match_type");

        ConfigureSyncRun(modelBuilder);
        ConfigureTransaction(modelBuilder);
        ConfigureReviewEvent(modelBuilder);
        ConfigureMerchantRule(modelBuilder);
    }

    private static void ConfigureSyncRun(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<SyncRun>();
        entity.ToTable("sync_runs");
        entity.HasKey(e => e.Id);

        entity.Property(e => e.Id).HasColumnName("id");
        entity.Property(e => e.OwnerGoogleSub).HasColumnName("owner_google_sub");
        entity.Property(e => e.OwnerEmail).HasColumnName("owner_email");
        entity.Property(e => e.Status).HasColumnName("status").HasColumnType("sync_run_status");
        entity.Property(e => e.EmailsReceivedCount).HasColumnName("emails_received_count");
        entity.Property(e => e.EmailsProcessedCount).HasColumnName("emails_processed_count");
        entity.Property(e => e.TransactionsCreatedCount).HasColumnName("transactions_created_count");
        entity.Property(e => e.DuplicatesCount).HasColumnName("duplicates_count");
        entity.Property(e => e.PendingReviewCount).HasColumnName("pending_review_count");
        entity.Property(e => e.ApprovedCount).HasColumnName("approved_count");
        entity.Property(e => e.SheetReadyCount).HasColumnName("sheet_ready_count");
        entity.Property(e => e.SheetSyncedCount).HasColumnName("sheet_synced_count");
        entity.Property(e => e.ErrorCount).HasColumnName("error_count");
        entity.Property(e => e.ErrorsJson).HasColumnName("errors").HasColumnType("jsonb");
        entity.Property(e => e.MetadataJson).HasColumnName("metadata").HasColumnType("jsonb");
        entity.Property(e => e.StartedAt).HasColumnName("started_at");
        entity.Property(e => e.CompletedAt).HasColumnName("completed_at");
        entity.Property(e => e.CreatedAt).HasColumnName("created_at");
        entity.Property(e => e.UpdatedAt).HasColumnName("updated_at");
    }

    private static void ConfigureTransaction(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<StoredTransaction>();
        entity.ToTable("transactions");
        entity.HasKey(e => e.Id);

        entity.Property(e => e.Id).HasColumnName("id");
        entity.Property(e => e.OwnerGoogleSub).HasColumnName("owner_google_sub");
        entity.Property(e => e.OwnerEmail).HasColumnName("owner_email");
        entity.Property(e => e.SyncRunId).HasColumnName("sync_run_id");
        entity.Property(e => e.MessageId).HasColumnName("message_id");
        entity.Property(e => e.ContentHash).HasColumnName("content_hash");
        entity.Property(e => e.Subject).HasColumnName("subject");
        entity.Property(e => e.EmailFrom).HasColumnName("email_from");
        entity.Property(e => e.EmailReceivedAt).HasColumnName("email_received_at");
        entity.Property(e => e.Merchant).HasColumnName("merchant");
        entity.Property(e => e.Description).HasColumnName("description");
        entity.Property(e => e.Category).HasColumnName("category");
        entity.Property(e => e.TransactionDate).HasColumnName("transaction_date");
        entity.Property(e => e.Amount).HasColumnName("amount").HasPrecision(14, 2);
        entity.Property(e => e.Currency).HasColumnName("currency");
        entity.Property(e => e.OriginalAmount).HasColumnName("original_amount").HasPrecision(14, 2);
        entity.Property(e => e.OriginalCurrency).HasColumnName("original_currency");
        entity.Property(e => e.ExchangeRate).HasColumnName("exchange_rate").HasPrecision(18, 8);
        entity.Property(e => e.ConfidenceScore).HasColumnName("confidence_score").HasPrecision(5, 4);
        entity.Property(e => e.ReviewStatus).HasColumnName("review_status").HasColumnType("transaction_review_status");
        entity.Property(e => e.ReviewReason).HasColumnName("review_reason");
        entity.Property(e => e.SheetSyncStatus).HasColumnName("sheet_sync_status").HasColumnType("sheet_sync_status");
        entity.Property(e => e.SheetSyncedAt).HasColumnName("sheet_synced_at");
        entity.Property(e => e.SheetRowId).HasColumnName("sheet_row_id");
        entity.Property(e => e.SheetError).HasColumnName("sheet_error");
        entity.Property(e => e.ParserVersion).HasColumnName("parser_version");
        entity.Property(e => e.ParserModel).HasColumnName("parser_model");
        entity.Property(e => e.ParsedAt).HasColumnName("parsed_at");
        entity.Property(e => e.ParsedPayloadJson).HasColumnName("parsed_payload").HasColumnType("jsonb");
        entity.Property(e => e.EmailMetadataJson).HasColumnName("email_metadata").HasColumnType("jsonb");
        entity.Property(e => e.CreatedAt).HasColumnName("created_at");
        entity.Property(e => e.UpdatedAt).HasColumnName("updated_at");

        entity.HasOne(e => e.SyncRun)
            .WithMany(e => e.Transactions)
            .HasForeignKey(e => e.SyncRunId)
            .OnDelete(DeleteBehavior.SetNull);
    }

    private static void ConfigureReviewEvent(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<ReviewEvent>();
        entity.ToTable("review_events");
        entity.HasKey(e => e.Id);

        entity.Property(e => e.Id).HasColumnName("id");
        entity.Property(e => e.TransactionId).HasColumnName("transaction_id");
        entity.Property(e => e.MerchantRuleId).HasColumnName("merchant_rule_id");
        entity.Property(e => e.OwnerGoogleSub).HasColumnName("owner_google_sub");
        entity.Property(e => e.OwnerEmail).HasColumnName("owner_email");
        entity.Property(e => e.ActorGoogleSub).HasColumnName("actor_google_sub");
        entity.Property(e => e.ActorEmail).HasColumnName("actor_email");
        entity.Property(e => e.EventType).HasColumnName("event_type").HasColumnType("review_event_type");
        entity.Property(e => e.PreviousValuesJson).HasColumnName("previous_values").HasColumnType("jsonb");
        entity.Property(e => e.NewValuesJson).HasColumnName("new_values").HasColumnType("jsonb");
        entity.Property(e => e.Note).HasColumnName("note");
        entity.Property(e => e.CreatedAt).HasColumnName("created_at");

        entity.HasOne(e => e.Transaction)
            .WithMany(e => e.ReviewEvents)
            .HasForeignKey(e => e.TransactionId)
            .OnDelete(DeleteBehavior.Cascade);

        entity.HasOne(e => e.MerchantRule)
            .WithMany(e => e.ReviewEvents)
            .HasForeignKey(e => e.MerchantRuleId)
            .OnDelete(DeleteBehavior.SetNull);
    }

    private static void ConfigureMerchantRule(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<MerchantRule>();
        entity.ToTable("merchant_rules");
        entity.HasKey(e => e.Id);

        entity.Property(e => e.Id).HasColumnName("id");
        entity.Property(e => e.OwnerGoogleSub).HasColumnName("owner_google_sub");
        entity.Property(e => e.OwnerEmail).HasColumnName("owner_email");
        entity.Property(e => e.MatchType).HasColumnName("match_type").HasColumnType("merchant_rule_match_type");
        entity.Property(e => e.MatchValue).HasColumnName("match_value");
        entity.Property(e => e.MerchantName).HasColumnName("merchant_name");
        entity.Property(e => e.Category).HasColumnName("category");
        entity.Property(e => e.Status).HasColumnName("status").HasColumnType("merchant_rule_status");
        entity.Property(e => e.CorrectionCount).HasColumnName("correction_count");
        entity.Property(e => e.ActivationThreshold).HasColumnName("activation_threshold");
        entity.Property(e => e.FirstSeenAt).HasColumnName("first_seen_at");
        entity.Property(e => e.LastSeenAt).HasColumnName("last_seen_at");
        entity.Property(e => e.ActivatedAt).HasColumnName("activated_at");
        entity.Property(e => e.DisabledAt).HasColumnName("disabled_at");
        entity.Property(e => e.LastAppliedAt).HasColumnName("last_applied_at");
        entity.Property(e => e.MetadataJson).HasColumnName("metadata").HasColumnType("jsonb");
        entity.Property(e => e.CreatedAt).HasColumnName("created_at");
        entity.Property(e => e.UpdatedAt).HasColumnName("updated_at");
    }
}
