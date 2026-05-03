# AGENTS.md

## Scope
Backend business services and external integrations live here.

## Key Vault pattern
- Prefer reading local/Azure app settings first.
- If a required secret is absent from configuration, fetch it from Key Vault using `KEY_VAULT_URI`.
- Never log secret values or full connection strings.

## Supabase
- `SupabaseConnectionStringProvider` builds the EF Core connection string.
- Use `SUPABASE_HOST` for the pooler host.
- Load `supabase-project-id` and `supabase-prod-db-password` from config or Key Vault.

## Parsing and persistence
- `OpenAiExpenseParser` only parses and normalizes model output.
- The OpenAI prompt must request `confidence_score` for every parsed result.
- `OpenAiExpenseParser` accepts numeric or numeric-string `confidence_score` values and normalizes them into `0..1`.
- `TransactionPersistenceService` owns sync run creation, transaction insertion, review routing, sheet-ready status, and dedupe counts.
- `TransactionReviewService` owns pending-review queries, approve/correct updates, review events, sheet-synced marking, and sync-run count recalculation.
- Use the `0.80` confidence threshold for automatic approval.
- Low-confidence or missing-score transactions are `PendingReview` and `NotReady`; their `ReviewReason` is stored in Spanish for the extension UI.
- `IsSheetReadyAfterParsing` is the shared helper for filtering response entries to the extension.
