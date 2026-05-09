# AGENTS.md

## Scope
Background Gmail, backend, Sheets, and label orchestration lives here.

## Sync flow
1. `handlers/extractEmailsFromLabel.js` starts the user sync.
2. `lib/core.js` reads Gmail messages and builds email payloads.
3. `pushEmailsToBackend` sends emails to `/api/OnEmailPush`.
4. Backend persists all parsed transactions, including pending-review rows.
5. Backend returns only sheet-ready parsed `entries` plus `pendingReview` count.
6. Existing flow appends returned entries to Sheets and moves Gmail labels.
7. If `pendingReview > 0`, popup opens the dedicated review UI.

## Email payload
- Build payload fields in `BackgroundCore.fetchMessageMetadata`.
- Keep sending `messageId`, `subject`, `sender`, `date`, and `message`.
- `messageId` is required for backend dedupe quality.

## Boundaries
- Do not call Supabase from the extension.
- Do not hardcode backend environment URLs; use manifest host permissions.
- Review fetch/approve calls go through `handlers/reviewTransactions.js`.
- On review approval, the background flow updates the backend first, appends the returned final row to Google Sheets, then marks the backend transaction as `sheet_synced`.
- Keep the review approval flow sequential and defensive: do not mark `sheet_synced` unless the Sheets append succeeds.
- Surface errors back to the review page so it can re-enable controls and show a concise failure state.
