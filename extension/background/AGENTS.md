# AGENTS.md

## Scope
Background Gmail, backend, Sheets, and label orchestration lives here.

## Sync flow
1. `handlers/extractEmailsFromLabel.js` starts the user sync.
2. `lib/core.js` reads Gmail messages and builds email payloads.
3. `pushEmailsToBackend` sends emails to `/api/OnEmailPush`.
4. Backend returns parsed `entries`.
5. Existing flow appends entries to Sheets and moves Gmail labels.

## Email payload
- Build payload fields in `BackgroundCore.fetchMessageMetadata`.
- Keep sending `messageId`, `subject`, `sender`, `date`, and `message`.
- `messageId` is required for backend dedupe quality.

## Boundaries
- Do not call Supabase from the extension.
- Do not hardcode backend environment URLs; use manifest host permissions.
