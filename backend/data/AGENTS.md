# AGENTS.md

## Scope
EF Core persistence mapping lives here.

## Tables
- `AiGastosDbContext` maps existing Supabase tables; do not assume migrations own schema yet.
- Entity classes live in `entities/`, one file per entity.
- Keep enum names aligned with Postgres enum labels through Npgsql snake-case mapping.

## Current storage flow
- `sync_runs` records each `OnEmailPush` attempt.
- `transactions` stores one parsed result per email unless dedupe finds an existing row.
- `transactions.confidence_score` stores parser confidence as a `0..1` decimal.
- `transactions.review_status` is `approved` for sheet-ready parser results and `pending_review` for missing or low confidence.
- `transactions.review_reason` should be user-facing Spanish text.
- `transactions.sheet_sync_status` is `ready` only when the extension may append the transaction to Google Sheets; pending-review rows use `not_ready`.
- `review_events` and `merchant_rules` are mapped for upcoming review/learning milestones.

## Dedupe contract
- Preferred key: `owner_google_sub + message_id`.
- Fallback key: `owner_google_sub + content_hash` only when `message_id` is missing.
