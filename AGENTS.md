# AGENTS.md

## Purpose
This repository has two deployment environments for the Azure Function backend:
- `main` branch deploys to production Function App.
- `staging` branch deploys to staging Function App.

Use this file as the operational guide before changing code, workflows, or manifests.

## Repository layout
- `backend/`: Azure Functions (.NET isolated, solution file `EmailParserService.slnx`).
- `extension/`: Chrome extension source.
- `.github/workflows/main_email-processor-ai.yml`: production deploy workflow (`main` only).
- `.github/workflows/staging_email-processor-ai-staging.yml`: staging deploy workflow (`staging` only).

## Deployment behavior
- Pushing to `main` triggers production deployment to app name `email-processor-ai`.
- Pushing to `staging` triggers staging deployment to app name `email-processor-ai-staging`.
- Both workflows build from `./backend` using:
  - `dotnet build EmailParserService.slnx --configuration Release --output ./output`

## Extension environment behavior
- Extension backend endpoint is resolved at runtime from `manifest.json` host permissions:
  - `extension/background/lib/core.js` reads the first `*.azurewebsites.net` host permission.
  - It appends `/api/OnEmailPush`.
- Environment manifests:
  - `extension/manifest.prod.json`
  - `extension/manifest.staging.json`

## Extension packaging
Use the script instead of manually editing `manifest.json`:

```bash
./extension/build-extension-zip.sh prod
./extension/build-extension-zip.sh staging
```

Outputs:
- `extension.zip` (prod)
- `extension-staging.zip` (staging)

The script copies the selected manifest into a temporary build folder as `manifest.json`, so working files are not modified.

## Branch and release flow
Recommended safe flow:
1. Work and validate changes in `staging`.
2. Push to `staging` and wait for staging GitHub Action to pass.
3. Validate staging endpoint (`/api/HealthCheck`) and extension behavior.
4. Open PR `staging -> main`.
5. Merge PR to promote to production.

## Required Azure/GitHub setup assumptions
- Staging and prod Function Apps both exist and are configured.
- GitHub secrets exist for each workflow (client/tenant/subscription IDs).
- OIDC federated credentials are configured for the matching branch refs.
- Staging app settings mirror required production settings (`OPENAI_*`, `KEY_VAULT_*`, `GOOGLE_*`, etc.).

## Safety rules for future agents
- Never push directly to `main` for unvalidated changes.
- Never change workflow branch triggers unless explicitly requested.
- Avoid changing production secrets, app names, or function app targets without user approval.
- Do not commit zip artifacts.
- Keep `manifest.prod.json` and `manifest.staging.json` as source-of-truth; package via script.

## Transaction persistence behavior
- `OnEmailPush` validates the Google token, parses emails, converts USD to CRC when needed, persists sync history, then returns only sheet-ready entries to the extension.
- Supabase Postgres is the source of truth for stored transactions and sync runs.
- Google Sheets remains an output layer; do not treat it as durable backend state.
- Dedupe is by `owner_google_sub + message_id`; if `messageId` is missing, backend uses a content hash fallback.
- Extension payloads should include `messageId`, `subject`, `sender`, `date`, and `message`.

## Review workflow behavior
- Parser output includes `confidence_score` as a normalized `0..1` value.
- `confidence_score < 0.80` routes a stored transaction to review:
  - `review_status = pending_review`
  - `sheet_sync_status = not_ready`
  - `review_reason` is stored in Spanish for display in the review UI.
- Missing `confidence_score` also routes to review.
- Auto-approved transactions use:
  - `review_status = approved`
  - `sheet_sync_status = ready`
- `OnEmailPush` response includes only approved/sheet-ready entries in `entries` so the existing extension Sheet append path does not append pending-review transactions.
- `OnEmailPush` also returns `pendingReview` with the count filtered out for review.
- The extension review UI loads pending-review rows from `/api/review/transactions`.
- Approving from the review UI posts corrections to `/api/review/transactions/{transactionId}`, appends the returned final row to Google Sheets, then calls the same endpoint with `mark_sheet_synced`.

## Supabase connection behavior
- Backend uses EF Core with Npgsql.
- Use the Supabase pooler host because direct `db.<project>.supabase.co` connections are IPv6-only unless the paid IPv4 add-on is enabled.
- Required app settings/secrets:
  - `SUPABASE_HOST` in local/Azure settings, e.g. `aws-1-us-west-2.pooler.supabase.com`
  - Key Vault secret `supabase-project-id`
  - Key Vault secret `supabase-prod-db-password`
- The provider builds username as `postgres.{projectId}` for the pooler.

## Exchange rate behavior (implemented)
- USD transactions are converted to CRC before returning `OnEmailPush` response.
  - Implemented in `backend/functions/OnEmailPush.cs`.
  - USD detection uses the currency code in description (e.g. `(USD)`), then:
    - `amount` is converted to CRC using the table rate.
    - description currency code is switched to `(CRC)`.
    - original USD amount is appended as `(<usd_amount>$)` (example: `(9.99$)`).
- Non-USD currencies are left unchanged.

## Exchange rate source of truth
- Request-time conversion reads the rate from Azure Table Storage (no external exchange API call per request).
- Table defaults/config:
  - Service URI: `https://compute911d.table.core.windows.net/` (`EXCHANGE_RATE_TABLE_SERVICE_URI`)
  - Table name: `conversionRate` (`EXCHANGE_RATE_TABLE_NAME`)
  - Row fields used: `From`, `To`, `Rate` (and `UpdatedAtUtc` when refreshed)
- Service implementation: `backend/services/ExchangeRateService.cs`.

## Daily refresh job
- Function: `RefreshExchangeRate` in `backend/functions/RefreshExchangeRate.cs`.
- Schedule: `0 0 6 * * *` (06:00 UTC = midnight Costa Rica).
- Refresh flow:
  - Reads API key from Key Vault secret `Exchange-rate-API`.
  - Calls `https://v6.exchangerate-api.com/v6/{api-key}/pair/USD/CRC` (configurable via `EXCHANGE_RATE_API_URL_TEMPLATE`).
  - Upserts table row with latest `Rate` and `UpdatedAtUtc`.

## Local development notes
- Timer triggers require a valid `AzureWebJobsStorage`.
  - With `UseDevelopmentStorage=true`, run Azurite locally.
  - If needed for local debugging, timer can be disabled via `AzureWebJobs.RefreshExchangeRate.Disabled=true` in `backend/local.settings.json`.
- Temporary manual HTTP trigger for rate refresh was removed after validation; do not re-enable unless explicitly requested.
