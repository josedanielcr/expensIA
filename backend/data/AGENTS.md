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
- `review_events` and `merchant_rules` are mapped for upcoming review/learning milestones.

## Dedupe contract
- Preferred key: `owner_google_sub + message_id`.
- Fallback key: `owner_google_sub + content_hash` only when `message_id` is missing.
