# AGENTS.md

## Scope
Backend Azure Functions live here. Keep changes small and consistent with the existing .NET isolated structure.

## Current shape
- Entry points are in `functions/`.
- Request/response DTOs belong in `contracts/` or `models/`.
- EF Core context and table entities live in `data/`.
- Dependency registration helpers live in `extensions/`.
- Shared behavior belongs in `services/`.
- Keep `Program.cs` readable; add service/config registration through extension methods.

## Persistence direction
- Supabase Postgres is the source of truth for transactions, sync runs, reviews, and merchant rules.
- The backend owns all Supabase access; do not call Supabase directly from the extension.
- Use the validated Google token `sub` and `email` as owner fields.
- Dedupe transactions by `owner + messageId`; use a content hash only when `messageId` is unavailable.
- `OnEmailPush` must keep its response shape compatible with the existing Google Sheets append flow, but return only approved/sheet-ready entries in `entries`.
- Include `pendingReview` in the `OnEmailPush` response when low-confidence entries were stored for review.
- Persist before returning results to the extension.

## Review workflow
- `ExpenseParseResult` includes `confidence_score`; parser normalization clamps scores into `0..1`.
- `TransactionPersistenceService` uses `0.80` as the auto-approval threshold.
- Transactions with missing score or `confidence_score < 0.80` must be stored as:
  - `ReviewStatus = PendingReview`
  - `SheetSyncStatus = NotReady`
  - Spanish `ReviewReason` suitable for the extension UI.
- Transactions with `confidence_score >= 0.80` are stored as approved and sheet-ready.
- Keep review reasons user-facing Spanish strings unless a separate internal reason/code field is added.
- Review endpoints live in `functions/ReviewTransactions.cs`.
- Review mutation/query behavior belongs in `TransactionReviewService`; keep endpoint functions thin.
- `GET /api/review/transactions` returns current-user pending-review rows only.
- `POST /api/review/transactions/{transactionId}` supports approving/correcting a row and marking it `sheet_synced`.
- Approve/correct should update the stored final transaction state before the extension appends it to Google Sheets.
- Marking a transaction `sheet_synced` happens only after the extension has successfully appended the final row to Google Sheets.

## Supabase connection
- Use EF Core/Npgsql.
- Use Supabase pooler, not direct `db.<project>.supabase.co`, unless IPv4 add-on is enabled.
- `SUPABASE_HOST` should come from local settings or Azure app settings.
- Key Vault secrets currently expected:
  - `supabase-project-id`
  - `supabase-prod-db-password`
- The pooler username is built as `postgres.{projectId}`.

## Safety
- Never log tokens, API keys, raw auth headers, or secrets.
- Do not commit `local.settings.json` secrets or generated Azure output.
- Keep Google Sheets as an output layer, not backend state.

## Checks
- Prefer `dotnet build EmailParserService.slnx` for backend validation.
- Add or run focused tests when behavior changes.
