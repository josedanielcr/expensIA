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
- `TransactionPersistenceService` owns sync run creation, transaction insertion, and dedupe counts.
